using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal sealed class MapSettlement : IDisposable
    {
        private static readonly Vector2 GranaryOffset = new Vector2(.0020f, .0018f);
        private readonly MapTerrainGeometry terrain;
        private readonly Func<Vector2, bool> isDryLand;
        private readonly float aspect;
        private readonly GameObject settlement;
        private readonly MeshFilter filter;
        private readonly Material material;
        private Mesh mesh;

        public MapSettlement(Transform parent, MapTerrainGeometry terrain, Texture2D shadow, float aspect,
            Func<Vector2, bool> isDryLand)
        {
            this.terrain = terrain;
            this.aspect = aspect;
            this.isDryLand = isDryLand;
            var shader = Resources.Load<Shader>("MapNature");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The settlement requires the map nature shader.");

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
        }

        public void Show(Vector3 capital, bool granary, bool expanded)
        {
            var vertices = new List<Vector3>();
            var anchors = new List<Vector3>();
            var colours = new List<Color32>();
            var indices = new List<int>();
            var timber = new Color32(158, 111, 71, 255);
            var thatch = new Color32(184, 145, 76, 255);
            var storeWalls = new Color32(171, 136, 94, 255);
            var storeRoof = new Color32(139, 71, 43, 255);

            var hasGranarySite = TryAnchor(GranaryOffset, out var store);
            if (granary && !hasGranarySite)
                House(capital, .00125f, storeWalls, storeRoof);
            else
                House(capital, .00108f, timber, thatch);
            if (granary && hasGranarySite) House(store, .00125f, storeWalls, storeRoof);

            AddHut(new Vector2(-.0019f, .0014f));
            AddHut(new Vector2(.0005f, -.0021f));
            if (expanded) AddHut(new Vector2(-.0026f, -.0017f));

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

            void AddHut(Vector2 offset)
            {
                if (TryAnchor(offset, out var anchor)) House(anchor, .00078f, timber, thatch);
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
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(settlement);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
        }
    }
}
