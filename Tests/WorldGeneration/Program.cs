using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GrandStrategy.WorldGeneration;

internal static class Program
{
    private static int assertions;
    private static void Require(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }

    private static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { assertions++; return; }
        throw new Exception(message);
    }

    private static HeightmapRecipe Small(int seed = 271828)
    { var recipe = HeightmapRecipe.OldWorld(); recipe.Width = 128; recipe.Height = 64; recipe.Seed = seed; return recipe; }

    private static void Main(string[] args)
    {
        var recipe = Small();
        float previous = 0;
        var first = HeightmapGenerator.Generate(recipe, (fraction, _) =>
        { Require(fraction >= previous && fraction <= 1, "Progress regressed."); previous = fraction; return true; });
        var repeat = HeightmapGenerator.Generate(recipe);
        Require(first.Elevations.SequenceEqual(repeat.Elevations), "Same recipe and seed must reproduce exactly.");
        Require(!first.Elevations.SequenceEqual(HeightmapGenerator.Generate(Small(17)).Elevations), "Different seeds must change terrain.");
        Require(first.LandFraction > .05f && first.LandFraction < .95f, "Default world must contain useful land and water.");
        Require(first.Elevations.All(v => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0 && v <= 1), "Invalid heights.");
        Require(Enumerable.Range(0, recipe.Width).All(x => first.Elevations[x] == 0 && first.Elevations[(recipe.Height - 1) * recipe.Width + x] == 0), "North/south borders must be ocean.");
        Require(Enumerable.Range(0, recipe.Height).All(y => first.Elevations[y * recipe.Width] == 0 && first.Elevations[y * recipe.Width + recipe.Width - 1] == 0), "West/east borders must be ocean.");
        recipe.Seed = 19; recipe.Steps[0].StrengthMin = .1f;
        Require(first.Recipe.Seed == 271828 && first.Recipe.Steps[0].StrengthMin == .40f, "Result must retain its original recipe.");
        var copy = (float[])first.Elevations.Clone();
        try { HeightmapGenerator.Generate(Small(), (fraction, _) => fraction < .85f); throw new Exception("Cancellation not observed."); }
        catch (OperationCanceledException) { assertions++; }
        Require(copy.SequenceEqual(first.Elevations), "Cancellation must not change a completed world.");
        var oversized = Small(); oversized.Width = int.MaxValue; Reject(() => HeightmapGenerator.Generate(oversized), "Oversized map accepted.");
        var invalid = Small(); invalid.Steps[0].Radius = float.NaN; Reject(() => HeightmapGenerator.Generate(invalid), "NaN accepted.");
        invalid = Small(); invalid.Steps[0].CountMax = 100000; Reject(() => HeightmapGenerator.Generate(invalid), "Unbounded step count accepted.");
        invalid = Small(); invalid.Steps[0].XMin = .9f; invalid.Steps[0].XMax = .2f; Reject(() => HeightmapGenerator.Generate(invalid), "Inverted range accepted.");
        var noHills = Small(); noHills.Steps[0].Enabled = false;
        Require(!first.Elevations.SequenceEqual(HeightmapGenerator.Generate(noHills).Elevations), "Disabling a step must change terrain.");

        // Asymmetric sentinel values verify byte order and row orientation independently of terrain.
        var sentinels = new float[128 * 64]; sentinels[0] = 0; sentinels[1] = 1; sentinels[128] = .5f; sentinels[sentinels.Length - 1] = .25f;
        using (var bytes = new MemoryStream())
        {
            HeightmapEncoding.WriteRaw16(new HeightmapResult(Small(), sentinels), bytes);
            Require(bytes.CanWrite && bytes.Length == sentinels.Length * 2, "RAW length or stream ownership incorrect.");
            var raw = bytes.ToArray();
            Require(raw[0] == 0 && raw[1] == 0 && raw[2] == 255 && raw[3] == 255, "RAW encoding invalid.");
            Require(raw[256] == 0 && raw[257] == 128 && raw[raw.Length - 2] == 0 && raw[raw.Length - 1] == 64, "RAW orientation/endianness invalid.");
        }
        CheckWater();
        CheckRivers();
        Console.WriteLine($"PASS: {assertions} assertions; core determinism, snapshots, coverage, bounds, cancellation, progress, RAW16, water topology, OpenGS palette, and river routing and carving.");
        if (args.Contains("--benchmark"))
        {
            foreach (var dimensions in new[] { (1024, 512), (2048, 1024), (3072, 2048) })
                for (var run = 0; run < 2; run++)
                {
                    var settings = HeightmapRecipe.OldWorld(); settings.Width = dimensions.Item1; settings.Height = dimensions.Item2;
                    var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var timer = Stopwatch.StartNew();
                    var world = HeightmapGenerator.Generate(settings); timer.Stop(); process.Refresh();
                    Console.WriteLine($"{settings.Width}x{settings.Height} {(run == 0 ? "first" : "repeat")} elapsed={timer.Elapsed.TotalSeconds:F3}s cpu={(process.TotalProcessorTime - cpu).TotalSeconds:F3}s processPeak={process.PeakWorkingSet64 / 1048576.0:F1}MiB land={world.LandFraction:P1} max={world.Maximum:F3}");
                    cpu = process.TotalProcessorTime; timer.Restart();
                    var water = WaterMap.Classify(world); timer.Stop(); process.Refresh();
                    Console.WriteLine($"  classify elapsed={timer.Elapsed.TotalSeconds:F3}s cpu={(process.TotalProcessorTime - cpu).TotalSeconds:F3}s processPeak={process.PeakWorkingSet64 / 1048576.0:F1}MiB ocean={water.OceanCount} lake={water.LakeCount}");
                    cpu = process.TotalProcessorTime;
                    timer.Restart();
                    var network = RiverGenerator.Generate(world);
                    timer.Stop();
                    process.Refresh();
                    Console.WriteLine($"  rivers elapsed={timer.Elapsed.TotalSeconds:F3}s cpu={(process.TotalProcessorTime - cpu).TotalSeconds:F3}s processPeak={process.PeakWorkingSet64 / 1048576.0:F1}MiB channels={network.ChannelCells}");
                    if (settings.Width == 1024 && run == 0 && args.Length > 1)
                    {
                        var directory = args.Last(); Directory.CreateDirectory(directory);
                        using (var stream = File.Create(Path.Combine(directory, "heightmap.raw"))) HeightmapEncoding.WriteRaw16(world, stream);
                    }
                }
        }
    }

    private static void CheckRivers()
    {
        var recipe = Small();
        recipe.Width = 32;
        recipe.Height = 32;
        var sea = recipe.SeaLevel;

        foreach (var outlet in new[] { 0, 16 * 32 + 16 })
        {
            var elevations = Enumerable.Repeat(sea + .25f, 1024).ToArray();
            elevations[outlet] = sea / 2;
            // A dry depression inside a flat catchment must drain without becoming a lake.
            elevations[15 * 32 + 14] = sea + .02f;
            var original = (float[])elevations.Clone();
            var world = new HeightmapResult(recipe, elevations);
            var water = WaterMap.Classify(world);
            var rivers = RiverGenerator.Generate(world);
            var repeat = RiverGenerator.Generate(world);
            Require(rivers.ChannelCells > 0, "A catchment with one water outlet should produce rivers.");
            Require(rivers.Directions.SequenceEqual(repeat.Directions)
                && rivers.Terrain.Elevations.SequenceEqual(repeat.Terrain.Elevations), "River generation must be deterministic.");
            Require(world.Elevations.SequenceEqual(original), "River generation modified the base world.");
            Require(rivers.Terrain.Elevations.Where((value, i) => value < original[i]).Any(), "River generation should carve terrain.");
            var composed = RiverMap.Compose(world, rivers.CutElevations, rivers.Directions,
                rivers.CatchmentPercent, rivers.CarveDepth);
            Require(composed.Terrain.Elevations.SequenceEqual(rivers.Terrain.Elevations), "Cut layer did not reconstruct the final terrain.");
            CheckRiverExport(rivers);
            var carvedWater = WaterMap.Classify(rivers.Terrain);
            for (var i = 0; i < elevations.Length; i++)
            {
                Require(carvedWater[i] == water[i], "River carving changed the OpenGS water classification.");
                Require(rivers.Terrain.Elevations[i] <= original[i], "Carving raised the terrain.");
                if (rivers.Directions[i] == 0) continue;

                var cell = i;
                var steps = 0;
                while (cell != outlet && steps++ < elevations.Length)
                {
                    var next = rivers.DownstreamIndex(cell);
                    Require(next >= 0, "River stopped before reaching its existing outlet.");
                    Require(rivers.Terrain.Elevations[next] <= rivers.Terrain.Elevations[cell], "A carved river flows uphill.");
                    cell = next;
                }
                Require(cell == outlet, "River flow contains a cycle or misses the outlet.");
            }

            try
            {
                RiverGenerator.Generate(world, progress: (fraction, _) => fraction < .92f);
                throw new Exception("Late river cancellation was ignored.");
            }
            catch (OperationCanceledException)
            {
                Require(world.Elevations.SequenceEqual(original), "Cancelling rivers modified the base world.");
            }
        }

        var dry = new HeightmapResult(recipe, Enumerable.Repeat(sea + .25f, 1024).ToArray());
        Require(RiverGenerator.Generate(dry).ChannelCells == 0, "A world without water cannot have an outlet.");
        var flooded = new HeightmapResult(recipe, new float[1024]);
        Require(RiverGenerator.Generate(flooded).ChannelCells == 0, "A world without land cannot have channels.");
        Reject(() => RiverGenerator.Generate(dry, carveDepth: float.NaN), "Invalid river depth accepted.");
    }

    private static void CheckRiverExport(RiverMap rivers)
    {
        using var original = new MemoryStream();
        using var cuts = new MemoryStream();
        using var final = new MemoryStream();
        HeightmapEncoding.WriteRaw16(rivers.Base, original);
        HeightmapEncoding.WriteRiverCuts(rivers, cuts);
        HeightmapEncoding.WriteRaw16(rivers.Terrain, final);
        Require(cuts.Length == rivers.Base.Elevations.Length * 4L, "Cut export must contain one float32 per base sample.");
        original.Position = cuts.Position = final.Position = 0;
        using var originalReader = new BinaryReader(original);
        using var cutReader = new BinaryReader(cuts);
        using var finalReader = new BinaryReader(final);
        for (var i = 0; i < rivers.Base.Elevations.Length; i++)
        {
            var baseSample = originalReader.ReadUInt16();
            var cut = cutReader.ReadSingle();
            Require(cut == rivers.CutElevations[i], "Exported cut is not the original float sample.");
            var composed = cut == 0f
                ? baseSample : (ushort)Math.Round(cut * 65535, MidpointRounding.AwayFromZero);
            Require(composed == finalReader.ReadUInt16(), "Exported layers did not reconstruct the final RAW16 sample.");
        }
    }

    private static void CheckWater()
    {
        var recipe = Small(); recipe.Width = 32; recipe.Height = 32;
        var heights = Enumerable.Repeat(.6f, 1024).ToArray();
        // Boundary-connected sea, an enclosed basin, a corner-connected sea, and a diagonal-only basin.
        heights[5 * 32] = .1f; heights[5 * 32 + 1] = .1f; heights[5 * 32 + 2] = .1f;
        heights[10 * 32 + 10] = .1f; heights[10 * 32 + 11] = .1f;
        heights[0] = .1f; heights[33] = .1f;
        heights[12 * 32 + 12] = recipe.SeaLevel;
        var world = new HeightmapResult(recipe, heights); var water = WaterMap.Classify(world);
        Require(water[5 * 32 + 2] == WaterKind.Ocean && water[0] == WaterKind.Ocean, "Boundary water must flood inland as ocean.");
        Require(water[10 * 32 + 10] == WaterKind.Lake && water[10 * 32 + 11] == WaterKind.Lake, "Enclosed water must be lake.");
        Require(water[33] == WaterKind.Lake, "Diagonal-only contact must not create an ocean passage.");
        Require(water[12 * 32 + 12] == WaterKind.Land, "Sea-level equality must agree with the generator's land rule.");
        Require(water.OceanCount == 4 && water.LakeCount == 3 && water.LandCount == 1017, "Classification coverage/counts incorrect.");
        Require(ReferenceEquals(water.Source, world) && heights[33] == .1f, "Classification must retain source identity without changing elevations.");
        heights[10 * 32] = .1f;
        for (var x = 1; x <= 10; x++) heights[10 * 32 + x] = .1f;
        var connected = WaterMap.Classify(world);
        Require(connected[10 * 32 + 11] == WaterKind.Ocean, "A cardinal channel must connect an inland basin to ocean.");
        Require(water[10 * 32 + 11] == WaterKind.Lake, "An existing classification must remain a completed snapshot.");
        var land = WaterMap.Classify(new HeightmapResult(recipe, Enumerable.Repeat(.6f, 1024).ToArray()));
        Require(land.LandCount == 1024 && land.OceanCount == 0 && land.LakeCount == 0, "All-land maps must work without a water queue.");
        var ocean = WaterMap.Classify(new HeightmapResult(recipe, new float[1024]));
        Require(ocean.OceanCount == 1024 && ocean.LakeCount == 0, "All-water maps must not overflow the queue at duplicate edges.");
        var rgb = OpenGsLandPalette.Color(WaterKind.Ocean);
        Require(rgb.R == 5 && rgb.G == 20 && rgb.B == 18, "Wrong OpenGS ocean bytes.");
        rgb = OpenGsLandPalette.Color(WaterKind.Lake);
        Require(rgb.R == 0 && rgb.G == 255 && rgb.B == 0, "Wrong OpenGS lake bytes.");
        rgb = OpenGsLandPalette.Color(WaterKind.Land);
        Require(rgb.R == 255 && rgb.G == 255 && rgb.B == 255, "Wrong OpenGS land bytes.");
        Reject(() => OpenGsLandPalette.Color((WaterKind)255), "Unknown water kinds must not become land.");
        Reject(() => WaterMap.Classify(new HeightmapResult(recipe, new float[100])), "Wrong raster size accepted.");
        var bad = Enumerable.Repeat(.6f, 1024).ToArray(); bad[50] = float.NaN;
        Reject(() => WaterMap.Classify(new HeightmapResult(recipe, bad)), "NaN water elevation accepted.");
    }
}
