using System;

namespace GrandStrategy.WorldGeneration
{
    internal enum NatureKind { None, Broadleaf, Conifer, Rock, Reeds }

    internal static class MapNatureRules
    {
        public static bool IsForest(BiomeKind kind)
            => kind == BiomeKind.TemperateForest || kind == BiomeKind.TropicalForest || kind == BiomeKind.BorealForest;

        public static float Snow(float elevation, float seaLevel, float? celsius)
        {
            var altitude = (elevation - seaLevel) / (1f - seaLevel);
            var height = Ramp(altitude, .43f, .76f);
            var cold = celsius.HasValue ? 1f - Ramp(celsius.Value, -10f, 1f) : Ramp(altitude, .65f, .9f);
            return height * cold;
        }

        public static NatureKind Pick(BiomeKind biome, bool dry, float slope, float snow, float roll, float grove)
        {
            if (!dry) return NatureKind.None;
            if (biome == BiomeKind.Alpine || slope > .65f)
                return slope < 2.2f && roll < .15f ? NatureKind.Rock : NatureKind.None;
            if (biome == BiomeKind.Wetland && slope < .25f && snow < .25f)
                return roll < .25f && grove > .36f ? NatureKind.Reeds : NatureKind.None;
            if (IsForest(biome) && roll < Ramp(grove, .31f, .68f) * .88f * (1f - .72f * snow))
                return biome == BiomeKind.BorealForest || snow > .25f ? NatureKind.Conifer : NatureKind.Broadleaf;
            if (biome == BiomeKind.Grassland && snow < .15f && grove > .74f && roll < .34f)
                return NatureKind.Broadleaf;
            return slope > .28f && roll > .98f ? NatureKind.Rock : NatureKind.None;
        }

        public static float Grove(int seed, float x, float y)
        {
            var regional = Field(seed ^ 739, x * 13f, y * 13f);
            var pockets = Field(seed ^ 983, x * 41f, y * 41f);
            return .72f * regional + .28f * pockets;
        }

        private static float Field(int seed, float x, float y)
        {
            var ix = (int)MathF.Floor(x);
            var iy = (int)MathF.Floor(y);
            var tx = Ramp(x - ix, 0f, 1f);
            var ty = Ramp(y - iy, 0f, 1f);
            var a = Mix(Random01(seed, ix, iy), Random01(seed, ix + 1, iy), tx);
            var b = Mix(Random01(seed, ix, iy + 1), Random01(seed, ix + 1, iy + 1), tx);
            return Mix(a, b, ty);

            static float Mix(float a, float b, float t) => a + (b - a) * t;
        }

        public static float Random01(int seed, int x, int y)
        {
            unchecked
            {
                var hash = (uint)seed ^ ((uint)x * 0x9e3779b9u) ^ ((uint)y * 0x85ebca6bu);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return (hash >> 8) / 16777216f;
            }
        }

        private static float Ramp(float value, float low, float high)
        {
            var t = Math.Clamp((value - low) / (high - low), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
