using Unity.Collections;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    // The hard biome raster describes gameplay. These two fields soften only the painted surface.
    internal static class MapGroundBlend
    {
        private static readonly Color32[] MaterialChannels =
        {
            new Color32(255, 0, 0, 0), new Color32(0, 255, 0, 0),
            new Color32(0, 0, 255, 0), new Color32(0, 0, 0, 255)
        };

        public static void Bake(int width, int height, NativeArray<Color32> colours, NativeArray<Color32> surface,
            out Texture2D paletteTexture, out Texture2D lowMaterialTexture, out Texture2D highMaterialTexture)
        {
            var fieldWidth = Mathf.Min(width, 1024);
            var fieldHeight = Mathf.Min(height, 512);
            var count = fieldWidth * fieldHeight;
            var palette = new Color32[count];
            var lowWeights = new Color32[count];
            var highWeights = new Color32[count];
            var scratch = new Color32[count];
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            for (var y = 0; y < fieldHeight; y++)
                for (var x = 0; x < fieldWidth; x++)
                {
                    var sx = (int)((x + .5f) * width / fieldWidth);
                    var sy = (int)((y + .5f) * height / fieldHeight);
                    var i = sy * width + sx;
                    var colour = colours[i];
                    if (colour.a == 0) continue;

                    var target = y * fieldWidth + x;
                    palette[target] = linear ? (Color32)((Color)colour).linear : colour;
                    // The eight land IDs are contiguous, with four channels in each field.
                    var material = surface[i].r - (byte)BiomeKind.Wetland;
                    if (material < MaterialChannels.Length)
                        lowWeights[target] = MaterialChannels[material];
                    else
                        highWeights[target] = MaterialChannels[material - MaterialChannels.Length];
                }

            var radiusY = Mathf.Max(1, Mathf.RoundToInt(fieldHeight * .016f));
            var radiusX = Mathf.Max(1, Mathf.RoundToInt(fieldWidth * height / (float)width * .016f));
            Smooth(palette);
            Smooth(lowWeights);
            Smooth(highWeights);
            paletteTexture = null;
            lowMaterialTexture = null;
            highMaterialTexture = null;
            try
            {
                paletteTexture = Upload(palette, "Blended land palette");
                lowMaterialTexture = Upload(lowWeights, "Blended wetland, desert, grass and temperate forest");
                highMaterialTexture = Upload(highWeights, "Blended tropical forest, boreal forest, tundra and alpine");
            }
            catch
            {
                Object.Destroy(paletteTexture);
                Object.Destroy(lowMaterialTexture);
                Object.Destroy(highMaterialTexture);
                paletteTexture = null;
                lowMaterialTexture = null;
                highMaterialTexture = null;
                throw;
            }

            void Smooth(Color32[] field)
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    Box(field, scratch, fieldHeight, fieldWidth, fieldWidth, 1, radiusX);
                    Box(scratch, field, fieldWidth, fieldHeight, 1, fieldWidth, radiusY);
                }
            }

            Texture2D Upload(Color32[] field, string name)
            {
                var texture = new Texture2D(fieldWidth, fieldHeight, TextureFormat.RGBA32, true, true)
                {
                    name = name, hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                };
                try
                {
                    texture.SetPixels32(field);
                    texture.Apply(true, true);
                    return texture;
                }
                catch
                {
                    Object.Destroy(texture);
                    throw;
                }
            }
        }

        private static void Box(Color32[] source, Color32[] target, int lines, int length,
            int lineStride, int pixelStride, int radius)
        {
            var scale = 1f / (2 * radius + 1);
            for (var line = 0; line < lines; line++)
            {
                var start = line * lineStride;
                var sum = Vector4.zero;
                for (var i = -radius; i <= radius; i++) sum += Pixel(i);
                for (var i = 0; i < length; i++)
                {
                    var average = sum * scale;
                    target[start + i * pixelStride] = new Color32((byte)Mathf.RoundToInt(average.x),
                        (byte)Mathf.RoundToInt(average.y), (byte)Mathf.RoundToInt(average.z), (byte)Mathf.RoundToInt(average.w));
                    sum += Pixel(i + radius + 1) - Pixel(i - radius);
                }

                Vector4 Pixel(int i)
                {
                    var p = source[start + Mathf.Clamp(i, 0, length - 1) * pixelStride];
                    return new Vector4(p.r, p.g, p.b, p.a);
                }
            }
        }
    }
}
