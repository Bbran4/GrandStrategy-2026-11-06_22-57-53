// Triangulates world heights for the editor preview and playable map, with optional sculpted heights from
// Unity Terrain.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    public static class HeightmapMesh
    {
        public static Vector2Int GridSize(int width, int height, int longestSide, int authoredResolution = 0)
        {
            var aspect = (float)width / height;
            // The editable Terrain is sampled independently of the world and province rasters.
            // Keep the original UV endpoints, and never oversample the selected heightmap.
            var maxColumns = authoredResolution > 0 ? authoredResolution : width;
            var maxRows = authoredResolution > 0 ? authoredResolution : height;
            var columns = Mathf.Min(maxColumns, Mathf.Max(2, Mathf.RoundToInt(longestSide * Mathf.Min(1f, aspect)) + 1));
            var rows = Mathf.Min(maxRows, Mathf.Max(2, Mathf.RoundToInt(longestSide / Mathf.Max(1f, aspect)) + 1));
            return new Vector2Int(columns, rows);
        }

        // Sample the generated and authored heights in the same UV space. Water and its immediate banks
        // keep their generated heights.
        public static Mesh Create(HeightmapResult world, float heightScale, int longestSide = 256,
            TerrainData authored = null, WaterMap water = null, byte[] rivers = null, bool useAuthoredHeights = true)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (float.IsNaN(heightScale) || float.IsInfinity(heightScale) || heightScale <= 0 || heightScale > 1)
                throw new ArgumentOutOfRangeException(nameof(heightScale));
            int width = world.Recipe.Width, height = world.Recipe.Height;
            var aspect = (float)width / height;
            var authoredResolution = authored == null ? 0 : authored.heightmapResolution;
            var grid = GridSize(width, height, longestSide, authoredResolution);
            var columns = grid.x;
            var rows = grid.y;
            // Comparison views share the editable heightmap's mesh density even when sampling generated heights.
            var edited = authored != null && useAuthoredHeights
                ? authored.GetHeights(0, 0, authoredResolution, authoredResolution) : null;
            if (edited != null && water == null)
                throw new ArgumentNullException(nameof(water));
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
                    if (edited != null && IsDry(x0, y0) && IsDry(x1, y0) && IsDry(x0, y1) && IsDry(x1, y1))
                    {
                        var px = u * (authoredResolution - 1);
                        var py = v * (authoredResolution - 1);
                        var ax = Mathf.FloorToInt(px);
                        var ay = Mathf.FloorToInt(py);
                        var right = Mathf.Min(ax + 1, authoredResolution - 1);
                        var top = Mathf.Min(ay + 1, authoredResolution - 1);
                        var south = Mathf.Lerp(edited[ay, ax], edited[ay, right], px - ax);
                        var north = Mathf.Lerp(edited[top, ax], edited[top, right], px - ax);
                        elevation = Mathf.Max(world.Recipe.SeaLevel, Mathf.Lerp(south, north, py - ay));
                    }
                    // Current classified water is below sea level. Lake-specific elevations come with hydrology.
                    vertices[y * columns + x] = new Vector3((u - .5f) * aspect,
                        Mathf.Max(0, elevation - world.Recipe.SeaLevel) * heightScale, v - .5f);
                    uvs[y * columns + x] = new Vector2(u, v);
                }
            bool IsDry(int x, int y) => IsLand(x, y)
                && (x == 0 || IsLand(x - 1, y)) && (x == width - 1 || IsLand(x + 1, y))
                && (y == 0 || IsLand(x, y - 1)) && (y == height - 1 || IsLand(x, y + 1));

            bool IsLand(int x, int y)
            {
                var cell = y * width + x;
                return water[cell] == WaterKind.Land && (rivers == null || rivers[cell] == 0);
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
