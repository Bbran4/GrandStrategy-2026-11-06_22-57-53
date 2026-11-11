// Holds deterministic placement and climate rules for nature props, snow and lake ice.
using System;

namespace GrandStrategy.WorldGeneration
{
    internal enum TreeSpecies { Oak, Birch, Spruce, StonePine, Olive, Acacia, Palm }

    internal enum NatureKind { None, Broadleaf, Conifer, Rock, Reeds }

    internal static class MapNatureRules
    {
        public static bool IsForest(BiomeKind kind)
            => kind == BiomeKind.TemperateForest || kind == BiomeKind.TropicalForest || kind == BiomeKind.BorealForest;

        // Combine elevation and temperature so high ground and very cold lowlands can both receive snow.
        public static float Snow(float elevation, float seaLevel, float? celsius)
        {
            var altitude = (elevation - seaLevel) / (1f - seaLevel);
            var height = Ramp(altitude, .43f, .76f);
            var cold = celsius.HasValue ? 1f - Ramp(celsius.Value, -10f, 1f) : Ramp(altitude, .65f, .9f);
            var lowland = celsius.HasValue ? 1f - Ramp(celsius.Value, -24f, -15f) : 0f;
            return Math.Max(height * cold, .86f * lowland);
        }

        public static float LakeIce(float? celsius) => celsius.HasValue
            ? 1f - Ramp(celsius.Value, -13f, -4f) : 0f;

        // Select a type of scenery from biome, slope, snow and the supplied random and grove samples.
        public static NatureKind Pick(BiomeKind biome, bool dry, float slope, float snow, float roll, float grove)
        {
            if (!dry) return NatureKind.None;
            if (biome == BiomeKind.Alpine || slope > .65f)
                return slope < 2.2f && roll < .15f ? NatureKind.Rock : NatureKind.None;
            if (biome == BiomeKind.Wetland && slope < .25f && snow < .25f)
                return roll < .25f && grove > .36f ? NatureKind.Reeds : NatureKind.None;
            if (IsForest(biome) && roll < Ramp(grove, .22f, .60f) * .95f * (1f - .48f * snow))
                return biome == BiomeKind.BorealForest || snow > .25f ? NatureKind.Conifer : NatureKind.Broadleaf;
            if (biome == BiomeKind.Desert && slope < .3f && grove > .62f && roll < .11f)
                return NatureKind.Broadleaf;
            if (biome == BiomeKind.Grassland && roll < Ramp(grove, .49f, .72f) * .75f * (1f - snow))
                return NatureKind.Broadleaf;
            return slope > .28f && roll > .98f ? NatureKind.Rock : NatureKind.None;
        }

        // Pick a tree species suited to the local biome and placement conditions.
        public static TreeSpecies ChooseTree(BiomeKind biome, float temperature, float moisture, float roll)
        {
            if (biome == BiomeKind.BorealForest || temperature < 6f)
                return roll < .76f ? TreeSpecies.Spruce : TreeSpecies.Birch;
            if (temperature >= 23f)
            {
                if (moisture < .40f) return roll < .84f ? TreeSpecies.Acacia : TreeSpecies.Olive;
                return roll < .62f ? TreeSpecies.Palm : TreeSpecies.Oak;
            }
            if (temperature >= 15f && moisture < .60f)
                return roll < .53f ? TreeSpecies.StonePine : roll < .86f ? TreeSpecies.Olive : TreeSpecies.Oak;
            return roll < .69f ? TreeSpecies.Oak : TreeSpecies.Birch;
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

        // Hash coordinates and a seed to produce repeatable variation without sharing a random number
        // generator.
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
