using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal static class MapGroundPatterns
    {
        public static Texture2D Create()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Biome ground patterns", hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4
            };
            try
            {
                var pixels = texture.GetRawTextureData<Color32>();
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var u = (x + .5f) / size;
                        var v = (y + .5f) / size;
                        var grain = Noise(u, v, 48, 48, 31);
                        var clumps = Noise(u, v, 7, 7, 79);
                        var forest = .65f * clumps + .35f * grain;
                        var grass = .55f * Noise(u, v, 32, 11, 47) + .45f * grain;
                        var dunes = .5f + .5f * Mathf.Sin(2f * Mathf.PI * (6f * v + .9f * clumps));
                        var desert = .65f * dunes + .35f * grain;
                        var wetland = .7f * Noise(u, v, 12, 6, 57) + .3f * grain;
                        pixels[y * size + x] = new Color32(Byte(forest), Byte(grass), Byte(desert), Byte(wetland));
                    }

                texture.Apply(true, true);
                return texture;
            }
            catch
            {
                Object.Destroy(texture);
                throw;
            }
        }

        private static byte Byte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        private static float Noise(float u, float v, int columns, int rows, int seed)
        {
            var x = Mathf.FloorToInt(u * columns);
            var y = Mathf.FloorToInt(v * rows);
            var tx = Mathf.SmoothStep(0f, 1f, u * columns - x);
            var ty = Mathf.SmoothStep(0f, 1f, v * rows - y);
            var nextX = (x + 1) % columns;
            var nextY = (y + 1) % rows;
            var a = Mathf.Lerp(MapNatureRules.Random01(seed, x, y), MapNatureRules.Random01(seed, nextX, y), tx);
            var b = Mathf.Lerp(MapNatureRules.Random01(seed, x, nextY), MapNatureRules.Random01(seed, nextX, nextY), tx);
            return Mathf.Lerp(a, b, ty);
        }
    }
}
