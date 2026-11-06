using System;

namespace GrandStrategy.WorldGeneration
{
    public sealed class HeightmapResult
    {
        public HeightmapRecipe Recipe { get; }
        // Row-major, south to north. X=0 is west, Y=0 is south. Values are normalized, not metres.
        public float[] Elevations { get; }
        public float LandFraction { get; }
        public float Minimum { get; }
        public float Maximum { get; }

        internal HeightmapResult(HeightmapRecipe recipe, float[] elevations)
        {
            Recipe = recipe; Elevations = elevations;
            var land = 0; var min = 1f; var max = 0f;
            foreach (var height in elevations)
            {
                if (height >= recipe.SeaLevel) land++;
                min = Math.Min(min, height); max = Math.Max(max, height);
            }
            LandFraction = (float)land / elevations.Length; Minimum = min; Maximum = max;
        }
    }

    /// <summary>Pure C# raster generation; no Unity objects, global random state, or file writes.</summary>
    public static class HeightmapGenerator
    {
        // Return false from progress to cancel. No partial result is published.
        public static HeightmapResult Generate(HeightmapRecipe input, Func<float, string, bool> progress = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            input.Validate();
            var recipe = input.Copy();
            int width = recipe.Width, height = recipe.Height, size = width * height;
            var values = new float[size]; var warpedX = new float[size]; var warpedY = new float[size];
            var scratch = new float[size];
            var random = new StableRandom(recipe.Seed);
            float aspect = (float)width / height;
            float stage = 0, stages = recipe.Steps.Count + 2;
            for (var y = 0; y < height; y++)
            {
                if ((y & 15) == 0) Report(progress, y / (float)height / stages, "Preparing coastline distortion");
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var u = x / (float)(width - 1); var v = y / (float)(height - 1);
                    warpedX[i] = u + Warp(u * aspect, v, recipe.Seed) * recipe.CoastWarp;
                    warpedY[i] = v + Warp(u * aspect + 71, v - 39, recipe.Seed) * recipe.CoastWarp;
                    values[i] = .07f;
                }
            }
            stage++;
            for (var stepIndex = 0; stepIndex < recipe.Steps.Count; stepIndex++)
            {
                var step = recipe.Steps[stepIndex];
                if (!step.Enabled) { stage++; continue; }
                // Each step has an independent stream, so disabling a step doesn't shuffle later placement.
                random = new StableRandom(unchecked(recipe.Seed + stepIndex * 104729));
                var count = random.Integer(step.CountMin, step.CountMax);
                string label = $"{stepIndex + 1}/{recipe.Steps.Count}: {step.Operation}";
                Report(progress, stage / stages, label);
                if (step.Operation == HeightmapOperation.Smooth || step.Operation == HeightmapOperation.Erode)
                {
                    for (var pass = 0; pass < count; pass++)
                    {
                        Report(progress, (stage + pass / (float)count) / stages, label);
                        Relax(values, scratch, width, height, step.StrengthMin,
                            step.Operation == HeightmapOperation.Erode, progress, (stage + pass / (float)count) / stages, label);
                    }
                }
                else
                {
                    var features = new Feature[count];
                    for (var n = 0; n < count; n++)
                        features[n] = new Feature(random.Between(step.XMin, step.XMax), random.Between(step.YMin, step.YMax),
                            random.Between(step.StrengthMin, step.StrengthMax), random.Between(-2.8f, 2.8f), random.Between(0, 100));
                    for (var y = 0; y < height; y++)
                    {
                        if ((y & 15) == 0) Report(progress, (stage + y / (float)height) / stages, label);
                        for (var x = 0; x < width; x++)
                        {
                            var i = y * width + x; var value = values[i];
                            var u = warpedX[i]; var v = warpedY[i];
                            if (step.Operation == HeightmapOperation.MultiplyLand)
                            {
                                if (value > recipe.SeaLevel) value = recipe.SeaLevel + (value - recipe.SeaLevel) * step.StrengthMin;
                            }
                            else if (step.Operation == HeightmapOperation.Detail)
                            {
                                var detail = Noise(u * aspect * 22, v * 22, recipe.Seed)
                                    + .45f * Noise(u * aspect * 51, v * 51, recipe.Seed + 1)
                                    + .2f * Noise(u * aspect * 103, v * 103, recipe.Seed + 2);
                                value += detail * step.StrengthMin;
                            }
                            else foreach (var feature in features)
                            {
                                var dx = (u - feature.X) * aspect; var dy = v - feature.Y;
                                if (step.Operation == HeightmapOperation.Strait)
                                {
                                    // Meandering channel, full map span; bounded edges supply its outlets.
                                    var along = step.Horizontal ? u * aspect : v;
                                    var across = step.Horizontal ? v - feature.Y : (u - feature.X) * aspect;
                                    across += .035f * Noise(along * 7, feature.Phase, recipe.Seed);
                                    var cut = (float)Math.Exp(-across * across / (2 * step.Radius * step.Radius));
                                    value = Lerp(value, Math.Min(value, recipe.SeaLevel - .06f), cut * feature.Strength);
                                    continue;
                                }
                                float distance;
                                if (step.Operation == HeightmapOperation.Range || step.Operation == HeightmapOperation.Trough)
                                {
                                    var along = dx * feature.Cos + dy * feature.Sin;
                                    var across = -dx * feature.Sin + dy * feature.Cos;
                                    across += .018f * Noise(along * 13, feature.Phase, recipe.Seed);
                                    var end = Math.Max(0, Math.Abs(along) - step.Length * .5f);
                                    distance = (across * across + end * end) / (step.Radius * step.Radius);
                                }
                                else distance = (dx * dx + dy * dy) / (step.Radius * step.Radius);
                                if (distance > 12) continue;
                                var amount = feature.Strength * (float)Math.Exp(-distance * 1.6f);
                                if (step.Operation == HeightmapOperation.Range)
                                {
                                    var ridge = 1 - Math.Abs(Noise(u * aspect * 61 + feature.Phase, v * 61, recipe.Seed));
                                    amount *= .45f + .55f * ridge;
                                }
                                value += step.Operation == HeightmapOperation.Pit || step.Operation == HeightmapOperation.Trough ? -amount : amount;
                            }
                            values[i] = value;
                        }
                    }
                }
                stage++;
            }
            for (var y = 0; y < height; y++)
            {
                if ((y & 15) == 0) Report(progress, (stage + y / (float)height) / stages, "Finishing bounded coastlines");
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1); var v = y / (float)(height - 1);
                    var edge = Math.Min(Math.Min(u * aspect, (1 - u) * aspect), Math.Min(v, 1 - v));
                    var mask = Clamp(edge / recipe.EdgeFalloff);
                    mask = mask * mask * (3 - 2 * mask);
                    values[y * width + x] = Clamp(values[y * width + x] * mask);
                }
            }
            Report(progress, 1, "Complete");
            return new HeightmapResult(recipe, values);
        }

        private static void Relax(float[] values, float[] scratch, int width, int height, float strength,
            bool erosion, Func<float, string, bool> progress, float fraction, string label)
        {
            if (erosion) Array.Clear(scratch, 0, scratch.Length);
            var talus = .008f * 512 / width;
            for (var y = 0; y < height; y++)
            {
                if ((y & 31) == 0) Report(progress, fraction, label);
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    int west = x > 0 ? i - 1 : i, east = x < width - 1 ? i + 1 : i;
                    int south = y > 0 ? i - width : i, north = y < height - 1 ? i + width : i;
                    if (!erosion)
                    {
                        scratch[i] = Lerp(values[i], (values[i] + values[west] + values[east] + values[south] + values[north]) / 5, strength);
                        continue;
                    }
                    var lowest = west;
                    if (values[east] < values[lowest]) lowest = east;
                    if (values[south] < values[lowest]) lowest = south;
                    if (values[north] < values[lowest]) lowest = north;
                    var transfer = Math.Max(0, values[i] - values[lowest] - talus) * strength * .25f;
                    scratch[i] -= transfer; scratch[lowest] += transfer;
                }
            }
            for (var i = 0; i < values.Length; i++) values[i] = erosion ? values[i] + scratch[i] : scratch[i];
        }

        private static void Report(Func<float, string, bool> progress, float value, string label)
        {
            if (progress != null && !progress(value, label)) throw new OperationCanceledException("Heightmap generation cancelled.");
        }

        internal static float Clamp(float x) => Math.Max(0, Math.Min(1, x));
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Warp(float x, float y, int seed) => .65f * Noise(x * 4, y * 4, seed)
            + .25f * Noise(x * 13, y * 13, seed) + .1f * Noise(x * 37, y * 37, seed);
        private static float Noise(float x, float y, int seed)
        {
            var ix = (int)Math.Floor(x); var iy = (int)Math.Floor(y);
            var fx = x - ix; var fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Lerp(Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), fx),
                Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), fx), fy);
        }
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 144269);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 8388607.5f - 1;
            }
        }
        private readonly struct Feature
        {
            public readonly float X, Y, Strength, Cos, Sin, Phase;
            public Feature(float x, float y, float strength, float angle, float phase)
            { X = x; Y = y; Strength = strength; Cos = (float)Math.Cos(angle); Sin = (float)Math.Sin(angle); Phase = phase; }
        }
        private struct StableRandom
        {
            private uint state;
            public StableRandom(int seed) { state = unchecked((uint)seed) ^ 0xa341316cu; if (state == 0) state = 1; }
            private float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state >> 8) / 16777216f; }
            public float Between(float min, float max) => min + Next() * (max - min);
            public int Integer(int min, int max) => min + (int)(Next() * (max - min + 1));
        }
    }
}
