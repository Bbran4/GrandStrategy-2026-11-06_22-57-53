using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    internal static class MapBorderPaths
    {
        private const int Directions = 15;

        // Fill eligible water cells with missing display IDs, starting from the nearest seed.
        // The imported raster stays authoritative for picking, navigation, and campaign ownership.
        internal static int[] FillWaterGaps(int width, int[] source, Func<int, bool> isWater,
            Func<int, bool> canSeed, Func<int, bool> preserveRegion = null)
        {
            int[] result = null;
            var queue = new Queue<int>();
            for (var cell = 0; cell < source.Length; cell++)
            {
                if (!isWater(cell) || (source[cell] >= 0
                    && (canSeed(cell) || preserveRegion?.Invoke(cell) == true))) continue;
                result ??= (int[])source.Clone();
                result[cell] = -1;
                var x = cell % width;
                if (Seed(x > 0 ? cell - 1 : -1) || Seed(x + 1 < width ? cell + 1 : -1)
                    || Seed(cell - width) || Seed(cell + width))
                    queue.Enqueue(cell);

                bool Seed(int other)
                {
                    if (other < 0 || other >= source.Length || source[other] < 0 || !canSeed(other)) return false;
                    result[cell] = source[other];
                    return true;
                }
            }

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                var x = cell % width;
                if (x > 0) Spread(cell - 1);
                if (x + 1 < width) Spread(cell + 1);
                Spread(cell - width);
                Spread(cell + width);

                void Spread(int other)
                {
                    if (other < 0 || other >= source.Length || result[other] >= 0 || !isWater(other)) return;
                    result[other] = result[cell];
                    queue.Enqueue(other);
                }
            }
            return result ?? source;
        }

        internal static int[] OutlineLakes(int width, int[] source, Func<int, bool> isLake,
            Func<int, bool> hasAuthoredLakeId)
        {
            var raster = FillWaterGaps(width, source, isLake, cell => isLake(cell) && hasAuthoredLakeId(cell));
            if (ReferenceEquals(raster, source)) return source;
            // Only the display raster uses this ID. Disconnected lakes already have separate outlines.
            for (var cell = 0; cell < raster.Length; cell++)
                if (raster[cell] < 0 && isLake(cell)) raster[cell] = int.MaxValue;
            return raster;
        }

        // Paths use corners on a (width + 1) by (height + 1) grid. The callback consumes each path
        // before the buffer is reused. Closed paths repeat the first corner at the end.
        public static void Trace(int width, int height, int[] raster, Action<IReadOnlyList<int>, bool> draw)
        {
            var stride = width + 1;
            var flags = new byte[stride * (height + 1)];
            var steps = new[] { 1, stride, -1, -stride };
            for (var y = 0; y <= height; y++)
                for (var x = 0; x <= width; x++)
                {
                    var vertex = y * stride + x;
                    var cell = y * width + x;
                    if (x < width && (y == 0 ? -1 : raster[cell - width]) != (y == height ? -1 : raster[cell]))
                        Link(vertex, 0);
                    if (y < height && (x == 0 ? -1 : raster[cell - 1]) != (x == width ? -1 : raster[cell]))
                        Link(vertex, 1);
                }

            var path = new List<int>();
            for (var vertex = 0; vertex < flags.Length; vertex++)
                if (IsJunction(vertex))
                    while (Remaining(vertex) != 0)
                        Walk(vertex, false);
            for (var vertex = 0; vertex < flags.Length; vertex++)
                while (Remaining(vertex) != 0)
                    Walk(vertex, true);

            void Link(int vertex, int direction)
            {
                flags[vertex] |= (byte)(1 << direction);
                flags[vertex + steps[direction]] |= (byte)(1 << ((direction + 2) & 3));
            }

            int Remaining(int vertex) => (flags[vertex] & Directions) & ~(flags[vertex] >> 4);

            bool IsJunction(int vertex)
            {
                var edges = flags[vertex] & Directions;
                var rest = edges & (edges - 1);
                return rest != 0 && (rest & (rest - 1)) != 0;
            }

            void Walk(int start, bool closed)
            {
                path.Clear();
                path.Add(start);
                var vertex = start;
                do
                {
                    var remaining = Remaining(vertex);
                    if (remaining == 0) break;
                    var direction = 0;
                    while ((remaining & (1 << direction)) == 0) direction++;
                    flags[vertex] |= (byte)(16 << direction);
                    vertex += steps[direction];
                    flags[vertex] |= (byte)(16 << ((direction + 2) & 3));
                    path.Add(vertex);
                } while (vertex != start && !IsJunction(vertex));
                draw(path, closed && vertex == start);
            }
        }
    }
}
