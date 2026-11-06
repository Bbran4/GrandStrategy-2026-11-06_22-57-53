using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.WorldGeneration.Editor
{
    /// <summary>Owns a private preview scene and disposable graphics resources; never edits the user's scene.</summary>
    internal sealed class Heightmap3DPreview : IDisposable
    {
        private PreviewRenderUtility renderer;
        private Mesh mesh;
        private Material material;
        private HeightmapResult world;
        private float verticalScale;
        private float yaw = -20, pitch = 48, zoom = 1;
        private bool useUnlit;

        public void SetWorld(HeightmapResult next, Texture2D texture, float heightScale, bool exactColours)
        {
            try
            {
                if (renderer == null)
                {
                    renderer = new PreviewRenderUtility();
                    renderer.camera.clearFlags = CameraClearFlags.SolidColor;
                    renderer.camera.backgroundColor = new Color(.035f, .045f, .055f);
                    renderer.camera.fieldOfView = 35;
                    renderer.camera.nearClipPlane = .01f; renderer.camera.farClipPlane = 50;
                    renderer.lights[0].intensity = 1.1f;
                    renderer.lights[0].transform.rotation = Quaternion.Euler(50, -35, 0);
                    renderer.lights[1].intensity = .45f;
                    renderer.ambientColor = new Color(.4f, .4f, .4f);
                }
                if (material == null || useUnlit != exactColours)
                {
                    var shaderName = exactColours ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
                    var shader = Shader.Find(shaderName);
                    if (shader == null) throw new InvalidOperationException("3D preview requires the project's URP Lit and Unlit shaders.");
                    var nextMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
                    material = nextMaterial; useUnlit = exactColours;
                    material.SetColor("_BaseColor", Color.white);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0);
                }
                material.SetTexture("_BaseMap", texture);
                if (!ReferenceEquals(world, next) || mesh == null || !Mathf.Approximately(verticalScale, heightScale))
                {
                    var nextMesh = CreateMesh(next, heightScale);
                    if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = nextMesh; world = next; verticalScale = heightScale;
                }
            }
            catch { Dispose(); throw; }
        }

        internal static Mesh CreateMesh(HeightmapResult world, float heightScale)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (float.IsNaN(heightScale) || float.IsInfinity(heightScale) || heightScale <= 0 || heightScale > 1)
                throw new ArgumentOutOfRangeException(nameof(heightScale));
            int width = world.Recipe.Width, height = world.Recipe.Height;
            var aspect = (float)width / height;
            // Bound geometry independently of raster size. Endpoints remain aligned with the source.
            int columns = Mathf.Min(width, Mathf.Max(2, Mathf.RoundToInt(256 * Mathf.Min(1, aspect)) + 1));
            int rows = Mathf.Min(height, Mathf.Max(2, Mathf.RoundToInt(256 / Mathf.Max(1, aspect)) + 1));
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
            var mesh = new Mesh { name = "World heightmap preview", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            try
            {
                mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        public void ResetView() { yaw = -20; pitch = 48; zoom = 1; }

        public bool HandleInput(Rect rect)
        {
            var e = Event.current;
            var id = GUIUtility.GetControlID("GrandStrategy3DPreview".GetHashCode(), FocusType.Passive, rect);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); return true; }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    { yaw += e.delta.x * .5f; pitch = Mathf.Clamp(pitch + e.delta.y * .35f, 8, 85); e.Use(); return true; }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); return true; }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition)) { zoom = Mathf.Clamp(zoom * Mathf.Exp(e.delta.y * .06f), .3f, 3); e.Use(); return true; }
                    break;
            }
            return false;
        }

        public void Draw(Rect rect)
        {
            if (renderer == null || mesh == null || rect.width < 2 || rect.height < 2 || Event.current.type != EventType.Repaint) return;
            var target = new Vector3(0, mesh.bounds.max.y * .35f, 0);
            var radius = mesh.bounds.extents.magnitude;
            var distance = radius / Mathf.Sin(renderer.camera.fieldOfView * Mathf.Deg2Rad / 2) * zoom
                / Mathf.Min(1, rect.width / rect.height);
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            renderer.camera.transform.SetPositionAndRotation(target + rotation * Vector3.back * distance, rotation);
            renderer.BeginPreview(rect, GUIStyle.none);
            Texture image;
            try { renderer.DrawMesh(mesh, Matrix4x4.identity, material, 0); renderer.Render(true); }
            finally { image = renderer.EndPreview(); }
            GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, false);
        }

        public void Dispose()
        {
            // Keep ownership if cleanup fails, allowing a later Dispose to retry it.
            if (renderer != null) { renderer.Cleanup(); renderer = null; }
            if (mesh != null) { UnityEngine.Object.DestroyImmediate(mesh); mesh = null; }
            if (material != null) { UnityEngine.Object.DestroyImmediate(material); material = null; }
            world = null;
        }
    }
}
