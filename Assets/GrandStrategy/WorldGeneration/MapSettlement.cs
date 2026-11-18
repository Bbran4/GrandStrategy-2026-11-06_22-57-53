// Builds the visible capital settlement and updates its appearance as the campaign develops.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal sealed class MapSettlement : IDisposable
    {
        public const float ProductionHalfWidth = .0010f;
        public const float BuildRadius = CampaignState.VillageRadius;
        public const float TownBuildRadius = CampaignState.TownRadius;
        public const float GranaryHalfWidth = .00125f;
        private static readonly Vector2 FirstHut = new Vector2(-.0019f, .0014f);
        private static readonly Vector2 SecondHut = new Vector2(.0005f, -.0021f);
        private static readonly Vector2 ThirdHut = new Vector2(-.0026f, -.0017f);
        public static readonly Vector2 GranaryOffset = new Vector2(.0020f, .0018f);
        private readonly MapTerrainGeometry terrain;
        private readonly Func<Vector2, bool> isDryLand;
        private readonly float aspect;
        private readonly GameObject settlement;
        private readonly MeshFilter filter;
        private readonly Material material;
        private Mesh mesh;
        private readonly LineRenderer boundary;
        private readonly Material boundaryMaterial;

        public static bool Contains(Vector3 capital, Vector3 point, float radius)
        {
            var x = point.x - capital.x;
            var z = point.z - capital.z;
            return x * x + z * z <= radius * radius;
        }

        public MapSettlement(Transform parent, MapTerrainGeometry terrain, Texture2D shadow, float aspect,
            Func<Vector2, bool> isDryLand)
        {
            this.terrain = terrain;
            this.aspect = aspect;
            this.isDryLand = isDryLand;
            var shader = Resources.Load<Shader>("MapNature");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The settlement requires the map nature shader.");

            var boundaryShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (boundaryShader == null || !boundaryShader.isSupported)
                throw new InvalidOperationException("City limits require the URP Unlit shader.");

            settlement = new GameObject("Capital village", typeof(MeshFilter), typeof(MeshRenderer))
                { hideFlags = HideFlags.DontSave };
            settlement.transform.SetParent(parent, false);
            filter = settlement.GetComponent<MeshFilter>();
            material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.SetFloat("_Visibility", 1f);
            material.SetTexture("_TerrainShadow", shadow);
            var sun = terrain.SunDirection;
            material.SetVector("_MapSun", new Vector4(sun.x, sun.y, sun.z, 1f / aspect));
            settlement.GetComponent<MeshRenderer>().sharedMaterial = material;
            var outline = new GameObject("City limits", typeof(LineRenderer))
                { hideFlags = HideFlags.DontSave };
            outline.transform.SetParent(settlement.transform, false);
            boundary = outline.GetComponent<LineRenderer>();
            boundary.useWorldSpace = false;
            boundary.loop = true;
            boundary.widthMultiplier = .00027f;
            boundaryMaterial = new Material(boundaryShader) { hideFlags = HideFlags.DontSave };
            boundaryMaterial.SetColor("_BaseColor", new Color(1f, .8f, .35f));
            boundary.sharedMaterial = boundaryMaterial;
            boundary.enabled = false;
        }

        public static bool FitsGranary(Vector3 capital, Vector3 site, float radius)
        {
            var offset = new Vector2(site.x - capital.x, site.z - capital.z);
            const float clearance = GranaryHalfWidth * 1.42f;
            return offset.magnitude + clearance <= radius
                && ClearOf(Vector2.zero, .00108f)
                && ClearOf(FirstHut, .00078f)
                && ClearOf(SecondHut, .00078f)
                && ClearOf(ThirdHut, .00078f);

            bool ClearOf(Vector2 building, float halfWidth)
                => Mathf.Abs(offset.x - building.x) >= GranaryHalfWidth + halfWidth
                    || Mathf.Abs(offset.y - building.y) >= (GranaryHalfWidth + halfWidth) * .77f;
        }

        public void SetBoundaryVisible(bool visible) => boundary.enabled = visible;

        // Place or refresh settlement models on the sampled terrain surface.
        public void Show(Vector3 capital, bool granary, bool expanded, Vector3? granarySite, SettlementBuilding production, Func<SettlementBuilding, Vector3?> placement, Vector3? dockWater, float radius, bool independent = false)
        {
            const int segments = 96;
            boundary.positionCount = segments;
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var x = capital.x + Mathf.Cos(angle) * radius;
                var z = capital.z + Mathf.Sin(angle) * radius;
                var uv = new Vector2(Mathf.Clamp01(x / aspect + .5f), Mathf.Clamp01(z + .5f));
                boundary.SetPosition(i, terrain.Sample(uv, out _) + Vector3.up * .0007f);
            }

            var vertices = new List<Vector3>();
            var anchors = new List<Vector3>();
            var colours = new List<Color32>();
            var indices = new List<int>();
            var occupied = new List<Vector3> { capital };
            Vector3? farmSite = null;
            var productionAnchors = new List<Vector3>();
            var timber = new Color32(158, 111, 71, 255);
            var thatch = independent ? new Color32(139, 87, 158, 255) : new Color32(184, 145, 76, 255);
            var storeWalls = new Color32(171, 136, 94, 255);
            var storeRoof = new Color32(139, 71, 43, 255);

            var hasGranarySite = TryAnchor(GranaryOffset, out var store);
            if (granarySite.HasValue)
            {
                store = granarySite.Value;
                hasGranarySite = true;
            }
            if (granary && !hasGranarySite)
                House(capital, .00125f, storeWalls, storeRoof);
            else
                House(capital, .00108f, timber, thatch);
            if (granary && hasGranarySite)
            {
                House(store, GranaryHalfWidth, storeWalls, storeRoof);
                occupied.Add(store);
            }

            AddHut(FirstHut);
            AddHut(SecondHut);
            if (expanded) AddHut(ThirdHut);

            foreach (var building in new[] { SettlementBuilding.Farm, SettlementBuilding.LumberCamp,
                SettlementBuilding.IronMine, SettlementBuilding.Docks })
            {
                var fixedSite = placement(building);
                if (fixedSite.HasValue) occupied.Add(fixedSite.Value);
            }

            AddProduction(SettlementBuilding.Farm, new Color32(110, 126, 53, 255), thatch);
            AddProduction(SettlementBuilding.LumberCamp, timber, new Color32(92, 65, 42, 255));
            AddProduction(SettlementBuilding.IronMine, new Color32(107, 104, 101, 255), new Color32(66, 61, 59, 255));
            AddProduction(SettlementBuilding.Docks, storeWalls, new Color32(81, 104, 119, 255));
            foreach (var anchor in productionAnchors) AddPath(anchor);
            if (farmSite.HasValue) AddFields(farmSite.Value);

            var next = new Mesh { name = "Capital village", hideFlags = HideFlags.DontSave };
            try
            {
                next.SetVertices(vertices);
                next.SetUVs(0, anchors);
                next.SetColors(colours);
                next.SetTriangles(indices, 0);
                next.RecalculateNormals();
                next.UploadMeshData(true);
            }
            catch
            {
                UnityEngine.Object.Destroy(next);
                throw;
            }
            filter.sharedMesh = next;
            UnityEngine.Object.Destroy(mesh);
            mesh = next;

            // Check the site and add a small building to the settlement mesh.
            void AddHut(Vector2 offset)
            {
                if (!TryAnchor(offset, out var anchor)) return;
                House(anchor, .00078f, timber, thatch);
                occupied.Add(anchor);
            }

            void AddProduction(SettlementBuilding building, Color32 walls, Color32 roof)
            {
                if ((production & building) == 0) return;
                const int sites = 24;
                const float halfWidth = ProductionHalfWidth;
                var fixedSite = placement(building);
                if (fixedSite.HasValue)
                {
                    AddProductionModel(fixedSite.Value);
                    if (building == SettlementBuilding.Docks && dockWater.HasValue)
                        Pier(fixedSite.Value, dockWater.Value);
                    return;
                }
                for (var i = 0; i < sites; i++)
                {
                    var angle = i * (2f * Mathf.PI / sites);
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .0059f;
                    if (!TryAnchor(offset, out var anchor)
                        || !TryAnchor(offset + new Vector2(-halfWidth, -halfWidth * .77f), out _)
                        || !TryAnchor(offset + new Vector2(-halfWidth, halfWidth * .77f), out _)
                        || !TryAnchor(offset + new Vector2(halfWidth, -halfWidth * .77f), out _)
                        || !TryAnchor(offset + new Vector2(halfWidth, halfWidth * .77f), out _)) continue;

                    var blocked = false;
                    foreach (var other in occupied)
                        if (Mathf.Abs(other.x - anchor.x) < halfWidth + GranaryHalfWidth
                            && Mathf.Abs(other.z - anchor.z) < (halfWidth + GranaryHalfWidth) * .77f)
                        {
                            blocked = true;
                            break;
                        }

                    if (blocked) continue;
                    AddProductionModel(anchor);
                    occupied.Add(anchor);
                    return;
                }

                void AddProductionModel(Vector3 anchor)
                {
                    House(anchor, halfWidth, walls, roof);
                    productionAnchors.Add(anchor);
                    if (building == SettlementBuilding.Farm) farmSite = anchor;
                }
            }

            void AddPath(Vector3 destination)
            {
                var endOffset = new Vector2(destination.x - capital.x, destination.z - capital.z);
                var length = endOffset.magnitude;
                const float setback = .0011f;
                if (length <= setback * 2f) return;

                var along = endOffset / length;
                var across = new Vector2(-along.y, along.x);
                const int steps = 24;
                const float pathHalfWidth = .00017f;
                var left = new Vector3[steps + 1];
                var right = new Vector3[steps + 1];
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var bend = (attempt + 1) / 2 * .0011f * (attempt % 2 == 0 ? 1f : -1f);
                    var fits = true;
                    for (var i = 0; i <= steps; i++)
                    {
                        var t = i / (float)steps;
                        var offset = along * Mathf.Lerp(setback, length - setback, t)
                            + across * (Mathf.Sin(t * Mathf.PI) * bend);
                        if (!SamplePath(offset + across * pathHalfWidth, out left[i])
                            || !SamplePath(offset - across * pathHalfWidth, out right[i]))
                        {
                            fits = false;
                            break;
                        }
                    }

                    if (!fits) continue;
                    var dirt = new Color32(126, 100, 73, 255);
                    for (var i = 0; i < steps; i++)
                    {
                        AddTriangle(right[i], left[i], left[i + 1], capital, dirt);
                        AddTriangle(right[i], left[i + 1], right[i + 1], capital, dirt);
                    }
                    return;
                }

                bool SamplePath(Vector2 offset, out Vector3 point)
                {
                    point = default;
                    if (offset.sqrMagnitude > radius * radius || !TryAnchor(offset, out point))
                        return false;
                    foreach (var building in occupied)
                    {
                        if (building == capital || building == destination) continue;
                        if (Mathf.Abs(point.x - building.x) < GranaryHalfWidth
                            && Mathf.Abs(point.z - building.z) < GranaryHalfWidth * .77f)
                            return false;
                    }
                    point += Vector3.up * .00012f;
                    return true;
                }
            }

            void AddFields(Vector3 farm)
            {
                const int rows = 6;
                const int intervals = 4;
                const float halfWidth = .00115f;
                const float halfLength = .0010f;
                var farmOffset = new Vector2(farm.x - capital.x, farm.z - capital.z);
                var field = new Vector3[rows + 1, intervals + 1];
                var soil = new Color32(101, 72, 43, 255);
                var cropA = new Color32(126, 139, 61, 255);
                var cropB = new Color32(150, 151, 68, 255);

                for (var direction = 0; direction < 8; direction++)
                {
                    var angle = direction * Mathf.PI / 4f;
                    var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    var across = new Vector2(-along.y, along.x);
                    var centre = farmOffset + along * .00245f;
                    if (!SampleField()) continue;

                    for (var row = 0; row < rows; row++)
                        for (var interval = 0; interval < intervals; interval++)
                        {
                            var a = field[row, interval];
                            var b = field[row + 1, interval];
                            var c = field[row + 1, interval + 1];
                            var d = field[row, interval + 1];
                            AddTriangle(a, b, c, farm, soil);
                            AddTriangle(a, c, d, farm, soil);

                            var rise = Vector3.up * .00010f;
                            var nearLeft = Vector3.Lerp(a, b, .22f) + rise;
                            var nearRight = Vector3.Lerp(a, b, .73f) + rise;
                            var farLeft = Vector3.Lerp(d, c, .22f) + rise;
                            var farRight = Vector3.Lerp(d, c, .73f) + rise;
                            var crop = row % 2 == 0 ? cropA : cropB;
                            AddTriangle(nearLeft, nearRight, farRight, farm, crop);
                            AddTriangle(nearLeft, farRight, farLeft, farm, crop);
                        }
                    return;

                    bool SampleField()
                    {
                        for (var row = 0; row <= rows; row++)
                            for (var interval = 0; interval <= intervals; interval++)
                            {
                                var width = Mathf.Lerp(-halfWidth, halfWidth, row / (float)rows);
                                var length = Mathf.Lerp(-halfLength, halfLength, interval / (float)intervals);
                                var offset = centre + across * width + along * length;
                                if (offset.sqrMagnitude > radius * radius || !TryAnchor(offset, out var point))
                                    return false;
                                foreach (var building in occupied)
                                    if (Mathf.Abs(building.x - point.x) < GranaryHalfWidth
                                        && Mathf.Abs(building.z - point.z) < GranaryHalfWidth * .77f)
                                        return false;
                                field[row, interval] = point + Vector3.up * .00018f;
                            }
                        return true;
                    }
                }
            }

            void Pier(Vector3 dock, Vector3 waterPoint)
            {
                var direction = waterPoint - dock;
                direction.y = 0f;
                var distance = direction.magnitude;
                if (distance <= .00001f) return;
                direction /= distance;
                var side = Vector3.Cross(Vector3.up, direction) * .00048f;
                var start = dock + direction * Mathf.Min(distance * .45f, ProductionHalfWidth * .6f);
                var end = waterPoint + direction * .00055f;
                const int plankCount = 12;
                var deck = new Vector3[plankCount + 1];
                for (var i = 0; i < deck.Length; i++)
                {
                    var point = Vector3.Lerp(start, end, i / (float)plankCount);
                    var uv = new Vector2(Mathf.Clamp01(point.x / aspect + .5f), Mathf.Clamp01(point.z + .5f));
                    point.y = Mathf.Max(terrain.Sample(uv, out _).y, Mathf.Lerp(dock.y, waterPoint.y, i / (float)plankCount))
                        + .00032f;
                    deck[i] = point;
                }
                for (var i = 0; i < plankCount; i++)
                {
                    var a = deck[i];
                    var b = deck[i + 1];
                    var colour = i % 2 == 0 ? timber : storeWalls;
                    AddTriangle(a - side, b - side, b + side, dock, colour);
                    AddTriangle(a - side, b + side, a + side, dock, colour);
                    var down = Vector3.down * .00022f;
                    AddTriangle(a - side + down, b - side + down, b - side, dock, storeRoof);
                    AddTriangle(a - side + down, b - side, a - side, dock, storeRoof);
                    AddTriangle(a + side, b + side, b + side + down, dock, storeRoof);
                    AddTriangle(a + side, b + side + down, a + side + down, dock, storeRoof);
                }
            }

            bool TryAnchor(Vector2 offset, out Vector3 anchor)
            {
                var uv = new Vector2((capital.x + offset.x) / aspect + .5f, capital.z + offset.y + .5f);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f || !isDryLand(uv))
                {
                    anchor = default;
                    return false;
                }
                anchor = terrain.Sample(uv, out _);
                return true;
            }

            void House(Vector3 anchor, float halfWidth, Color32 wall, Color32 roof)
            {
                var x = halfWidth;
                var z = halfWidth * .77f;
                var floor = -halfWidth * .32f;
                var eave = Vector3.up * (halfWidth * 1.05f);
                var a = anchor + new Vector3(-x, floor, -z);
                var b = anchor + new Vector3(x, floor, -z);
                var c = anchor + new Vector3(x, floor, z);
                var d = anchor + new Vector3(-x, floor, z);
                var topA = a + eave;
                var topB = b + eave;
                var topC = c + eave;
                var topD = d + eave;
                var ridgeA = (topA + topB) * .5f + Vector3.up * (halfWidth * .66f);
                var ridgeB = (topD + topC) * .5f + Vector3.up * (halfWidth * .66f);
                Quad(a, topA, topB, b, wall);
                Quad(b, topB, topC, c, wall);
                Quad(c, topC, topD, d, wall);
                Quad(d, topD, topA, a, wall);
                Quad(topA, topD, ridgeB, ridgeA, roof);
                Quad(topB, ridgeA, ridgeB, topC, roof);
                Triangle(topA, ridgeA, topB, wall);
                Triangle(topC, ridgeB, topD, wall);

                void Quad(Vector3 p, Vector3 q, Vector3 r, Vector3 t, Color32 colour)
                {
                    Triangle(p, q, r, colour);
                    Triangle(p, r, t, colour);
                }

                void Triangle(Vector3 p, Vector3 q, Vector3 r, Color32 colour)
                    => AddTriangle(p, q, r, anchor, colour);
            }

            void AddTriangle(Vector3 p, Vector3 q, Vector3 r, Vector3 anchor, Color32 colour)
            {
                Add(p); Add(q); Add(r);
                void Add(Vector3 point)
                {
                    indices.Add(vertices.Count);
                    vertices.Add(point);
                    anchors.Add(anchor);
                    colours.Add(colour);
                }
            }
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(settlement);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(boundaryMaterial);
        }
    }
}
