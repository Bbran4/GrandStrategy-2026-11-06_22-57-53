using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    internal static class HeightmapMesh
    {
        internal static Mesh Create(HeightmapResult world, float heightScale, int longestSide = 256)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (float.IsNaN(heightScale) || float.IsInfinity(heightScale) || heightScale <= 0 || heightScale > 1)
                throw new ArgumentOutOfRangeException(nameof(heightScale));
            int width = world.Recipe.Width, height = world.Recipe.Height;
            var aspect = (float)width / height;
            // Bound geometry independently of raster size. Endpoints remain aligned with the source.
            int columns = Mathf.Min(width, Mathf.Max(2, Mathf.RoundToInt(longestSide * Mathf.Min(1, aspect)) + 1));
            int rows = Mathf.Min(height, Mathf.Max(2, Mathf.RoundToInt(longestSide / Mathf.Max(1, aspect)) + 1));
            var vertices = new Vector3[columns * rows]; var uvs = new Vector2[vertices.Length];
            var triangles = new int[(columns - 1) * (rows - 1) * 6];
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < columns; x++)
                {
                    var u = x / (float)(columns - 1); var v = y / (float)(rows - 1);
                    var sampleX = u * (width - 1); var sampleY = v * (height - 1);
                    var x0 = Mathf.FloorToInt(sampleX); var y0 = Mathf.FloorToInt(sampleY);
                    var x1 = Mathf.Min(x0 + 1, width - 1); var y1 = Mathf.Min(y0 + 1, height - 1);
                    var lower = Mathf.Lerp(world.Elevations[y0 * width + x0], world.Elevations[y0 * width + x1], sampleX - x0);
                    var upper = Mathf.Lerp(world.Elevations[y1 * width + x0], world.Elevations[y1 * width + x1], sampleX - x0);
                    var elevation = Mathf.Lerp(lower, upper, sampleY - y0);
                    // Current classified water is below sea level. Lake-specific elevations come with hydrology.
                    vertices[y * columns + x] = new Vector3((u - .5f) * aspect,
                        Mathf.Max(0, elevation - world.Recipe.SeaLevel) * heightScale, v - .5f);
                    uvs[y * columns + x] = new Vector2(u, v);
                }
            var t = 0;
            for (var y = 0; y < rows - 1; y++)
                for (var x = 0; x < columns - 1; x++)
                {
                    var i = y * columns + x;
                    triangles[t++] = i; triangles[t++] = i + columns; triangles[t++] = i + 1;
                    triangles[t++] = i + 1; triangles[t++] = i + columns; triangles[t++] = i + columns + 1;
                }
            var mesh = new Mesh { name = "World heightmap", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                else UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }
    }
}
