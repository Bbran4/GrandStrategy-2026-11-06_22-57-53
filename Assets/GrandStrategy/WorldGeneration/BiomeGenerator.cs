using System;

namespace GrandStrategy.WorldGeneration
{
    // Exported IDs are append-only.
    public enum BiomeKind : byte
    {
        DeepOcean, ShallowSea, Lake, River, Wetland, Desert, Grassland,
        TemperateForest, TropicalForest, BorealForest, Tundra, Alpine
    }

    public sealed class BiomeMap
    {
        public ClimateMap Climate { get; }
        public HeightmapResult Source => Climate.Source;
        public byte[] Samples { get; }
        public BiomeKind this[int index] => (BiomeKind)Samples[index];

        internal BiomeMap(ClimateMap climate, byte[] samples)
        {
            Climate = climate;
            Samples = samples;
        }

        public Rgb24 OpenGsTerrainColour(int index, bool navigableRiver = false)
        {
            var biome = this[index];
            if (biome == BiomeKind.Lake) return new Rgb24(58, 91, 255);
            if (biome == BiomeKind.DeepOcean) return new Rgb24(2, 38, 150);
            if (biome == BiomeKind.ShallowSea || navigableRiver) return new Rgb24(56, 118, 217);

            var relief = BiomeGenerator.Relief(Source, index);
            if (biome == BiomeKind.Alpine || relief >= .62f) return new Rgb24(157, 192, 208);
            if (biome == BiomeKind.River || biome == BiomeKind.Wetland) return new Rgb24(76, 96, 35);
            if (relief >= .33f) return new Rgb24(248, 255, 153);
            switch (biome)
            {
                case BiomeKind.TropicalForest: return new Rgb24(127, 191, 0);
                case BiomeKind.TemperateForest:
                case BiomeKind.BorealForest: return new Rgb24(89, 199, 85);
                case BiomeKind.Desert: return new Rgb24(255, 127, 0);
                default: return new Rgb24(255, 129, 66);
            }
        }

        public static Rgb24 PreviewColour(BiomeKind kind)
        {
            switch (kind)
            {
                case BiomeKind.DeepOcean: return new Rgb24(17, 48, 86);
                case BiomeKind.ShallowSea: return new Rgb24(32, 101, 144);
                case BiomeKind.Lake: return new Rgb24(63, 133, 179);
                case BiomeKind.River: return new Rgb24(56, 163, 200);
                case BiomeKind.Wetland: return new Rgb24(95, 130, 88);
                case BiomeKind.Desert: return new Rgb24(213, 163, 94);
                case BiomeKind.Grassland: return new Rgb24(165, 177, 91);
                case BiomeKind.TemperateForest: return new Rgb24(59, 114, 60);
                case BiomeKind.TropicalForest: return new Rgb24(29, 130, 69);
                case BiomeKind.BorealForest: return new Rgb24(57, 97, 83);
                case BiomeKind.Tundra: return new Rgb24(160, 177, 166);
                case BiomeKind.Alpine: return new Rgb24(196, 187, 171);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public static class BiomeGenerator
    {
        public static BiomeMap Generate(ClimateMap climate, WaterMap water, RiverMap rivers = null,
            Func<float, string, bool> progress = null)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            var terrain = climate.Source;
            if (water == null || !ReferenceEquals(water.Source, terrain))
                throw new ArgumentException("Water classification must describe the climate terrain.", nameof(water));
            if (rivers != null && !ReferenceEquals(rivers.Terrain, terrain))
                throw new ArgumentException("The rivers must describe the climate terrain.", nameof(rivers));

            var width = terrain.Recipe.Width;
            var height = terrain.Recipe.Height;
            var sea = terrain.Recipe.SeaLevel;
            var count = terrain.Elevations.Length;
            var riverSurface = rivers?.BuildSurfaceMask();
            var distance = new byte[count];
            var samples = new byte[count];
            // A short freshwater influence band scales with the map's raster resolution.
            var radius = Math.Max(1, Math.Min(8, Math.Min(width, height) / 256));
            var outside = radius + 1;

            for (var y = 0; y < height; y++)
            {
                if ((y & 31) == 0) Report(progress, .3f * y / height, "Measuring freshwater proximity");
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var freshwater = water[i] == WaterKind.Lake || (riverSurface != null && riverSurface[i] != 0);
                    var d = freshwater ? 0 : outside;
                    if (x > 0) d = Math.Min(d, distance[i - 1] + 1);
                    if (y > 0) d = Math.Min(d, distance[i - width] + 1);
                    distance[i] = (byte)d;
                }
            }
            for (var y = height - 1; y >= 0; y--)
            {
                if (((height - 1 - y) & 31) == 0)
                    Report(progress, .3f + .3f * (height - 1 - y) / height, "Completing freshwater proximity");
                for (var x = width - 1; x >= 0; x--)
                {
                    var i = y * width + x;
                    if (x < width - 1) distance[i] = (byte)Math.Min(distance[i], distance[i + 1] + 1);
                    if (y < height - 1) distance[i] = (byte)Math.Min(distance[i], distance[i + width] + 1);
                }
            }

            for (var y = 0; y < height; y++)
            {
                if ((y & 31) == 0) Report(progress, .6f + .4f * y / height, "Assigning biomes");
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    BiomeKind biome;
                    if (water[i] == WaterKind.Lake) biome = BiomeKind.Lake;
                    else if (water[i] == WaterKind.Ocean)
                        biome = terrain.Elevations[i] >= sea * .7f ? BiomeKind.ShallowSea : BiomeKind.DeepOcean;
                    else if (riverSurface != null && riverSurface[i] != 0) biome = BiomeKind.River;
                    else
                    {
                        var nearWater = distance[i] <= radius;
                        var moisture = climate.Moisture[i] / 255f + .20f * (1f - distance[i] / (float)outside);
                        biome = LandBiome(climate.TemperatureCelsius(i), moisture, Relief(terrain, i), nearWater);
                    }
                    samples[i] = (byte)biome;
                }
            }

            Report(progress, 1f, "Biomes complete");
            return new BiomeMap(climate, samples);
        }

        internal static float Relief(HeightmapResult terrain, int index) =>
            Math.Max(0f, terrain.Elevations[index] - terrain.Recipe.SeaLevel) / (1f - terrain.Recipe.SeaLevel);

        private static BiomeKind LandBiome(float temperature, float moisture, float relief, bool nearWater)
        {
            if (relief >= .62f && temperature < 8f) return BiomeKind.Alpine;
            if (temperature <= 0f) return BiomeKind.Tundra;
            if (nearWater && relief < .18f && temperature > 4f && moisture >= .62f) return BiomeKind.Wetland;
            if (temperature >= 12f && moisture < .24f) return BiomeKind.Desert;
            if (moisture < .47f) return BiomeKind.Grassland;
            if (temperature < 10f) return BiomeKind.BorealForest;
            if (temperature >= 22f && moisture >= .65f) return BiomeKind.TropicalForest;
            return BiomeKind.TemperateForest;
        }

        private static void Report(Func<float, string, bool> progress, float fraction, string stage)
        {
            if (progress != null && !progress(fraction, stage)) throw new OperationCanceledException();
        }
    }
}
