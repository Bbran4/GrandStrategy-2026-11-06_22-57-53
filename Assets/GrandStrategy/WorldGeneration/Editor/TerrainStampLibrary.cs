// Generates grayscale masks for Unity's native Terrain brush palette.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    internal static class TerrainStampLibrary
    {
        private const string Folder = "Assets/GrandStrategy/WorldGeneration/TerrainStamps";
        private const int Resolution = 512;

        [MenuItem("Tools/GrandStrategy/Create Terrain Stamp Library", false, 102)]
        private static void CreateLibrary()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/GrandStrategy/WorldGeneration", "TerrainStamps");

            CreateMask("Jagged Ridge", JaggedRidge);
            CreateMask("Eroded Peak", ErodedPeak);
            CreateMask("Rolling Foothills", RollingFoothills);
            CreateMask("Glacial Valley", GlacialValley);
            CreateMask("River Gorge", RiverGorge);
            CreateMask("Mountain Pass", MountainPass);

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Folder);
        }

        private static void CreateMask(string name, Func<float, float, float> profile)
        {
            var path = $"{Folder}/{name}.png";
            if (File.Exists(path)) return;

            var samples = new float[Resolution * Resolution];
            var peak = 0f;
            for (var y = 0; y < Resolution; y++)
                for (var x = 0; x < Resolution; x++)
                {
                    var u = 2f * x / (Resolution - 1) - 1f;
                    var v = 2f * y / (Resolution - 1) - 1f;
                    // Fade to black before the perimeter so the brush leaves no rectangular seam.
                    var taper = 1f - Smooth(.76f, .98f, new Vector2(u, v).magnitude);
                    var value = taper * Mathf.Max(0f, profile(u, v));
                    samples[y * Resolution + x] = value;
                    peak = Mathf.Max(peak, value);
                }

            var pixels = new Color32[samples.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var gray = (byte)Mathf.RoundToInt(255f * samples[i] / peak);
                pixels[i] = new Color32(gray, gray, gray, 255);
            }

            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static float JaggedRidge(float x, float y)
        {
            var spine = .12f * Mathf.Sin(x * 4.2f) + .04f * Mathf.Sin(x * 14f);
            var cross = y - spine;
            var width = cross > 0f ? .29f : .47f;
            var flank = Mathf.Exp(-2.2f * Mathf.Abs(cross) / width);
            var crest = .42f + .58f * Mathf.PerlinNoise(x * 5f + 18f, 3.6f);
            var gullies = .63f + .37f * Mathf.PerlinNoise(x * 11f + cross * 6f + 22f, y * 4f + 15f);
            return (1f - Smooth(.39f, .94f, Mathf.Abs(x))) * flank * crest * gullies;
        }

        private static float ErodedPeak(float x, float y)
        {
            x = (x + .1f) * 1.05f;
            y = (y - .08f) * 1.28f;
            var radius = new Vector2(x, y).magnitude;
            var angle = Mathf.Atan2(y, x);
            var spurs = .12f * Mathf.Cos(angle * 5f + radius * 5f)
                + .07f * Mathf.Sin(angle * 8f - radius * 6f);
            var slope = Mathf.Pow(Mathf.Clamp01(1f - radius / (.79f + spurs)), 1.45f);
            var gullies = .78f + .22f * Mathf.PerlinNoise(14f + 8f * x, 22f + 8f * y);
            return slope * Mathf.Lerp(1f, gullies, Smooth(.03f, .25f, radius));
        }

        private static float RollingFoothills(float x, float y)
        {
            return .8f * Hill(x + .42f, y + .12f, .31f, .24f)
                + Hill(x + .12f, y - .15f, .33f, .29f)
                + .66f * Hill(x - .33f, y + .09f, .27f, .23f)
                + .47f * Hill(x - .40f, y - .31f, .24f, .21f);
        }

        // These three profiles are positive masks. Unity's Subtract mode makes them cuts.
        private static float GlacialValley(float x, float y)
        {
            var center = .15f * Mathf.Sin(x * 3.4f);
            var distance = Mathf.Abs(y - center);
            var halfFloor = .12f + .05f * Mathf.Cos(x * 4f);
            return (1f - Smooth(halfFloor, halfFloor + .23f, distance))
                * (1f - Smooth(.48f, .93f, Mathf.Abs(x)));
        }

        private static float RiverGorge(float x, float y)
        {
            var center = .15f * Mathf.Sin(x * 5f) + .05f * Mathf.Sin(x * 10f);
            var width = .12f + .025f * Mathf.Cos(x * 7f);
            var section = Mathf.Clamp01(1f - Mathf.Abs(y - center) / width);
            return section * (1f - Smooth(.53f, .94f, Mathf.Abs(x)));
        }

        private static float MountainPass(float x, float y)
        {
            var centre = .10f * x;
            var width = .20f + .26f * Mathf.Abs(x);
            var cut = 1f - Smooth(.03f, width, Mathf.Abs(y - centre));
            return cut * (1f - Smooth(.18f, .81f, Mathf.Abs(x)));
        }

        private static float Hill(float x, float y, float width, float length)
        {
            x /= width;
            y /= length;
            return Mathf.Exp(-(x * x + y * y) * 1.4f);
        }

        private static float Smooth(float start, float end, float value)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
    }
}
