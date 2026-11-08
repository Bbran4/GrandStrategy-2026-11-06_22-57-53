using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal static class MapGroundPatterns
    {
        public static Texture2D Create() => Bake("Biome ground patterns", Ground);
        public static Texture2D CreateGrassVariants(bool forest) => forest
            ? Bake("Forest moss, litter and understory", Forest)
            : Bake("Plains short grass, dry grass and meadow", Plains);

        private static Texture2D Bake(string name, System.Func<float, float, Color32> sample)
        {
            const int size = 192;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = name, hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4
            };
            try
            {
                var pixels = texture.GetRawTextureData<Color32>();
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                        pixels[y * size + x] = sample((x + .5f) / size, (y + .5f) / size);

                texture.Apply(true, true);
                return texture;
            }
            catch
            {
                Object.Destroy(texture);
                throw;
            }
        }

        private static Color32 Ground(float u, float v)
        {
            var grain = Noise(u, v, 46, 46, 31);
            var mottling = Noise(u, v, 9, 11, 79);
            var brokenGround = Noise(u, v, 23, 16, 47);
            var sediment = Noise(u, v, 14, 26, 53);
            var damp = Noise(u, v, 7, 13, 57);
            var vegetation = .64f * mottling + .26f * brokenGround + .10f * grain;
            var stone = .55f * brokenGround + .45f * grain;
            var sand = .72f * sediment + .28f * grain;
            var mud = .70f * damp + .30f * brokenGround;
            return new Color32(Byte(vegetation), Byte(stone), Byte(sand), Byte(mud));
        }

        private static Color32 Plains(float u, float v)
        {
            var blades = Noise(u, v, 58, 34, 141);
            var shortGrass = .75f * blades + .25f * Noise(u, v, 29, 29, 143);
            var dryTufts = .62f * Noise(u, v, 19, 27, 149) + .38f * Noise(u, v, 72, 24, 151);
            var meadow = .56f * Noise(u, v, 16, 18, 157) + .44f * Noise(u, v, 47, 63, 163);
            return new Color32(Byte(shortGrass), Byte(dryTufts), Byte(meadow), 255);
        }

        private static Color32 Forest(float u, float v)
        {
            var moss = .74f * Noise(u, v, 14, 17, 173) + .26f * Noise(u, v, 58, 57, 179);
            var litter = .48f * Noise(u, v, 25, 18, 181) + .52f * Noise(u, v, 71, 57, 191);
            var understory = .68f * Noise(u, v, 21, 30, 193) + .32f * Noise(u, v, 43, 55, 197);
            return new Color32(Byte(moss), Byte(litter), Byte(understory), 255);
        }

        public static Rgb24 Palette(BiomeKind kind)
        {
            switch (kind)
            {
                case BiomeKind.DeepOcean: return new Rgb24(52, 72, 81);
                case BiomeKind.ShallowSea: return new Rgb24(70, 95, 101);
                case BiomeKind.Lake: return new Rgb24(73, 103, 108);
                case BiomeKind.River: return new Rgb24(83, 118, 120);
                case BiomeKind.Wetland: return new Rgb24(108, 115, 76);
                case BiomeKind.Desert: return new Rgb24(172, 147, 102);
                case BiomeKind.Grassland: return new Rgb24(140, 149, 93);
                case BiomeKind.TemperateForest: return new Rgb24(109, 125, 76);
                case BiomeKind.TropicalForest: return new Rgb24(94, 118, 73);
                case BiomeKind.BorealForest: return new Rgb24(100, 113, 85);
                case BiomeKind.Tundra: return new Rgb24(143, 149, 123);
                case BiomeKind.Alpine: return new Rgb24(139, 136, 122);
                default: throw new System.ArgumentOutOfRangeException(nameof(kind));
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
