using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    internal sealed class MapNature : IDisposable
    {
        private const int SitesPerMapHeight = 192;
        private const int ChunkSize = 16;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private GameObject root;
        private Material material;
        private float visibility;

        public MapNature(GameObject parent, MapTerrainGeometry geometry, PlayableMapSurface surface,
            int seed, Func<int, BiomeKind> biomeAt, Func<int, float> snowAt)
        {
            var shader = Resources.Load<Shader>("MapNature");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The map nature shader requires Universal Render Pipeline.");

            try
            {
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
                root = new GameObject("Map nature") { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(parent.transform, false);
                root.SetActive(false);
                var builder = new ChunkBuilder();
                var width = Mathf.Min(1024, Mathf.CeilToInt(surface.Aspect * SitesPerMapHeight));
                for (var z0 = 0; z0 < SitesPerMapHeight; z0 += ChunkSize)
                    for (var x0 = 0; x0 < width; x0 += ChunkSize)
                    {
                        for (var z = z0; z < z0 + ChunkSize; z++)
                            for (var x = x0; x < Mathf.Min(width, x0 + ChunkSize); x++)
                                AddSite(x, z, width, builder);
                        if (builder.Count == 0) continue;
                        var mesh = builder.Build();
                        meshes.Add(mesh);
                        var chunk = new GameObject("Nature chunk", typeof(MeshFilter), typeof(MeshRenderer))
                            { hideFlags = HideFlags.DontSave };
                        chunk.transform.SetParent(root.transform, false);
                        chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = chunk.GetComponent<MeshRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
            }
            catch
            {
                Dispose();
                throw;
            }

            void AddSite(int x, int z, int width, ChunkBuilder builder)
            {
                var u = (x + .12f + .76f * MapNatureRules.Random01(seed ^ 113, x, z)) / width;
                var v = (z + .12f + .76f * MapNatureRules.Random01(seed ^ 211, x, z)) / SitesPerMapHeight;
                var uv = new Vector2(u, v);
                var cell = surface.CellAt(uv);
                if (!surface.IsDryLand(cell)) return;
                var anchor = geometry.Sample(uv, out var slope);
                var kind = MapNatureRules.Pick(biomeAt(cell), true, slope, snowAt(cell),
                    MapNatureRules.Random01(seed ^ 307, x, z), MapNatureRules.Random01(seed ^ 401, x / 5, z / 5));
                if (kind == NatureKind.None || !HasDryFootprint(surface, uv)) return;
                var size = Mathf.Lerp(.0013f, .0027f, MapNatureRules.Random01(seed ^ 503, x, z));
                var rotation = 2f * Mathf.PI * MapNatureRules.Random01(seed ^ 601, x, z);
                builder.Add(kind, anchor, size, rotation);
            }
        }

        private static bool HasDryFootprint(PlayableMapSurface surface, Vector2 uv)
        {
            var v = Mathf.Max(.0015f, 1.1f / surface.Map.Height);
            var u = v / surface.Aspect;
            if (uv.x <= u || uv.x >= 1f - u || uv.y <= v || uv.y >= 1f - v) return false;
            for (var y = -1; y <= 1; y++)
                for (var x = -1; x <= 1; x++)
                    if (!surface.IsDryLand(surface.CellAt(uv + new Vector2(x * u, y * v)))) return false;
            return true;
        }

        public void SetZoom(float zoom)
        {
            var next = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(.09f, .27f, zoom));
            if (visibility == next) return;
            visibility = next;
            material.SetFloat("_Visibility", visibility);
            root.SetActive(visibility > 0f);
        }

        public void SetCapital(Vector3 point)
            => material.SetVector("_Capital", new Vector4(point.x, point.z, .006f, 0f));

        public void Dispose()
        {
            foreach (var mesh in meshes) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(material);
        }

        private sealed class ChunkBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> anchors = new List<Vector3>();
            private readonly List<Color32> colours = new List<Color32>();
            private readonly List<int> triangles = new List<int>();
            public int Count => vertices.Count;

            public void Add(NatureKind kind, Vector3 anchor, float size, float rotation)
            {
                switch (kind)
                {
                    case NatureKind.Broadleaf:
                        Tier(-.05f, .085f, .52f, .065f, 4, new Color32(99, 69, 39, 255));
                        Tier(.34f, .14f, .62f, .34f, 6, new Color32(56, 97, 45, 255));
                        Tier(.62f, .34f, 1.04f, 0f, 6, new Color32(79, 121, 59, 255));
                        break;
                    case NatureKind.Conifer:
                        Tier(-.05f, .065f, .35f, .055f, 4, new Color32(91, 65, 43, 255));
                        Tier(.22f, .31f, .87f, 0f, 6, new Color32(41, 81, 58, 255));
                        Tier(.48f, .24f, 1.12f, 0f, 6, new Color32(56, 101, 71, 255));
                        break;
                    case NatureKind.Rock:
                        Tier(-.14f, .40f, .08f, .37f, 5, new Color32(113, 107, 94, 255));
                        Tier(.08f, .37f, .31f, 0f, 5, new Color32(149, 142, 126, 255));
                        break;
                    case NatureKind.Reeds:
                        var green = new Color32(107, 127, 55, 255);
                        for (var i = 0; i < 3; i++)
                            Face(Point(-.02f, .07f, i, 3), Point(.49f, .28f, i, 3),
                                Point(-.02f, .07f, i + 1, 3), green);
                        break;
                }

                void Tier(float y0, float r0, float y1, float r1, int sides, Color32 colour)
                {
                    for (var side = 0; side < sides; side++)
                    {
                        var a = Point(y0, r0, side, sides);
                        var b = Point(y0, r0, side + 1, sides);
                        var c = Point(y1, r1, side, sides);
                        var d = Point(y1, r1, side + 1, sides);
                        Face(a, c, b, colour);
                        if (r1 > 0f) Face(b, c, d, colour);
                    }
                }

                Vector3 Point(float y, float radius, int side, int sides)
                {
                    var angle = rotation + (side % sides) * 2f * Mathf.PI / sides;
                    return anchor + size * new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                }

                void Face(Vector3 a, Vector3 b, Vector3 c, Color32 colour)
                {
                    Append(a, colour);
                    Append(b, colour);
                    Append(c, colour);
                }

                void Append(Vector3 vertex, Color32 colour)
                {
                    triangles.Add(vertices.Count);
                    vertices.Add(vertex);
                    colours.Add(colour);
                    anchors.Add(anchor);
                }
            }

            public Mesh Build()
            {
                var mesh = new Mesh { name = "Map nature chunk", hideFlags = HideFlags.DontSave };
                try
                {
                    mesh.SetVertices(vertices);
                    mesh.SetColors(colours);
                    mesh.SetUVs(0, anchors);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateNormals();
                    mesh.UploadMeshData(true);
                    return mesh;
                }
                catch
                {
                    UnityEngine.Object.Destroy(mesh);
                    throw;
                }
                finally
                {
                    vertices.Clear();
                    colours.Clear();
                    anchors.Clear();
                    triangles.Clear();
                }
            }
        }
    }
}
