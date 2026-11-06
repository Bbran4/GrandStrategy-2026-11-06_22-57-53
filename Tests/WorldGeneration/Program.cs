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
        Console.WriteLine($"PASS: {assertions} assertions; core determinism, snapshots, coverage, bounds, cancellation, progress, and RAW16 encoding.");
        if (args.Contains("--benchmark"))
        {
            foreach (var dimensions in new[] { (1024, 512), (2048, 1024), (3072, 2048) })
                for (var run = 0; run < 2; run++)
                {
                    var settings = HeightmapRecipe.OldWorld(); settings.Width = dimensions.Item1; settings.Height = dimensions.Item2;
                    var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var timer = Stopwatch.StartNew();
                    var world = HeightmapGenerator.Generate(settings); timer.Stop(); process.Refresh();
                    Console.WriteLine($"{settings.Width}x{settings.Height} {(run == 0 ? "first" : "repeat")} elapsed={timer.Elapsed.TotalSeconds:F3}s cpu={(process.TotalProcessorTime - cpu).TotalSeconds:F3}s processPeak={process.PeakWorkingSet64 / 1048576.0:F1}MiB land={world.LandFraction:P1} max={world.Maximum:F3}");
                    if (settings.Width == 1024 && run == 0 && args.Length > 1)
                    {
                        var directory = args.Last(); Directory.CreateDirectory(directory);
                        using (var stream = File.Create(Path.Combine(directory, "heightmap.raw"))) HeightmapEncoding.WriteRaw16(world, stream);
                    }
                }
        }
    }
}
