// Runs the standalone world-generation and campaign checks without opening Unity.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    // Generate deterministic fixtures and run focused checks for terrain, exports, geography and the
    // opening campaign.
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
        CheckCampaign();
        CheckSettlementGeography();
        CheckBorderPaths();
        CheckProvinceShape();
        CheckNature();
        Console.WriteLine($"PASS: {assertions} assertions; core determinism, snapshots, coverage, bounds, cancellation, progress, RAW16, water topology, OpenGS palette, river routing and carving, campaign integration and restore, and settlement opening.");
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
        var flow = wideRiver.BuildSurfaceDirections();
        Require(flow.Select(direction => direction != 0).SequenceEqual(waterSurface.Select(mask => mask != 0)),
            "Flow coverage must exactly match the river footprint.");
        Require(flow.All(direction => direction <= 8)
            && Enumerable.Range(0, flow.Length).All(i => wideRiver.Directions[i] == 0
                || waterSurface[i] == 0 || flow[i] == wideRiver.Directions[i]),
            "River flow must retain the generated downstream direction at channel centres.");
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

    // Exercise the campaign through founding, development, expansion and save restoration.
    private static void CheckCampaign()
    {
        var recipe = Small();
        var regions = new[]
        {
            new ProvinceInfo { Id = "Capital", Rgb = 1, Type = "land", Terrain = "mountains" },
            new ProvinceInfo { Id = "Neighbour", Rgb = 2, Type = "land", Terrain = "mountains" },
            new ProvinceInfo { Id = "Beyond", Rgb = 3, Type = "land" },
            new ProvinceInfo { Id = "Sea", Rgb = 4, Type = "ocean" }
        };
        var raster = new int[recipe.Width * recipe.Height];
        raster[0] = 1;
        raster[1] = 2;
        raster[2] = 3;
        raster[recipe.Width] = 4;
        var biomes = new byte[raster.Length];
        biomes[0] = (byte)BiomeKind.TemperateForest;
        var map = ProvinceMap.Build(recipe, raster, regions, biomes: biomes);
        var key = CampaignState.MapKey(map, recipe.Seed);
        Require(key != CampaignState.MapKey(map, recipe.Seed + 1), "Save keys should distinguish world seeds.");
        Reject(() => CampaignState.Found(map, key, recipe.Width, "Tribe", 0), "A water province was made capital.");
        var campaign = CampaignState.Found(map, key, 0, "  Tribe  ", 1);
        Require(campaign.Paused && campaign.Day == 1 && campaign.Capital == 0 && campaign.Owns(0)
            && !campaign.Owns(1) && campaign.Population(1) > 0 && campaign.Population(3) == 0,
            "Founding must establish a capital and leave independent populations on land.");
        Require(!campaign.TryStartIntegration(2) && !campaign.TryStartIntegration(3)
            && campaign.Coin == CampaignState.StartingCoin, "Unreachable or water provinces must not spend coin.");
        Require(campaign.TryStartIntegration(1) && !campaign.TryStartIntegration(1)
            && campaign.Coin == CampaignState.StartingCoin - CampaignState.IntegrationCost,
            "Starting integration should charge once.");
        Require(!campaign.Advance(60d) && campaign.Day == 1
            && campaign.IntegrationDaysLeft == CampaignState.IntegrationDuration, "Pause must freeze time and integration.");
        campaign.SetSpeed(4);
        campaign.Paused = false;
        Require(!campaign.Advance(.125d) && campaign.Day == 1, "Sub-day updates must accumulate.");
        Require(!campaign.Advance(.375d) && campaign.Day == 3
            && campaign.IntegrationDaysLeft == CampaignState.IntegrationDuration - 2, "Speed must advance daily progress.");

        var json = new JsonSerializerOptions { IncludeFields = true };
        var save = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(campaign.ToSave(), json), json);
        Reject(() => CampaignState.Restore(map, "other map", save, 5), "Accepted a save from another map.");
        var restored = CampaignState.Restore(map, key, save, 5);
        Require(restored.Paused && restored.TribeName == "Tribe" && restored.Colour == 1 && restored.CapitalCell == 0
            && restored.Speed == 4 && restored.Coin == campaign.Coin && restored.Population(1) == campaign.Population(1)
            && restored.Day == campaign.Day && restored.IntegrationDaysLeft == campaign.IntegrationDaysLeft
            && restored.IntegrationTarget == 1 && !restored.Owns(1), "Restore must preserve the campaign and pause.");
        restored.Paused = false;
        Require(restored.Advance(restored.IntegrationDaysLeft / 4d) && restored.Owns(1) && restored.Owns(0)
            && restored.IntegrationTarget == -1 && restored.Day == CampaignState.IntegrationDuration + 1,
            "Integration must transfer ownership on the scheduled day.");
        Require(restored.TryStartIntegration(2), "A new border must open further integration.");

        var fordTerrain = Enumerable.Repeat(1f, raster.Length).ToArray();
        var fordRivers = new byte[raster.Length];
        var fordCell = recipe.Width + 1;
        fordRivers[fordCell] = 1;
        var fordMap = ProvinceMap.Build(recipe, Enumerable.Repeat(1, raster.Length).ToArray(),
            new[] { new ProvinceInfo { Id = "Banks", Rgb = 1, Type = "land" } });
        fordMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, fordTerrain)), fordRivers, fordTerrain);
        var localStart = CampaignState.Found(fordMap, "local-start", fordCell - 1, "Local start", 0, localCapitalClaim: true);
        var originalIndependents = localStart.IndependentPopulation(localStart.Capital);
        Require(!localStart.Owns(localStart.Capital) && originalIndependents > 0 && localStart.OwnedPopulation == 100
            && localStart.TryRecruitWarband() && localStart.OwnedPopulation == 100 - CampaignState.WarbandRecruits
            && localStart.IndependentPopulation(localStart.Capital) == originalIndependents,
            "A local start must leave the region independent and recruit only from the capital.");
        var localStartSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(localStart.ToSave(), json), json);
        localStart = CampaignState.Restore(fordMap, "local-start", localStartSave, 5);
        Require(!localStart.Owns(localStart.Capital) && localStart.TryStartIntegration(localStart.Capital)
            && localStart.AdvanceToNextDecision() && !localStart.Owns(localStart.Capital)
            && localStart.IndependentPopulation(localStart.Capital) == 0
            && localStart.SettlementPopulation(localStart.CapitalCell) == localStart.Population(localStart.Capital),
            "Integration must add residents to a local capital without granting the region.");

        var riverClan = CampaignState.Found(map, key, 0, "River Clan", 0, localCapitalClaim: true, tradition: ClanTradition.River);
        var standardClan = CampaignState.Found(map, key, 0, "Standard Clan", 0, localCapitalClaim: true);
        Require(riverClan.Coin == standardClan.Coin - 20 && riverClan.Wood == standardClan.Wood + 10
            && riverClan.BuildingInfo(0, SettlementBuilding.Docks).Wood
                == standardClan.BuildingInfo(0, SettlementBuilding.Docks).Wood - 10,
            "River clans must trade starting coin for timber and cheaper docks.");
        Require(CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(riverClan.ToSave(), json), json), 5).Tradition == ClanTradition.River,
            "Clan tradition must survive save and load.");

        var highland = CampaignState.Found(map, key, 0, "Highland Clan", 0,
            localCapitalClaim: true, tradition: ClanTradition.Highland);
        var highlandCoin = highland.Coin;
        var highlandWood = highland.Wood;
        Require(highlandCoin == standardClan.Coin + 20 && highlandWood == standardClan.Wood - 10
            && highland.TryRecruitWarband() && highland.Coin == highlandCoin - CampaignState.WarbandCoin + 10
            && highland.Wood == highlandWood - CampaignState.WarbandWood + 10
            && highland.SettlementPopulation(0) == 100 - CampaignState.WarbandRecruits,
            "Highland clans must trade starting wood for coin and cheaper recruitment without reducing population cost.");

        var frontierClaim = CampaignState.Found(map, key, 0, "Claims", 0, localCapitalClaim: true);
        frontierClaim.IndependentTerritoryResolver = cell => cell == 1;
        Require(!frontierClaim.TryFoundSettlement(1) && frontierClaim.Coin == CampaignState.StartingCoin
            && frontierClaim.TryFoundSettlement(2), "Independent claims must prevent founding without charging resources.");

        var localIntegration = CampaignState.Found(map, key, 0, "Local integration", 0, localCapitalClaim: true);
        Require(!localIntegration.TryStartIntegration(1)
            && localIntegration.TryStartIntegration(1, ExpansionApproach.Diplomacy, settlementCell: 1),
            "A community without a clan settlement needs an integration site.");
        localIntegration = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(localIntegration.ToSave(), json), json), 5);
        Require(localIntegration.AdvanceToNextDecision() && !localIntegration.Owns(1)
            && localIntegration.SettlementCells.Contains(1) && localIntegration.IndependentPopulation(1) == 0
            && localIntegration.NegotiatedIntegrations == 1,
            "Completing a restored integration must create a settlement without granting the region.");

        var authoredCampaign = CampaignState.Found(map, key, 0, "Older campaign", 0);
        Require(authoredCampaign.TryStartIntegration(1) && authoredCampaign.AdvanceToNextDecision()
            && authoredCampaign.Owns(1) && authoredCampaign.TryStartIntegration(2),
            "The older campaign fixture needs an authored holding and a second pending integration.");
        var authoredPending = JsonSerializer.Serialize(authoredCampaign.ToSave(), json);
        var migratedClaims = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(authoredPending, json), 5);
        var pendingDays = migratedClaims.IntegrationDaysLeft;
        var paidBalance = migratedClaims.Coin;
        migratedClaims.UseSettlementClaims();
        Require(migratedClaims.Owns(0) && migratedClaims.Owns(1) && !migratedClaims.Owns(2)
            && migratedClaims.IntegrationNeedsSettlement && migratedClaims.Coin == paidBalance
            && migratedClaims.DaysToNextDecision == CampaignState.WinterStart - migratedClaims.Day,
            "Migration must preserve past holdings and payments and wait for a viable settlement.");
        migratedClaims.Paused = false;
        migratedClaims.Advance(4);
        Require(migratedClaims.Day == authoredCampaign.Day + 4
            && migratedClaims.IntegrationDaysLeft == pendingDays && !migratedClaims.Owns(2),
            "A pending integration without a settlement must wait while the calendar continues.");
        migratedClaims = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(migratedClaims.ToSave(), json), json), 5);
        Require(migratedClaims.IntegrationNeedsSettlement
            && !migratedClaims.TrySetIntegrationSettlement(1)
            && !migratedClaims.TrySetIntegrationSettlement(2, provinceRoot: 42)
            && migratedClaims.TrySetIntegrationSettlement(2, provinceRoot: migratedClaims.CapitalCell)
            && !migratedClaims.TrySetIntegrationSettlement(2)
            && migratedClaims.Coin == paidBalance,
            "The restored order must accept one valid site and keep its original payment.");
        Require(migratedClaims.AdvanceToNextDecision() && migratedClaims.SettlementCells.Contains(2)
            && migratedClaims.SettlementProvinceRoots.Single() == migratedClaims.CapitalCell
            && migratedClaims.Owns(1) && !migratedClaims.Owns(2),
            "Completing the migrated integration must join the settlement's claim and preserve the old holding.");

        var autoClaims = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(authoredPending, json), 5);
        autoClaims.IndependentVillageSiteResolver = region => region == 2 ? 2 : -1;
        autoClaims.UseSettlementClaims();
        Require(!autoClaims.IntegrationNeedsSettlement && autoClaims.IntegrationSettlementCell == 2
            && autoClaims.IntegrationDaysLeft == pendingDays && autoClaims.AdvanceToNextDecision()
            && autoClaims.SettlementCells.Contains(2) && !autoClaims.Owns(2),
            "Loading with a viable settlement site must resume a paid integration automatically.");
        var futureClaims = CampaignState.Found(map, key, 0, "Older start", 0);
        futureClaims.UseSettlementClaims();
        Require(futureClaims.Owns(0) && !futureClaims.TryStartIntegration(1)
            && futureClaims.TryStartIntegration(1, settlementCell: 1)
            && futureClaims.AdvanceToNextDecision() && !futureClaims.Owns(1),
            "New integrations in an upgraded campaign must require a settlement and claim only through it.");

        var independent = CampaignState.Found(map, key, 0, "Independent", 0, localCapitalClaim: true);
        independent.IndependentVillageSiteResolver = region => region == 1 ? 1 : -1;
        independent.IndependentFarmSiteResolver = cell => cell;
        independent.Paused = false;
        independent.Advance(14);
        var villagePopulation = independent.IndependentPopulation(1);
        var foodOffer = new TradeOffer(TradeResource.Coin, 1, TradeResource.Food, 1);
        var openingStock = independent.EvaluateTrade(1, foodOffer).Stock;
        Require(independent.IndependentVillageCell == 1 && !independent.TryFoundSettlement(1),
            "An independent community must establish its own village and reserve the site.");
        independent.Advance(15);
        Require(independent.IndependentPopulation(1) > villagePopulation && independent.IndependentFarmCell == 1
            && independent.EvaluateTrade(1, foodOffer).Stock > openingStock,
            "The independent village must grow and develop a larger food market when it builds a farm.");
        independent.Advance(15);
        Require(independent.IndependentVillageGuarded, "A mature independent village must raise local guards.");
        independent = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(independent.ToSave(), json), json), 5);
        Require(independent.IndependentVillageCell == 1 && independent.TryStartIntegration(1)
            && independent.AdvanceToNextDecision() && independent.IndependentVillageCell == -1
            && independent.SettlementCells.Contains(1) && independent.IndependentPopulation(1) == 0
            && independent.HasSettlementBuilding(1, SettlementBuilding.Farm)
            && independent.BuildingPlacement(1, SettlementBuilding.Farm) == 1,
            "Integration must bring the existing independent village and its farm into the clan at their saved locations.");

        var occupied = CampaignState.Found(map, key, 0, "Occupation", 0, localCapitalClaim: true);
        occupied.IndependentVillageSiteResolver = region => region == 1 ? 1 : -1;
        occupied.MilitaryOccupationResolver = cell => cell == 1;
        Require(occupied.TryRecruitWarband() && occupied.TryMarchWarband(new List<int> { 0, 1 }),
            "The occupation fixture requires a warband in the target region.");
        occupied.Paused = false;
        occupied.Advance(27);
        var beforeOccupation = occupied.IndependentPopulation(1);
        Require(occupied.TryStartIntegration(1, ExpansionApproach.Force) && occupied.IndependentVillageOccupied,
            "The independent village must detect occupation in the target region.");
        var operationSave = JsonSerializer.Serialize(occupied.ToSave(), json);
        var missed = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(operationSave, json), 5);
        missed.MilitaryOccupationResolver = _ => false;
        missed.Paused = false;
        Require(!missed.Advance(10) && missed.OperationMissedObjective && !missed.WarbandCommitted
            && missed.IndependentVillageCell == 1 && missed.SuccessfulOperations == 0 && !missed.SettlementCells.Contains(1),
            "An operation must expire without capturing a village it never occupies.");
        var captured = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(operationSave, json), 5);
        captured.MilitaryOccupationResolver = cell => cell == 1;
        captured.Paused = false;
        Require(captured.Advance(10) && !captured.OperationMissedObjective
            && captured.IndependentVillageCell == -1 && captured.SettlementCells.Contains(1)
            && captured.SuccessfulOperations == 1, "Occupying the objective must allow capture of the village.");
        var guardedSave = JsonSerializer.Deserialize<CampaignSave>(operationSave, json);
        guardedSave.IndependentVillageGuarded = true;
        var guarded = CampaignState.Restore(map, key, guardedSave, 5);
        var operationsStart = guarded.Day;
        guarded.MilitaryOccupationResolver = _ => guarded.Day >= operationsStart + 9;
        guarded.Paused = false;
        Require(!guarded.Advance(10) && guarded.OperationMissedObjective,
            "Two days of occupation must not capture a guarded village.");
        guardedSave = JsonSerializer.Deserialize<CampaignSave>(operationSave, json);
        guardedSave.IndependentVillageGuarded = true;
        guarded = CampaignState.Restore(map, key, guardedSave, 5);
        guarded.MilitaryOccupationResolver = _ => guarded.Day >= operationsStart + 8;
        guarded.Paused = false;
        guarded.Advance(9);
        Require(guarded.ObjectiveOccupiedDays == 2 && guarded.ObjectiveHoldDaysRequired == 3,
            "Guarded objectives must record the number of consecutive held days.");
        guarded = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(guarded.ToSave(), json), json), 5);
        guarded.MilitaryOccupationResolver = _ => true;
        guarded.Paused = false;
        Require(guarded.Advance(1) && guarded.SettlementCells.Contains(1),
            "A third consecutive day of restored occupation must capture the guarded village.");
        occupied.Advance(2);
        Require(occupied.IndependentPopulation(1) == beforeOccupation && occupied.TryWithdrawWarband()
            && !occupied.IndependentVillageOccupied, "Occupation must suspend village growth and end on withdrawal.");
        occupied.Advance(15);
        Require(occupied.IndependentPopulation(1) > beforeOccupation, "Growth must resume after the occupation lifts.");

        var manoeuvreRaster = new int[raster.Length];
        manoeuvreRaster[0] = 1;
        manoeuvreRaster[1] = 2;
        manoeuvreRaster[recipe.Width] = 4;
        manoeuvreRaster[2] = 2;
        manoeuvreRaster[3] = 3;
        var manoeuvreMap = ProvinceMap.Build(recipe, manoeuvreRaster, regions, biomes: biomes);
        var manoeuvre = CampaignState.Found(manoeuvreMap, "manoeuvre", 0, "Manoeuvre", 0);
        Require(manoeuvre.TryRecruitWarband() && manoeuvre.TryMarchWarband(new List<int> { 0, 1 }),
            "The repositioning fixture needs a warband in the target region.");
        manoeuvre.AdvanceToNextDecision();
        Require(manoeuvre.TryStartIntegration(1, ExpansionApproach.Force) && !manoeuvre.TryRepositionWarband(0)
            && !manoeuvre.TryMarchWarband(new List<int> { 1, 2 }) && manoeuvre.TryRepositionWarband(2),
            "A committed warband may reposition within its target region, but may not leave without withdrawing.");
        manoeuvre = CampaignState.Restore(manoeuvreMap, "manoeuvre", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(manoeuvre.ToSave(), json), json), 5);
        manoeuvre.AdvanceToNextDecision();
        Require(manoeuvre.WarbandCell == 2 && manoeuvre.WarbandCommitted && manoeuvre.IntegrationDaysLeft == 9,
            "Repositioning must survive a save and use the existing operation clock.");

        var fordCampaign = CampaignState.Found(fordMap, "ford", fordCell - 1, "Ford", 0);
        Require(fordMap.IsFord(fordCell) && !fordCampaign.TrySecureFord(fordCell)
            && fordCampaign.TryRecruitWarband() && fordCampaign.TrySecureFord(fordCell)
            && !fordCampaign.TrySecureFord(fordCell), "A stationary warband must secure an adjacent ford only once.");
        var fordSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(fordCampaign.ToSave(), json), json);
        Require(CampaignState.Restore(fordMap, "ford", fordSave, 5).IsFordSecured(fordCell),
            "Secured fords must survive save and load.");
        Require(fordCampaign.TryChangeFordAccess(fordCell) && fordCampaign.IsFordClosed(fordCell)
            && fordCampaign.IsFordSecured(fordCell), "Closing a crossing must preserve its clan claim.");
        var closureSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(fordCampaign.ToSave(), json), json);
        fordCampaign = CampaignState.Restore(fordMap, "ford", closureSave, 5);
        Require(fordCampaign.IsFordClosed(fordCell) && fordCampaign.TryChangeFordAccess(fordCell)
            && !fordCampaign.IsFordClosed(fordCell), "A ford closure must survive reload and be reversible by its guard.");

        var garrisonSave = CampaignState.Found(fordMap, "garrison", fordCell - 1, "Garrison", 0).ToSave();
        garrisonSave.SecuredFords.Add(fordCell);
        var garrison = CampaignState.Restore(fordMap, "garrison", garrisonSave, 5);
        var beforeGuards = garrison.OwnedPopulation;
        Require(garrison.TryRaiseGarrison(garrison.CapitalCell) && !garrison.HasWarband
            && garrison.OwnedPopulation == beforeGuards - CampaignState.GarrisonRecruits
            && garrison.HasGarrisonAtFord(fordCell) && garrison.TryChangeFordAccess(fordCell) && garrison.IsFordClosed(fordCell),
            "A local garrison must recruit people and manage a nearby secured crossing without a warband.");
        var bridgeWood = garrison.Wood;
        Require(garrison.TryBuildBridge(fordCell) && !garrison.TryBuildBridge(fordCell)
            && garrison.Wood == bridgeWood - CampaignState.BridgeWood
            && fordMap.MarchStepCost(fordCell - 1, fordCell, garrison.HasBridge) < fordMap.MarchStepCost(fordCell - 1, fordCell)
            && !garrison.InfluenceInfrastructureCost(fordCell - 1, fordCell).HasValue,
            "A guarded bridge must charge once and reduce march cost across its banks.");
        garrison = CampaignState.Restore(fordMap, "garrison", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(garrison.ToSave(), json), json), 5);
        Require(garrison.HasGarrison(garrison.CapitalCell) && garrison.HasBridge(fordCell) && garrison.TryChangeFordAccess(fordCell)
            && garrison.InfluenceInfrastructureCost(fordCell - 1, fordCell) < 1f
            && garrison.TryDisbandGarrison(garrison.CapitalCell) && garrison.OwnedPopulation == beforeGuards
            && !garrison.TryChangeFordAccess(fordCell), "Saved garrisons must restore and return recruits on disbanding.");

        var frontierSaveBefore = CampaignState.Found(fordMap, "ford-frontier", fordCell - 1, "Frontier", 0,
            localCapitalClaim: true).ToSave();
        frontierSaveBefore.SecuredFords.Add(fordCell);
        var frontierJson = JsonSerializer.Serialize(frontierSaveBefore, json);
        var frontierRival = CampaignState.Restore(fordMap, "ford-frontier",
            JsonSerializer.Deserialize<CampaignSave>(frontierJson, json), 5);
        frontierRival.IndependentVillageSiteResolver = _ => fordCell + 1;
        frontierRival.IndependentTerritoryResolver = cell => cell == fordCell + 1;
        frontierRival.Paused = false;
        frontierRival.Advance(29);
        Require(frontierRival.FrontierFordThreat() == (fordCell, 0, 15) && frontierRival.IsFordSecured(fordCell),
            "The rival should warn about an exposed crossing before seizing it.");
        frontierRival.Advance(15);
        Require(frontierRival.IndependentVillageGuarded && !frontierRival.IsFordSecured(fordCell)
            && frontierRival.RivalFordHolder(fordCell) == 0 && frontierRival.HasEverSecuredFord,
            "A mature rival must seize an unguarded frontier ford even without an active operation.");
        frontierRival = CampaignState.Restore(fordMap, "ford-frontier", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(frontierRival.ToSave(), json), json), 5);
        Require(frontierRival.RivalFordHolder(fordCell) == 0 && frontierRival.TryNegotiateFordPassage(fordCell)
            && frontierRival.CanUseSupplyFord(fordCell), "Independent control must restore and allow a new access agreement.");

        var guardedFrontier = CampaignState.Restore(fordMap, "ford-frontier",
            JsonSerializer.Deserialize<CampaignSave>(frontierJson, json), 5);
        guardedFrontier.IndependentVillageSiteResolver = _ => fordCell + 1;
        guardedFrontier.IndependentTerritoryResolver = cell => cell == fordCell + 1;
        Require(guardedFrontier.TryRaiseGarrison(guardedFrontier.CapitalCell), "The frontier fixture needs guards.");
        guardedFrontier.Paused = false;
        guardedFrontier.Advance(29);
        Require(guardedFrontier.FrontierFordThreat().Ford == -1,
            "A protected ford must not show a takeover warning.");
        guardedFrontier.Advance(15);
        Require(guardedFrontier.IndependentVillageGuarded && guardedFrontier.IsFordSecured(fordCell),
            "A local garrison must prevent an autonomous rival takeover.");

        var expandedRival = CampaignState.Found(fordMap, "outpost", fordCell - 1, "Outpost", 0, localCapitalClaim: true);
        var rivalVillage = fordCell + 1;
        var rivalOutpost = rivalVillage + recipe.Width;
        expandedRival.IndependentVillageSiteResolver = _ => rivalVillage;
        expandedRival.IndependentFarmSiteResolver = _ => rivalVillage + 1;
        expandedRival.IndependentOutpostSiteResolver = _ => rivalOutpost;
        expandedRival.Paused = false;
        expandedRival.Advance(59);
        Require(expandedRival.IndependentOutpostCell == rivalOutpost && !expandedRival.TryFoundSettlement(rivalOutpost),
            "A mature farming community must establish a second site on its frontier.");
        expandedRival = CampaignState.Restore(fordMap, "outpost", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(expandedRival.ToSave(), json), json), 5);
        Require(expandedRival.TryStartIntegration(0) && expandedRival.AdvanceToNextDecision()
            && expandedRival.IndependentOutpostCell == -1 && expandedRival.SettlementCells.Contains(rivalVillage)
            && expandedRival.SettlementCells.Contains(rivalOutpost)
            && expandedRival.SettlementPopulation(rivalVillage) > expandedRival.SettlementPopulation(rivalOutpost)
            && expandedRival.OwnedPopulation == expandedRival.Population(0),
            "Integrating the community must preserve both sites and distribute its population without duplication.");

        var guarantee = CampaignState.Restore(fordMap, "ford-frontier",
            JsonSerializer.Deserialize<CampaignSave>(frontierJson, json), 5);
        guarantee.IndependentVillageSiteResolver = _ => fordCell + 1;
        guarantee.IndependentTerritoryResolver = cell => cell == fordCell + 1;
        var guaranteeOffer = new TradeOffer(TradeResource.Coin, 4, TradeResource.Food, 1);
        Require(!guarantee.TryGuaranteeFord(fordCell) && guarantee.TryTrade(0, guaranteeOffer),
            "Crossing guarantees must require a trusted trading partner.");
        guarantee.Paused = false;
        guarantee.Advance(14);
        Require(guarantee.TryTrade(0, guaranteeOffer), "Second market period must accept trade.");
        guarantee.Advance(15);
        var guaranteeCoin = guarantee.Coin - guaranteeOffer.GiveAmount;
        Require(guarantee.TryTrade(0, guaranteeOffer) && guarantee.TryGuaranteeFord(fordCell)
            && !guarantee.TryGuaranteeFord(fordCell) && guarantee.Coin == guaranteeCoin - CampaignState.FordGuaranteeCoin
            && guarantee.FrontierFordThreat().Ford == -1, "A trusted neighbour must honour a paid crossing guarantee.");
        guarantee = CampaignState.Restore(fordMap, "ford-frontier", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(guarantee.ToSave(), json), json), 5);
        guarantee.IndependentTerritoryResolver = cell => cell == fordCell + 1;
        guarantee.Paused = false;
        guarantee.Advance(15);
        Require(guarantee.IsFordSecured(fordCell) && guarantee.FordGuaranteeHolder(fordCell) == 0
            && guarantee.TryRecruitWarband() && guarantee.TryStartIntegration(0, ExpansionApproach.Force)
            && guarantee.FordGuaranteeHolder(fordCell) == -1,
            "Guarantees must survive reload and lapse if the clan goes to war with the guarantor.");

        var localFarm = CampaignState.Found(fordMap, "local-farm", fordCell - 1, "Local farm", 0);
        fordMap.LocalProductionYield = (cell, building) =>
            building == SettlementBuilding.Farm ? (cell == fordCell + 1 ? 3 : 0) : (int?)null;
        Require(!localFarm.TryBuildAtSettlement(localFarm.CapitalCell, SettlementBuilding.Farm, fordCell - 1)
            && localFarm.TryBuildAtSettlement(localFarm.CapitalCell, SettlementBuilding.Farm, fordCell + 1)
            && localFarm.SettlementBuildingInfo(localFarm.CapitalCell, SettlementBuilding.Farm, fordCell + 1).Yield == 3,
            "Manual farm construction must use local eligibility and yield.");

        var supplyRivers = new byte[raster.Length];
        for (var row = 0; row < recipe.Height; row++) supplyRivers[row * recipe.Width + 1] = 3;
        var supplyMap = ProvinceMap.Build(recipe, Enumerable.Repeat(1, raster.Length).ToArray(),
            new[] { new ProvinceInfo { Id = "Mine", Rgb = 1, Type = "land", Terrain = "mountains" } });
        supplyMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, fordTerrain)), supplyRivers, fordTerrain);
        var supply = CampaignState.Found(supplyMap, "supply", fordCell - 1, "Supply", 0);
        var mineSite = fordCell + 1;
        Require(supply.TryFoundSettlement(mineSite) && supply.TryBuildAtSettlement(mineSite, SettlementBuilding.IronMine, mineSite)
            && !supply.HasIronSupplyRoute(mineSite), "An unsecured ford must block the mining settlement's supply route.");
        supply.Paused = false;
        supply.Advance(4);
        Require(supply.Iron == 0 && supply.SettlementIronStock(mineSite) > 0,
            "An isolated mining settlement must stockpile iron locally.");
        var supplySave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(supply.ToSave(), json), json);
        var storedIron = supply.SettlementIronStock(mineSite);
        supplySave.SecuredFords.Add(fordCell);
        supplySave.Warband = true;
        supply = CampaignState.Restore(supplyMap, "supply", supplySave, 5);
        Require(supply.SettlementIronStock(mineSite) == storedIron && supply.HasIronSupplyRoute(mineSite)
            && supply.GetOverlandIronRoute(mineSite).Contains(fordCell),
            "Saved stockpiles and secured fords must restore.");
        Require(supply.TryChangeFordAccess(fordCell) && !supply.HasIronSupplyRoute(mineSite)
            && supply.TryChangeFordAccess(fordCell) && supply.HasIronSupplyRoute(mineSite),
            "Closing and reopening a ford must invalidate cached civilian routes.");
        var claimSeat = fordCell - 1;
        var claimCode = claimSeat + 1;
        var claimCells = new int[raster.Length];
        claimCells[claimSeat] = claimCells[mineSite] = claimCells[mineSite + 1] = claimCode;
        claimCells[mineSite + 2] = -1;
        claimCells[mineSite + 3] = claimCode;
        var claimReach = new HashSet<int>();
        var claimQueue = new Queue<int>();
        supplyMap.CollectConnectedClaim(claimCells, claimSeat, claimSeat, supply.CanUseSupplyFord, claimReach, claimQueue);
        Require(claimReach.SetEquals(new[] { claimSeat, mineSite, mineSite + 1 }) && claimCells[fordCell] == 0,
            "An open ford must connect matching claim banks without claiming the river or crossing independent land.");
        Require(supply.TryChangeFordAccess(fordCell), "The guard must be able to close the claim's crossing.");
        claimReach.Clear();
        supplyMap.CollectConnectedClaim(claimCells, claimSeat, claimSeat, supply.CanUseSupplyFord, claimReach, claimQueue);
        Require(claimReach.SetEquals(new[] { claimSeat }) && claimCells[mineSite] == claimCode,
            "A closure must detach the far bank while preserving its province membership.");
        Require(supply.TryChangeFordAccess(fordCell), "The guard must be able to reopen the claim's crossing.");
        claimReach.Clear();
        supplyMap.CollectConnectedClaim(claimCells, claimSeat, claimSeat, supply.CanUseSupplyFord, claimReach, claimQueue);
        Require(claimReach.Contains(mineSite), "Reopening passage must reconnect the detached claim.");

        var seamClaim = new int[raster.Length];
        var seamSeat = recipe.Width - 1;
        seamClaim[seamSeat] = seamClaim[seamSeat + 1] = seamSeat + 1;
        claimReach.Clear();
        supplyMap.CollectConnectedClaim(seamClaim, seamSeat, seamSeat, supply.CanUseSupplyFord, claimReach, claimQueue);
        Require(claimReach.SetEquals(new[] { seamSeat }), "Disconnected claim cells cannot wrap across a map row.");

        var plannedSite = mineSite + 2;
        var lonelySite = mineSite + 4;
        var splitRaster = Enumerable.Repeat(1, raster.Length).ToArray();
        splitRaster[plannedSite] = 2;
        var splitMap = ProvinceMap.Build(recipe, splitRaster, new[]
        {
            new ProvinceInfo { Id = "Clan", Rgb = 1, Type = "land" },
            new ProvinceInfo { Id = "Community", Rgb = 2, Type = "land" }
        });
        splitMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, fordTerrain)), supplyRivers, fordTerrain);
        var splitSave = CampaignState.Found(splitMap, "detached", claimSeat, "Detached claim", 0, localCapitalClaim: true).ToSave();
        splitSave.Coin = splitSave.Wood = 250;
        var split = CampaignState.Restore(splitMap, "detached", splitSave, 5);
        var nearVillage = claimSeat - recipe.Width;
        var newSeat = mineSite + 1;
        Require(split.TryFoundSettlement(mineSite, claimSeat) && split.TryFoundSettlement(newSeat, claimSeat)
            && split.TryFoundSettlement(nearVillage, claimSeat) && split.TryFoundSettlement(lonelySite, claimSeat)
            && split.TryStartIntegration(1, settlementCell: plannedSite, provinceRoot: claimSeat),
            "The province split fixture needs a home fragment, two detached fragments and a pending community.");
        var splitClaims = new int[raster.Length];
        foreach (var cell in new[] { claimSeat, nearVillage, mineSite, newSeat, plannedSite, lonelySite })
            splitClaims[cell] = claimCode;
        split.DetachedClaimResolver = cell =>
        {
            var home = new HashSet<int>();
            splitMap.CollectConnectedClaim(splitClaims, claimSeat, claimSeat, split.CanUseSupplyFord, home, claimQueue);
            if (home.Contains(cell)) return null;
            var fragment = new HashSet<int>();
            splitMap.CollectConnectedClaim(splitClaims, cell, claimSeat, split.CanUseSupplyFord, fragment, claimQueue);
            return fragment;
        };
        var beforeSplitPopulation = split.OwnedPopulation;
        var beforeSplitOrder = split.SettlementCells.ToArray();
        var beforeSplitCoin = split.Coin;
        Require(!split.TryEstablishDetachedProvince(claimSeat) && !split.TryEstablishDetachedProvince(nearVillage)
            && !split.TryEstablishDetachedProvince(plannedSite), "A seat, a connected village and a non-member cannot split the province.");
        Require(split.TryEstablishDetachedProvince(newSeat) && !split.TryEstablishDetachedProvince(newSeat)
            && split.SettlementProvinceRoots.SequenceEqual(new[] { newSeat, newSeat, claimSeat, claimSeat })
            && split.SettlementCells.SequenceEqual(beforeSplitOrder) && split.OwnedPopulation == beforeSplitPopulation
            && split.Coin == beforeSplitCoin,
            "A split must reassign only the selected component without changing settlement order, population or coin.");
        Require(split.ProvinceNumber(claimSeat) == 1 && split.ProvinceNumber(newSeat) == 2
            && split.ProvinceNumber(mineSite) == 0, "A split must assign its own number to the new province seat.");
        var splitSnapshot = JsonSerializer.Serialize(split.ToSave(), json);
        var unnumbered = JsonSerializer.Deserialize<CampaignSave>(splitSnapshot, json);
        unnumbered.SettlementProvinceNumbers = null;
        unnumbered.NextProvinceNumber = 0;
        Require(CampaignState.Restore(splitMap, "detached", unnumbered, 5).ProvinceNumber(newSeat) == 2,
            "Older saves must acquire their original display order on load.");
        split = CampaignState.Restore(splitMap, "detached", JsonSerializer.Deserialize<CampaignSave>(splitSnapshot, json), 5);
        Require(split.SettlementProvinceRoots.SequenceEqual(new[] { newSeat, newSeat, claimSeat, claimSeat })
            && split.AdvanceToNextDecision() && split.SettlementCells.Last() == plannedSite
            && split.SettlementProvinceRoots.Last() == newSeat && !split.Owns(1),
            "A later-founded seat and its pending settlement must restore as the same new province.");

        var movedSeat = CampaignState.Restore(splitMap, "detached", JsonSerializer.Deserialize<CampaignSave>(splitSnapshot, json), 5);
        movedSeat.ProvinceSeatConnectionResolver = _ => false;
        Require(!movedSeat.TryMoveProvinceSeat(mineSite), "A disconnected village cannot take over a populated seat.");
        movedSeat.ProvinceSeatConnectionResolver = _ => true;
        Require(!movedSeat.TryMoveProvinceSeat(nearVillage) && !movedSeat.TryMoveProvinceSeat(newSeat)
            && movedSeat.TryMoveProvinceSeat(mineSite) && movedSeat.SettlementProvinceRoots.Take(2).All(root => root == mineSite),
            "A connected member can become its province seat while the capital keeps its seat.");
        movedSeat = CampaignState.Restore(splitMap, "detached", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(movedSeat.ToSave(), json), json), 5);
        Require(movedSeat.AdvanceToNextDecision() && movedSeat.SettlementCells.Last() == plannedSite
            && movedSeat.SettlementProvinceRoots.Last() == mineSite && movedSeat.ProvinceNumber(mineSite) == 2,
            "A saved seat relocation must redirect the province's pending integration.");

        var famineSeatSave = JsonSerializer.Deserialize<CampaignSave>(splitSnapshot, json);
        famineSeatSave.Day = 104;
        famineSeatSave.Food = 0;
        famineSeatSave.Granary = true;
        famineSeatSave.SettlementPopulations[1] = 100;
        famineSeatSave.SettlementPopulations[2] = 1;
        var famineSeat = CampaignState.Restore(splitMap, "detached", famineSeatSave, 5);
        famineSeat.Paused = false;
        famineSeat.Advance(1);
        Require(famineSeat.SettlementPopulation(newSeat) == 0 && famineSeat.SettlementPopulation(mineSite) > 0
            && famineSeat.SettlementProvinceRoots.Take(2).All(root => root == mineSite)
            && famineSeat.ProvinceNumber(mineSite) == 2 && famineSeat.ToSave().IntegrationProvinceRoot == mineSite + 1,
            "Losing a seat during a food tick must transfer the province and its pending order to an inhabited member.");

        var vacantSeatSave = JsonSerializer.Deserialize<CampaignSave>(splitSnapshot, json);
        vacantSeatSave.SettlementPopulations[2] = 0;
        var vacantSeat = CampaignState.Restore(splitMap, "detached", vacantSeatSave, 5);
        Require(vacantSeat.SettlementProvinceRoots.Take(2).All(root => root == mineSite)
            && vacantSeat.ToSave().IntegrationProvinceRoot == mineSite + 1,
            "Loading an older save with a vacant seat must select a populated successor.");

        var reunionSave = JsonSerializer.Deserialize<CampaignSave>(splitSnapshot, json);
        reunionSave.Warband = true;
        reunionSave.Coin = 200;
        reunionSave.SecuredFords.Add(fordCell);
        var reunion = CampaignState.Restore(splitMap, "detached", reunionSave, 5);
        foreach (var cell in new[] { mineSite, newSeat, plannedSite, plannedSite + 1 })
            splitClaims[cell] = newSeat + 1;
        var reunionBorders = new Dictionary<int, SortedSet<int>>();
        var reunionReach = new HashSet<int>();
        reunion.ProvinceBorderResolver = (from, to) =>
        {
            reunionBorders.Clear();
            reunionReach.Clear();
            splitMap.CollectConnectedClaim(splitClaims, claimSeat, claimSeat, reunion.CanUseSupplyFord, reunionReach, claimQueue);
            splitMap.CollectConnectedClaim(splitClaims, newSeat, newSeat, reunion.CanUseSupplyFord, reunionReach, claimQueue);
            splitMap.CollectProvinceBorders(splitClaims, reunionReach, reunion.CanUseSupplyFord, reunionBorders);
            return reunionBorders.TryGetValue(from, out var neighbours) && neighbours.Contains(to);
        };
        Require(reunion.TryChangeFordAccess(fordCell)
            && !reunion.TryMergeProvinces(newSeat, claimSeat) && reunionBorders.Count == 0,
            "A closed ford and contact through a detached fragment must not let provinces merge.");
        Require(reunion.TryChangeFordAccess(fordCell)
            && reunion.ProvinceBorderResolver(newSeat, claimSeat)
            && reunionBorders[claimSeat].SetEquals(new[] { newSeat })
            && !reunion.TryMergeProvinces(claimSeat, newSeat)
            && !reunion.TryMergeProvinces(newSeat, nearVillage),
            "An open ford must expose the mutual border while protecting the capital seat and rejecting non-seats.");
        var laterProvince = lonelySite + 1;
        Require(reunion.TryFoundSettlement(laterProvince) && reunion.ProvinceNumber(laterProvince) == 3,
            "An unrelated province must receive the next number before the merger.");
        var reunionPopulation = reunion.OwnedPopulation;
        Require(reunion.TryMergeProvinces(newSeat, claimSeat)
            && !reunion.TryMergeProvinces(newSeat, claimSeat)
            && reunion.SettlementProvinceRoots.Take(4).All(root => root == claimSeat)
            && reunion.OwnedPopulation == reunionPopulation && reunion.ProvinceNumber(laterProvince) == 3,
            "Merging must reassign the source seat and all of its villages without moving population.");
        reunion = CampaignState.Restore(splitMap, "detached", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(reunion.ToSave(), json), json), 5);
        Require(reunion.AdvanceToNextDecision() && reunion.SettlementCells.Last() == plannedSite
            && reunion.SettlementProvinceRoots.Last() == claimSeat && !reunion.Owns(1),
            "After saving a merger, a pending integration must join the surviving province seat.");
        Require(reunion.ProvinceNumber(claimSeat) == 1 && reunion.ProvinceNumber(newSeat) == 0
            && reunion.ProvinceNumber(laterProvince) == 3 && reunion.TryFoundSettlement(laterProvince + 1)
            && reunion.ProvinceNumber(laterProvince + 1) == 4,
            "A new province must receive a fresh number after a merger and reload.");

        supply.Paused = false;
        supply.Advance(5);
        var cargo = supply.IronShipment(mineSite);
        Require(supply.Iron == 0 && cargo.Amount > storedIron && cargo.Days > 0
            && supply.SettlementIronStock(mineSite) == 0,
            "An opened route must dispatch accumulated and newly produced iron before it is usable.");
        var inTransit = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(supply.ToSave(), json), json);
        supply = CampaignState.Restore(supplyMap, "supply", inTransit, 5);
        var journey = supply.IronShipmentJourney(mineSite);
        Require(supply.IronShipment(mineSite) == cargo && journey.Transport == IronSupplyRoute.Overland
            && journey.Path.Contains(fordCell) && journey.Progress == 0f,
            "An in-transit shipment and its original crossing must survive save and load.");
        Require(supply.TryChangeFordAccess(fordCell) && supply.IsIronShipmentHeld(mineSite),
            "Closing a ford must hold cargo that has not crossed yet.");
        supply.Paused = false;
        supply.Advance(cargo.Days + 1);
        Require(supply.Iron == 0 && supply.IronShipment(mineSite) == cargo
            && supply.IronShipmentJourney(mineSite).Progress == 0f,
            "Held cargo must remain stationary past its original arrival and keep its cargo.");
        var heldSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(supply.ToSave(), json), json);
        var reroute = CampaignState.Restore(supplyMap, "supply", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(heldSave, json), json), 5);
        Require(!reroute.TryRerouteIronShipment(mineSite) && reroute.IsIronShipmentHeld(mineSite),
            "A held shipment must keep its cargo if there is no alternative.");
        var alternateSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(heldSave, json), json);
        var alternateFord = fordCell + recipe.Width;
        alternateSave.SecuredFords.Add(alternateFord);
        reroute = CampaignState.Restore(supplyMap, "supply", alternateSave, 5);
        Require(reroute.TryRerouteIronShipment(mineSite) && reroute.IsFordClosed(fordCell)
            && !reroute.IsIronShipmentHeld(mineSite) && reroute.IronShipment(mineSite).Amount == cargo.Amount
            && reroute.IronShipmentJourney(mineSite).Path.Contains(alternateFord)
            && !reroute.IronShipmentJourney(mineSite).Path.Contains(fordCell),
            "A held shipment must conserve cargo and use the alternate ford without reopening the original.");
        reroute.Paused = false;
        reroute.Advance(reroute.IronShipment(mineSite).Days);
        Require(reroute.Iron == cargo.Amount, "Rerouted cargo must arrive once without losing or duplicating iron.");
        supply = CampaignState.Restore(supplyMap, "supply", heldSave, 5);
        Require(supply.IsIronShipmentHeld(mineSite) && supply.TryChangeFordAccess(fordCell)
            && !supply.IsIronShipmentHeld(mineSite), "The hold must survive save and resume when the ford reopens.");
        supply.Paused = false;
        supply.Advance(cargo.Days);
        Require(supply.Iron == cargo.Amount && !supply.IsIronShipmentHeld(mineSite),
            "The capital must receive cargo once travel finishes.");

        var farCapital = fordCell - 1 + 6 * recipe.Width;
        var farVillage = fordCell + 8;
        var roadSupply = CampaignState.Found(supplyMap, "long-supply", farCapital, "Long supply", 0);
        Require(roadSupply.TryFoundSettlement(farVillage)
            && roadSupply.TryBuildAtSettlement(farVillage, SettlementBuilding.IronMine, farVillage)
            && roadSupply.TrySetLocalFoodTarget(farVillage, 5),
            "The long route needs a mine and a village reserve across the river.");
        var roadSupplySave = roadSupply.ToSave();
        roadSupplySave.SecuredFords.Add(fordCell);
        roadSupplySave.Warband = true;
        roadSupplySave.WarbandRoute = new List<int> { fordCell - 1 };
        roadSupply = CampaignState.Restore(supplyMap, "long-supply", roadSupplySave, 5);
        roadSupply.Paused = false;
        roadSupply.Advance(4);
        var initialFood = roadSupply.FoodShipmentJourney(farVillage);
        var initialIron = roadSupply.IronShipmentJourney(farVillage);
        var initialIronCargo = roadSupply.IronShipment(farVillage);
        var openRoadSave = JsonSerializer.Serialize(roadSupply.ToSave(), json);
        Require(!roadSupply.TryRerouteFoodShipment(farVillage) && !roadSupply.TryRerouteIronShipment(farVillage),
            "Deliveries on an open route cannot be rerouted from a crossing.");
        Require(roadSupply.TryChangeFordAccess(fordCell), "The guard must be able to close the ford.");
        var foodApproach = roadSupply.DaysUntilShipmentWait(farVillage, TradeResource.Food);
        var ironApproach = roadSupply.DaysUntilShipmentWait(farVillage, TradeResource.Iron);
        Require(foodApproach > 1 && ironApproach > 1 && !roadSupply.HeldShipmentsAtFord(fordCell).Any(),
            "Closing a distant ford must warn approaching convoys without immediately stopping them.");
        var approaching = roadSupply.ShipmentsThreatenedByFord(fordCell).ToArray();
        Require(approaching.Length == 2
            && approaching.Contains((farCapital, farVillage, TradeResource.Food, 5, foodApproach, false))
            && approaching.Contains((farVillage, farCapital, TradeResource.Iron, initialIronCargo.Amount, ironApproach, false))
            && !roadSupply.ShipmentsThreatenedByFord(alternateFord).Any(),
            "The threatened crossing must identify both approaching deliveries and their individual arrival times.");
        roadSupply.Advance(1);
        Require(roadSupply.FoodShipmentJourney(farVillage).Progress > initialFood.Progress
            && roadSupply.IronShipmentJourney(farVillage).Progress > initialIron.Progress
            && !roadSupply.HeldShipmentsAtFord(fordCell).Any(),
            "Both convoys must keep moving on the open stretch before the bank.");
        var approachSave = JsonSerializer.Serialize(roadSupply.ToSave(), json);
        Require(!roadSupply.TryRerouteFoodShipment(farVillage) && !roadSupply.TryRerouteIronShipment(farVillage)
            && roadSupply.ShipmentsThreatenedByFord(fordCell).All(row => !row.Held)
            && roadSupply.FoodShipmentJourney(farVillage).Path.SequenceEqual(initialFood.Path)
            && roadSupply.IronShipmentJourney(farVillage).Path.SequenceEqual(initialIron.Path),
            "With no alternate ford, approaching convoys must retain their original journeys.");

        var earlyBypassSave = JsonSerializer.Deserialize<CampaignSave>(approachSave, json);
        earlyBypassSave.SecuredFords.Add(alternateFord);
        var earlyBypass = CampaignState.Restore(supplyMap, "long-supply", earlyBypassSave, 5);
        Require(earlyBypass.TryRerouteFoodShipment(farVillage) && earlyBypass.TryRerouteIronShipment(farVillage)
            && earlyBypass.IsFordClosed(fordCell) && !earlyBypass.ShipmentsThreatenedByFord(fordCell).Any(),
            "Both deliveries can be diverted before they reach the closed crossing.");
        var bypassFoodJourney = earlyBypass.FoodShipmentJourney(farVillage);
        var bypassIronJourney = earlyBypass.IronShipmentJourney(farVillage);
        Require(bypassFoodJourney.Path[0] == initialFood.Path[1] && bypassIronJourney.Path[0] == initialIron.Path[1]
            && bypassFoodJourney.Path.Contains(alternateFord) && bypassIronJourney.Path.Contains(alternateFord)
            && earlyBypass.LocalFoodShipment(farVillage).Amount == 5
            && earlyBypass.IronShipment(farVillage).Amount == initialIronCargo.Amount,
            "Rerouting must start at each convoy's reached tile and preserve all cargo.");
        earlyBypass.Paused = false;
        earlyBypass.Advance(Math.Max(earlyBypass.LocalFoodShipment(farVillage).Days, earlyBypass.IronShipment(farVillage).Days));
        Require(earlyBypass.LocalFoodStock(farVillage) == 5 && earlyBypass.Iron == initialIronCargo.Amount,
            "Diverted convoys must deliver before the original ford reopens.");

        var formerlyHeldSave = JsonSerializer.Deserialize<CampaignSave>(approachSave, json);
        var formerlyHeldFood = formerlyHeldSave.SettlementFood.Single();
        formerlyHeldFood.HoldDay = formerlyHeldSave.Day;
        formerlyHeldSave.SettlementFood[0] = formerlyHeldFood;
        var formerlyHeldIron = formerlyHeldSave.Construction.Single();
        formerlyHeldIron.IronHoldDay = formerlyHeldSave.Day;
        formerlyHeldSave.Construction[0] = formerlyHeldIron;
        var migratedSupply = CampaignState.Restore(supplyMap, "long-supply", formerlyHeldSave, 5);
        Require(!migratedSupply.HeldShipmentsAtFord(fordCell).Any()
            && migratedSupply.DaysUntilShipmentWait(farVillage, TradeResource.Food) == foodApproach - 1
            && migratedSupply.DaysUntilShipmentWait(farVillage, TradeResource.Iron) == ironApproach - 1
            && migratedSupply.LocalFoodShipment(farVillage).Amount == formerlyHeldFood.InTransit
            && migratedSupply.IronShipment(farVillage).Amount == formerlyHeldIron.IronInTransit,
            "Older saves with convoys stopped far from a ford must resume the approach with their cargo.");

        var reopenedEarly = CampaignState.Restore(supplyMap, "long-supply",
            JsonSerializer.Deserialize<CampaignSave>(approachSave, json), 5);
        Require(reopenedEarly.TryChangeFordAccess(fordCell)
            && reopenedEarly.DaysUntilShipmentWait(farVillage, TradeResource.Food) == -1
            && reopenedEarly.IronShipment(farVillage).Days == initialIronCargo.Days - 1,
            "Reopening before a convoy reaches the bank must preserve its arrival schedule.");

        roadSupply = CampaignState.Restore(supplyMap, "long-supply",
            JsonSerializer.Deserialize<CampaignSave>(approachSave, json), 5);
        roadSupply.Paused = false;
        roadSupply.Advance(initialIronCargo.Days + 2);
        var waitingFood = roadSupply.FoodShipmentJourney(farVillage);
        var waitingIron = roadSupply.IronShipmentJourney(farVillage);
        var foodBank = waitingFood.Path.ToList().IndexOf(fordCell) - 1;
        var ironBank = waitingIron.Path.ToList().IndexOf(fordCell) - 1;
        Require(waitingFood.Held && roadSupply.IsIronShipmentHeld(farVillage)
            && Math.Abs(waitingFood.Progress * (waitingFood.Path.Count - 1) - foodBank) < 0.001f
            && Math.Abs(waitingIron.Progress * (waitingIron.Path.Count - 1) - ironBank) < 0.001f
            && roadSupply.ShipmentsThreatenedByFord(fordCell).Count(row => row.Held && row.DaysToFord == 0) == 2
            && roadSupply.HeldShipmentsAtFord(fordCell).Count() == 2 && roadSupply.Iron == 0,
            "Fast-forward after loading must stop both convoys at their respective banks.");

        var crossed = CampaignState.Restore(supplyMap, "long-supply",
            JsonSerializer.Deserialize<CampaignSave>(openRoadSave, json), 5);
        crossed.Paused = false;
        crossed.Advance(Math.Max(foodBank, ironBank) + 2);
        Require(crossed.TryChangeFordAccess(fordCell)
            && crossed.DaysUntilShipmentWait(farVillage, TradeResource.Food) == -1
            && crossed.DaysUntilShipmentWait(farVillage, TradeResource.Iron) == -1,
            "A closed ford behind the convoys must not stop them.");
        crossed.Advance(crossed.IronShipment(farVillage).Days);
        Require(crossed.Iron == initialIronCargo.Amount && !crossed.HeldShipmentsAtFord(fordCell).Any(),
            "Cargo that crossed before closure must arrive on schedule.");

        var valley = CampaignState.Found(supplyMap, "valley", fordCell - 1, "Valley", 0, localCapitalClaim: true);
        Require(valley.TryFoundSettlement(mineSite) && valley.TryBuildAtSettlement(mineSite, SettlementBuilding.IronMine, mineSite),
            "The valley needs a mine across the river from the capital.");
        var valleySave = valley.ToSave();
        valleySave.SecuredFords.Add(fordCell);
        valley = CampaignState.Restore(supplyMap, "valley", valleySave, 5);
        valley.IndependentVillageSiteResolver = _ => mineSite + 1;
        valley.IndependentTerritoryResolver = cell => cell == mineSite;
        valley.Paused = false;
        valley.Advance(43);
        Require(valley.TrySetLocalFoodTarget(mineSite, 5), "The valley village needs a food reserve order.");
        valley.Advance(1);
        var valleyFoodCargo = valley.LocalFoodShipment(mineSite);
        Require(valleyFoodCargo == (5, valleyFoodCargo.Days, true) && valley.FoodHeldAtFord(fordCell) == (1, 5L),
            "A rival takeover must hold the village's incoming food as well as outgoing supplies.");
        var valleyCargo = valley.IronShipment(mineSite);
        var deliveredBeforeClosure = valley.Iron;
        Require(valley.IsFordHeldByRival(fordCell) && valley.IsIronShipmentHeld(mineSite) && valleyCargo.Amount > 0
            && valley.SuppliesHeldAtFord(fordCell) == (1, (long)valleyCargo.Amount),
            "A rival takeover on a production day must cut a live supply route and hold outgoing cargo.");
        var manifest = valley.HeldShipmentsAtFord(fordCell).ToArray();
        Require(manifest.Length == 2
            && manifest.Contains((valley.CapitalCell, mineSite, TradeResource.Food, valleyFoodCargo.Amount))
            && manifest.Contains((mineSite, valley.CapitalCell, TradeResource.Iron, valleyCargo.Amount)),
            "The ford must report both shipment directions, affected settlements and quantities.");
        Require(!valley.HeldShipmentsAtFord(alternateFord).Any(),
            "A different crossing must not report this ford's cargo.");
        valley.Advance(valleyCargo.Days + 1);
        Require(valley.Iron == deliveredBeforeClosure && valley.TryNegotiateFordPassage(fordCell)
            && !valley.IsIronShipmentHeld(mineSite) && !valley.HeldShipmentsAtFord(fordCell).Any(),
            "Negotiated passage must clear both shipment types from the crossing.");
        valley.Advance(valleyCargo.Days);
        Require(valley.Iron == deliveredBeforeClosure + valleyCargo.Amount,
            "The reopened crossing must deliver precisely the previously held cargo.");
        Require(valley.LocalFoodStock(mineSite) == 5 && valley.LocalFoodShipment(mineSite).Amount == 0,
            "Negotiated passage must allow the village's food to arrive.");

        var isolatedFood = CampaignState.Found(supplyMap, "isolated-food", fordCell - 1, "Isolated food", 0);
        Require(isolatedFood.TryFoundSettlement(mineSite) && isolatedFood.IsFoodIsolated(mineSite),
            "The food fixture requires a village isolated by the river.");
        var foodWinterSave = isolatedFood.ToSave();
        foodWinterSave.Day = CampaignState.WinterStart;
        foodWinterSave.Food = 100;
        isolatedFood = CampaignState.Restore(supplyMap, "isolated-food", foodWinterSave, 5);
        var provisionedVillage = CampaignState.Restore(supplyMap, "isolated-food", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(isolatedFood.ToSave(), json), json), 5);
        Require(provisionedVillage.TryBuildAtSettlement(mineSite, SettlementBuilding.Farm),
            "An isolated village must be able to build its own farm.");
        var beforeWinter = isolatedFood.SettlementPopulation(mineSite);
        isolatedFood.Paused = provisionedVillage.Paused = false;
        isolatedFood.Advance(15);
        provisionedVillage.Advance(15);
        Require(isolatedFood.Food > isolatedFood.FoodUpkeep && isolatedFood.LocalFoodShortfall(mineSite) > 0
            && isolatedFood.SettlementPopulation(mineSite) < beforeWinter,
            "An isolated village without food must shrink even while the capital has plenty.");
        Require(provisionedVillage.LocalFoodStock(mineSite) > 0
            && provisionedVillage.SettlementPopulation(mineSite) >= beforeWinter,
            "A local farm must feed an isolated village through winter.");
        provisionedVillage = CampaignState.Restore(supplyMap, "isolated-food", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(provisionedVillage.ToSave(), json), json), 5);
        Require(provisionedVillage.LocalFoodStock(mineSite) > 0, "Local food stocks must survive save and load.");

        var stocked = CampaignState.Found(supplyMap, "stocked", fordCell - 1, "Stocked", 0);
        Require(stocked.TryFoundSettlement(mineSite), "The reserve fixture requires a second village.");
        var stockedSave = stocked.ToSave();
        stockedSave.Food = 100;
        stockedSave.Warband = true;
        stockedSave.SecuredFords.Add(fordCell);
        stocked = CampaignState.Restore(supplyMap, "stocked", stockedSave, 5);
        Require(stocked.TrySetLocalFoodTarget(mineSite, 10) && !stocked.TrySetLocalFoodTarget(mineSite, 31),
            "Only valid reserve targets may be set.");
        stocked.Paused = false;
        stocked.Advance(4);
        var foodCargo = stocked.LocalFoodShipment(mineSite);
        Require(stocked.LocalFoodTarget(mineSite) == 10 && stocked.LocalFoodStock(mineSite) == 0
            && foodCargo.Amount == 10 && foodCargo.Days > 0 && !foodCargo.Held,
            "Capital food should be dispatched before it reaches the village reserve.");
        var roadJourney = stocked.FoodShipmentJourney(mineSite);
        Require(roadJourney.Path.SequenceEqual(new[] { stocked.CapitalCell, fordCell, mineSite })
            && roadJourney.Transport == IronSupplyRoute.Overland && roadJourney.Progress == 0f,
            "The overland convoy must travel from the capital to the village.");
        Require(stocked.TryChangeFordAccess(fordCell) && stocked.FoodHeldAtFord(fordCell) == (1, 10L),
            "Closing the ford must hold the food in transit.");
        var heldFoodSave = JsonSerializer.Serialize(stocked.ToSave(), json);
        var alternateFoodSave = JsonSerializer.Deserialize<CampaignSave>(heldFoodSave, json);
        alternateFoodSave.SecuredFords.Add(alternateFord);
        var reroutedFood = CampaignState.Restore(supplyMap, "stocked", alternateFoodSave, 5);
        Require(!stocked.TryRerouteFoodShipment(mineSite) && reroutedFood.TryRerouteFoodShipment(mineSite)
            && reroutedFood.IsFordClosed(fordCell), "Food rerouting must require an open alternate ford.");
        Require(stocked.HeldShipmentsAtFord(fordCell).Single() == (stocked.CapitalCell, mineSite, TradeResource.Food, 10)
            && !reroutedFood.HeldShipmentsAtFord(fordCell).Any(),
            "Rerouting must clear the original crossing's cargo only when an alternate route succeeds.");
        var alternateJourney = reroutedFood.FoodShipmentJourney(mineSite);
        Require(alternateJourney.Path.Contains(alternateFord) && !alternateJourney.Path.Contains(fordCell)
            && !alternateJourney.Held, "The visible convoy must follow the new crossing after rerouting.");
        reroutedFood.Paused = false;
        reroutedFood.Advance(reroutedFood.LocalFoodShipment(mineSite).Days);
        Require(reroutedFood.LocalFoodStock(mineSite) == 10 && reroutedFood.LocalFoodShipment(mineSite).Amount == 0,
            "The alternate route must deliver the held food exactly once.");
        stocked = CampaignState.Restore(supplyMap, "stocked", JsonSerializer.Deserialize<CampaignSave>(heldFoodSave, json), 5);
        stocked.Paused = false;
        stocked.Advance(foodCargo.Days + 1);
        Require(stocked.LocalFoodStock(mineSite) == 0
            && stocked.LocalFoodShipment(mineSite) == (foodCargo.Amount, foodCargo.Days, true),
            "A saved delivery must stay held beyond its original arrival date.");
        var heldJourney = stocked.FoodShipmentJourney(mineSite);
        Require(heldJourney.Held && heldJourney.Progress == roadJourney.Progress
            && heldJourney.Path.SequenceEqual(roadJourney.Path),
            "The convoy must stay parked on its restored route while the ford is closed.");
        Require(stocked.TryChangeFordAccess(fordCell) && stocked.FoodHeldAtFord(fordCell).Shipments == 0,
            "Reopening the crossing must release the delivery.");
        stocked.Advance(0.5);
        var movingJourney = stocked.FoodShipmentJourney(mineSite);
        Require(movingJourney.Progress > heldJourney.Progress && !movingJourney.Held,
            "The convoy must resume movement between whole days after passage reopens.");
        stocked.Advance(foodCargo.Days);
        Require(stocked.LocalFoodStock(mineSite) == 10 && stocked.LocalFoodShipment(mineSite).Amount == 0,
            "The released cargo must reach the reserve exactly once.");
        var granaryVillageSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(stocked.ToSave(), json), json);
        granaryVillageSave.Coin = granaryVillageSave.Wood = granaryVillageSave.Food = 200;
        var granaryVillage = CampaignState.Restore(supplyMap, "stocked", granaryVillageSave, 5);
        var villageGranarySite = mineSite + 1;
        Require(granaryVillage.GranaryCellAt(mineSite) == -1
            && !granaryVillage.TrySetLocalFoodTarget(mineSite, 60)
            && !granaryVillage.TryBuildVillageGranary(mineSite, fordCell)
            && !granaryVillage.TryBuildVillageGranary(granaryVillage.CapitalCell, villageGranarySite),
            "An older village keeps its base reserve limit, and cannot build a granary on a ford.");
        Require(granaryVillage.TryBuildVillageGranary(mineSite, villageGranarySite)
            && !granaryVillage.TryBuildVillageGranary(mineSite, villageGranarySite + 1)
            && granaryVillage.Wood == 200 - CampaignState.VillageGranaryWood
            && granaryVillage.Coin == 200 - CampaignState.VillageGranaryCoin
            && !granaryVillage.HasGranary && granaryVillage.SettlementHappinessBreakdown(mineSite).Granary == 5,
            "A village granary charges once, grants local happiness and leaves capital storage independent.");
        Require(granaryVillage.LocalFoodTargetLimit(mineSite) == 60
            && granaryVillage.TrySetLocalFoodTarget(mineSite, 60)
            && !granaryVillage.TrySetLocalFoodTarget(mineSite, 61),
            "A village granary must double the local reserve target limit.");
        granaryVillage.Paused = false;
        granaryVillage.Advance(CampaignState.ProductionInterval - granaryVillage.Day % CampaignState.ProductionInterval);
        Require(granaryVillage.LocalFoodShipment(mineSite).Amount == 50 && granaryVillage.LocalFoodStock(mineSite) == 10,
            "The expanded reserve must draw a larger shipment through the usual supply route.");
        granaryVillage = CampaignState.Restore(supplyMap, "stocked", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(granaryVillage.ToSave(), json), json), 5);
        Require(granaryVillage.GranaryCellAt(mineSite) == villageGranarySite
            && granaryVillage.LocalFoodTarget(mineSite) == 60 && granaryVillage.LocalFoodTargetLimit(mineSite) == 60,
            "A saved village granary must restore its exact site and capacity while a delivery is in flight.");
        granaryVillage.Paused = false;
        granaryVillage.Advance(granaryVillage.LocalFoodShipment(mineSite).Days);
        Require(granaryVillage.LocalFoodStock(mineSite) == 60 && granaryVillage.LocalFoodShipment(mineSite).Amount == 0,
            "A restored delivery must fill the expanded reserve once.");
        Require(granaryVillage.TrySetLocalFoodTarget(mineSite, 0), "The village must be able to release its reserve.");
        granaryVillage.Advance(CampaignState.ProductionInterval);
        Require(granaryVillage.LocalFoodStock(mineSite) == 0 && granaryVillage.GranaryCellAt(mineSite) == villageGranarySite,
            "Releasing all local food stock must keep the granary in place.");

        var emergencySave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(stocked.ToSave(), json), json);
        emergencySave.Day = 104;
        emergencySave.Granary = true;
        emergencySave.WarbandSupply = WarbandSupplyPolicy.Forage;
        emergencySave.Food = 0;
        emergencySave.Pop[0] = 500;
        emergencySave.SettlementPopulations = new List<int> { 250, 250 };
        var savedEmergency = JsonSerializer.Serialize(emergencySave, json);
        var shortage = CampaignState.Restore(supplyMap, "stocked", emergencySave, 5);
        Require(!shortage.IsFoodIsolated(mineSite) && shortage.SettlementMealOutlook(mineSite) == (2L, 2L, true)
            && shortage.SettlementMealOutlook(shortage.CapitalCell) == (0L, 2L, false)
            && shortage.SettlementHappinessBreakdown(mineSite).Food == 0,
            "A stocked village must forecast a local ration during a clan shortage.");
        shortage.Paused = false;
        shortage.Advance(1);
        Require(shortage.SettlementPopulation(mineSite) == 250 && shortage.SettlementPopulation(shortage.CapitalCell) == 225
            && shortage.LocalFoodStock(mineSite) == 8 && shortage.LocalFoodUsedLastMeal(mineSite) == 2
            && shortage.LocalFoodShortfall(mineSite) == 0 && shortage.WinterShortfall == 2,
            "A village must eat only its own reserve and avoid starvation without feeding the capital.");
        var shortageReload = CampaignState.Restore(supplyMap, "stocked", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(shortage.ToSave(), json), json), 5);
        Require(shortageReload.LocalFoodStock(mineSite) == 8 && shortageReload.LocalFoodUsedLastMeal(mineSite) == 2,
            "The reserve balance and last-meal usage must survive save and load.");

        var partialReserveSave = JsonSerializer.Deserialize<CampaignSave>(savedEmergency, json);
        var partialReserve = partialReserveSave.SettlementFood.Single();
        partialReserve.Food = 1;
        partialReserveSave.SettlementFood[0] = partialReserve;
        var partialReserveCampaign = CampaignState.Restore(supplyMap, "stocked", partialReserveSave, 5);
        partialReserveCampaign.Paused = false;
        partialReserveCampaign.Advance(1);
        Require(partialReserveCampaign.SettlementPopulation(mineSite) == 237
            && partialReserveCampaign.LocalFoodStock(mineSite) == 0
            && partialReserveCampaign.LocalFoodUsedLastMeal(mineSite) == 1
            && partialReserveCampaign.LocalFoodShortfall(mineSite) == 1
            && partialReserveCampaign.WinterShortfall == 3,
            "A partial reserve ration must reduce population loss and account for the remaining shortfall.");

        var pooledRationsSave = JsonSerializer.Deserialize<CampaignSave>(savedEmergency, json);
        pooledRationsSave.SettlementFood[0] = partialReserve;
        pooledRationsSave.Food = 3;
        var pooledRations = CampaignState.Restore(supplyMap, "stocked", pooledRationsSave, 5);
        Require(pooledRations.SettlementMealOutlook(mineSite) == (3L, 4L, false),
            "The outlook must retain the shared ration when it offers more food than local stores.");
        pooledRations.Paused = false;
        pooledRations.Advance(1);
        Require(pooledRations.SettlementPopulation(mineSite) == 243 && pooledRations.LocalFoodStock(mineSite) == 1
            && pooledRations.LocalFoodUsedLastMeal(mineSite) == 0 && pooledRations.WinterShortfall == 1,
            "A better shared ration must leave local stores intact.");

        Require(stocked.TryChangeFordAccess(fordCell) && stocked.IsFoodIsolated(mineSite),
            "The reserve fixture needs loss of supply access.");
        var blockedStock = stocked.ToSave();
        blockedStock.Day = CampaignState.WinterStart;
        stocked = CampaignState.Restore(supplyMap, "stocked", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(blockedStock, json), json), 5);
        stocked.Paused = false;
        stocked.Advance(15);
        Require(stocked.LocalFoodTarget(mineSite) == 10 && stocked.LocalFoodStock(mineSite) < 10
            && stocked.LocalFoodStock(mineSite) > 0 && stocked.LocalFoodShortfall(mineSite) == 0,
            "An isolated village must survive from its saved food reserve.");

        var portsRaster = Enumerable.Repeat(1, raster.Length).ToArray();
        for (var row = 0; row < recipe.Height; row++) portsRaster[row * recipe.Width + 1] = 2;
        var supplyWater = WaterMap.Classify(new HeightmapResult(recipe, fordTerrain));
        var portsMap = ProvinceMap.Build(recipe, portsRaster, new[]
        {
            new ProvinceInfo { Id = "Banks", Rgb = 1, Type = "land", Terrain = "mountains" },
            new ProvinceInfo { Id = "River", Rgb = 2, Type = "ocean" }
        }).WithSettlementGeography(supplyWater, supplyRivers, null);
        portsMap.BuildMarchTerrain(supplyWater, supplyRivers, fordTerrain);
        var passage = CampaignState.Found(portsMap, "passage", fordCell - 1, "Passage", 0, localCapitalClaim: true);
        var passageCoin = passage.Coin;
        Require(passage.FordPassagePartner(fordCell) == 0 && !passage.HasWarband
            && passage.TryNegotiateFordPassage(fordCell) && passage.Coin == passageCoin - CampaignState.FordPassageCoin
            && passage.HasFordPassage(fordCell) && !passage.IsFordSecured(fordCell)
            && !passage.TryNegotiateFordPassage(fordCell),
            "A reachable independent community must sell permanent civilian passage without military occupation.");
        passage = CampaignState.Restore(portsMap, "passage", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(passage.ToSave(), json), json), 5);
        Require(passage.HasFordPassage(fordCell) && passage.GetOverlandIronRoute(mineSite).Contains(fordCell),
            "Negotiated passage must restore and permit overland supply through the crossing.");
        Require(passage.TryRecruitWarband() && passage.TryStartIntegration(0, ExpansionApproach.Force)
            && passage.IsPassageSuspended(fordCell) && passage.GetOverlandIronRoute(mineSite) == null,
            "A passage partner must suspend access and invalidate civilian routes during hostilities.");
        var hostilePassage = JsonSerializer.Serialize(passage.ToSave(), json);
        passage = CampaignState.Restore(portsMap, "passage", JsonSerializer.Deserialize<CampaignSave>(hostilePassage, json), 5);
        Require(passage.IsPassageSuspended(fordCell) && passage.TryWithdrawWarband()
            && !passage.IsPassageSuspended(fordCell) && passage.GetOverlandIronRoute(mineSite).Contains(fordCell),
            "Suspension must restore with hostilities and lift when the operation ends.");
        var contest = CampaignState.Restore(portsMap, "passage", JsonSerializer.Deserialize<CampaignSave>(
            hostilePassage, json), 5);
        contest.Paused = false;
        contest.Advance(2);
        Require(!contest.IsFordHeldByRival(fordCell), "Suspension must precede a rival takeover.");
        contest.Advance(1);
        Require(contest.IsFordHeldByRival(fordCell) && contest.TryWithdrawWarband() && contest.IsFordClosed(fordCell)
            && contest.GetOverlandIronRoute(mineSite) == null, "A rival takeover must outlast a withdrawal.");
        var contested = JsonSerializer.Serialize(contest.ToSave(), json);
        var retake = CampaignState.Restore(portsMap, "passage", JsonSerializer.Deserialize<CampaignSave>(contested, json), 5);
        var retakeFood = retake.Food;
        Require(retake.RivalFordHolder(fordCell) == 0 && retake.TrySecureFord(fordCell)
            && retake.IsFordSecured(fordCell) && !retake.IsFordHeldByRival(fordCell)
            && retake.Food == retakeFood - CampaignState.FordRetakeFood
            && retake.GetOverlandIronRoute(mineSite).Contains(fordCell), "A warband on the bank must be able to retake the crossing.");
        var undersupplied = JsonSerializer.Deserialize<CampaignSave>(contested, json);
        undersupplied.Food = CampaignState.FordRetakeFood - 1;
        var noSupplies = CampaignState.Restore(portsMap, "passage", undersupplied, 5);
        Require(!noSupplies.TrySecureFord(fordCell) && noSupplies.IsFordHeldByRival(fordCell)
            && noSupplies.Food == CampaignState.FordRetakeFood - 1,
            "A failed retake must leave rival control and supplies untouched.");
        var renewed = CampaignState.Restore(portsMap, "passage", JsonSerializer.Deserialize<CampaignSave>(contested, json), 5);
        Require(renewed.TryNegotiateFordPassage(fordCell) && !renewed.IsFordHeldByRival(fordCell)
            && !renewed.IsFordSecured(fordCell) && renewed.GetOverlandIronRoute(mineSite).Contains(fordCell),
            "Renewed passage must reopen a rival-held crossing without a military claim.");

        var portsSave = CampaignState.Found(portsMap, "ports", fordCell - 1, "Ports", 0).ToSave();
        portsSave.Coin = portsSave.Wood = 200;
        var ports = CampaignState.Restore(portsMap, "ports", portsSave, 5);
        Require(ports.TryFoundSettlement(mineSite) && ports.TryBuildAtSettlement(mineSite, SettlementBuilding.IronMine, mineSite)
            && ports.TryBuildAtSettlement(mineSite, SettlementBuilding.Docks, mineSite)
            && ports.GetIronSupplyRoute(mineSite) == IronSupplyRoute.None,
            "A producer's dock cannot deliver iron without a receiving dock.");
        Require(ports.TryBuildAtSettlement(ports.CapitalCell, SettlementBuilding.Docks, ports.CapitalCell)
            && ports.GetIronSupplyRoute(mineSite) == IronSupplyRoute.Waterway,
            "Two docks on the same navigable channel must open iron shipments.");
        Require(ports.GetWaterwayIronRoute(mineSite).SequenceEqual(new[] { mineSite, fordCell, ports.CapitalCell }),
            "The waterway route must join the loading dock, channel and receiving dock.");
        Require(ports.TrySetLocalFoodTarget(mineSite, 5), "The port village must be able to request food.");
        ports.Paused = false;
        ports.Advance(4);
        var foodBoat = ports.FoodShipmentJourney(mineSite);
        Require(foodBoat.Transport == IronSupplyRoute.Waterway
            && foodBoat.Path.SequenceEqual(new[] { ports.CapitalCell, fordCell, mineSite }),
            "A food boat must follow the channel from the capital's dock to the village dock.");
        var reloadedPort = CampaignState.Restore(portsMap, "ports", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(ports.ToSave(), json), json), 5);
        Require(reloadedPort.FoodShipmentJourney(mineSite).Path.SequenceEqual(foodBoat.Path),
            "The waterway convoy must appear on the same route after loading.");
        var waterCargo = ports.IronShipment(mineSite);
        Require(ports.Iron == 0 && waterCargo.Amount > 0 && ports.SettlementIronStock(mineSite) == 0,
            "Waterway access must dispatch local iron on the production tick.");
        ports.Advance(waterCargo.Days);
        Require(ports.Iron == waterCargo.Amount && ports.LocalFoodStock(mineSite) == 5
            && ports.FoodShipmentJourney(mineSite).Path == null, "Delivered cargo must disappear from the map.");

        var transferCapital = fordCell - 1 + 6 * recipe.Width;
        var transferVillage = fordCell + 1 + 6 * recipe.Width;
        var capitalDock = transferCapital + recipe.Width;
        var villageDock = transferVillage + recipe.Width;
        var transferSave = CampaignState.Found(portsMap, "dock-transfer", transferCapital, "Dock transfer", 0).ToSave();
        transferSave.Coin = transferSave.Wood = 200;
        transferSave.SecuredFords.Add(fordCell);
        transferSave.Warband = true;
        transferSave.WarbandRoute = new List<int> { fordCell - 1 };
        var transfer = CampaignState.Restore(portsMap, "dock-transfer", transferSave, 5);
        Require(transfer.TryFoundSettlement(transferVillage)
            && transfer.TryBuildAtSettlement(transferVillage, SettlementBuilding.IronMine, transferVillage)
            && transfer.TrySetLocalFoodTarget(transferVillage, 5), "The transfer fixture requires food and iron cargo.");
        transfer.Paused = false;
        transfer.Advance(6);
        var groundFood = transfer.FoodShipmentJourney(transferVillage);
        var groundIron = transfer.IronShipmentJourney(transferVillage);
        var transferIron = transfer.IronShipment(transferVillage).Amount;
        Require(groundFood.Transport == IronSupplyRoute.Overland && groundIron.Transport == IronSupplyRoute.Overland
            && transfer.TryChangeFordAccess(fordCell)
            && !transfer.TryRerouteFoodShipment(transferVillage, byWater: true)
            && !transfer.TryRerouteIronShipment(transferVillage, byWater: true),
            "Water diversion requires working docks and must leave the original convoy intact if they are missing.");
        Require(transfer.TryBuildAtSettlement(transferCapital, SettlementBuilding.Docks, capitalDock)
            && !transfer.TryRerouteFoodShipment(transferVillage, byWater: true)
            && transfer.TryBuildAtSettlement(transferVillage, SettlementBuilding.Docks, villageDock),
            "Both ends of the service need docks on the same waterway.");
        Require(transfer.TryRerouteFoodShipment(transferVillage, byWater: true)
            && transfer.TryRerouteIronShipment(transferVillage, byWater: true)
            && transfer.IsShipmentTransferringToDock(transferVillage, TradeResource.Food)
            && transfer.IsShipmentTransferringToDock(transferVillage, TradeResource.Iron)
            && !transfer.ShipmentsThreatenedByFord(fordCell).Any(),
            "The affected convoys must start a transfer to their respective loading docks.");
        var foodTransferPath = transfer.FoodShipmentJourney(transferVillage).Path;
        var ironTransferPath = transfer.IronShipmentJourney(transferVillage).Path;
        Require(foodTransferPath[0] == groundFood.Path[2] && foodTransferPath.Last() == capitalDock
            && ironTransferPath[0] == groundIron.Path[2] && ironTransferPath.Last() == villageDock
            && !foodTransferPath.Contains(fordCell) && !ironTransferPath.Contains(fordCell),
            "Cargo must travel overland from each convoy's current location to its actual loading dock.");
        transfer = CampaignState.Restore(portsMap, "dock-transfer", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(transfer.ToSave(), json), json), 5);
        transfer.Paused = false;
        transfer.Advance(transfer.LocalFoodShipment(transferVillage).Days);
        Require(transfer.FoodShipmentJourney(transferVillage).Transport == IronSupplyRoute.Waterway
            && transfer.IronShipmentJourney(transferVillage).Transport == IronSupplyRoute.Waterway
            && transfer.FoodShipmentJourney(transferVillage).Path.First() == capitalDock
            && transfer.IronShipmentJourney(transferVillage).Path.First() == villageDock
            && transfer.LocalFoodStock(transferVillage) == 0 && transfer.Iron == 0,
            "Reaching the docks must launch both boats without crediting cargo early.");
        transfer = CampaignState.Restore(portsMap, "dock-transfer", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(transfer.ToSave(), json), json), 5);
        transfer.Paused = false;
        transfer.Advance(transfer.IronShipment(transferVillage).Days);
        Require(transfer.LocalFoodStock(transferVillage) == 5 && transfer.Iron == transferIron
            && transfer.IsFordClosed(fordCell) && transfer.FoodShipmentJourney(transferVillage).Path == null,
            "Saved water diversions must deliver each load once while the ford stays closed.");

        var hill = recipe.Width + 2;
        var marchHeights = Enumerable.Repeat(recipe.SeaLevel, raster.Length).ToArray();
        marchHeights[hill] = 1f;
        var hillMap = ProvinceMap.Build(recipe, Enumerable.Repeat(1, raster.Length).ToArray(),
            new[] { new ProvinceInfo { Id = "Hills", Rgb = 1, Type = "land" } });
        hillMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, marchHeights)), null, marchHeights);
        var detour = hillMap.FindWarbandRoute(hill - 1, hill + 1);
        Require(detour.Count == 5 && !detour.Contains(hill), "Routing must prefer a cheaper detour around steep ground.");
        var climb = CampaignState.Found(hillMap, "hill", hill - 1, "Climb", 0);
        Require(climb.TryRecruitWarband() && climb.TryMarchWarband(new List<int> { hill - 1, hill })
            && climb.WarbandTravelDays > 1, "A steep step must take longer than flat terrain.");
        climb.Paused = false;
        climb.Advance(1);
        var climbSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(climb.ToSave(), json), json);
        var resumedClimb = CampaignState.Restore(hillMap, "hill", climbSave, 5);
        Require(resumedClimb.WarbandCell == hill - 1 && resumedClimb.WarbandTravelDays == climb.WarbandTravelDays
            && resumedClimb.WarbandTravelDays > 0, "Partial progress across a steep step must survive a save.");

        var saddle = 12 * recipe.Width + 12;
        var passHeights = Enumerable.Repeat(.56f, raster.Length).ToArray();
        passHeights[saddle] = .65f;
        passHeights[saddle - 1] = passHeights[saddle + 1] = .60f;
        passHeights[saddle - recipe.Width] = passHeights[saddle + recipe.Width] = .72f;
        passHeights[saddle - 2 * recipe.Width] = passHeights[saddle + 2 * recipe.Width] = .78f;
        var passMap = ProvinceMap.Build(recipe, Enumerable.Repeat(1, raster.Length).ToArray(),
            new[] { new ProvinceInfo { Id = "Saddle", Rgb = 1, Type = "land", Terrain = "mountains" } });
        passMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, passHeights)), null, passHeights);
        Require(passMap.IsMountainPass(saddle) && passMap.UsesMountainPass(saddle - 1, saddle)
            && passMap.UsesMountainPass(saddle, saddle + 1) && !passMap.UsesMountainPass(saddle, saddle + recipe.Width)
            && passMap.CanMarchBetween(saddle - 1, saddle) && !passMap.IsFord(saddle),
            "A dry saddle must allow ordinary land travel, with the pass oriented between the lower exits.");

        var ridgeHeights = (float[])passHeights.Clone();
        ridgeHeights[saddle + 2 * recipe.Width] = .67f;
        var ridgeMap = ProvinceMap.Build(recipe, Enumerable.Repeat(1, raster.Length).ToArray(),
            new[] { new ProvinceInfo { Id = "Ridge", Rgb = 1, Type = "land", Terrain = "mountains" } });
        ridgeMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, ridgeHeights)), null, ridgeHeights);
        Require(!ridgeMap.IsMountainPass(saddle) && !passMap.IsMountainPass(saddle + 5)
            && passMap.MarchStepCost(saddle - 1, saddle) < ridgeMap.MarchStepCost(saddle - 1, saddle)
            && passMap.InfluenceElevationCost(saddle - 1, saddle) < ridgeMap.InfluenceElevationCost(saddle - 1, saddle)
            && passMap.MarchStepCost(saddle, saddle + recipe.Width) == ridgeMap.MarchStepCost(saddle, saddle + recipe.Width)
            && passMap.InfluenceElevationCost(saddle, saddle + recipe.Width) == ridgeMap.InfluenceElevationCost(saddle, saddle + recipe.Width),
            "A pass must reduce both grade penalties only along the crossing axis, and require opposing ridges.");

        var interruptedPass = new byte[raster.Length];
        interruptedPass[saddle + 1] = 1;
        passMap.BuildMarchTerrain(WaterMap.Classify(new HeightmapResult(recipe, passHeights)), interruptedPass, passHeights);
        Require(!passMap.IsMountainPass(saddle), "A river through the approach must interrupt the dry saddle.");

        var passRecipe = HeightmapRecipe.OldWorld();
        passRecipe.Width = 1024;
        passRecipe.Height = 512;
        var passRivers = RiverGenerator.Generate(HeightmapGenerator.Generate(passRecipe));
        var generatedPassMap = ProvinceMap.Build(passRecipe, Enumerable.Repeat(1, passRivers.Terrain.Elevations.Length).ToArray(),
            new[] { new ProvinceInfo { Id = "Generated relief", Rgb = 1, Type = "land" } });
        generatedPassMap.BuildMarchTerrain(WaterMap.Classify(passRivers.Terrain), passRivers.BuildSurfaceDirections(),
            passRivers.Terrain.Elevations);
        Require(Enumerable.Range(0, generatedPassMap.ProvinceAt.Length).Any(generatedPassMap.IsMountainPass),
            "Mountain passes must occur on standard generated terrain, including its carved rivers.");

        var roadSave = CampaignState.Found(hillMap, "road", hill - 1, "Road", 0).ToSave();
        roadSave.Coin = roadSave.Wood = 200;
        var road = CampaignState.Restore(hillMap, "road", roadSave, 5);
        Require(road.TryFoundSettlement(hill + 1) && road.TryBuildRoad(hill + 1)
            && !road.TryBuildRoad(hill + 1) && road.HasRoadToCapital(hill + 1)
            && road.Wood == 200 - CampaignState.SettlementWood - CampaignState.RoadWood,
            "A connected village can pay once to build a road.");
        road = CampaignState.Restore(hillMap, "road", JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(road.ToSave(), json), json), 5);
        var roadPath = road.Roads.Single().Path;
        Require(road.HasRoadEdge(roadPath[1], roadPath[0])
            && road.InfluenceInfrastructureCost(roadPath[1], roadPath[0]) < 1f
            && hillMap.MarchStepCost(hill - 1, hill, hasRoad: (_, _) => true) < hillMap.MarchStepCost(hill - 1, hill),
            "Roads must restore in both directions and remove rough-ground march penalties.");

        var march = CampaignState.Found(map, key, 0, "March", 0);
        var beforeRecruitment = march.SettlementPopulation(0);
        Require(march.TryRecruitWarband() && march.WarbandCell == 0
            && march.SettlementPopulation(0) == beforeRecruitment - CampaignState.WarbandRecruits
            && !march.TryMarchWarband(new List<int> { 0, 2 })
            && march.TryMarchWarband(new List<int> { 0, 1 }),
            "A recruited warband must start at the capital and accept only connected routes.");
        var marchSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(march.ToSave(), json), json);
        march = CampaignState.Restore(map, key, marchSave, 5);
        Require(march.WarbandCell == 0 && march.WarbandTravelDays == 1,
            "An active march must survive save and load.");
        march.Paused = false;
        march.Advance(1);
        Require(march.WarbandCell == 1 && march.WarbandTravelDays == 0,
            "A marching warband must reach its destination on the campaign clock.");
        march.SetWarbandSupply(WarbandSupplyPolicy.Provisioned);
        Require(march.WarbandFoodCost == 2 && march.TryMarchWarband(new List<int> { 1, 0, 1 })
            && march.WarbandTravelDays == 1, "Provisioning must buy a faster march at greater food cost.");
        march.SetWarbandSupply(WarbandSupplyPolicy.Forage);
        Require(march.WarbandFoodCost == 0 && march.WarbandTravelDays == 2,
            "Foraging must trade march speed for lower food costs.");
        Require(march.EstimateMarchDays(new List<int> { 1, 0, 1 }) == 2 && march.TryHaltWarband()
            && march.WarbandCell == 1 && march.WarbandTravelDays == 0 && !march.TryHaltWarband(),
            "A march estimate must match the current policy, and halting must keep the warband at its current cell.");

        var frontier = CampaignState.Found(map, key, 0, "Frontier", 0);
        frontier.LandContactResolver = province => province == 2;
        Require(!frontier.CanTradeWith(1) && !frontier.TryStartIntegration(1)
            && frontier.CanTradeWith(2) && frontier.TryStartIntegration(2),
            "Settlement contact must govern land trade and integration even across authored boundaries.");
        var frontierSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(frontier.ToSave(), json), json);
        Require(CampaignState.Restore(map, key, frontierSave, 5).IntegrationTarget == 2,
            "An integration opened by settlement contact must survive save and load.");

        var townSave = CampaignState.Found(map, key, 0, "Town", 0).ToSave();
        townSave.Pop[0] = townSave.SettlementPopulations[0] = CampaignState.TownPopulation;
        var town = CampaignState.Restore(map, key, townSave, 5);
        Require(town.TryUpgradeTown(0) && town.IsTown(0) && !town.TryUpgradeTown(0)
            && town.Coin == CampaignState.StartingCoin - CampaignState.TownCoin,
            "A populous settlement should pay for a town upgrade only once.");
        Require(CampaignState.Restore(map, key,
            JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(town.ToSave(), json), json), 5).IsTown(0),
            "Town upgrades must survive save and load.");
        Require(!town.TrySpecialiseTown(0, SettlementBuilding.Farm) && town.TryBuildAtSettlement(0, SettlementBuilding.Farm)
            && town.TrySpecialiseTown(0, SettlementBuilding.Farm) && !town.TrySpecialiseTown(0, SettlementBuilding.Farm),
            "A town can specialise once it has the matching building, and can only specialise once.");
        Require(CampaignState.Restore(map, key,
            JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(town.ToSave(), json), json), 5)
                .TownSpeciality(0) == SettlementBuilding.Farm,
            "Town specialisation must survive save and load.");

        Require(!town.TryEstablishRegionalCouncil(), "A town alone must not qualify for a regional council.");
        Require(!town.TryEnactSettlementLaw(SettlementLaw.VillageCommons),
            "A clan without a regional council cannot enact a settlement law.");
        var councilSave = town.ToSave();
        councilSave.Coin = councilSave.Wood = 200;
        councilSave.CompletedTradeDeliveries = CampaignState.TradeMilestoneDeliveries;
        var council = CampaignState.Restore(map, key, councilSave, 5);
        Require(council.TryFoundSettlement(1) && council.TryEstablishRegionalCouncil()
            && !council.TryEstablishRegionalCouncil() && council.TradeCapacity == 2
            && council.Coin == 200 - CampaignState.SettlementCoin - CampaignState.RegionalCouncilCoin,
            "A qualifying clan must pay once to establish a council and unlock a trade agreement.");
        council = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(council.ToSave(), json), json), 5);
        Require(council.HasRegionalCouncil && council.TradeCapacity == 2,
            "The regional council and its agreement slot must survive save and load.");
        var charterSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(council.ToSave(), json), json);
        charterSave.Wood = 20;
        var charterClan = CampaignState.Restore(map, key, charterSave, 5);
        var lawBudget = charterClan.Coin;
        Require(charterClan.ActiveSettlementLaw == SettlementLaw.Customary && !charterClan.CanFoundSettlement(2)
            && !charterClan.TryEnactSettlementLaw(SettlementLaw.Customary)
            && !charterClan.TryEnactSettlementLaw((SettlementLaw)99) && charterClan.Coin == lawBudget,
            "Old saves use customary costs and invalid or duplicate changes cannot spend coin.");
        Require(charterClan.TryEnactSettlementLaw(SettlementLaw.VillageCommons)
            && charterClan.Coin == lawBudget - CampaignState.SettlementLawCoin
            && charterClan.SettlementWoodCost == 20 && charterClan.TownWoodCost == 25
            && charterClan.TryFoundSettlement(2) && charterClan.Wood == 0,
            "Village Commons must reduce the real cost to found a village and raise the town cost.");
        var commonsSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(charterClan.ToSave(), json), json);
        commonsSave.Wood = 25;
        commonsSave.Coin = 100;
        commonsSave.SettlementPopulations[1] = CampaignState.TownPopulation;
        commonsSave.Pop[1] = CampaignState.TownPopulation;
        charterClan = CampaignState.Restore(map, key, commonsSave, 5);
        Require(charterClan.ActiveSettlementLaw == SettlementLaw.VillageCommons
            && charterClan.SettlementLawReviewIn == CampaignState.SettlementLawReviewDays
            && !charterClan.TryEnactSettlementLaw(SettlementLaw.TownCharters)
            && charterClan.DaysToNextDecision == CampaignState.SettlementLawReviewDays,
            "A saved settlement law must retain its review period and offer a calendar stop.");
        var reviewDay = charterClan.Day + CampaignState.SettlementLawReviewDays;
        charterClan.AdvanceToNextDecision();
        Require(charterClan.Paused && charterClan.Day == reviewDay && charterClan.SettlementLawReviewIn == 0
            && charterClan.TryEnactSettlementLaw(SettlementLaw.TownCharters)
            && charterClan.SettlementWoodCost == 30 && charterClan.TownWoodCost == 15,
            "Town Charters must lower the upgrade cost while making new villages dearer.");
        var woodBeforeCharteredTown = charterClan.Wood;
        Require(charterClan.TryUpgradeTown(1) && charterClan.Wood == woodBeforeCharteredTown - 15,
            "The selected law must apply when the village upgrades to a town.");

        Require(!council.TryResearch(CampaignTechnology.Agronomy), "Research must wait for the council to earn points.");
        council.Paused = false;
        council.Advance(29);
        Require(council.ResearchPoints == CampaignState.ResearchCost
            && council.TryResearch(CampaignTechnology.Agronomy) && council.ResearchPoints == 0
            && council.ResearchProductionBonus(SettlementBuilding.Farm) == 1
            && !council.TryResearch(CampaignTechnology.Agronomy),
            "Council research must earn and spend points once for a permanent upgrade.");
        Require(CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(council.ToSave(), json), json), 5).HasTechnology(CampaignTechnology.Agronomy),
            "Researched technology must survive saves.");

        Require(council.SkillPoints == 1 && !council.TryUnlockPerk(CampaignPerk.Envoys)
            && council.TryUnlockPerk(CampaignPerk.Stewardship) && council.SkillPoints == 0
            && council.SettlementWoodCost == CampaignState.SettlementWood - 5,
            "A milestone point must unlock the root perk before either specialisation.");
        var skillsSave = council.ToSave();
        skillsSave.NegotiatedIntegrations = CampaignState.DiplomacyMilestoneIntegrations;
        var skilled = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(skillsSave, json), json), 5);
        Require(skilled.HasPerk(CampaignPerk.Stewardship) && skilled.SkillPoints == 1
            && skilled.TryUnlockPerk(CampaignPerk.Envoys) && skilled.NegotiationCoinCost == CampaignState.IntegrationCost - 5
            && !skilled.TryUnlockPerk(CampaignPerk.TownPlanning),
            "Saved perks must count against earned points and apply their campaign costs.");

        var goodwill = CampaignState.Found(map, key, 0, "Goodwill", 0);
        var fairOffer = new TradeOffer(TradeResource.Coin, 4, TradeResource.Food, 1);
        Require(goodwill.TryTrade(1, fairOffer) && goodwill.TryTrade(1, fairOffer) && goodwill.TradeGoodwill(1) == 1,
            "A community must gain goodwill at most once in a market period.");
        goodwill.Paused = false;
        goodwill.Advance(14);
        Require(goodwill.TryTrade(1, fairOffer) && goodwill.TradeGoodwill(1) == 2,
            "Continued trade must build goodwill across periods.");
        goodwill.Advance(15);
        Require(goodwill.TryTrade(1, fairOffer) && goodwill.IsTrustedTradePartner(1)
            && goodwill.NegotiationCostWith(1) == CampaignState.IntegrationCost - CampaignState.TrustedPartnerDiscount
            && goodwill.FordPassageCostWith(1) == CampaignState.FordPassageCoin - CampaignState.TrustedPartnerDiscount,
            "Trusted partners must offer lower passage and negotiation costs.");
        goodwill = CampaignState.Restore(map, key, JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(goodwill.ToSave(), json), json), 5);
        Require(goodwill.IsTrustedTradePartner(1) && goodwill.TryRecruitWarband()
            && goodwill.TryMarchWarband(new List<int> { 0, 1 }), "Trust must persist across saves.");
        goodwill.AdvanceToNextDecision();
        Require(goodwill.TryStartIntegration(1, ExpansionApproach.Force) && goodwill.TradeGoodwill(1) == 0,
            "Launching an operation must reset goodwill with the target.");

        var standardTaxes = CampaignState.Found(map, key, 0, "Standard", 0);
        var heavyTaxes = CampaignState.Found(map, key, 0, "Heavy", 0);
        heavyTaxes.SetTaxPolicy(TaxPolicy.Heavy);
        Require(heavyTaxes.TaxIncome > standardTaxes.TaxIncome
            && heavyTaxes.CapitalHappiness < standardTaxes.CapitalHappiness,
            "Heavy taxes must trade capital happiness for coin.");
        standardTaxes.Paused = heavyTaxes.Paused = false;
        standardTaxes.Advance(14);
        heavyTaxes.Advance(14);
        Require(heavyTaxes.Population(0) < standardTaxes.Population(0),
            "Heavy taxes must slow fed capital growth.");
        var heavyTaxSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(heavyTaxes.ToSave(), json), json);
        Require(CampaignState.Restore(map, key, heavyTaxSave, 5).Taxes == TaxPolicy.Heavy,
            "Tax policy must survive save and load.");

        var joined = CampaignState.Found(map, key, 0, "Joined", 0);
        var originalCapital = joined.SettlementPopulation(0);
        var originalResidents = joined.Population(1);
        Require(joined.TryFoundSettlement(1, joined.CapitalCell)
            && joined.SettlementProvinceRoots.Single() == joined.CapitalCell
            && joined.SettlementPopulation(0) == originalCapital - originalCapital / 2
            && joined.SettlementPopulation(1) == originalCapital / 2
            && joined.IndependentPopulation(1) == originalResidents
            && joined.OwnedPopulation == originalCapital && !joined.Owns(1),
            "Founding inside an influence province must move settlers across authored boundaries without duplicating population.");
        Require(joined.CanTradeWith(1) && joined.TryStartIntegration(1),
            "Founding alongside an independent community must leave trade and diplomacy available.");
        Require(joined.AdvanceToNextDecision() && joined.Owns(1) && joined.IndependentPopulation(1) == 0
            && joined.SettlementPopulation(1) == joined.Population(1),
            "Integrating a region with an existing village must incorporate the remaining population exactly once.");

        var settlers = CampaignState.Found(map, key, 0, "Settlers", 0);
        Require(!settlers.TryFoundSettlement(recipe.Width) && settlers.Coin == CampaignState.StartingCoin,
            "Founding on water must leave resources untouched.");
        var colonists = settlers.FoundingParty(1, -1);
        Require(colonists.Origin == 0 && colonists.People > 0
            && settlers.TryFoundSettlement(1) && !settlers.Owns(1) && settlers.SettlementCells.Contains(1)
            && settlers.SettlementPopulation(0) == originalCapital - colonists.People
            && settlers.SettlementPopulation(1) == colonists.People
            && settlers.IndependentPopulation(1) == originalResidents
            && settlers.Coin == CampaignState.StartingCoin - CampaignState.SettlementCoin,
            "A village must claim settlement ground while keeping the regional community independent.");
        Require(settlers.TryBuildAtSettlement(1, SettlementBuilding.Farm, 1)
            && settlers.HasSettlementBuilding(1, SettlementBuilding.Farm)
            && !settlers.HasSettlementBuilding(0, SettlementBuilding.Farm)
            && settlers.SettlementHappiness(1) == settlers.CapitalHappiness + 5,
            "Production construction must belong to the settlement where it was built.");
        var settlementSave = JsonSerializer.Deserialize<CampaignSave>(
            JsonSerializer.Serialize(settlers.ToSave(), json), json);
        var restoredSettlers = CampaignState.Restore(map, key, settlementSave, 5);
        Require(!restoredSettlers.Owns(1) && restoredSettlers.SettlementCells.Contains(1)
            && restoredSettlers.SettlementProvinceRoots.Single() == 1
            && restoredSettlers.SettlementPopulation(1) + restoredSettlers.IndependentPopulation(1) == restoredSettlers.Population(1)
            && restoredSettlers.HasSettlementBuilding(1, SettlementBuilding.Farm)
            && restoredSettlers.BuildingPlacement(1, SettlementBuilding.Farm) == 1,
            "A founded settlement and its claim must survive save and load.");

        var granary = CampaignState.Found(map, key, 0, "Granary", 0);
        var initialWood = granary.Wood;
        Require(granary.TryBuildGranary() && !granary.TryBuildGranary() && !granary.TryRecruitWarband()
            && granary.Wood == initialWood - CampaignState.GranaryWood && granary.WinterFoodRemaining == 2,
            "Early wood must buy only one major investment and the granary must reduce the winter forecast.");
        granary.Paused = false;
        granary.Advance(500);
        Require(granary.Day == CampaignState.WinterStart && granary.Paused && granary.WinterShortfall == 0
            && granary.WinterFoodRemaining == 2, "Winter must pause the clock before drawing down reserves.");
        var winterSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(granary.ToSave(), json), json);
        granary = CampaignState.Restore(map, key, winterSave, 5);
        Require(granary.HasGranary && granary.Paused && granary.WinterFoodRemaining == 2,
            "The granary and winter forecast must survive save and load.");
        granary.Paused = false;
        granary.Advance(500);
        Require(granary.Day == CampaignState.WinterEnd && granary.Paused && granary.WinterShortfall == 0
            && granary.WinterFoodRemaining == 0, "A prepared village must reach thaw without hunger.");

        var hungerSave = CampaignState.Found(map, key, 0, "Hungry", 0).ToSave();
        hungerSave.Day = 104;
        hungerSave.Pop[0] = hungerSave.SettlementPopulations[0] = 250;
        hungerSave.Food = 0;
        var hungry = CampaignState.Restore(map, key, hungerSave, 5);
        Require(hungry.FoodUpkeep == 2, "250 population must consume two food per cycle.");
        hungry.Paused = false;
        hungry.Advance(1);
        Require(hungry.WinterShortfall == 2 && hungry.Population(0) == 225 && hungry.LastPopulationChange == -25,
            "A fully unfed population must lose ten percent.");
        hungerSave.Food = 2;
        hungerSave.Granary = true;
        hungry.Advance(15);
        Require(hungry.Population(0) == 225 && hungry.LastPopulationChange == 0 && hungry.Food == 0,
            "Population losses must stop once upkeep is fully covered.");

        var partialSave = CampaignState.Found(map, key, 0, "Short rations", 0).ToSave();
        partialSave.Day = 104;
        partialSave.Pop[0] = partialSave.SettlementPopulations[0] = 250;
        partialSave.Food = 1;
        partialSave.Granary = true;
        var partial = CampaignState.Restore(map, key, partialSave, 5);
        partial.Paused = false;
        partial.Advance(1);
        Require(partial.Population(0) == 237 && partial.WinterShortfall == 1,
            "A half ration should cause half the full starvation rate, rounded up.");

        var trader = CampaignState.Found(map, key, 0, "Trader", 0);
        var startingFood = trader.Food;
        var buyFood = new TradeOffer(TradeResource.Coin, 30, TradeResource.Food, 10);
        var lowOffer = new TradeOffer(TradeResource.Coin, 1, TradeResource.Food, 10);
        var buyIron = new TradeOffer(TradeResource.Wood, 10, TradeResource.Iron, 2);
        var sellIron = new TradeOffer(TradeResource.Iron, 2, TradeResource.Wood, 1);
        var initialStock = trader.EvaluateTrade(1, buyFood).Stock;
        Require(!trader.TryTrade(2, buyFood) && !trader.TryTrade(1, lowOffer)
            && trader.EvaluateTrade(1, lowOffer).Willingness < 100 && trader.Coin == CampaignState.StartingCoin,
            "Inaccessible and unfavorable trades must leave both sides unchanged.");
        Require(trader.TryTrade(1, buyFood) && trader.TradeSpending == 30
            && trader.EvaluateTrade(1, buyFood).Stock == initialStock - 10
            && trader.Food == startingFood + 10 && trader.Coin == CampaignState.StartingCoin - 30,
            "Buying food must exchange both stock balances.");
        Require(trader.TryTrade(1, buyIron) && trader.Iron == 2 && trader.Wood == initialWood - 10
            && trader.TryTrade(1, sellIron) && trader.Iron == 0 && trader.Wood == initialWood - 9,
            "The same neighbour must accept repeat barter between non-coin resources.");
        var tradeSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(trader.ToSave(), json), json);
        trader = CampaignState.Restore(map, key, tradeSave, 5);
        Require(trader.EvaluateTrade(1, buyFood).Stock == initialStock - 10 && trader.HasTradedWith(1),
            "Market stock and trade history must persist.");
        trader.Paused = false;
        trader.Advance(14);
        trader.Paused = true;
        Require(trader.EvaluateTrade(1, buyFood).Stock > initialStock - 10
            && trader.TryStartIntegration(1, ExpansionApproach.Trade),
            "Markets replenish over time and a completed exchange opens a pact.");

        Require(trader.DaysToNextDecision == 20 && trader.AdvanceToNextDecision()
            && trader.Paused && trader.Day == 35 && trader.Owns(1) && trader.FirstExpansion == ExpansionApproach.Trade,
            "Advance must stop at the trade pact and record the route before winter.");

        var warrior = CampaignState.Found(map, key, 0, "Warrior", 0);
        Require(!warrior.TryStartIntegration(1, ExpansionApproach.Force) && warrior.TryRecruitWarband()
            && !warrior.TryBuildGranary() && !warrior.TryStartIntegration(1, ExpansionApproach.Force)
            && warrior.TryMarchWarband(new List<int> { 0, 1 }),
            "Force must require a paid warband and a march to its target.");
        var warriorPopulation = warrior.Population(warrior.Capital);
        Require(warrior.DaysToNextDecision == 1, "The next milestone should stop at the warband's arrival.");
        warrior.AdvanceToNextDecision();
        Require(warrior.TryStartIntegration(1, ExpansionApproach.Force)
            && !warrior.TryMarchWarband(new List<int> { 1, 0 })
            && warrior.EvaluateTrade(1, new TradeOffer(TradeResource.Coin, 1, TradeResource.Food, 1)).Willingness == 0,
            "An operation must commit the warband and suspend trade with its target.");
        var withdrawal = CampaignState.Restore(map, key,
            JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(warrior.ToSave(), json), json), 5);
        var supplies = withdrawal.Food;
        Require(withdrawal.TryWithdrawWarband() && !withdrawal.TryWithdrawWarband() && withdrawal.IntegrationTarget == -1
            && withdrawal.MilitaryPressure == 0 && withdrawal.Food == supplies && !withdrawal.Owns(1)
            && withdrawal.EvaluateTrade(1, new TradeOffer(TradeResource.Coin, 1, TradeResource.Food, 1)).Stock > 0
            && withdrawal.TryMarchWarband(new List<int> { 1, 0 }),
            "Withdrawal must end pressure and reopen trade and movement without refunding supplies.");
        warrior.Paused = false;
        Require(warrior.Advance(10) && warrior.Owns(1) && warrior.FirstExpansion == ExpansionApproach.Force
            && warrior.Food > startingFood && warrior.Population(warrior.Capital) < warriorPopulation
            && warrior.SuccessfulOperations == 1,
            "Force must secure a province and supplies with casualties.");

        var economy = CampaignState.Found(map, key, 0, "Economy", 0);
        Require(economy.OwnedPopulation == economy.Population(0) && economy.Iron == 0,
            "Independent population must stay out of the tribal total.");
        Require(!economy.TryBuild(1, SettlementBuilding.Farm) && !economy.TryBuild(3, SettlementBuilding.IronMine)
            && economy.Coin == CampaignState.StartingCoin && economy.Wood == initialWood,
            "Construction in independent or water provinces must not spend resources.");
        Require(economy.TryBuild(0, SettlementBuilding.Farm) && economy.TryBuild(0, SettlementBuilding.LumberCamp)
            && economy.TryBuild(0, SettlementBuilding.IronMine) && !economy.TryBuild(0, SettlementBuilding.Farm)
            && economy.Coin == 20 && economy.Wood == 0, "Each building must charge exactly once per province.");

        var firstUpkeep = economy.FoodUpkeep;
        var population = economy.Population(0);
        economy.Paused = false;
        economy.Advance(14);
        var firstTax = economy.TaxIncome;
        Require(economy.Day == 15 && economy.Coin == 20 + firstTax
            && economy.Population(0) > population && economy.Population(1) == population
            && economy.Food == startingFood + 3 * (CampaignState.Fertility(map.Provinces[0]) + 3) - firstUpkeep
            && economy.Wood == 10 && economy.Iron == 3 && economy.TaxCollected == firstTax,
            "Taxes and site-specific construction output must follow the calendar.");

        var economySave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(economy.ToSave(), json), json);
        economy = CampaignState.Restore(map, key, economySave, 5);
        Require(economy.HasBuilding(0, SettlementBuilding.Farm) && economy.HasBuilding(0, SettlementBuilding.LumberCamp)
            && economy.HasBuilding(0, SettlementBuilding.IronMine) && economy.Iron == 3 && economy.Coin == 20 + firstTax,
            "Province buildings and resources must survive a save round trip.");

        Require(restored.OwnedPopulation == (long)restored.Population(0) + restored.Population(1),
            "Integration must add the new province's population to the tribal total.");

        var postWinter = CampaignState.Found(map, key, 0, "Tax timing", 0).ToSave();
        postWinter.Day = CampaignState.WinterEnd;
        var batched = CampaignState.Restore(map, key, postWinter, 5);
        Require(batched.TryStartIntegration(1), "Could not start post-winter integration.");
        var steppedSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(batched.ToSave(), json), json);
        var stepped = CampaignState.Restore(map, key, steppedSave, 5);
        batched.Paused = stepped.Paused = false;
        batched.Advance(45);
        for (var day = 0; day < 45; day++) stepped.Advance(1);
        Require(batched.Owns(1) && batched.Coin == CampaignState.StartingCoin - CampaignState.IntegrationCost
            + firstTax + 2 * batched.TaxIncome && batched.Coin == stepped.Coin && batched.Food == stepped.Food
            && batched.OwnedPopulation == stepped.OwnedPopulation && batched.Wood == stepped.Wood, "Fast-forward must apply the new tax base from the day a province joins.");

        batched.Advance(450);
        Require(batched.Coin > CampaignState.StartingCoin, "Taxes must grow the treasury beyond the starting balance.");
        var taxSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(batched.ToSave(), json), json);
        Require(CampaignState.Restore(map, key, taxSave, 5).Coin == batched.Coin,
            "A treasury above the starting balance must survive reload.");

        var exporter = CampaignState.Found(map, key, 0, "Exporter", 0);
        var export = new TradeOffer(TradeResource.Food, 12, TradeResource.Coin, 5);
        Require(exporter.TryStartTradeAgreement(1, export) && !exporter.TryStartTradeAgreement(1, export)
            && exporter.Food == startingFood && exporter.TradeReceipts == 0,
            "Signing reserves a slot but waits for the next economy tick.");
        exporter.SetFoodReserve(45);
        exporter.Paused = false;
        exporter.Advance(14);
        Require(exporter.TradeAgreements[0].LastAttemptDay == 15
            && exporter.TradeAgreements[0].LastIssue == "Protected food reserve." && exporter.TradeReceipts == 0,
            "The reserve must block food exports without transferring goods or coin.");
        var exportSave = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(exporter.ToSave(), json), json);
        exporter = CampaignState.Restore(map, key, exportSave, 5);
        Require(exporter.FoodReserve == 45 && exporter.TradeAgreements[0].LastIssue == "Protected food reserve.",
            "The agreement and reserve must survive a save round trip.");
        exporter.SetFoodReserve(10);
        exporter.Paused = false;
        exporter.Advance(15);
        Require(exporter.TradeAgreements[0].LastIssue == null && exporter.TradeReceipts == 5
            && exporter.CompletedTradeDeliveries == 1
            && exporter.Coin == CampaignState.StartingCoin + exporter.TaxCollected + exporter.TradeReceipts,
            "An agreement must resume and book receipts separately from taxes.");
        Require(exporter.TryStartIntegration(1), "Could not integrate the trade partner.");
        exporter.Advance(30);
        Require(exporter.Owns(1) && exporter.TradeAgreements.Count == 0 && exporter.TradeReceipts == 10,
            "An agreement must end before shipping on the day its partner joins.");

        var equippedSave = CampaignState.Found(map, key, 0, "Iron warband", 0).ToSave();
        equippedSave.Warband = true;
        equippedSave.Iron = CampaignState.IronWarbandIron;
        var equipped = CampaignState.Restore(map, key, equippedSave, 5);
        Require(equipped.TryUpgradeWarband() && equipped.HasIronWarband && !equipped.TryUpgradeWarband()
            && equipped.Iron == 0 && equipped.TryMarchWarband(new List<int> { 0, 1 }),
            "Equipping the warband must spend iron exactly once.");
        equipped.AdvanceToNextDecision();
        Require(equipped.TryStartIntegration(1, ExpansionApproach.Force), "The equipped warband must be able to attack after arriving.");
        var equippedPopulation = equipped.Population(0);
        equipped.Paused = false;
        equipped.Advance(10);
        Require(equipped.Population(0) == equippedPopulation - 9, "Iron equipment must halve friendly losses.");

        var previousSave = CampaignState.Found(map, key, 0, "Previous save", 0).ToSave();
        previousSave.Version = 2;
        previousSave.Buildings = null;
        var upgraded = CampaignState.Restore(map, key, previousSave, 5);
        Require(upgraded.Iron == 0 && !upgraded.HasBuilding(0, SettlementBuilding.Farm)
            && upgraded.TryBuild(0, SettlementBuilding.Farm), "Version 2 saves must gain empty construction slots.");

        var oldTrade = CampaignState.Found(map, key, 0, "Old trader", 0).ToSave();
        oldTrade.Version = 3;
        oldTrade.Coin = 130;
        oldTrade.TradePartner = 1;
        oldTrade.Markets = null;
        oldTrade.IntegrationTarget = 1;
        oldTrade.IntegrationDaysLeft = 5;
        oldTrade.PendingApproach = ExpansionApproach.Trade;
        var oldTrader = CampaignState.Restore(map, key, oldTrade, 5);
        Require(oldTrader.Coin == 130 && oldTrader.HasTradedWith(1)
            && oldTrader.IntegrationDaysLeft == 5, "Version 3 saves must retain treasury and trade pacts.");

        var legacy = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(campaign.ToSave(), json), json);
        legacy.Version = 1;
        legacy.Food = legacy.Wood = 0;
        legacy.PendingApproach = ExpansionApproach.None;
        var migrated = CampaignState.Restore(map, key, legacy, 5);
        Require(migrated.Food == startingFood && migrated.Wood == initialWood && !migrated.HasTradedWith(1)
            && migrated.PendingApproach == ExpansionApproach.Diplomacy && migrated.IntegrationTarget == 1,
            "Legacy campaigns must keep progress and get initial village supplies.");
    }

    // Check that terrain and water connections affect construction and trade eligibility.
    private static void CheckSettlementGeography()
    {
        var recipe = Small();
        var width = recipe.Width;
        var raster = new int[width * recipe.Height];
        var biomes = new byte[raster.Length];
        var rivers = new byte[raster.Length];
        var heights = Enumerable.Repeat(1f, raster.Length).ToArray();
        var coast = width + 1;
        var mountain = 3 * width + 6;
        var tundra = 2 * width + 10;
        var isolated = 2 * width + 20;
        raster[coast] = 1;
        raster[mountain] = 2;
        raster[tundra] = 3;
        raster[isolated] = 4;
        biomes[coast] = (byte)BiomeKind.BorealForest;
        biomes[mountain] = (byte)BiomeKind.Alpine;
        biomes[tundra] = (byte)BiomeKind.Tundra;
        biomes[isolated] = (byte)BiomeKind.Grassland;
        for (var x = 0; x <= 8; x++) heights[x] = 0;
        heights[tundra + 1] = 0;
        rivers[width + 4] = rivers[2 * width + 4] = rivers[3 * width + 5] = 1;
        rivers[isolated + 1] = 1;
        var regions = new[]
        {
            new ProvinceInfo { Id = "Coast", Rgb = 1, Type = "land" },
            new ProvinceInfo { Id = "Mountain", Rgb = 2, Type = "land" },
            new ProvinceInfo { Id = "Tundra", Rgb = 3, Type = "land" },
            new ProvinceInfo { Id = "Isolated river", Rgb = 4, Type = "land" }
        };
        var authored = ProvinceMap.Build(recipe, raster, regions);
        var water = WaterMap.Classify(new HeightmapResult(recipe, heights));
        var map = authored.WithSettlementGeography(water, rivers, biomes);
        Require(authored.Provinces[0].TradeWaterways == null, "Runtime geography must not mutate the authored asset.");
        var key = CampaignState.MapKey(map, recipe.Seed);

        var seaTribe = CampaignState.Found(map, key, coast, "Coastal tribe", 0);
        Require(seaTribe.CanBuild(0, SettlementBuilding.LumberCamp)
            && !seaTribe.CanBuild(0, SettlementBuilding.IronMine) && !seaTribe.CanTradeWith(1),
            "Forest supplies timber, and overseas trade starts closed.");
        Require(seaTribe.TryBuild(0, SettlementBuilding.Docks) && seaTribe.TradeCapacity == 2
            && seaTribe.CanTradeWith(1) && !seaTribe.CanTradeWith(3) && !seaTribe.CanReachIndependentProvince(1),
            "Docks must connect coasts to the same river system, without opening isolated waterways or integration.");

        var alpine = CampaignState.Found(map, key, mountain, "Mountain tribe", 0);
        Require(alpine.CanBuild(1, SettlementBuilding.Docks) && alpine.CanBuild(1, SettlementBuilding.IronMine)
            && alpine.BuildingInfo(1, SettlementBuilding.IronMine).Yield == 2
            && !alpine.CanBuild(1, SettlementBuilding.LumberCamp),
            "A river bank in alpine country must support docks and mines but require forest for lumber.");
        var cold = CampaignState.Found(map, key, tundra, "Tundra tribe", 0);
        Require(!cold.CanBuild(2, SettlementBuilding.Farm) && !cold.CanBuild(2, SettlementBuilding.Docks),
            "Tundra blocks farms, and a lake alone must not qualify for docks.");

        var old = cold.ToSave();
        old.Version = 4;
        old.Buildings[2] = (int)SettlementBuilding.Farm;
        var migrated = CampaignState.Restore(map, key, old, 5);
        Require(migrated.HasBuilding(2, SettlementBuilding.Farm) && migrated.TradeAgreements.Count == 0,
            "A saved building must survive migration even when its site no longer qualifies for new construction.");
    }

    private static void CheckNature()
    {
        Require(MapNatureRules.Pick(BiomeKind.TemperateForest, false, .1f, 0f, 0f, 1f) == NatureKind.None,
            "Water must block placement.");
        Require(MapNatureRules.Pick(BiomeKind.TemperateForest, true, .1f, 0f, .1f, .15f) == NatureKind.None
            && MapNatureRules.Pick(BiomeKind.TemperateForest, true, .1f, 0f, .1f, .8f) == NatureKind.Broadleaf,
            "Forested regions should have both clearings and dense groves.");
        Require(MapNatureRules.Pick(BiomeKind.Grassland, true, .1f, 0f, .4f, .8f) == NatureKind.Broadleaf
            && MapNatureRules.Pick(BiomeKind.Grassland, true, .1f, 0f, .01f, .4f) == NatureKind.None,
            "Grassland should have groves separated by open areas.");
        Require(MapNatureRules.Pick(BiomeKind.TemperateForest, true, .1f, 0f, .55f, .5f) == NatureKind.Broadleaf,
            "Typical woodland should have enough sites to read as a forest.");
        Require(MapNatureRules.Pick(BiomeKind.BorealForest, true, .1f, 0f, .1f, .8f) == NatureKind.Conifer
            && MapNatureRules.Pick(BiomeKind.TemperateForest, true, .85f, 0f, .1f, .8f) == NatureKind.Rock,
            "Species and exposed rocks must follow biome and slope.");
        Require(MapNatureRules.Snow(.95f, .32f, -14f) == 1f && MapNatureRules.Snow(.95f, .32f, 5f) == 0f
            && MapNatureRules.Snow(.40f, .32f, -14f) == 0f, "Persistent snow needs altitude and cold.");

        Require(MapNatureRules.ChooseTree(BiomeKind.TemperateForest, 12f, .65f, .1f) == TreeSpecies.Oak
            && MapNatureRules.ChooseTree(BiomeKind.TemperateForest, 12f, .65f, .95f) == TreeSpecies.Birch
            && MapNatureRules.ChooseTree(BiomeKind.BorealForest, 3f, .64f, .1f) == TreeSpecies.Spruce,
            "Temperate and cold groves should select the expected species.");
        Require(MapNatureRules.ChooseTree(BiomeKind.Grassland, 19f, .45f, .2f) == TreeSpecies.StonePine
            && MapNatureRules.ChooseTree(BiomeKind.Grassland, 19f, .45f, .7f) == TreeSpecies.Olive
            && MapNatureRules.ChooseTree(BiomeKind.Desert, 28f, .24f, .2f) == TreeSpecies.Acacia
            && MapNatureRules.ChooseTree(BiomeKind.TropicalForest, 27f, .68f, .2f) == TreeSpecies.Palm,
            "Warm woodland should select species by climate.");
        Require(MapNatureRules.Snow(.40f, .32f, -25f) > .8f
            && MapNatureRules.LakeIce(-14f) == 1f && MapNatureRules.LakeIce(-2f) == 0f,
            "Severe cold should show lowland snow and lake ice.");

        var grove = MapNatureRules.Grove(17, .43f, .57f);
        Require(grove == MapNatureRules.Grove(17, .43f, .57f) && grove >= 0f && grove <= 1f
            && Math.Abs(grove - MapNatureRules.Grove(17, .431f, .571f)) < .08f,
            "Grove density must be deterministic and continuous.");
    }

    // Verify that smoothing preserves the relationships between cell selection and province borders.
    private static void CheckProvinceShape()
    {
        var riverCrossing = new[] { 0, 0, 1, 1, 8, 8, 8, 8, 0, 0, 1, 1, 9, 9, 9, 9 };
        bool RiverCell(int cell) => cell >= 4 && cell < 8;
        var bridged = MapBorderPaths.FillWaterGaps(4, riverCrossing, RiverCell, cell => riverCrossing[cell] < 2);
        Require(bridged.SequenceEqual(new[] { 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 9, 9, 9, 9 })
            && riverCrossing[4] == 8, "A political line must cross a river without filling ocean or editing imported IDs.");
        var oneProvince = new[] { 0, 0, 8, 8, 8, 8, 0, 0 };
        var joined = MapBorderPaths.FillWaterGaps(2, oneProvince, cell => cell >= 2 && cell < 6,
            cell => oneProvince[cell] == 0);
        Require(joined.All(id => id == 0), "A river inside one province must not gain a separate border.");
        var authored = MapBorderPaths.FillWaterGaps(2, oneProvince, cell => cell >= 2 && cell < 6,
            cell => oneProvince[cell] == 0, cell => oneProvince[cell] == 8);
        Require(ReferenceEquals(authored, oneProvince),
            "An authored river province must retain both of its bank boundaries.");

        var partlyAuthored = new[] { 0, 0, 8, -1, 8, -1, 0, 0 };
        var retained = MapBorderPaths.FillWaterGaps(2, partlyAuthored, cell => cell >= 2 && cell < 6,
            cell => partlyAuthored[cell] == 0, cell => partlyAuthored[cell] == 8);
        Require(retained.SequenceEqual(new[] { 0, 0, 8, 0, 8, 0, 0, 0 }),
            "An authored river ID must not spread to nearby river pixels outside its polygon.");

        var lakes = new[] { 0, 0, 0, 0, 0, 7, 0, 0, 0, 0 };
        var lakeOutline = MapBorderPaths.OutlineLakes(5, lakes, cell => cell == 5 || cell == 6 || cell == 9,
            cell => lakes[cell] == 7);
        Require(lakeOutline.SequenceEqual(new[] { 0, 0, 0, 0, 0, 7, 7, 0, 0, int.MaxValue }),
            "A partly authored lake should get one shore outline and a separate unassigned lake should still get a border.");
        Require(lakes[6] == 0 && lakes[9] == 0, "Lake outlines must not change imported province or territory IDs.");

        var raster = new[] { 0, 1, 1, 1 };
        var shape = new ProvinceShape(2, 2, raster);
        var corner = shape.Corner(4);
        Require(corner.X < 1f && corner.Y < 1f, "The stepped province corner should move inward.");
        Require(raster[shape.CellAt(.98f, .98f)] == 1 && raster[shape.CellAt(.55f, .55f)] == 0,
            "Picking must follow the smoothed edge on both sides of the original raster corner.");
        Require(raster[shape.CellAt(corner.X - .01f, corner.Y - .01f)] == 0
            && raster[shape.CellAt(corner.X + .01f, corner.Y + .01f)] == 1,
            "The shared border corner must delimit the same regions used for selection.");
        Require(shape.CellAt(2f, 2f) == 3 && shape.PackedCorner(0) == 0,
            "The full map perimeter must stay fixed.");

        var junction = new ProvinceShape(2, 2, new[] { 0, 1, 1, 0 });
        Require(junction.PackedCorner(4) == 0, "A four-way junction must stay connected.");

        // Points formed inside each warped cell must resolve to that cell, even across the old raster edge.
        var random = new Random(143);
        const int width = 6;
        const int height = 5;
        for (var trial = 0; trial < 48; trial++)
        {
            var labels = Enumerable.Range(0, width * height).Select(_ => random.Next(-1, 3)).ToArray();
            var warped = new ProvinceShape(width, height, labels);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = y * warped.Stride + x;
                    var a = warped.Corner(i);
                    var b = warped.Corner(i + 1);
                    var c = warped.Corner(i + warped.Stride);
                    var d = warped.Corner(i + warped.Stride + 1);
                    foreach (var u in new[] { .02f, .5f, .98f })
                        foreach (var v in new[] { .02f, .5f, .98f })
                        {
                            var px = Lerp(Lerp(a.X, b.X, u), Lerp(c.X, d.X, u), v);
                            var py = Lerp(Lerp(a.Y, b.Y, u), Lerp(c.Y, d.Y, u), v);
                            Require(warped.CellAt(px, py) == y * width + x, "A warped cell lost an interior point.");
                        }
                }
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    private static void CheckBorderPaths()
    {
        var edges = new HashSet<(int, int)>();
        var junctionEnds = 0;
        MapBorderPaths.Trace(2, 2, new[] { 0, 1, 1, 0 }, (path, closed) =>
        {
            Require(!closed, "A checkerboard path should stop at its junctions.");
            if (path[0] == 4) junctionEnds++;
            if (path[path.Count - 1] == 4) junctionEnds++;
            for (var i = 1; i < path.Count; i++)
                Require(edges.Add((Math.Min(path[i - 1], path[i]), Math.Max(path[i - 1], path[i]))),
                    "A shared border was traced twice.");
        });
        Require(edges.Count == 12 && junctionEnds == 4, "Border tracing must preserve all arms of a four-way junction.");

        var loops = new List<int>();
        MapBorderPaths.Trace(3, 3, new[] { 0, 0, 0, 0, -1, 0, 0, 0, 0 }, (path, closed) =>
        {
            Require(closed && path[0] == path[path.Count - 1], "A border around a hole must close.");
            loops.Add(path.Count - 1);
        });
        Require(loops.OrderBy(n => n).SequenceEqual(new[] { 4, 12 }), "Both the outer outline and hole must be retained.");
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
