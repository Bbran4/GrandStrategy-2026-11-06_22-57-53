using System;

namespace GrandStrategy.WorldGeneration
{
    public enum PrevailingWind { WestToEast, EastToWest, SouthToNorth, NorthToSouth }

    [Serializable]
    public sealed class ClimateSettings
    {
        public float Equator = .15f;
        public float ElevationCooling = 30f;
        public float MoistureReach = .28f;
        public PrevailingWind Wind = PrevailingWind.WestToEast;

        public ClimateSettings Copy() => (ClimateSettings)MemberwiseClone();

        public void Validate()
        {
            if (!(Equator >= 0f && Equator <= 1f))
                throw new ArgumentOutOfRangeException(nameof(Equator));
            if (!(ElevationCooling >= 0f && ElevationCooling <= 50f))
                throw new ArgumentOutOfRangeException(nameof(ElevationCooling));
            if (!(MoistureReach >= .05f && MoistureReach <= 1f))
                throw new ArgumentOutOfRangeException(nameof(MoistureReach));
            if ((int)Wind < (int)PrevailingWind.WestToEast || (int)Wind > (int)PrevailingWind.NorthToSouth)
                throw new ArgumentOutOfRangeException(nameof(Wind));
        }
    }

    public sealed class ClimateMap
    {
        public const float MinimumTemperature = -40f;
        public const float MaximumTemperature = 45f;

        public HeightmapResult Source { get; }
        public ClimateSettings Settings { get; }
        public byte[] Temperature { get; }
        public byte[] Moisture { get; }

        internal ClimateMap(HeightmapResult source, ClimateSettings settings, byte[] temperature, byte[] moisture)
        {
            Source = source;
            Settings = settings;
            Temperature = temperature;
            Moisture = moisture;
        }

        public float TemperatureCelsius(int index) =>
            MinimumTemperature + Temperature[index] / 255f * (MaximumTemperature - MinimumTemperature);
    }

    public static class ClimateGenerator
    {
        public static ClimateMap Generate(HeightmapResult terrain, WaterMap water, ClimateSettings settings,
            Func<float, string, bool> progress = null)
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (water == null || !ReferenceEquals(water.Source, terrain))
                throw new ArgumentException("Water classification must describe the climate source terrain.", nameof(water));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            settings = settings.Copy();

            var width = terrain.Recipe.Width;
            var height = terrain.Recipe.Height;
            var sea = terrain.Recipe.SeaLevel;
            var count = terrain.Elevations.Length;
            var distance = new int[count];
            var temperature = new byte[count];
            var moisture = new byte[count];
            var far = width + height;
            var latitudeSpan = Math.Max(settings.Equator, 1f - settings.Equator);

            // Two Manhattan-distance passes find the closest ocean sample without a search per pixel.
            for (var y = 0; y < height; y++)
            {
                if ((y & 31) == 0) Report(progress, .35f * y / height, "Measuring distance from the ocean");
                var latitude = Math.Abs(y / (float)(height - 1) - settings.Equator) / latitudeSpan;
                var seaTemperature = 30f - 42f * latitude;
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var aboveSea = Math.Max(0f, terrain.Elevations[i] - sea) / (1f - sea);
                    var celsius = seaTemperature - aboveSea * settings.ElevationCooling;
                    temperature[i] = Encode((celsius - ClimateMap.MinimumTemperature)
                        / (ClimateMap.MaximumTemperature - ClimateMap.MinimumTemperature));

                    var value = water[i] == WaterKind.Ocean ? 0 : far;
                    if (x > 0) value = Math.Min(value, distance[i - 1] + 1);
                    if (y > 0) value = Math.Min(value, distance[i - width] + 1);
                    distance[i] = value;
                }
            }
            for (var y = height - 1; y >= 0; y--)
            {
                if (((height - 1 - y) & 31) == 0)
                    Report(progress, .35f + .25f * (height - 1 - y) / height, "Completing coastal distance");
                for (var x = width - 1; x >= 0; x--)
                {
                    var i = y * width + x;
                    if (x < width - 1) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
                    if (y < height - 1) distance[i] = Math.Min(distance[i], distance[i + width] + 1);
                }
            }

            var alongX = settings.Wind == PrevailingWind.WestToEast || settings.Wind == PrevailingWind.EastToWest;
            var reversed = settings.Wind == PrevailingWind.EastToWest || settings.Wind == PrevailingWind.NorthToSouth;
            var lines = alongX ? height : width;
            var steps = alongX ? width : height;
            var stride = (alongX ? 1 : width) * (reversed ? -1 : 1);
            var reach = settings.MoistureReach * Math.Min(width, height);
            for (var line = 0; line < lines; line++)
            {
                if ((line & 31) == 0 || line == lines - 1)
                    Report(progress, .6f + .4f * line / lines, "Applying wind and rain shadows");
                var origin = alongX ? line * width : line;
                var index = reversed ? origin - stride * (steps - 1) : origin;
                var vapour = 0f;
                var previousElevation = 0f;
                for (var step = 0; step < steps; step++, index += stride)
                {
                    if (water[index] == WaterKind.Ocean)
                    {
                        vapour = 1f;
                        previousElevation = 0f;
                        moisture[index] = 255;
                        continue;
                    }

                    var elevation = Math.Max(0f, terrain.Elevations[index] - sea) / (1f - sea);
                    var uplift = Math.Max(0f, elevation - previousElevation);
                    var rain = vapour * Math.Min(.9f, 3f * uplift);
                    vapour = Math.Max(0f, vapour - rain - .7f / reach);
                    var coastal = Math.Max(0f, 1f - distance[index] / reach);
                    moisture[index] = Encode(.08f + .5f * coastal + .4f * vapour + .3f * rain);
                    previousElevation = elevation;
                }
            }

            Report(progress, 1f, "Climate complete");
            return new ClimateMap(terrain, settings, temperature, moisture);
        }

        private static byte Encode(float value) =>
            (byte)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 255f, MidpointRounding.AwayFromZero);

        private static void Report(Func<float, string, bool> progress, float fraction, string stage)
        {
            if (progress != null && !progress(fraction, stage)) throw new OperationCanceledException();
        }
    }
}
