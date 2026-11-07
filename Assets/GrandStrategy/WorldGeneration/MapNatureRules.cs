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
                return slope < 2.2f && roll < .30f ? NatureKind.Rock : NatureKind.None;
            if (biome == BiomeKind.Wetland && slope < .25f && snow < .25f)
                return roll < .50f ? NatureKind.Reeds : NatureKind.None;
            if (IsForest(biome) && roll < (.32f + .48f * grove) * (1f - .8f * snow))
                return biome == BiomeKind.BorealForest || snow > .25f ? NatureKind.Conifer : NatureKind.Broadleaf;
            if (biome == BiomeKind.Grassland && snow < .15f && grove > .78f && roll < .12f)
                return NatureKind.Broadleaf;
            return roll > .97f ? NatureKind.Rock : NatureKind.None;
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
