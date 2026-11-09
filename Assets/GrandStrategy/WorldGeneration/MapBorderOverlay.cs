using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    internal enum MapViewMode { Terrain, Realms, Territories, Provinces }

    internal sealed class MapBorderOverlay : IDisposable
    {
        private readonly ProvinceMap map;
        private readonly ProvinceShape provinceShape;
        private readonly MapTerrainGeometry geometry;
        private readonly int[] territoryRaster;
        private readonly Layer provinces;
        private readonly Layer territories;
        private readonly Func<int, bool> isDryLand;
        private readonly Layer realms;

        public bool HasTerritories => territoryRaster != null;

        public MapBorderOverlay(GameObject surface, MapTerrainGeometry geometry, ProvinceMap map, ProvinceShape provinceShape,
            Func<int, bool> isDryLand)
        {
            this.map = map;
            this.provinceShape = provinceShape;
            this.geometry = geometry;
            this.isDryLand = isDryLand;
            territoryRaster = GetTerritories(map);

            try
            {
                var shader = Resources.Load<Shader>("MapBorder");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The map border shader requires Universal Render Pipeline.");

                provinces = new Layer(surface.transform, shader, "Province borders", 1.25f, 0,
                    new Color32(40, 45, 40, 235));
                territories = new Layer(surface.transform, shader, "Territory borders", 2.15f, 1,
                    new Color32(45, 50, 48, 238));
                realms = new Layer(surface.transform, shader, "Realm borders", 3.35f, 3, Color.white);
                provinces.Mesh = CreateMesh(map.ProvinceAt, "Province outlines", true);
                SetView(MapViewMode.Provinces);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void SetView(MapViewMode mode)
        {
            var showTerritories = HasTerritories && mode != MapViewMode.Provinces;
            if (showTerritories && territories.Mesh == null)
                territories.Mesh = CreateMesh(territoryRaster, "Territory outlines");
            provinces.Visible = mode != MapViewMode.Territories;
            territories.Visible = showTerritories;
            realms.Visible = realms.Mesh != null;
        }

        public void SetRealm(CampaignState campaign, Color colour)
        {
            var next = campaign == null ? null : CreateMesh(map.ProvinceAt, "Realm frontier", true, (a, b) =>
                campaign.Owns(map.ProvinceAt[a]) != campaign.Owns(map.ProvinceAt[b]));
            UnityEngine.Object.Destroy(realms.Mesh);
            realms.Mesh = next;
            realms.Visible = next != null;
            realms.Colour = colour;
        }

        private sealed class Layer : IDisposable
        {
            private readonly MeshFilter filter;
            private readonly MeshRenderer renderer;
            private readonly Material material;

            public Layer(Transform parent, Shader shader, string name, float width, int order, Color colour)
            {
                var gameObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer))
                    { hideFlags = HideFlags.DontSave };
                gameObject.transform.SetParent(parent, false);
                filter = gameObject.GetComponent<MeshFilter>();
                renderer = gameObject.GetComponent<MeshRenderer>();
                material = new Material(shader) { hideFlags = HideFlags.DontSave, renderQueue = 3000 + order };
                material.SetFloat("_WidthPixels", width);
                Colour = colour;
                renderer.sharedMaterial = material;
            }

            public Mesh Mesh { get => filter.sharedMesh; set => filter.sharedMesh = value; }
            public bool Visible { set => renderer.enabled = value; }
            public Color Colour { set => material.SetColor("_BorderColour", value); }

            public void Dispose()
            {
                UnityEngine.Object.Destroy(Mesh);
                UnityEngine.Object.Destroy(material);
            }
        }

        private Mesh CreateMesh(int[] raster, string label, bool useProvinceShape = false, Func<int, int, bool> include = null)
        {
            var starts = new List<Vector3>();
            var ends = new List<Vector3>();
            var corners = new List<Vector2>();
            var indices = new List<int>();
            var crossings = new List<float>(8);
            MapBorderPaths.Trace(map.Width, map.Height, raster, (path, closed) =>
            {
                var count = closed ? path.Count - 1 : path.Count;
                var first = SmoothedCorner(path, count, 0, closed, useProvinceShape);
                var previous = first;
                for (var i = 1; i < count; i++)
                {
                    var current = SmoothedCorner(path, count, i, closed, useProvinceShape);
                    if (Includes(path[i - 1], path[i])) FollowTerrain(previous, current);
                    previous = current;
                }
                if (closed && Includes(path[count - 1], path[0])) FollowTerrain(previous, first);
            });

            var mesh = new Mesh { name = label, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.SetVertices(starts);
                mesh.SetUVs(0, corners);
                mesh.SetUVs(1, ends);
                mesh.SetTriangles(indices, 0, false);
                var bounds = geometry.Bounds;
                bounds.Expand(.02f);
                mesh.bounds = bounds;
                mesh.UploadMeshData(true);
                return mesh;
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                throw;
            }

            bool Includes(int a, int b)
            {
                var start = Math.Min(a, b);
                var stride = map.Width + 1;
                var x = start % stride;
                var y = start / stride;
                var first = Math.Abs(a - b) == 1 ? Cell(x, y - 1) : Cell(x - 1, y);
                var second = Cell(x, y);
                return IsLandProvinceCell(first) && IsLandProvinceCell(second)
                    && (include == null || include(first, second));
            }

            int Cell(int x, int y) => x >= 0 && x < map.Width && y >= 0 && y < map.Height
                ? y * map.Width + x : -1;

            void FollowTerrain(Vector2 a, Vector2 b)
            {
                // Split at mesh cell sides and their diagonals so no chord cuts through a slope.
                var ax = a.x / map.Width * (geometry.Columns - 1);
                var ay = a.y / map.Height * (geometry.Rows - 1);
                var bx = b.x / map.Width * (geometry.Columns - 1);
                var by = b.y / map.Height * (geometry.Rows - 1);
                crossings.Clear();
                AddCrossings(ax, bx);
                AddCrossings(ay, by);
                AddCrossings(ax + ay, bx + by);
                crossings.Sort();
                var previous = TerrainPoint(a);
                var last = 0f;
                foreach (var t in crossings)
                {
                    if (t - last < .0001f) continue;
                    var point = TerrainPoint(Vector2.Lerp(a, b, t));
                    AddSegment(previous, point);
                    previous = point;
                    last = t;
                }
                AddSegment(previous, TerrainPoint(b));
            }

            void AddCrossings(float a, float b)
            {
                var first = Mathf.FloorToInt(Mathf.Min(a, b)) + 1;
                var limit = Mathf.CeilToInt(Mathf.Max(a, b));
                for (var grid = first; grid < limit; grid++)
                {
                    var t = (grid - a) / (b - a);
                    if (t > .0001f && t < .9999f) crossings.Add(t);
                }
            }

            void AddSegment(Vector3 a, Vector3 b)
            {
                var i = starts.Count;
                starts.Add(a); starts.Add(a); starts.Add(a); starts.Add(a);
                ends.Add(b); ends.Add(b); ends.Add(b); ends.Add(b);
                corners.Add(new Vector2(0f, -1f)); corners.Add(new Vector2(0f, 1f));
                corners.Add(new Vector2(1f, -1f)); corners.Add(new Vector2(1f, 1f));
                indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
                indices.Add(i + 2); indices.Add(i + 1); indices.Add(i + 3);
            }
        }

        private bool IsLandProvinceCell(int cell)
            => cell >= 0 && isDryLand(cell) && CampaignState.IsLand(map, map.ProvinceAt[cell]);

        private Vector2 SmoothedCorner(IReadOnlyList<int> path, int count, int i, bool closed, bool useProvinceShape)
        {
            var point = useProvinceShape
                ? provinceShape.Corner(path[i])
                : ProvinceShape.Smooth(path, count, i, closed, map.Width + 1, .44f);
            return new Vector2(point.X, point.Y);
        }

        private Vector3 TerrainPoint(Vector2 corner)
            => geometry.Sample(new Vector2(corner.x / map.Width, corner.y / map.Height), out _) + Vector3.up * .0001f;

        private static int[] GetTerritories(ProvinceMap map)
        {
            if (map.HasTerritoryPreview) return map.TerritoryAt;

            // Older assets may have membership metadata without a territory image.
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var membership = new int[map.Provinces.Length];
            for (var i = 0; i < membership.Length; i++)
            {
                var id = map.Provinces[i].TerritoryId;
                if (string.IsNullOrEmpty(id))
                {
                    membership[i] = -1;
                    continue;
                }
                if (!ids.TryGetValue(id, out var territory)) ids.Add(id, territory = ids.Count);
                membership[i] = territory;
            }

            if (ids.Count == 0) return null;
            var raster = new int[map.ProvinceAt.Length];
            for (var i = 0; i < raster.Length; i++)
            {
                var province = map.ProvinceAt[i];
                raster[i] = province < 0 ? -1 : membership[province];
            }
            return raster;
        }

        public void Dispose()
        {
            provinces?.Dispose();
            territories?.Dispose();
            realms?.Dispose();
        }
    }
}
