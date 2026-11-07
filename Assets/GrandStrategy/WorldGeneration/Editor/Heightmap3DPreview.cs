using System;
using UnityEditor;
using UnityEngine;

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
                    var nextMesh = HeightmapMesh.Create(next, heightScale);
                    if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = nextMesh; world = next; verticalScale = heightScale;
                }
            }
            catch { Dispose(); throw; }
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

        public bool TryPick(Rect rect, Vector2 mouse, out Vector2 uv)
        {
            uv = default;
            if (renderer == null || mesh == null || rect.width < 2 || rect.height < 2 || !rect.Contains(mouse))
                return false;

            var camera = renderer.camera;
            var aspect = rect.width / rect.height;
            var verticalTangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * Mathf.Max(1f, 1f / aspect);
            var x = (mouse.x - rect.x) / rect.width * 2f - 1f;
            var y = 1f - (mouse.y - rect.y) / rect.height * 2f;
            var direction = camera.transform.rotation * new Vector3(x * aspect * verticalTangent, y * verticalTangent, 1);
            var origin = camera.transform.position;

            // Pick against the same mesh used for the preview, including its downsampling.
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var edge1 = vertices[triangles[i + 1]] - a;
                var edge2 = vertices[triangles[i + 2]] - a;
                var cross = Vector3.Cross(direction, edge2);
                var determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < .0000001f) continue;
                var inverse = 1f / determinant;
                var offset = origin - a;
                var u = Vector3.Dot(offset, cross) * inverse;
                if (u < 0f || u > 1f) continue;
                var offsetCross = Vector3.Cross(offset, edge1);
                var v = Vector3.Dot(direction, offsetCross) * inverse;
                if (v < 0f || u + v > 1f) continue;
                var t = Vector3.Dot(edge2, offsetCross) * inverse;
                if (t >= 0f && t < nearest) nearest = t;
            }

            if (float.IsPositiveInfinity(nearest)) return false;
            var point = origin + nearest * direction;
            uv = new Vector2(point.x / ((float)world.Recipe.Width / world.Recipe.Height) + .5f, point.z + .5f);
            return true;
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
