using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration
{
    internal enum MapViewMode { Terrain, Territories, Provinces }

    internal sealed class MapBorderOverlay : IDisposable
    {
        private readonly ProvinceMap map;
        private readonly ProvinceShape provinceShape;
        private readonly MapTerrainGeometry geometry;
        private readonly int[] territoryRaster;
        private Material material;
        private MeshFilter filter;
        private MeshRenderer renderer;
        private Mesh provinceBorders;
        private Mesh territoryBorders;

        public bool HasTerritories => territoryRaster != null;

        public MapBorderOverlay(GameObject surface, MapTerrainGeometry geometry, ProvinceMap map, ProvinceShape provinceShape)
        {
            this.map = map;
            this.provinceShape = provinceShape;
            this.geometry = geometry;
            territoryRaster = GetTerritories(map);

            try
            {
                var shader = Resources.Load<Shader>("MapBorder");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The map border shader requires Universal Render Pipeline.");
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
                var overlay = new GameObject("Map borders", typeof(MeshFilter), typeof(MeshRenderer))
                    { hideFlags = HideFlags.DontSave };
                overlay.transform.SetParent(surface.transform, false);
                filter = overlay.GetComponent<MeshFilter>();
                renderer = overlay.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
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
            switch (mode)
            {
                case MapViewMode.Terrain:
                    renderer.enabled = false;
                    return;
                case MapViewMode.Territories:
                    if (!HasTerritories) throw new InvalidOperationException("This map has no territory data.");
                    if (territoryBorders == null) territoryBorders = CreateMesh(territoryRaster, "Territory outlines");
                    filter.sharedMesh = territoryBorders;
                    material.SetFloat("_WidthPixels", 2.2f);
                    break;
                case MapViewMode.Provinces:
                    if (provinceBorders == null) provinceBorders = CreateMesh(map.ProvinceAt, "Province outlines", true);
                    filter.sharedMesh = provinceBorders;
                    material.SetFloat("_WidthPixels", 1.6f);
                    break;
            }
            renderer.enabled = true;
        }

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
            UnityEngine.Object.Destroy(provinceBorders);
            UnityEngine.Object.Destroy(territoryBorders);
            UnityEngine.Object.Destroy(material);
        }
    }
}
