using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    /// <summary>Run with -batchmode -executeMethod GrandStrategy.WorldGeneration.Editor.WorldGenerationSmokeChecks.Run.</summary>
    public static class WorldGenerationSmokeChecks
    {
        public static void Run()
        {
            var path = "Assets/WorldGenerationSmoke-" + Guid.NewGuid().ToString("N") + ".asset";
            WorldHeightmap asset = null;
            try
            {
                var recipe = HeightmapRecipe.OldWorld();
                var world = HeightmapGenerator.Generate(recipe);
                var expected = world.Elevations[123456];
                var json = JsonUtility.ToJson(recipe);
                var restored = JsonUtility.FromJson<HeightmapRecipe>(json); restored.Validate();
                var regenerated = HeightmapGenerator.Generate(restored);
                for (var i = 0; i < world.Elevations.Length; i++)
                    if (world.Elevations[i] != regenerated.Elevations[i]) throw new Exception("Recipe JSON changed generation.");
                var network = RiverGenerator.Generate(world, RiverGenerator.MinimumCatchmentPercent);
                if (network.ChannelCells == 0) throw new Exception("The smoke-check world did not generate rivers.");
                const float authoredHeightScale = .17f;
                asset = ScriptableObject.CreateInstance<WorldHeightmap>();
                if (asset.HeightScale != WorldHeightmap.DefaultHeightScale)
                    throw new Exception("An asset without a serialized height scale should use the original preview default.");
                var authoredClimate = ClimateGenerator.Generate(network.Terrain, WaterMap.Classify(network.Terrain),
                    new ClimateSettings { Wind = PrevailingWind.NorthToSouth });
                var authoredBiomes = BiomeGenerator.Generate(authoredClimate, WaterMap.Classify(network.Terrain), network);
                CheckRecipeRoundTrip(recipe, network, authoredClimate, authoredBiomes);
                asset.Initialize(network.Terrain, authoredHeightScale, network, authoredClimate, authoredBiomes);
                AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
                Resources.UnloadAsset(asset); asset = null;
                asset = AssetDatabase.LoadAssetAtPath<WorldHeightmap>(path);
                var loaded = asset.Read();
                var loadedNetwork = asset.ReadRivers();
                if (loadedNetwork == null || loadedNetwork.Base.Elevations[123456] != expected
                    || loadedNetwork.ChannelCells != network.ChannelCells || loaded.Elevations.Length != 1024 * 512)
                    throw new Exception("River asset disk round-trip failed.");
                for (var i = 0; i < loaded.Elevations.Length; i++)
                    if (loaded.Elevations[i] != network.Terrain.Elevations[i]
                        || loadedNetwork.CutElevations[i] != network.CutElevations[i]
                        || loadedNetwork.Directions[i] != network.Directions[i]
                        || loadedNetwork.Widths[i] != network.Widths[i])
                        throw new Exception("Saved river geometry, cut layer, direction, or width changed.");
                if (asset.HeightScale != authoredHeightScale)
                    throw new Exception("Terrain height scale did not survive the asset disk round-trip.");
                var output = Path.Combine(Path.GetTempPath(), "GrandStrategy-worldgen-qa");
                Directory.CreateDirectory(output);
                var water = WaterMap.Classify(loadedNetwork.Terrain);
                var access = RiverNavigation.Build(loadedNetwork, water, RiverNavigation.DefaultMaximumRise);
                var savedClimateSettings = asset.ReadClimateSettings();
                if (savedClimateSettings == null || savedClimateSettings.Wind != PrevailingWind.NorthToSouth)
                    throw new Exception("Climate settings did not survive the asset disk round-trip.");
                var climate = ClimateGenerator.Generate(loadedNetwork.Terrain, water, savedClimateSettings);
                for (var i = 0; i < climate.Moisture.Length; i++)
                    if (climate.Temperature[i] != authoredClimate.Temperature[i]
                        || climate.Moisture[i] != authoredClimate.Moisture[i])
                        throw new Exception("Loaded climate differs from the authored climate.");
                if (!asset.HasBiomes) throw new Exception("Biome settings were not retained.");
                var biomes = BiomeGenerator.Generate(climate, water, loadedNetwork);
                for (var i = 0; i < biomes.Samples.Length; i++)
                    if (biomes.Samples[i] != authoredBiomes.Samples[i])
                        throw new Exception("Loaded biomes differ from the authored biomes.");
                foreach (HeightmapPreviewMode mode in Enum.GetValues(typeof(HeightmapPreviewMode)))
                {
                    var texture = HeightmapPreview.Create(loadedNetwork.Terrain, mode, water, loadedNetwork, access, climate,
                        dataColors: HeightmapPreview.IsClimateMode(mode), biomes: biomes);
                    try
                    {
                        var png = texture.EncodeToPNG();
                        if (png.Length < 1000) throw new Exception("Preview PNG unexpectedly empty.");
                        if (mode == HeightmapPreviewMode.LandAndWater)
                            CheckMaskPixels(png, water);
                        if (mode == HeightmapPreviewMode.NavalAccess)
                            CheckMaskPixels(png, water, access.CopyCells());
                        if (HeightmapPreview.IsClimateMode(mode))
                            CheckClimatePixels(png, loadedNetwork.Terrain,
                                mode == HeightmapPreviewMode.Temperature ? climate.Temperature : climate.Moisture);
                        File.WriteAllBytes(Path.Combine(output, mode + ".png"), png);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
                CheckTerrainPng(output, biomes);
                CheckTerrainPng(output, biomes, access);
                CheckOpenGsPng(output);
                var mesh = HeightmapMesh.Create(loaded, asset.HeightScale);
                try
                {
                    if (mesh.vertexCount != 257 * 129 || Math.Abs(mesh.bounds.size.x - 2) > .00001f || Math.Abs(mesh.bounds.size.z - 1) > .00001f)
                        throw new Exception("3D preview geometry dimensions incorrect.");
                    foreach (var normal in mesh.normals)
                        if (normal.y < 0) throw new Exception("3D preview triangles point downward.");
                    if (mesh.bounds.min.y < -.00001f) throw new Exception("Preview water must be clamped to its surface.");
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
                WorldGeneratorWindow.Open();
                Debug.Log($"WORLDGEN_SMOKE_PASS: 1024x512 seed={recipe.Seed} land={world.LandFraction:P1}; recipe regeneration, disk asset round-trip, preview modes including rivers, exact OpenGS PNG round-trip, 3D mesh, window creation. PNGs: {output}");
            }
            finally
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                else if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static void CheckOpenGsPng(string output)
        {
            var recipe = HeightmapRecipe.OldWorld(); recipe.Width = 32; recipe.Height = 32;
            recipe.Steps.Clear();
            var hill = HeightmapRecipe.Step(HeightmapOperation.Hill, 1, 1, .65f, .65f, .5f);
            hill.XMin = hill.XMax = hill.YMin = hill.YMax = .5f;
            var basin = HeightmapRecipe.Step(HeightmapOperation.Pit, 1, 1, .8f, .8f, .1f);
            basin.XMin = basin.XMax = basin.YMin = basin.YMax = .5f;
            recipe.Steps.Add(hill); recipe.Steps.Add(basin);
            var world = HeightmapGenerator.Generate(recipe); var water = WaterMap.Classify(world);
            if (water.LandCount == 0 || water.LakeCount == 0 || water.OceanCount == 0)
                throw new Exception("OpenGS PNG check requires all three surface types.");
            var texture = HeightmapPreview.Create(world, HeightmapPreviewMode.LandAndWater, water);
            try
            {
                var bytes = texture.EncodeToPNG();
                CheckMaskPixels(bytes, water);
                File.WriteAllBytes(Path.Combine(output, "OpenGS-sentinel.png"), bytes);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void CheckRecipeRoundTrip(HeightmapRecipe terrain, RiverMap rivers, ClimateMap climate, BiomeMap biomes)
        {
            var file = new WorldRecipe
            {
                Format = WorldRecipe.FormatId,
                Version = WorldRecipe.CurrentVersion,
                Recipe = terrain,
                HeightScale = .17f,
                BuildRivers = true,
                RiverCatchment = rivers.CatchmentPercent,
                RiverDepth = rivers.CarveDepth,
                RiverWidthScale = rivers.WidthScale,
                BuildClimate = true,
                Climate = climate.Settings,
                BuildBiomes = true,
                MaximumNavalRise = .12f,
                ExportNavalMask = true
            };
            var restored = JsonUtility.FromJson<WorldRecipe>(JsonUtility.ToJson(file));
            if (restored.HeightScale != file.HeightScale || restored.MaximumNavalRise != file.MaximumNavalRise
                || !restored.ExportNavalMask || restored.Climate.Wind != file.Climate.Wind)
                throw new Exception("World recipe lost its saved controls.");
            var rebuilt = restored.Rebuild();
            if (rebuilt.Rivers == null || rebuilt.Climate == null || rebuilt.Biomes == null)
                throw new Exception("World recipe lost its saved stages.");
            for (var i = 0; i < rebuilt.Terrain.Elevations.Length; i++)
                if (rebuilt.Terrain.Elevations[i] != rivers.Terrain.Elevations[i]
                    || rebuilt.Rivers.Widths[i] != rivers.Widths[i]
                    || rebuilt.Climate.Temperature[i] != climate.Temperature[i]
                    || rebuilt.Climate.Moisture[i] != climate.Moisture[i]
                    || rebuilt.Biomes.Samples[i] != biomes.Samples[i])
                    throw new Exception("World recipe JSON did not reproduce all world layers.");
        }

        private static void CheckTerrainPng(string output, BiomeMap biomes, RiverNavigation access = null)
        {
            var texture = HeightmapPreview.Create(biomes.Source, HeightmapPreviewMode.Biomes,
                navigation: access, dataColors: true, biomes: biomes);
            try
            {
                var bytes = texture.EncodeToPNG();
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    if (!ImageConversion.LoadImage(decoded, bytes)
                        || decoded.width != biomes.Source.Recipe.Width || decoded.height != biomes.Source.Recipe.Height)
                        throw new Exception("OpenGS Terrain PNG has incorrect dimensions or could not be decoded.");
                    var pixels = decoded.GetPixels32();
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var expected = biomes.OpenGsTerrainColour(i, access != null && access[i] != 0);
                        var pixel = pixels[i];
                        if (pixel.r != expected.R || pixel.g != expected.G || pixel.b != expected.B || pixel.a != 255)
                            throw new Exception($"Wrong OpenGS Terrain colour at sample {i}.");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(decoded); }
                File.WriteAllBytes(Path.Combine(output, access == null ? "terrain.png" : "terrain-naval.png"), bytes);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void CheckClimatePixels(byte[] png, HeightmapResult terrain, byte[] samples)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(decoded, png)
                    || decoded.width != terrain.Recipe.Width || decoded.height != terrain.Recipe.Height)
                    throw new Exception("Climate PNG has incorrect dimensions or could not be decoded.");
                var pixels = decoded.GetPixels32();
                for (var i = 0; i < samples.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.r != samples[i] || pixel.g != samples[i] || pixel.b != samples[i] || pixel.a != 255)
                        throw new Exception($"Wrong climate grayscale value at sample {i}.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
        }

        private static void CheckMaskPixels(byte[] png, WaterMap water, byte[] riverCells = null)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(decoded, png)) throw new Exception("Could not decode OpenGS PNG.");
                if (decoded.width != water.Source.Recipe.Width || decoded.height != water.Source.Recipe.Height)
                    throw new Exception("OpenGS PNG dimensions changed.");
                var pixels = decoded.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var kind = water[i];
                    var expected = kind == WaterKind.Lake ? new Color32(0, 255, 0, 255)
                        : kind == WaterKind.Ocean || (riverCells != null && riverCells[i] != 0)
                            ? new Color32(5, 20, 18, 255)
                            : new Color32(255, 255, 255, 255);
                    var actual = pixels[i];
                    if (actual.r != expected.r || actual.g != expected.g || actual.b != expected.b || actual.a != 255)
                        throw new Exception($"Wrong OpenGS RGBA at sample {i}.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
        }
    }
}
