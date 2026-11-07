using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    internal static class MapBorderPaths
    {
        private const int Directions = 15;

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
