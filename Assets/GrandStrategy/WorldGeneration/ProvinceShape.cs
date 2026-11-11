// Smooths province corners while keeping the raster IDs available for picking and shader overlays.
using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    // The raster supplies each cell's identity. Its shared corners move together to form the
    // smoothed shape used by the border mesh, fill shader, and mouse picking.
    internal sealed class ProvinceShape
    {
        // Keep the maximum displacement below half the minimum altitude of a grid triangle
        // (sqrt(2) / 4), even after quantization. This keeps every cell convex.
        private const float MaximumOffset = .34f;
        private const float Quantization = 256f;
        private readonly ushort[] corners;
        private readonly int width;
        private readonly int height;
        public int Stride => width + 1;
        public int CornerCount => corners.Length;

        // Trace borders and store compact corner offsets shared by the visible geometry and selection
        // logic.
        public ProvinceShape(int width, int height, int[] raster)
        {
            this.width = width;
            this.height = height;
            corners = new ushort[Stride * (height + 1)];

            MapBorderPaths.Trace(width, height, raster, (path, closed) =>
            {
                var count = closed ? path.Count - 1 : path.Count;
                for (var i = 0; i < count; i++)
                {
                    var corner = path[i];
                    var x = corner % Stride;
                    var y = corner / Stride;
                    // Keep the world perimeter and shared junctions anchored.
                    if (x == 0 || x == width || y == 0 || y == height) continue;
                    var point = Smooth(path, count, i, closed, Stride, MaximumOffset);
                    var dx = (int)Math.Round((point.X - x) * Quantization);
                    var dy = (int)Math.Round((point.Y - y) * Quantization);
                    corners[corner] = (ushort)((dx & 255) | ((dy & 255) << 8));
                }
            });
        }

        public ushort PackedCorner(int index) => corners[index];

        public (float X, float Y) Corner(int index)
        {
            var packed = corners[index];
            return (index % Stride + Signed(packed & 255) / Quantization,
                index / Stride + Signed(packed >> 8) / Quantization);
        }

        public bool CellMoved(int x, int y)
        {
            var i = y * Stride + x;
            return (corners[i] | corners[i + 1] | corners[i + Stride] | corners[i + Stride + 1]) != 0;
        }

        // Resolve a point to its province raster cell after applying the same corner smoothing seen on
        // screen.
        public int CellAt(float x, float y)
        {
            var cellX = Math.Clamp((int)x, 0, width - 1);
            var cellY = Math.Clamp((int)y, 0, height - 1);
            var original = cellY * width + cellX;
            if (!CellMoved(cellX, cellY) || Contains(cellX, cellY, x, y)) return original;

            // Displacement is less than half a cell, so only the nearest neighbours in
            // each axis and their shared diagonal can contain the point.
            var nextX = cellX + (x - cellX < .5f ? -1 : 1);
            var nextY = cellY + (y - cellY < .5f ? -1 : 1);
            if (Contains(nextX, cellY, x, y)) return cellY * width + nextX;
            if (Contains(cellX, nextY, x, y)) return nextY * width + cellX;
            if (Contains(nextX, nextY, x, y)) return nextY * width + nextX;
            return original;
        }

        private bool Contains(int cellX, int cellY, float x, float y)
        {
            if (cellX < 0 || cellX >= width || cellY < 0 || cellY >= height) return false;
            var i = cellY * Stride + cellX;
            var a = Corner(i);
            var b = Corner(i + 1);
            var c = Corner(i + 1 + Stride);
            var d = Corner(i + Stride);
            return Inside(a, b) && Inside(b, c) && Inside(c, d) && Inside(d, a);

            bool Inside((float X, float Y) start, (float X, float Y) end)
                => (end.X - start.X) * (y - start.Y) - (end.Y - start.Y) * (x - start.X) >= -.000001f;
        }

        private static int Signed(int code) => (code ^ 128) - 128;

        internal static (float X, float Y) Smooth(IReadOnlyList<int> path, int count, int i, bool closed,
            int stride, float limit)
        {
            var current = At(i);
            if (!closed && (i == 0 || i == count - 1)) return current;
            var a = At(i - 2);
            var b = At(i - 1);
            var c = At(i + 1);
            var d = At(i + 2);
            var dx = (a.X + 2f * b.X + 2f * current.X + 2f * c.X + d.X) / 8f - current.X;
            var dy = (a.Y + 2f * b.Y + 2f * current.Y + 2f * c.Y + d.Y) / 8f - current.Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared > limit * limit)
            {
                var scale = limit / MathF.Sqrt(distanceSquared);
                dx *= scale;
                dy *= scale;
            }
            return (current.X + dx, current.Y + dy);

            (int X, int Y) At(int index)
            {
                var corner = path[closed ? (index + count) % count : Math.Clamp(index, 0, count - 1)];
                return (corner % stride, corner / stride);
            }
        }
    }
}
