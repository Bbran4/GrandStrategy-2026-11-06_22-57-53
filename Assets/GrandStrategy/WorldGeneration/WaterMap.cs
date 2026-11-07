using System;

namespace GrandStrategy.WorldGeneration
{
    public enum WaterKind : byte { Land, Ocean, Lake }

    /// <summary>Raster topology at the saved world's sea level, not a lake-filling simulation.</summary>
    public sealed class WaterMap
    {
        private readonly WaterKind[] cells;
        public HeightmapResult Source { get; }
        public int OceanCount { get; }
        public int LakeCount { get; }
        public int LandCount => cells.Length - OceanCount - LakeCount;
        public WaterKind this[int index] => cells[index];

        private WaterMap(HeightmapResult source, WaterKind[] cells, int oceanCount, int lakeCount)
        { Source = source; this.cells = cells; OceanCount = oceanCount; LakeCount = lakeCount; }

        public static WaterMap Classify(HeightmapResult world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            world.Recipe.Validate();
            var width = world.Recipe.Width; var height = world.Recipe.Height;
            if (world.Elevations.Length != width * height)
                throw new ArgumentException("Heightmap dimensions do not match the samples.");
            var cells = new WaterKind[world.Elevations.Length];
            var waterCount = 0;
            for (var i = 0; i < cells.Length; i++)
            {
                var elevation = world.Elevations[i];
                if (float.IsNaN(elevation) || float.IsInfinity(elevation) || elevation < 0 || elevation > 1)
                    throw new ArgumentException("Water classification requires finite normalized elevations.");
                if (elevation < world.Recipe.SeaLevel) { cells[i] = WaterKind.Lake; waterCount++; }
            }
            // Bounded FIFO: every water sample is enqueued at most once, marked on insertion.
            var queue = new int[waterCount];
            var tail = 0;
            for (var x = 0; x < width; x++)
            { Enqueue(x, cells, queue, ref tail); Enqueue((height - 1) * width + x, cells, queue, ref tail); }
            for (var y = 1; y < height - 1; y++)
            { Enqueue(y * width, cells, queue, ref tail); Enqueue(y * width + width - 1, cells, queue, ref tail); }
            for (var head = 0; head < tail; head++)
            {
                var index = queue[head]; var x = index % width;
                // Four-neighbour connectivity: touching diagonally does not make an ocean passage.
                if (x > 0) Enqueue(index - 1, cells, queue, ref tail);
                if (x < width - 1) Enqueue(index + 1, cells, queue, ref tail);
                if (index >= width) Enqueue(index - width, cells, queue, ref tail);
                if (index < cells.Length - width) Enqueue(index + width, cells, queue, ref tail);
            }
            return new WaterMap(world, cells, tail, waterCount - tail);
        }

        private static void Enqueue(int index, WaterKind[] cells, int[] queue, ref int tail)
        {
            if (cells[index] != WaterKind.Lake) return;
            cells[index] = WaterKind.Ocean; queue[tail++] = index;
        }
    }

    public readonly struct Rgb24
    {
        public readonly byte R, G, B;
        public Rgb24(byte r, byte g, byte b) { R = r; G = g; B = b; }
    }

    /// <summary>Exact data colours, shared by the OpenGS preview and lossless PNG export.</summary>
    public static class OpenGsLandPalette
    {
        private static readonly Rgb24 Ocean = new Rgb24(5, 20, 18);

        public static Rgb24 Color(WaterKind kind, bool riverWater = false)
        {
            switch (kind)
            {
                case WaterKind.Ocean: return Ocean;
                case WaterKind.Lake: return new Rgb24(0, 255, 0);
                case WaterKind.Land: return riverWater ? Ocean : new Rgb24(255, 255, 255);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
