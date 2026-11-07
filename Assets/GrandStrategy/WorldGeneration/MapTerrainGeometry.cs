using System;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    // Sample the same triangles the camera renders, including the heightmap's downsampling.
    internal sealed class MapTerrainGeometry
    {
        private readonly Vector3[] vertices;
        public int Columns { get; }
        public int Rows { get; }
        public Bounds Bounds { get; }

        public MapTerrainGeometry(Mesh terrain)
        {
            vertices = terrain.vertices;
            Bounds = terrain.bounds;
            Columns = Array.FindIndex(vertices, point => point.z > vertices[0].z);
            Rows = vertices.Length / Columns;
        }

        public Vector3 Sample(Vector2 uv, out float slope)
        {
            var sx = uv.x * (Columns - 1);
            var sy = uv.y * (Rows - 1);
            var x = Mathf.Min((int)sx, Columns - 2);
            var y = Mathf.Min((int)sy, Rows - 2);
            var fx = sx - x;
            var fy = sy - y;
            var i = y * Columns + x;
            var a = vertices[i].y;
            var b = vertices[i + 1].y;
            var c = vertices[i + Columns].y;
            var d = vertices[i + Columns + 1].y;
            var lower = fx + fy <= 1f;
            var dx = lower ? b - a : d - c;
            var dz = lower ? c - a : d - b;
            var height = lower ? a + dx * fx + dz * fy : d - dx * (1f - fx) - dz * (1f - fy);
            var gradient = new Vector2(dx * (Columns - 1) / Bounds.size.x, dz * (Rows - 1) / Bounds.size.z);
            slope = gradient.magnitude;
            return new Vector3((uv.x - .5f) * Bounds.size.x, height, (uv.y - .5f) * Bounds.size.z);
        }
    }
}
