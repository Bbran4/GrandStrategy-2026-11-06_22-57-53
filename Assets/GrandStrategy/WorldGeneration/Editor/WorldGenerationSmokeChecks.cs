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
                asset = ScriptableObject.CreateInstance<WorldHeightmap>(); asset.Initialize(world);
                AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
                Resources.UnloadAsset(asset); asset = null;
                asset = AssetDatabase.LoadAssetAtPath<WorldHeightmap>(path);
                var loaded = asset.Read();
                if (loaded.Elevations[123456] != expected || loaded.Elevations.Length != 1024 * 512)
                    throw new Exception("Asset disk round-trip failed.");
                var output = Path.Combine(Path.GetTempPath(), "GrandStrategy-worldgen-qa");
                Directory.CreateDirectory(output);
                foreach (HeightmapPreviewMode mode in Enum.GetValues(typeof(HeightmapPreviewMode)))
                {
                    var texture = HeightmapPreview.Create(loaded, mode);
                    try
                    {
                        var png = texture.EncodeToPNG();
                        if (png.Length < 1000) throw new Exception("Preview PNG unexpectedly empty.");
                        File.WriteAllBytes(Path.Combine(output, mode + ".png"), png);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                }
                WorldGeneratorWindow.Open();
                Debug.Log($"WORLDGEN_SMOKE_PASS: 1024x512 seed={recipe.Seed} land={world.LandFraction:P1}; recipe regeneration, disk asset round-trip, three preview modes, window creation. PNGs: {output}");
            }
            finally
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                else if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
