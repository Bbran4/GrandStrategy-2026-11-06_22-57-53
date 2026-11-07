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
        CheckRiverWidths();
        CheckNavalAccess();
        CheckClimate();
        CheckBiomes();
        CheckProvinceMap();
        CheckWorldRecipe();
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

    private static void CheckNavalAccess()
    {
        var recipe = Small();
        recipe.Width = 32;
        recipe.Height = 32;
        var sea = recipe.SeaLevel;
        var heights = Enumerable.Repeat(.7f, 1024).ToArray();
        var cuts = new float[heights.Length];
        var directions = new byte[heights.Length];
        var mouth = 33;
        var inland = 66;
        var upland = 99;
        var lake = 16 * 32 + 16;
        var lakeReach = lake - 1;
        heights[0] = heights[lake] = sea / 2;
        cuts[mouth] = sea + .01f;
        cuts[inland] = sea + .04f;
        cuts[upland] = sea + .14f;
        cuts[lakeReach] = sea + .01f;
        directions[mouth] = directions[inland] = directions[upland] = 6; // Southwest.
        directions[lakeReach] = 1; // East into the lake.
        var world = new HeightmapResult(recipe, heights);
        var river = RiverMap.Compose(world, cuts, directions,
            RiverGenerator.DefaultCatchmentPercent, RiverGenerator.DefaultCarveDepth);
        var water = WaterMap.Classify(river.Terrain);
        var access = RiverNavigation.Build(river, water, .08f);
        Require(access.ChannelCount == 2, "Only the low sea-connected reaches should admit ships.");
        Require((access[mouth] & RiverNavigation.Channel) != 0
            && (access[inland] & RiverNavigation.Channel) != 0, "The route must extend upstream from the mouth.");
        Require(access[32] == RiverNavigation.Connector && access[65] == RiverNavigation.Connector,
            "Diagonal reaches should have cardinal mask connections without adding movement nodes.");
        Require(access[lakeReach] == 0 && access[lake] == 0 && access[upland] == 0,
            "Lake routes and high riverbeds must stay out of the naval corridor.");
        var extended = RiverNavigation.Build(river, water, .16f);
        Require(extended.ChannelCount == 3 && (extended[upland] & RiverNavigation.Channel) != 0,
            "A higher rise limit should extend an existing ocean route.");
        Require(water[mouth] == WaterKind.Land && water[lake] == WaterKind.Lake,
            "Naval access must preserve the source geography.");

        var widths = new byte[directions.Length];
        widths[mouth] = widths[inland] = widths[upland] = widths[lakeReach] = 4;
        var wideRiver = RiverMap.Compose(world, cuts, directions,
            RiverGenerator.DefaultCatchmentPercent, RiverGenerator.DefaultCarveDepth, widths);
        var wideAccess = RiverNavigation.Build(wideRiver, WaterMap.Classify(wideRiver.Terrain), .08f);
        var waterSurface = wideRiver.BuildSurfaceMask();
        var bankwardWater = 2 * recipe.Width + 4;
        var uplandOnlyWater = 3 * recipe.Width + 5;
        Require(wideAccess.ChannelCount == 2, "Wider water must keep the same two route nodes.");
        Require(waterSurface[bankwardWater] != 0 && wideAccess[bankwardWater] == RiverNavigation.WaterFootprint,
            "Naval corridors must include the full width of their river water.");
        Require(waterSurface[uplandOnlyWater] != 0 && wideAccess[uplandOnlyWater] == 0,
            "A wide upland river must not gain naval access.");
        Require(wideAccess[lake] == 0, "A wide naval mask must preserve lakes.");
        for (var i = 0; i < waterSurface.Length; i++)
            Require((wideAccess[i] & RiverNavigation.WaterFootprint) == 0 || waterSurface[i] != 0,
                "Naval river width must follow the shared river surface.");
    }

    private static void CheckRiverWidths()
    {
        var recipe = Small();
        recipe.Width = 256;
        recipe.Height = 128;
        var heights = Enumerable.Repeat(recipe.SeaLevel + .25f, recipe.Width * recipe.Height).ToArray();
        heights[0] = recipe.SeaLevel / 2;
        var world = new HeightmapResult(recipe, heights);
        var narrow = RiverGenerator.Generate(world, widthScale: RiverGenerator.MinimumWidthScale);
        var wide = RiverGenerator.Generate(world, widthScale: RiverGenerator.MaximumWidthScale);
        Require(wide.Directions.SequenceEqual(narrow.Directions), "Width must preserve the drainage routes.");
        Require(wide.Widths.Max() > wide.Widths.Where(width => width > 0).Min(),
            "The river mouth should be wider than headwaters.");
        for (var i = 0; i < wide.Directions.Length; i++)
        {
            if (wide.Directions[i] == 0) continue;
            var downstream = wide.DownstreamIndex(i);
            if (wide.Directions[downstream] != 0)
                Require(wide.Widths[downstream] >= wide.Widths[i], "A river narrowed downstream.");
        }
        Require(wide.BuildSurfaceMask().Count(cell => cell != 0) > narrow.BuildSurfaceMask().Count(cell => cell != 0),
            "The width control should widen the visible river surface.");
        Require(wide.CutElevations.Count(cut => cut != 0) > narrow.CutElevations.Count(cut => cut != 0),
            "The width control should widen the carved channel.");
        Require(wide.BuildSurfaceMask()[0] == 0, "The river surface must preserve the ocean at its outlet.");
    }

    private static void CheckClimate()
    {
        var recipe = Small();
        recipe.Width = 32;
        recipe.Height = 32;
        var sea = recipe.SeaLevel;
        var heights = new float[1024];
        for (var y = 0; y < recipe.Height; y++)
            for (var x = 0; x < recipe.Width; x++)
                heights[y * recipe.Width + x] = x == 0 || x == 31 ? sea / 2 : x == 15 ? .90f : sea + .04f;
        var source = new HeightmapResult(recipe, heights);
        var water = WaterMap.Classify(source);
        var settings = new ClimateSettings { MoistureReach = .7f };
        var westWind = ClimateGenerator.Generate(source, water, settings);
        var warm = 5 * 32 + 8;
        var north = 28 * 32 + 8;
        var mountain = 5 * 32 + 15;
        var leeward = 5 * 32 + 23;
        Require(westWind.Temperature[warm] > westWind.Temperature[north], "Latitude should cool the far north.");
        Require(westWind.Temperature[warm] > westWind.Temperature[mountain], "Mountains should be cooler than lowlands at the same latitude.");
        Require(westWind.Moisture[warm] > westWind.Moisture[leeward], "The mountain should create a dry leeward side.");
        settings.Wind = PrevailingWind.EastToWest;
        var eastWind = ClimateGenerator.Generate(source, water, settings);
        Require(eastWind.Moisture[warm] < eastWind.Moisture[leeward], "Reversing wind should reverse the rain shadow.");
        Require(westWind.Temperature.SequenceEqual(eastWind.Temperature), "Wind should leave temperature unchanged.");
        Require(westWind.Settings.Wind == PrevailingWind.WestToEast, "Editing climate settings changed a completed snapshot.");
        var repeat = ClimateGenerator.Generate(source, water, westWind.Settings);
        Require(westWind.Temperature.SequenceEqual(repeat.Temperature) && westWind.Moisture.SequenceEqual(repeat.Moisture),
            "Climate should reproduce exactly from terrain and settings.");

        try
        {
            ClimateGenerator.Generate(source, water, settings, (fraction, _) => fraction < .75f);
            throw new Exception("Late climate cancellation was ignored.");
        }
        catch (OperationCanceledException)
        {
            Require(westWind.Moisture.SequenceEqual(repeat.Moisture), "Cancelling a new climate changed the previous result.");
        }

        settings.Equator = float.NaN;
        Reject(() => ClimateGenerator.Generate(source, water, settings), "Invalid climate settings accepted.");
    }

    private static void CheckBiomes()
    {
        var recipe = Small();
        recipe.Width = 32;
        recipe.Height = 32;
        var sea = recipe.SeaLevel;
        var heights = Enumerable.Repeat(sea + .04f, 1024).ToArray();
        var temperature = Enumerable.Repeat((byte)180, heights.Length).ToArray();
        var moisture = Enumerable.Repeat((byte)100, heights.Length).ToArray();
        const int riverCell = 32;
        const int lake = 20 * 32 + 20;
        const int wetland = lake - 1;
        heights[0] = 0;
        heights[1] = sea * .85f;
        heights[lake] = sea / 2;
        moisture[wetland] = 155;
        moisture[100] = 30;
        temperature[101] = 98;
        temperature[102] = 130;
        moisture[102] = 160;
        temperature[103] = 210;
        moisture[103] = 190;
        moisture[104] = 160;
        heights[105] = .9f;
        temperature[105] = 100;

        var cuts = new float[heights.Length];
        var directions = new byte[heights.Length];
        cuts[riverCell] = sea + .01f;
        directions[riverCell] = 7;
        var rivers = RiverMap.Compose(new HeightmapResult(recipe, heights), cuts, directions,
            RiverGenerator.DefaultCatchmentPercent, RiverGenerator.DefaultCarveDepth);
        var water = WaterMap.Classify(rivers.Terrain);
        var climate = new ClimateMap(rivers.Terrain, new ClimateSettings(), temperature, moisture);
        var map = BiomeGenerator.Generate(climate, water, rivers);
        Require(map[0] == BiomeKind.DeepOcean && map[1] == BiomeKind.ShallowSea && map[lake] == BiomeKind.Lake,
            "The biome layer must retain the existing water classes.");
        Require(map[riverCell] == BiomeKind.River && map[wetland] == BiomeKind.Wetland,
            "River water and freshwater wetlands should be represented.");
        Require(map[100] == BiomeKind.Desert && map[101] == BiomeKind.Tundra
            && map[102] == BiomeKind.BorealForest && map[103] == BiomeKind.TropicalForest
            && map[104] == BiomeKind.TemperateForest && map[105] == BiomeKind.Alpine,
            "Temperature, moisture, and relief must select the intended land biomes.");

        RequireRgb(map.OpenGsTerrainColour(0), 2, 38, 150, "deep ocean");
        RequireRgb(map.OpenGsTerrainColour(lake, navigableRiver: true), 58, 91, 255, "lake priority");
        RequireRgb(map.OpenGsTerrainColour(riverCell), 76, 96, 35, "land-mask river");
        RequireRgb(map.OpenGsTerrainColour(riverCell, navigableRiver: true), 56, 118, 217, "naval-mask river");
        RequireRgb(map.OpenGsTerrainColour(102), 89, 199, 85, "boreal forest");
        RequireRgb(map.OpenGsTerrainColour(103), 127, 191, 0, "jungle");
        RequireRgb(map.OpenGsTerrainColour(105), 157, 192, 208, "mountain");
        Require(map.Samples.SequenceEqual(BiomeGenerator.Generate(climate, water, rivers).Samples),
            "Biome generation should reproduce the same raster.");
    }

    private static void RequireRgb(Rgb24 colour, byte r, byte g, byte b, string name)
    {
        Require(colour.R == r && colour.G == g && colour.B == b, $"Wrong OpenGS Terrain colour for {name}.");
    }

    private static void CheckProvinceMap()
    {
        var recipe = Small();
        recipe.Width = 32;
        recipe.Height = 32;
        var provinces = new[]
        {
            new ProvinceInfo { Id = "PRV000014", Rgb = 0x112233, Type = "land", Terrain = "forest", TerritoryId = "TRT000002" },
            new ProvinceInfo { Id = "PRV000007", Rgb = 0x223344, Type = "ocean" },
            new ProvinceInfo { Id = "PRV000021", Rgb = 0x334455, Type = "land" }
        };
        var colors = new int[1024];
        colors[0] = provinces[0].Rgb;
        colors[1] = provinces[1].Rgb;
        colors[34] = provinces[2].Rgb; // Diagonal to the ocean cell.
        var biomes = new byte[1024];
        biomes[0] = (byte)BiomeKind.TemperateForest;
        var rivers = new byte[1024];
        rivers[0] = 1;
        var water = new WaterKind[1024];
        water[1] = WaterKind.Ocean;
        var territories = new[]
        {
            new TerritoryInfo { Id = "TRT000002", Rgb = 0x456789, Type = "land" },
            new TerritoryInfo { Id = "TRT000001", Rgb = 0x987654, Type = "ocean" }
        };
        var territoryColors = new int[1024];
        territoryColors[0] = territoryColors[34] = territories[0].Rgb;
        territoryColors[1] = territories[1].Rgb;
        var territoryAt = ProvinceMap.IndexTerritoryColors(territoryColors, territories, recipe.Width);
        var map = ProvinceMap.Build(recipe, (int[])colors.Clone(), provinces, territories,
            biomes: biomes, rivers: rivers, water: water, territoryAt: territoryAt);
        Require(map.HasTerritoryPreview && map.TerritoryAt[0] == 0 && map.TerritoryAt[34] == 0
            && map.TerritoryAt[1] == 1 && map.TerritoryAt[33] == -1,
            "The imported territory image should retain its own region layout.");
        Require(map.Territories[map.TerritoryAt[0]].Id == map.Provinces[0].TerritoryId,
            "Territory selection and history should resolve to the original ID.");
        Require(map.Provinces[0].Id == "PRV000014" && map.Provinces[0].TerritoryId == "TRT000002",
            "Province identity and territory membership must survive indexing.");
        Require(map.ProvinceAt[0] == 0 && map.ProvinceAt[1] == 1 && map.ProvinceAt[34] == 2
            && map.ProvinceAt[33] == -1 && map.UnassignedPixels == 1021, "RGB-to-province indexing changed the raster.");
        Require(map.Provinces[0].Neighbours.SequenceEqual(new[] { 1 })
            && map.Provinces[1].Neighbours.SequenceEqual(new[] { 0 })
            && map.Provinces[2].Neighbours.Length == 0, "Neighbours must share an edge.");
        Require(map.Provinces[0].TouchesOpenSea && map.Provinces[0].RiverSamples == 1
            && map.Provinces[0].BiomeCounts[(int)BiomeKind.TemperateForest] == 1, "Geographic summaries are misaligned.");
        // The water is assigned to a naval province, leaving the land province on the bank.
        var navalColors = new int[1024];
        var centre = 8 * recipe.Width + 7;
        navalColors[centre] = navalColors[centre + 1] = provinces[1].Rgb;
        navalColors[centre + 2] = provinces[0].Rgb;
        navalColors[700] = provinces[2].Rgb;
        var navalWater = new WaterKind[1024];
        navalWater[centre - 1] = WaterKind.Ocean;
        var navalDirections = new byte[1024];
        navalDirections[centre] = 5;
        var widths = new byte[1024];
        widths[centre] = 2;
        var naval = ProvinceMap.Build(recipe, navalColors, provinces, rivers: navalDirections, water: navalWater, riverWidths: widths);
        Require(naval.Provinces[0].RiverSamples == 0 && naval.Provinces[0].RiverWaterSamples == 0
            && naval.Provinces[0].RiverBankEdges == 1, "A province beside a wide naval river should report its bank.");
        Require(naval.Provinces[1].RiverSamples == 1 && naval.Provinces[1].RiverWaterSamples == 2,
            "The naval province should report the river surface inside it.");

        colors[10] = 0xabcdef;
        Reject(() => ProvinceMap.Build(recipe, colors, provinces), "Unknown province RGB accepted.");

        using var raw = new MemoryStream();
        var heights = Enumerable.Repeat(.25f, 1024).ToArray();
        heights[0] = 0f;
        heights[992] = 1f;
        HeightmapEncoding.WriteRaw16(new HeightmapResult(recipe, heights), raw);
        raw.Position = 0;
        var terrain = HeightmapEncoding.ReadRaw16(recipe, raw);
        Require(terrain.Elevations[0] == 0f && terrain.Elevations[992] == 1f
            && Math.Abs(terrain.Elevations[1] - .25f) <= 1f / 65535f, "RAW16 import changed orientation or precision.");
    }

    private static void CheckWorldRecipe()
    {
        var settings = Small();
        settings.SeaLevel = .28f;
        settings.CoastWarp = .10f;
        settings.Steps[0].Enabled = false;
        var recipe = new WorldRecipe
        {
            Format = WorldRecipe.FormatId,
            Version = WorldRecipe.CurrentVersion,
            Recipe = settings,
            HeightScale = .19f,
            BuildRivers = true,
            RiverCatchment = RiverGenerator.MinimumCatchmentPercent,
            RiverDepth = .02f,
            RiverWidthScale = 1.5f,
            BuildClimate = true,
            Climate = new ClimateSettings { Equator = .32f, Wind = PrevailingWind.NorthToSouth },
            BuildBiomes = true,
            MaximumNavalRise = .12f,
            ExportNavalMask = true
        };

        var baseTerrain = HeightmapGenerator.Generate(settings);
        var expectedRivers = RiverGenerator.Generate(baseTerrain, recipe.RiverCatchment, recipe.RiverDepth,
            widthScale: recipe.RiverWidthScale);
        var rebuilt = recipe.Rebuild();
        Require(rebuilt.Terrain.Elevations.SequenceEqual(expectedRivers.Terrain.Elevations),
            "A full recipe must apply its terrain and river controls in order.");
        if (expectedRivers.ChannelCells > 0)
            Require(rebuilt.Rivers.WidthScale == 1.5f
                && rebuilt.Rivers.Widths.SequenceEqual(expectedRivers.Widths), "River widths must reproduce.");
        Require(ReferenceEquals(rebuilt.Climate.Source, rebuilt.Terrain)
            && rebuilt.Climate.Settings.Equator == .32f && rebuilt.Climate.Settings.Wind == PrevailingWind.NorthToSouth,
            "Climate must apply its saved settings to the rebuilt final terrain.");
        Require(ReferenceEquals(rebuilt.Biomes.Climate, rebuilt.Climate), "Biomes must use the rebuilt climate.");

        var legacy = new WorldRecipe { Format = WorldRecipe.FormatId, Recipe = settings }.Rebuild();
        Require(legacy.Rivers == null && legacy.Climate == null && legacy.Biomes == null
            && legacy.Terrain.Elevations.SequenceEqual(baseTerrain.Elevations), "Legacy recipes must restore their terrain.");
        recipe.BuildClimate = false;
        Reject(() => recipe.Rebuild(), "A biome recipe without climate was accepted.");
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
        rgb = OpenGsLandPalette.Color(WaterKind.Land, riverWater: true);
        Require(rgb.R == 5 && rgb.G == 20 && rgb.B == 18, "River water must use the ocean RGB.");
        rgb = OpenGsLandPalette.Color(WaterKind.Lake, riverWater: true);
        Require(rgb.R == 0 && rgb.G == 255 && rgb.B == 0, "Lake RGB must take priority over a river marker.");
        Reject(() => OpenGsLandPalette.Color((WaterKind)255), "Unknown water kinds must not become land.");
        Reject(() => WaterMap.Classify(new HeightmapResult(recipe, new float[100])), "Wrong raster size accepted.");
        var bad = Enumerable.Repeat(.6f, 1024).ToArray(); bad[50] = float.NaN;
        Reject(() => WaterMap.Classify(new HeightmapResult(recipe, bad)), "NaN water elevation accepted.");
    }
}
