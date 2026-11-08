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
        public Vector3 SunDirection { get; }

        public MapTerrainGeometry(Mesh terrain)
        {
            vertices = terrain.vertices;
            Bounds = terrain.bounds;
            Columns = Array.FindIndex(vertices, point => point.z > vertices[0].z);
            Rows = vertices.Length / Columns;
            var dx = Bounds.size.x / (Columns - 1);
            var dz = Bounds.size.z / (Rows - 1);
            SunDirection = new Vector3(-dx, Mathf.Sqrt(dx * dx + dz * dz) * .76f, dz).normalized;
        }

        public Texture2D CreateSunShadow()
        {
            var texture = new Texture2D(Columns, Rows, TextureFormat.RGBA32, true, true)
            {
                name = "Terrain sun shadows", hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                var pixels = texture.GetRawTextureData<Color32>();
                var horizon = new float[vertices.Length];
                var rise = SunDirection.y * (Bounds.size.x / (Columns - 1)) / -SunDirection.x;
                // Sweep from the sun. Each vertex inherits the highest upstream occluder,
                // lowered by the light ray's vertical drop over one mesh diagonal.
                for (var y = Rows - 1; y >= 0; y--)
                    for (var x = 0; x < Columns; x++)
                    {
                        var i = y * Columns + x;
                        var height = vertices[i].y;
                        var blocker = x > 0 && y + 1 < Rows ? horizon[i + Columns - 1] - rise : height;
                        horizon[i] = Mathf.Max(height, blocker);
                        var shade = Mathf.SmoothStep(0f, 1f, (blocker - height) / (rise * .70f));
                        pixels[i] = new Color32((byte)Mathf.RoundToInt(shade * 255f), 0, 0, 255);
                    }

                texture.Apply(true, true);
                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
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
