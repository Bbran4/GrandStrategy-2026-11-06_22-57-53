using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    internal sealed class MapNature : IDisposable
    {
        private const int SitesPerMapHeight = 240;
        private const int ChunkSize = 16;
        public const float CapitalRadius = .006f;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private GameObject root;
        private Material material;
        private float visibility;
        public Texture2D Canopy { get; private set; }

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
                const int canopyHeight = 640;
                var canopyWidth = Mathf.Clamp(Mathf.CeilToInt(canopyHeight * surface.Aspect), 32, 3072);
                var canopyPixels = new Color32[canopyWidth * canopyHeight];
                var builder = new ChunkBuilder();
                var width = Mathf.Min(1024, Mathf.CeilToInt(surface.Aspect * SitesPerMapHeight));
                for (var z0 = 0; z0 < SitesPerMapHeight; z0 += ChunkSize)
                    for (var x0 = 0; x0 < width; x0 += ChunkSize)
                    {
                        for (var z = z0; z < z0 + ChunkSize; z++)
                            for (var x = x0; x < Mathf.Min(width, x0 + ChunkSize); x++)
                                AddSite(x, z, width, builder, canopyPixels, canopyWidth, canopyHeight);
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

                Canopy = new Texture2D(canopyWidth, canopyHeight, TextureFormat.RGBA32, true, true)
                {
                    name = "Forest canopy", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                };
                Canopy.SetPixels32(canopyPixels);
                Canopy.Apply(true, true);
            }
            catch
            {
                Dispose();
                throw;
            }

            void AddSite(int x, int z, int width, ChunkBuilder builder, Color32[] canopy, int canopyWidth, int canopyHeight)
            {
                var u = (x + .12f + .76f * MapNatureRules.Random01(seed ^ 113, x, z)) / width;
                var v = (z + .12f + .76f * MapNatureRules.Random01(seed ^ 211, x, z)) / SitesPerMapHeight;
                var uv = new Vector2(u, v);
                var cell = surface.CellAt(uv);
                if (!surface.IsDryLand(cell)) return;
                var anchor = geometry.Sample(uv, out var slope);
                var snow = snowAt(cell);
                var kind = MapNatureRules.Pick(biomeAt(cell), true, slope, snow,
                    MapNatureRules.Random01(seed ^ 307, x, z), MapNatureRules.Grove(seed, u * surface.Aspect, v));
                if (kind == NatureKind.None || !HasDryFootprint(surface, uv)) return;
                var size = Mathf.Lerp(.00095f, .0019f, MapNatureRules.Random01(seed ^ 503, x, z));
                var rotation = 2f * Mathf.PI * MapNatureRules.Random01(seed ^ 601, x, z);
                var variant = MapNatureRules.Random01(seed ^ 701, x, z);
                builder.Add(kind, anchor, size, rotation, variant, snow);
                if (kind == NatureKind.Broadleaf || kind == NatureKind.Conifer)
                    StampCanopy(canopy, canopyWidth, canopyHeight, uv, surface.Aspect, size, variant);
            }
        }

        private static void StampCanopy(Color32[] pixels, int width, int height, Vector2 uv, float aspect,
            float treeSize, float variant)
        {
            // The same trees that appear up close paint soft crown and shade footprints for distant views.
            var x = uv.x * width - .5f;
            var y = uv.y * height - .5f;
            var radius = .0014f + treeSize * 1.2f;
            var rx = radius * width / aspect;
            var ry = radius * height;
            for (var row = Mathf.Max(0, Mathf.FloorToInt(y - ry)); row <= Mathf.Min(height - 1, Mathf.CeilToInt(y + ry)); row++)
                for (var column = Mathf.Max(0, Mathf.FloorToInt(x - rx)); column <= Mathf.Min(width - 1, Mathf.CeilToInt(x + rx)); column++)
                {
                    var dx = (column - x) / rx;
                    var dy = (row - y) / ry;
                    var weight = Mathf.Clamp01(1f - dx * dx - dy * dy);
                    var i = row * width + column;
                    var shade = Mathf.RoundToInt(weight * 245f);
                    var crown = Mathf.RoundToInt(weight * weight * Mathf.Lerp(65f, 215f, variant));
                    var old = pixels[i];
                    pixels[i] = new Color32((byte)Mathf.Max(shade, old.r), (byte)Mathf.Max(crown, old.g), 0, 255);
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
            var next = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(.12f, .38f, zoom));
            if (visibility == next) return;
            visibility = next;
            material.SetFloat("_Visibility", visibility);
            if (root.activeSelf != (visibility > 0f)) root.SetActive(visibility > 0f);
        }

        public void SetCapital(Vector3 point)
            => material.SetVector("_Capital", new Vector4(point.x, point.z, CapitalRadius, 0f));

        public void Dispose()
        {
            foreach (var mesh in meshes) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(Canopy);
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(material);
        }

        private sealed class ChunkBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> anchors = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color32> colours = new List<Color32>();
            private readonly List<int> triangles = new List<int>();
            public int Count => vertices.Count;

            public void Add(NatureKind kind, Vector3 anchor, float size, float rotation, float variant, float snow)
            {
                var bark = new Color32(82, 69, 47, 255);
                var leaf = Color32.Lerp(new Color32(62, 79, 41, 255), new Color32(112, 123, 68, 255), variant);
                var pine = Color32.Lerp(new Color32(50, 68, 52, 255), new Color32(86, 105, 77, 255), variant);
                var frost = new Color32(160, 169, 153, 255);
                leaf = Color32.Lerp(leaf, frost, snow * .52f);
                pine = Color32.Lerp(pine, frost, snow * .64f);
                switch (kind)
                {
                    case NatureKind.Broadleaf:
                        Tier(-.09f, .066f, .54f, .037f, 4, bark);
                        Lobe(new Vector3(-.10f, .59f, -.05f), new Vector3(.33f, .28f, .29f), leaf);
                        Lobe(new Vector3(.18f, .67f, .08f), new Vector3(.25f, .24f, .25f), leaf);
                        Lobe(new Vector3(.02f, .79f, -.04f), new Vector3(.26f, .24f, .26f), leaf);
                        break;
                    case NatureKind.Conifer:
                        Tier(-.10f, .052f, .36f, .029f, 4, bark);
                        Tier(.22f, .12f, .34f, .24f, 7, pine);
                        Tier(.34f, .24f, .88f, .02f, 7, pine);
                        Tier(.56f, .14f, 1.10f, 0f, 7, pine);
                        break;
                    case NatureKind.Rock:
                        var stone = Color32.Lerp(new Color32(103, 103, 94, 255), new Color32(148, 137, 120, 255), variant);
                        Lobe(new Vector3(0f, -.04f, 0f), new Vector3(.35f, .20f, .23f),
                            Color32.Lerp(stone, frost, snow * .48f));
                        break;
                    case NatureKind.Reeds:
                        var green = new Color32(104, 113, 64, 255);
                        for (var i = 0; i < 3; i++)
                            Face(Point(-.02f, .07f, i, 3), Point(.30f, .18f, i, 3),
                                Point(-.02f, .07f, i + 1, 3), green);
                        break;
                }

                void Lobe(Vector3 offset, Vector3 radii, Color32 colour)
                {
                    const int sides = 7;
                    const int rows = 5;
                    var first = vertices.Count;
                    var sin = Mathf.Sin(rotation);
                    var cos = Mathf.Cos(rotation);
                    for (var row = 0; row < rows; row++)
                    {
                        var latitude = Mathf.PI * (row / (float)(rows - 1) - .5f);
                        var horizontal = Mathf.Cos(latitude);
                        for (var side = 0; side < sides; side++)
                        {
                            var angle = side * 2f * Mathf.PI / sides;
                            var unit = new Vector3(horizontal * Mathf.Cos(angle), Mathf.Sin(latitude), horizontal * Mathf.Sin(angle));
                            var uneven = 1f + .11f * Mathf.Sin(side * 4.1f + variant * 11f + row);
                            var vertex = offset + Vector3.Scale(unit, radii) * uneven;
                            var normal = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
                            Append(anchor + Rotate(vertex) * size, Rotate(normal).normalized, colour);
                            if (row == 0) continue;
                            var a = first + (row - 1) * sides + side;
                            var b = first + (row - 1) * sides + (side + 1) % sides;
                            triangles.Add(a); triangles.Add(a + sides); triangles.Add(b);
                            triangles.Add(b); triangles.Add(a + sides); triangles.Add(b + sides);
                        }
                    }

                    Vector3 Rotate(Vector3 point) => new Vector3(
                        point.x * cos - point.z * sin, point.y, point.x * sin + point.z * cos);
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
                    var normal = Vector3.Cross(b - a, c - a).normalized;
                    triangles.Add(vertices.Count);
                    triangles.Add(vertices.Count + 1);
                    triangles.Add(vertices.Count + 2);
                    Append(a, normal, colour);
                    Append(b, normal, colour);
                    Append(c, normal, colour);
                }

                void Append(Vector3 vertex, Vector3 normal, Color32 colour)
                {
                    vertices.Add(vertex);
                    normals.Add(normal);
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
                    mesh.SetNormals(normals);
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
                    normals.Clear();
                    anchors.Clear();
                    triangles.Clear();
                }
            }
        }
    }
}
