// Applies Scene view brush strokes to the same TerrainData used by Unity's Terrain Inspector.
using System;
using UnityEditor;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed partial class PlayableMapInspector
    {
        private enum SculptBrush { Raise, Lower, Smooth, Flatten }
        private SculptBrush sculptBrush;
        private float brushSize = 45f;
        private float brushStrength = .35f;
        private int paintLayer = 1;
        private Terrain brushTerrain;
        private WaterMap brushWater;
        private byte[] brushRivers;
        private bool brushPaint;
        private SceneView brushView;
        private int brushControl;
        private int strokeGroup = -1;
        private Vector3 lastDab;
        private float flattenHeight;
        private const string SculptUndo = "Sculpt Terrain";
        private const string PaintUndo = "Paint Terrain";

        private void DrawBrushControls(Terrain terrain, WorldHeightmap world, bool paint)
        {
            var data = terrain.terrainData;
            if (paint)
            {
                var layers = data.terrainLayers;
                if (layers.Length == 0 || layers.Length > 4)
                {
                    EditorGUILayout.HelpBox("Assign between one and four Terrain Layers to paint this terrain.", MessageType.Info);
                    return;
                }
                var names = Array.ConvertAll(layers, layer => layer == null ? "(Missing layer)" : layer.name);
                paintLayer = EditorGUILayout.Popup("Layer", Mathf.Clamp(paintLayer, 0, names.Length - 1), names);
            }
            else
            {
                sculptBrush = (SculptBrush)EditorGUILayout.EnumPopup("Brush", sculptBrush);
                if (sculptBrush == SculptBrush.Flatten)
                    EditorGUILayout.HelpBox("Flatten uses the height under the first click of each stroke.", MessageType.Info);
            }

            brushSize = EditorGUILayout.Slider("Diameter", brushSize, 5f, 200f);
            brushStrength = EditorGUILayout.Slider("Strength", brushStrength, .01f, 1f);

            if (brushTerrain != null)
            {
                EditorGUILayout.HelpBox("Click or drag in the Scene view. Press Escape to release the brush.", MessageType.Info);
                if (GUILayout.Button("Release Brush")) StopBrush();
            }
            else if (GUILayout.Button(paint ? "Activate Paint Brush" : "Activate Sculpt Brush"))
            {
                if (!paint)
                {
                    var rivers = world.ReadRivers();
                    brushWater = WaterMap.Classify(rivers?.Terrain ?? world.Read());
                    brushRivers = rivers?.BuildSurfaceMask();
                }
                brushPaint = paint;
                brushTerrain = terrain;
                SceneView.duringSceneGui += DrawBrushInScene;
                SceneView.lastActiveSceneView?.Focus();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Select Terrain for Unity Brush Tools"))
            {
                StopBrush();
                Selection.activeGameObject = terrain.gameObject;
            }
        }

        private void StopBrush()
        {
            FinishStroke();
            SceneView.duringSceneGui -= DrawBrushInScene;
            brushTerrain = null;
            brushWater = null;
            brushRivers = null;
            SceneView.RepaintAll();
        }

        private void DrawBrushInScene(SceneView scene)
        {
            var input = Event.current;
            if (brushTerrain == null || brushTerrain.terrainData == null || EditorApplication.isPlayingOrWillChangePlaymode
                || (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape))
            {
                if (input.type == EventType.KeyDown) input.Use();
                StopBrush();
                return;
            }

            var control = GUIUtility.GetControlID(FocusType.Passive);
            if (input.type == EventType.Layout && !input.alt) HandleUtility.AddDefaultControl(control);
            if (input.type == EventType.MouseMove) scene.Repaint();
            if (brushView != null && brushView != scene) return;
            if (strokeGroup >= 0 && input.type == EventType.MouseUp && input.button == 0)
            {
                FinishStroke();
                input.Use();
                return;
            }
            if (input.alt) return;

            var collider = brushTerrain.GetComponent<TerrainCollider>();
            if (collider == null || !collider.Raycast(HandleUtility.GUIPointToWorldRay(input.mousePosition),
                    out var hit, float.PositiveInfinity)) return;

            if (input.type == EventType.Repaint)
            {
                using (new Handles.DrawingScope(brushPaint ? new Color(.45f, .9f, .55f) : new Color(1f, .74f, .3f)))
                    Handles.DrawWireDisc(hit.point + Vector3.up, Vector3.up, brushSize * .5f);
                return;
            }

            var local = hit.point - brushTerrain.transform.position;
            if (input.type == EventType.MouseDown && input.button == 0 && HandleUtility.nearestControl == control)
            {
                input.Use();
                var data = brushTerrain.terrainData;
                var undoName = brushPaint ? PaintUndo : SculptUndo;
                Undo.IncrementCurrentGroup();
                strokeGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                Undo.RegisterCompleteObjectUndo(data, undoName);
                if (brushPaint) Undo.RegisterCompleteObjectUndo(data.alphamapTextures, undoName);
                brushControl = control;
                GUIUtility.hotControl = control;
                brushView = scene;
                flattenHeight = Mathf.Clamp01(local.y / data.size.y);
                lastDab = local;
                Dab(data, local);
                scene.Repaint();
            }
            else if (input.type == EventType.MouseDrag && input.button == 0 && strokeGroup >= 0)
            {
                input.Use();
                var distance = new Vector2(local.x - lastDab.x, local.z - lastDab.z).magnitude;
                var spacing = brushSize * .2f;
                if (distance < spacing) return;
                var steps = Mathf.Min(64, Mathf.FloorToInt(distance / spacing));
                var start = lastDab;
                var fraction = Mathf.Min(1f, spacing * steps / distance);
                for (var i = 1; i <= steps; i++)
                    Dab(brushTerrain.terrainData, Vector3.Lerp(start, local, i * fraction / steps));
                lastDab = Vector3.Lerp(start, local, fraction);
                scene.Repaint();
            }
        }

        private void FinishStroke()
        {
            if (strokeGroup < 0) return;
            if (GUIUtility.hotControl == brushControl) GUIUtility.hotControl = 0;
            var data = brushTerrain == null ? null : brushTerrain.terrainData;
            if (data != null)
            {
                if (brushPaint) MarkPaintDirty(data);
                else
                {
                    data.SyncHeightmap();
                    EditorUtility.SetDirty(data);
                    RefitTerrainOverlays(brushTerrain, SculptUndo);
                }
            }
            Undo.CollapseUndoOperations(strokeGroup);
            strokeGroup = -1;
            brushView = null;
            SceneView.RepaintAll();
        }

        private void Dab(TerrainData data, Vector3 centre)
        {
            var radius = brushSize * .5f;
            var width = brushPaint ? data.alphamapWidth : data.heightmapResolution;
            var height = brushPaint ? data.alphamapHeight : data.heightmapResolution;
            var size = data.size;
            var dx = size.x / (width - 1);
            var dz = size.z / (height - 1);
            var left = Mathf.Clamp(Mathf.FloorToInt((centre.x - radius) / dx), 0, width - 1);
            var bottom = Mathf.Clamp(Mathf.FloorToInt((centre.z - radius) / dz), 0, height - 1);
            var right = Mathf.Clamp(Mathf.CeilToInt((centre.x + radius) / dx), 0, width - 1);
            var top = Mathf.Clamp(Mathf.CeilToInt((centre.z + radius) / dz), 0, height - 1);
            var area = new RectInt(left, bottom, right - left + 1, top - bottom + 1);

            if (brushPaint)
            {
                var weights = data.GetAlphamaps(area.x, area.y, area.width, area.height);
                if ((uint)paintLayer >= (uint)weights.GetLength(2)) return;
                for (var y = 0; y < area.height; y++)
                    for (var x = 0; x < area.width; x++)
                    {
                        var mix = Strength(x, y);
                        if (mix == 0f) continue;
                        for (var layer = 0; layer < weights.GetLength(2); layer++)
                            weights[y, x, layer] = Mathf.Lerp(weights[y, x, layer], layer == paintLayer ? 1f : 0f, mix);
                    }
                data.SetAlphamaps(area.x, area.y, weights);
                return;
            }

            var heights = data.GetHeights(area.x, area.y, area.width, area.height);
            // Smoothing reads the original dab so the result is independent of scan order.
            var original = sculptBrush == SculptBrush.Smooth ? (float[,])heights.Clone() : heights;
            var recipe = brushWater.Source.Recipe;
            for (var y = 0; y < area.height; y++)
                for (var x = 0; x < area.width; x++)
                {
                    var mix = Strength(x, y);
                    if (mix == 0f) continue;
                    var sourceX = Mathf.FloorToInt((float)(left + x) / (width - 1) * (recipe.Width - 1));
                    var sourceY = Mathf.FloorToInt((float)(bottom + y) / (height - 1) * (recipe.Height - 1));
                    if (!brushWater.AllowsSculpt(sourceX, sourceY, brushRivers)) continue;
                    var old = original[y, x];
                    var next = sculptBrush switch
                    {
                        SculptBrush.Raise => old + mix * 5f / size.y,
                        SculptBrush.Lower => old - mix * 5f / size.y,
                        SculptBrush.Flatten => Mathf.Lerp(old, flattenHeight, mix),
                        _ => Mathf.Lerp(old, (original[y, Mathf.Max(0, x - 1)] + original[y, Mathf.Min(area.width - 1, x + 1)]
                            + original[Mathf.Max(0, y - 1), x] + original[Mathf.Min(area.height - 1, y + 1), x]) * .25f, mix)
                    };
                    heights[y, x] = Mathf.Clamp(next, Mathf.Min(old, recipe.SeaLevel), 1f);
                }
            data.SetHeightsDelayLOD(area.x, area.y, heights);

            float Strength(int x, int y)
            {
                var offset = new Vector2((left + x) * dx - centre.x, (bottom + y) * dz - centre.z);
                return brushStrength * (1f - Mathf.SmoothStep(0f, 1f, offset.magnitude / radius));
            }
        }

        private static void MarkPaintDirty(TerrainData data)
        {
            EditorUtility.SetDirty(data);
            foreach (var texture in data.alphamapTextures) EditorUtility.SetDirty(texture);
        }
    }
}
