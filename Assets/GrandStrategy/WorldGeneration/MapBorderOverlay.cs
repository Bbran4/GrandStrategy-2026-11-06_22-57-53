// Builds and owns the meshes for province and territory outlines on the playable surface.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    internal enum MapViewMode { Terrain, Territories, Provinces, Realm, Ownership, Biomes }

    internal sealed class MapBorderOverlay : IDisposable
    {
        private readonly ProvinceMap map;
        private readonly ProvinceShape provinceShape;
        private readonly MapTerrainGeometry geometry;
        private readonly int[] territoryRaster;
        private readonly Layer provinces;
        private readonly Layer territories;
        private readonly Func<int, bool> isDryLand;
        private readonly Func<int, bool> isRiver;
        private readonly Func<int, bool> isLake;

        public bool HasTerritories => territoryRaster != null;

        public MapBorderOverlay(GameObject surface, MapTerrainGeometry geometry, ProvinceMap map, ProvinceShape provinceShape,
            Func<int, bool> isDryLand, Func<int, bool> isRiver, Func<int, bool> isLake,
            Func<int, bool> isNavigableRiverCell)
        {
            this.map = map;
            this.geometry = geometry;
            this.isDryLand = isDryLand;
            this.isRiver = isRiver;
            this.isLake = isLake;
            var provinceRaster = MapBorderPaths.FillWaterGaps(map.Width, map.ProvinceAt, isRiver, CanSeedProvince,
                isNavigableRiverCell);
            provinceRaster = MapBorderPaths.OutlineLakes(map.Width, provinceRaster, isLake, cell =>
            {
                var province = map.ProvinceAt[cell];
                return province >= 0 && string.Equals(map.Provinces[province].Type, "lake", StringComparison.OrdinalIgnoreCase);
            });
            this.provinceShape = ReferenceEquals(provinceRaster, map.ProvinceAt) ? provinceShape
                : new ProvinceShape(map.Width, map.Height, provinceRaster);
            territoryRaster = CreateTerritoryRaster(isNavigableRiverCell);

            try
            {
                var shader = Resources.Load<Shader>("MapBorder");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The map border shader requires Universal Render Pipeline.");

                provinces = new Layer(surface.transform, shader, "Province borders", 1.25f, 0,
                    new Color32(40, 45, 40, 235));
                territories = new Layer(surface.transform, shader, "Territory borders", 2.15f, 1,
                    new Color32(45, 50, 48, 238));
                provinces.Mesh = CreateMesh(provinceRaster, "Province outlines", true);
                SetView(MapViewMode.Provinces);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // Show the appropriate outlines for the selected map mode.
        public void SetView(MapViewMode mode)
        {
            var showTerritories = HasTerritories && (mode == MapViewMode.Provinces
                || mode == MapViewMode.Territories || mode == MapViewMode.Realm);
            if (showTerritories && territories.Mesh == null)
                territories.Mesh = CreateMesh(territoryRaster, "Territory outlines");
            provinces.Visible = mode == MapViewMode.Provinces || mode == MapViewMode.Ownership
                || mode == MapViewMode.Realm;
            territories.Visible = showTerritories;
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
                material = new Material(shader) { hideFlags = HideFlags.DontSave, renderQueue = 4000 + order };
                material.SetFloat("_WidthPixels", width);
                material.SetColor("_BorderColour", colour);
                renderer.sharedMaterial = material;
                renderer.enabled = false;
            }

            public Mesh Mesh { get => filter.sharedMesh; set => filter.sharedMesh = value; }
            public bool Visible { set => renderer.enabled = value; }

            public void Dispose()
            {
                renderer.enabled = false;
                UnityEngine.Object.Destroy(Mesh);
                UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(filter.gameObject);
            }
        }

        // Convert traced border paths into segments that the border shader expands on screen.
        private Mesh CreateMesh(int[] raster, string label, bool useProvinceShape = false)
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
                    FollowTerrain(previous, current);
                    previous = current;
                }
                if (closed) FollowTerrain(previous, first);
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

            // Split segments at mesh crossings so the line follows the same terrain triangles as the
            // ground.
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

        private bool CanSeedProvince(int cell)
            => (isDryLand(cell) || isRiver(cell)) && CampaignState.IsLand(map, map.ProvinceAt[cell]);

        private Vector2 SmoothedCorner(IReadOnlyList<int> path, int count, int i, bool closed, bool useProvinceShape)
        {
            var point = useProvinceShape
                ? provinceShape.Corner(path[i])
                : ProvinceShape.Smooth(path, count, i, closed, map.Width + 1, .44f);
            return new Vector2(point.X, point.Y);
        }

        private Vector3 TerrainPoint(Vector2 corner)
            => geometry.Sample(new Vector2(corner.x / map.Width, corner.y / map.Height), out _) + Vector3.up * .0001f;

        private int[] CreateTerritoryRaster(Func<int, bool> isNavigableRiverCell)
        {
            var imported = GetTerritories(map);
            if (imported == null) return null;

            var raster = imported;
            var firstRiverId = map.HasTerritoryPreview ? map.Territories.Length : map.Provinces.Length;
            for (var cell = 0; cell < raster.Length; cell++)
            {
                if (!isNavigableRiverCell(cell) || HasTerritoryType(cell, "ocean")) continue;
                // A river province without its own water territory still needs a border in this view.
                if (ReferenceEquals(raster, imported)) raster = (int[])imported.Clone();
                raster[cell] = firstRiverId + map.ProvinceAt[cell];
            }
            raster = MapBorderPaths.FillWaterGaps(map.Width, raster, isRiver,
                cell => CanSeedProvince(cell) && (!map.HasTerritoryPreview || HasTerritoryType(cell, "land")),
                isNavigableRiverCell);
            return MapBorderPaths.OutlineLakes(map.Width, raster, isLake, cell => HasTerritoryType(cell, "lake"));

            bool HasTerritoryType(int cell, string type)
            {
                var id = imported[cell];
                return map.HasTerritoryPreview && id >= 0 && id < map.Territories.Length
                    && string.Equals(map.Territories[id]?.Type, type, StringComparison.OrdinalIgnoreCase);
            }
        }

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
        }
    }
}
