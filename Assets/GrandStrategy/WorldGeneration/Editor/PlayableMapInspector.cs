// Adds Unity Terrain authoring to the Playable Map Inspector. It creates editable scene terrain, stamps
// mountain ranges and prepares the paint layers read in Play Mode.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrandStrategy.WorldGeneration.Editor
{
    [CustomEditor(typeof(PlayableMap))]
    public sealed partial class PlayableMapInspector : UnityEditor.Editor
    {
        private const float DefaultSnowLine = .55f;
        private const string ScenePreviewName = "Terrain Scene Preview";
        private const string SeaMaterialName = "Scene Preview Sea";
        private const string RiverPreviewName = "River Channels";
        private const string ProvincePreviewName = "Province Outline Preview";
        private const string TerritoryPreviewName = "Territory Outline Preview";
        private const float RiverPreviewLift = .35f;
        private const float ProvincePreviewLift = .65f;
        private const float TerritoryPreviewLift = .85f;
        private static readonly GUIContent[] MeshDetailLabels =
        {
            new GUIContent("512 (comparison)"), new GUIContent("1024 (standard)"), new GUIContent("2048 (fine)")
        };
        private static readonly int[] MeshDetailValues = { 512, 1024, 2048 };
        private static readonly GUIContent[] HeightmapLabels =
        {
            new GUIContent("Match generated world"), new GUIContent("2049 × 2049")
        };
        private static readonly int[] HeightmapValues = { 0, 2049 };
        private int heightmapResolution;
        private float snowLine = DefaultSnowLine;
        private float stampLength = 190f;
        private float stampWidth = 85f;
        private enum StampShape { MountainRange, Valley }
        private StampShape stampShape;
        private float stampRise = 34f;
        private float stampDepth = 22f;
        private float stampAngle;
        private float stampFalloff = 2f;
        private Terrain stampTerrain;
        private WaterMap stampWater;
        private byte[] stampRivers;

        internal enum WorkspaceTab { Sculpt, Paint, Stamps, Preview }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Open World Editor"))
                TerrainWorldEditorWindow.Open((PlayableMap)target);
        }

        internal void DrawWorkspace(WorkspaceTab tab)
        {
            serializedObject.Update();
            var world = serializedObject.FindProperty("world").objectReferenceValue as WorldHeightmap;
            var provinceMap = serializedObject.FindProperty("provinceMap").objectReferenceValue as ProvinceMapAsset;
            var terrain = serializedObject.FindProperty("authoringTerrain");
            var source = serializedObject.FindProperty("authoringWorld");
            var assigned = terrain.objectReferenceValue as Terrain;

            var canEdit = world != null && assigned != null && assigned.terrainData != null
                && source.objectReferenceValue == world && !EditorApplication.isPlayingOrWillChangePlaymode;
            if (stampTerrain != null && (!canEdit || stampTerrain != assigned || tab != WorkspaceTab.Stamps))
                StopStamp();
            if (brushTerrain != null && (!canEdit || brushTerrain != assigned
                || (tab != WorkspaceTab.Sculpt && tab != WorkspaceTab.Paint) || brushPaint != (tab == WorkspaceTab.Paint)))
                StopBrush();

            if (world == null || assigned == null || assigned.terrainData == null || source.objectReferenceValue != world)
            {
                EditorGUILayout.HelpBox(world == null ? "Assign a World asset on the Playable Map first."
                    : assigned == null ? "Create a Scene Terrain from the current World asset to begin."
                    : "Create a Scene Terrain for the current World asset.", MessageType.Info);
                DrawCreateTerrain(world, assigned, terrain, source);
                return;
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                switch (tab)
                {
                    case WorkspaceTab.Sculpt:
                        DrawBrushControls(assigned, world, false);
                        break;
                    case WorkspaceTab.Paint:
                        DrawBrushControls(assigned, world, true);
                        DrawRepaint(assigned, world);
                        break;
                    case WorkspaceTab.Stamps:
                        DrawTerrainStamp(assigned, world);
                        break;
                    case WorkspaceTab.Preview:
                        DrawPreviewControls(assigned, world, provinceMap);
                        DrawCreateTerrain(world, assigned, terrain, source);
                        break;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRepaint(Terrain terrain, WorldHeightmap world)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Automatic Paint", EditorStyles.boldLabel);
            snowLine = EditorGUILayout.Slider("Snow Line", snowLine, .25f, .9f);
            var data = terrain.terrainData;
            using (new EditorGUI.DisabledScope(data.terrainLayers.Length != 4))
            {
                if (!GUILayout.Button("Repaint from Height and Slope") || !EditorUtility.DisplayDialog(
                    "Repaint Scene Terrain?", "This replaces the current paint, including brush strokes. Sculpted heights are kept.",
                    "Repaint", "Cancel")) return;
                var weights = BuildPaint(data, world.SeaLevel, snowLine);
                const string undoName = "Repaint Scene Terrain";
                Undo.RegisterCompleteObjectUndo(data, undoName);
                Undo.RegisterCompleteObjectUndo(data.alphamapTextures, undoName);
                data.SetAlphamaps(0, 0, weights);
                MarkPaintDirty(data);
                SceneView.RepaintAll();
            }
        }

        private void DrawPreviewControls(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinceMap)
        {
            DrawScenePreview(terrain, world);
            DrawRegionPreview(terrain, world, provinceMap, false);
            DrawRegionPreview(terrain, world, provinceMap, true);
            if (HasTerrainOverlays(terrain) && GUILayout.Button("Refit All Overlays to Terrain"))
            {
                RefitTerrainOverlays(terrain, "Refit Terrain Overlays");
                SceneView.RepaintAll();
            }
            DrawReferenceViews(terrain, world.SeaLevel);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("terrainComparison"),
                new GUIContent("Play Mode Terrain", "Compare generated terrain and your edits on the same mesh."));
            var meshDetail = serializedObject.FindProperty("authoredMeshDetail");
            meshDetail.intValue = EditorGUILayout.IntPopup(new GUIContent("Play Mode Mesh Detail",
                "Subdivisions along the longer side. Finer meshes take longer to build and use more memory."),
                meshDetail.intValue, MeshDetailLabels, MeshDetailValues);
            var samples = terrain.terrainData.heightmapResolution;
            var grid = HeightmapMesh.GridSize(world.Width, world.Height, meshDetail.intValue, samples);
            EditorGUILayout.LabelField("Playable Mesh", $"{grid.x:N0} × {grid.y:N0} vertices");
        }

        private void DrawCreateTerrain(WorldHeightmap world, Terrain assigned, SerializedProperty terrain, SerializedProperty source)
        {
            using (new EditorGUI.DisabledScope(world == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                heightmapResolution = EditorGUILayout.IntPopup(new GUIContent("New Terrain Heightmap",
                    "The fine heightmap gives brushes more samples to work with."),
                    heightmapResolution, HeightmapLabels, HeightmapValues);
                var label = assigned == null ? "Create Scene Terrain" : "Create Another Scene Terrain";
                if (!GUILayout.Button(label)) return;
                if (assigned != null && !EditorUtility.DisplayDialog("Replace Scene Terrain?",
                    "The current Terrain asset will be kept. A new scene Terrain will take its place.", "Create", "Cancel"))
                    return;
                try
                {
                    CreateTerrain(world, assigned, terrain, source);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, target);
                    EditorUtility.DisplayDialog("Could not create Scene Terrain", exception.Message, "OK");
                }
            }
        }

        // Arm a one-click Scene view tool while keeping stamp dimensions and angle in the Inspector.
        private void DrawTerrainStamp(Terrain terrain, WorldHeightmap world)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain Stamp", EditorStyles.boldLabel);
            stampShape = (StampShape)EditorGUILayout.EnumPopup("Shape", stampShape);
            stampLength = EditorGUILayout.Slider("Length", stampLength, 40f, 440f);
            stampWidth = EditorGUILayout.Slider("Width", stampWidth, 25f, 220f);
            if (stampShape == StampShape.Valley)
                stampDepth = EditorGUILayout.Slider("Depth", stampDepth, 5f, 90f);
            else
                stampRise = EditorGUILayout.Slider("Rise", stampRise, 5f, 90f);
            stampAngle = EditorGUILayout.Slider("Rotation", stampAngle, 0f, 180f);
            stampFalloff = EditorGUILayout.Slider("Falloff", stampFalloff, 1f, 4f);
            if (stampTerrain != null)
            {
                EditorGUILayout.HelpBox("Click an inland slope in the Scene view. Red marks protected water or banks. Escape cancels.", MessageType.Info);
                if (GUILayout.Button("Cancel Stamp")) StopStamp();
                SceneView.RepaintAll();
            }
            else if (GUILayout.Button(stampShape == StampShape.Valley ? "Place Valley" : "Place Mountain Range"))
            {
                var rivers = world.ReadRivers();
                stampWater = WaterMap.Classify(rivers?.Terrain ?? world.Read());
                stampRivers = rivers?.BuildSurfaceMask();
                stampTerrain = terrain;
                SceneView.duringSceneGui += DrawStampInScene;
                SceneView.lastActiveSceneView?.Focus();
                SceneView.RepaintAll();
            }
        }

        private void OnDisable()
        {
            StopStamp();
            StopBrush();
        }

        private void StopStamp()
        {
            SceneView.duringSceneGui -= DrawStampInScene;
            stampTerrain = null;
            stampWater = null;
            stampRivers = null;
            SceneView.RepaintAll();
        }

        // Raycast against the editable Terrain, draw the footprint on its surface and apply the stamp on a
        // left click.
        private void DrawStampInScene(SceneView scene)
        {
            var input = Event.current;
            if (stampTerrain == null || EditorApplication.isPlayingOrWillChangePlaymode
                || (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape))
            {
                if (input.type == EventType.KeyDown) input.Use();
                StopStamp();
                return;
            }

            if (input.type == EventType.Layout && !input.alt)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (input.type == EventType.MouseMove) scene.Repaint();

            var collider = stampTerrain.GetComponent<TerrainCollider>();
            var ray = HandleUtility.GUIPointToWorldRay(input.mousePosition);
            if (collider == null || !collider.Raycast(ray, out var hit, float.PositiveInfinity)) return;

            var radians = stampAngle * Mathf.Deg2Rad;
            var axis = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            if (input.type == EventType.Repaint)
            {
                var terrain = stampTerrain;
                var data = terrain.terrainData;
                var origin = terrain.transform.position;
                var points = new Vector3[49];
                for (var i = 0; i < points.Length; i++)
                {
                    var angle = i * Mathf.PI * 2f / (points.Length - 1);
                    var along = Mathf.Cos(angle) * stampLength * .5f;
                    var across = Mathf.Sin(angle) * stampWidth * .5f;
                    var x = hit.point.x + along * axis.x - across * axis.y;
                    var z = hit.point.z + along * axis.y + across * axis.x;
                    var y = data.GetInterpolatedHeight(Mathf.Clamp01((x - origin.x) / data.size.x),
                        Mathf.Clamp01((z - origin.z) / data.size.z)) + origin.y;
                    points[i] = new Vector3(x, y + 1f, z);
                }
                var colour = stampShape == StampShape.Valley ? new Color(.40f, .83f, 1f) : new Color(.95f, .7f, .28f);
                var editable = new List<Vector3>();
                var protectedEdges = new List<Vector3>();
                for (var i = 1; i < points.Length; i++)
                {
                    var middle = (points[i - 1] + points[i]) * .5f - origin;
                    var lines = CanStampAt(middle, data.size) ? editable : protectedEdges;
                    lines.Add(points[i - 1]);
                    lines.Add(points[i]);
                }
                var warning = new Color(1f, .27f, .22f);
                using (new Handles.DrawingScope(colour))
                    Handles.DrawLines(editable.ToArray());
                using (new Handles.DrawingScope(warning))
                    Handles.DrawLines(protectedEdges.ToArray());
                using (new Handles.DrawingScope(CanStampAt(hit.point - origin, data.size) ? colour : warning))
                    Handles.DotHandleCap(0, hit.point + Vector3.up,
                        Quaternion.identity, HandleUtility.GetHandleSize(hit.point) * .045f, EventType.Repaint);
            }
            if (input.type != EventType.MouseDown || input.button != 0 || input.alt) return;

            input.Use();
            try
            {
                if (!ApplyTerrainStamp(hit.point - stampTerrain.transform.position, axis))
                    scene.ShowNotification(new GUIContent("The stamp made no height changes. Try moving farther inland."));
            }
            finally
            {
                StopStamp();
                Repaint();
            }
        }

        private bool CanStampAt(Vector3 local, Vector3 size)
        {
            var u = local.x / size.x;
            var v = local.z / size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            var recipe = stampWater.Source.Recipe;
            return stampWater.AllowsSculpt(Mathf.FloorToInt(u * (recipe.Width - 1)),
                Mathf.FloorToInt(v * (recipe.Height - 1)), stampRivers);
        }

        // Change only the heightmap rectangle touched by the rotated stamp. The profile tapers at the edge
        // and fades out near sea level.
        private bool ApplyTerrainStamp(Vector3 centre, Vector2 axis)
        {
            var data = stampTerrain.terrainData;
            var step = data.heightmapScale;
            var last = data.heightmapResolution - 1;
            var radius = Mathf.Max(stampLength, stampWidth) * .5f;
            var left = Mathf.Clamp(Mathf.FloorToInt((centre.x - radius) / step.x), 0, last);
            var right = Mathf.Clamp(Mathf.CeilToInt((centre.x + radius) / step.x), 0, last);
            var bottom = Mathf.Clamp(Mathf.FloorToInt((centre.z - radius) / step.z), 0, last);
            var top = Mathf.Clamp(Mathf.CeilToInt((centre.z + radius) / step.z), 0, last);
            var heights = data.GetHeights(left, bottom, right - left + 1, top - bottom + 1);
            var source = stampWater.Source;
            var recipe = source.Recipe;
            var cut = stampShape == StampShape.Valley;
            var changed = false;
            var crossAxis = new Vector2(-axis.y, axis.x);

            for (var y = bottom; y <= top; y++)
                for (var x = left; x <= right; x++)
                {
                    var offset = new Vector2(x * step.x - centre.x, y * step.z - centre.z);
                    var along = Vector2.Dot(offset, axis) * 2f / stampLength;
                    var across = Vector2.Dot(offset, crossAxis) * 2f / stampWidth;
                    var footprint = along * along + across * across;
                    if (footprint >= 1f) continue;
                    var u = (float)x / last * (recipe.Width - 1);
                    var v = (float)y / last * (recipe.Height - 1);
                    var sourceX = Mathf.FloorToInt(u);
                    var sourceY = Mathf.FloorToInt(v);
                    if (!stampWater.AllowsSculpt(sourceX, sourceY, stampRivers)) continue;
                    var altitude = source.Elevations[Mathf.RoundToInt(v) * recipe.Width + Mathf.RoundToInt(u)];
                    var inland = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(recipe.SeaLevel + .015f, recipe.SeaLevel + .07f, altitude));
                    if (inland <= 0f) continue;

                    var envelope = Mathf.Pow(1f - footprint, stampFalloff);
                    var spine = across - .18f * Mathf.Sin(along * 6f);
                    var old = heights[y - bottom, x - left];
                    float height;
                    if (cut)
                    {
                        // A broad floor and tapered ends make the cut read as a pass from above.
                        var channel = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.13f, .68f, Mathf.Abs(spine)));
                        var depth = envelope * channel * inland * stampDepth / step.y;
                        height = Mathf.Max(Mathf.Min(old, recipe.SeaLevel + .015f), old - depth);
                    }
                    else
                    {
                        var ridge = Mathf.Exp(-4.5f * Mathf.Abs(spine));
                        var peaks = .52f + .48f * Mathf.PerlinNoise(along * 4.5f + 16f, 7.3f);
                        var gullies = Mathf.PerlinNoise(along * 13f + across * across * 4f + 22f, across * 2.5f + 9f);
                        var relief = envelope * peaks * (.36f + .64f * ridge) * (.64f + .36f * gullies);
                        height = Mathf.Min(1f, old + relief * inland * stampRise / step.y);
                    }
                    heights[y - bottom, x - left] = height;
                    changed |= height != old;
                }

            if (!changed) return false;
            var undoName = cut ? "Stamp Valley" : "Stamp Mountain Range";
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(data, undoName);
            data.SetHeights(left, bottom, heights);
            EditorUtility.SetDirty(data);

            // Include one height sample outside the edit because interpolated positions can also move there.
            var affected = Rect.MinMaxRect((left - 1) * step.x, (bottom - 1) * step.z,
                (right + 1) * step.x, (top + 1) * step.z);
            RefitTerrainOverlays(stampTerrain, undoName, affected);

            return true;
        }

        private static bool HasTerrainOverlays(Terrain terrain)
            => terrain.transform.Find(ScenePreviewName + "/" + RiverPreviewName) != null
                || terrain.transform.Find(ProvincePreviewName) != null
                || terrain.transform.Find(TerritoryPreviewName) != null;

        // A sculpt only changes elevation, so reuse the existing overlay vertices and connectivity.
        private static void RefitTerrainOverlays(Terrain terrain, string undoName, Rect? affected = null)
        {
            var data = terrain.terrainData;
            var size = data.size;
            Refit(ScenePreviewName + "/" + RiverPreviewName, RiverPreviewLift);
            Refit(ProvincePreviewName, ProvincePreviewLift);
            Refit(TerritoryPreviewName, TerritoryPreviewLift);

            void Refit(string path, float lift)
            {
                var layer = terrain.transform.Find(path);
                var mesh = layer == null ? null : layer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) return;

                var vertices = mesh.vertices;
                var changed = false;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var vertex = vertices[i];
                    if (affected.HasValue && !affected.Value.Contains(new Vector2(vertex.x, vertex.z))) continue;
                    var elevation = data.GetInterpolatedHeight(vertex.x / size.x, vertex.z / size.z) + lift;
                    if (Mathf.Approximately(vertex.y, elevation)) continue;
                    vertices[i].y = elevation;
                    changed = true;
                }
                if (!changed) return;

                Undo.RegisterCompleteObjectUndo(mesh, undoName);
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
            }
        }

        // Save the TerrainData and layers as assets, then link the new scene object to the playable map.
        private void CreateTerrain(WorldHeightmap world, Terrain previous, SerializedProperty terrain, SerializedProperty source)
        {
            var map = (PlayableMap)target;
            var scene = map.gameObject.scene;
            if (string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("Save the scene before creating its Terrain.");

            var generated = world.Read();
            var width = generated.Recipe.Width;
            var height = generated.Recipe.Height;
            var resolution = heightmapResolution > 0 ? heightmapResolution
                : Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height) - 1) + 1, 33, 2049);
            var scale = PlayableMap.TerrainAuthoringScale;
            var size = new Vector3((float)width / height, world.HeightScale, 1f) * scale;
            var data = new TerrainData { heightmapResolution = resolution, alphamapResolution = 1024, size = size };
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets", $"{scene.name} Terrain.asset").Replace('\\', '/'));
            GameObject terrainObject = null;
            try
            {
                data.SetHeights(0, 0, Resample(generated, resolution));
                AssetDatabase.CreateAsset(data, assetPath);
                var ground = Resources.Load<Material>("PlayableMapGround");
                if (ground != null)
                {
                    AddLayers(data, ground);
                    data.SetAlphamaps(0, 0, BuildPaint(data, world.SeaLevel, snowLine));
                }

                terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.name = "Map Terrain (Authoring)";
                if (terrainObject.scene != scene)
                    SceneManager.MoveGameObjectToScene(terrainObject, scene);
                terrainObject.transform.position = new Vector3(-size.x * .5f, -world.SeaLevel * size.y, -size.z * .5f);
                var native = terrainObject.GetComponent<Terrain>();
                native.heightmapPixelError = 2;
                native.drawInstanced = true;
                CreateScenePreview(native, world);
                AssetDatabase.SaveAssets();

                Undo.RegisterCreatedObjectUndo(terrainObject, "Create Scene Terrain");
                if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
                terrain.objectReferenceValue = native;
                source.objectReferenceValue = world;
                serializedObject.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeGameObject = terrainObject;
            }
            catch
            {
                if (terrainObject != null) DestroyImmediate(terrainObject);
                if (AssetDatabase.Contains(data)) AssetDatabase.DeleteAsset(assetPath);
                else DestroyImmediate(data);
                throw;
            }
        }

        private void DrawReferenceViews(Terrain terrain, float seaLevel)
        {
            var view = SceneView.lastActiveSceneView;
            var start = serializedObject.FindProperty("startAtReferenceView");
            var savedFocus = serializedObject.FindProperty("referenceViewFocus");
            using (new EditorGUI.DisabledScope(view == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Map Overview"))
                        FrameScene(view, terrain, seaLevel, 1f, Vector2.zero);
                    if (GUILayout.Button("Inspect Ridge"))
                        FrameScene(view, terrain, seaLevel, PlayableMap.ReferenceViewZoom, FocusFromScene(view, terrain));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save Ridge as Play Start"))
                    {
                        // Save the requested centre before clamping, since Scene and Game view aspect ratios can differ.
                        savedFocus.vector2Value = FocusFromScene(view, terrain);
                        start.boolValue = true;
                        serializedObject.ApplyModifiedProperties();
                        FrameScene(view, terrain, seaLevel, PlayableMap.ReferenceViewZoom, savedFocus.vector2Value);
                    }
                    if (start.boolValue && GUILayout.Button("View Saved Ridge"))
                        FrameScene(view, terrain, seaLevel, PlayableMap.ReferenceViewZoom, savedFocus.vector2Value);
                }
            }

            if (start.boolValue && GUILayout.Button("Restore Overview on Play"))
            {
                start.boolValue = false;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private static Vector2 FocusFromScene(SceneView view, Terrain terrain)
        {
            var local = view.pivot - terrain.transform.position - terrain.terrainData.size * .5f;
            return new Vector2(local.x, local.z) / PlayableMap.TerrainAuthoringScale;
        }

        private static void FrameScene(SceneView view, Terrain terrain, float seaLevel, float zoom, Vector2 focus)
        {
            view.in2DMode = false;
            var size = terrain.terrainData.size;
            var aspect = Mathf.Max(.001f, view.camera.aspect);
            var frame = PlayableMap.CameraFrame(size.x / size.z, aspect, zoom, focus);
            var scale = PlayableMap.TerrainAuthoringScale;
            var point = terrain.transform.position + new Vector3(size.x * .5f + frame.Focus.x * scale,
                seaLevel * size.y, size.z * .5f + frame.Focus.y * scale);
            // SceneView.size uses the shorter viewport axis. CameraFrame returns the vertical half-size.
            view.LookAt(point, frame.Rotation, frame.HalfHeight * scale * Mathf.Min(1f, aspect), true, true);
            view.Repaint();
        }

        private static void DrawRegionPreview(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinces,
            bool territories)
        {
            if (world == null || provinces == null || terrain.terrainData == null) return;
            var label = territories ? "Territory Outlines" : "Province Outlines";
            var previewName = territories ? TerritoryPreviewName : ProvincePreviewName;
            var preview = terrain.transform.Find(previewName);
            var enabled = preview != null && preview.gameObject.activeSelf;
            var available = provinces.Map != null && (!territories || provinces.Map.HasTerritoryPreview);
            if (!available)
            {
                // An existing overlay can still be hidden if its source asset no longer has this layer.
                if (!enabled) return;
                EditorGUILayout.HelpBox($"The current map has no source data for {label}.", MessageType.Warning);
            }
            var show = EditorGUILayout.Toggle(new GUIContent(label,
                "Trace imported boundaries touching land while you sculpt."), enabled);
            var refresh = available && enabled && show && GUILayout.Button("Refresh " + label);
            if (show == enabled && !refresh) return;

            try
            {
                if (show)
                {
                    provinces.ValidateTerrain(world.Read());
                    RebuildRegionPreview(terrain, provinces.Map, preview, previewName, label, territories);
                }
                if (preview != null && show != enabled)
                {
                    Undo.RecordObject(preview.gameObject, "Toggle " + label);
                    preview.gameObject.SetActive(show);
                }
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                AssetDatabase.SaveAssets();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not update " + label, exception.Message, "OK");
            }
        }

        // Follow the imported raster on the editable Terrain. Subdivide each edge to avoid crossing
        // through terrain samples when the editing heightmap is finer than the province grid.
        private static void RebuildRegionPreview(Terrain terrain, ProvinceMap map, Transform preview,
            string previewName, string label, bool territories)
        {
            var data = terrain.terrainData;
            var path = AssetDatabase.GetAssetPath(data);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (string.IsNullOrEmpty(path) || shader == null)
                throw new InvalidOperationException(label + " need saved TerrainData and the URP Unlit shader.");

            var width = map.Width;
            var height = map.Height;
            var samples = data.heightmapResolution - 1;
            var across = Mathf.CeilToInt((float)samples / width);
            var along = Mathf.CeilToInt((float)samples / height);
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var raster = territories ? map.TerritoryAt : map.ProvinceAt;
            var land = territories
                ? Array.ConvertAll(map.Territories, territory => IsLand(territory?.Type))
                : Array.ConvertAll(map.Provinces, province => IsLand(province?.Type));
            var lift = territories ? TerritoryPreviewLift : ProvincePreviewLift;

            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var id = raster[index];
                    if (x + 1 < width && DrawEdge(id, raster[index + 1]))
                        AddEdge(x + 1, y, x + 1, y + 1, along);
                    if (y + 1 < height && DrawEdge(id, raster[index + width]))
                        AddEdge(x, y + 1, x + 1, y + 1, across);
                }

            if (preview == null)
            {
                Material material = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Material candidate && candidate.name == previewName)
                    {
                        material = candidate;
                        break;
                    }
                if (material == null)
                {
                    material = new Material(shader) { name = previewName };
                    material.SetColor("_BaseColor", territories ? new Color(.32f, .84f, 1f) : new Color(.99f, .72f, .20f));
                    AssetDatabase.AddObjectToAsset(material, data);
                }

                var child = new GameObject(previewName, typeof(MeshFilter), typeof(MeshRenderer)) { tag = "EditorOnly" };
                preview = child.transform;
                preview.SetParent(terrain.transform, false);
                var renderer = child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Undo.RegisterCreatedObjectUndo(child, "Add " + label);
            }

            var filter = preview.GetComponent<MeshFilter>();
            var mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = previewName };
                AssetDatabase.AddObjectToAsset(mesh, data);
                Undo.RecordObject(filter, "Assign " + label);
                filter.sharedMesh = mesh;
            }
            else
                Undo.RegisterCompleteObjectUndo(mesh, "Refresh " + label);

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            EditorUtility.SetDirty(mesh);

            static bool IsLand(string type) => string.Equals(type, "land", StringComparison.OrdinalIgnoreCase);

            bool DrawEdge(int a, int b) => a != b
                && (((uint)a < (uint)land.Length && land[a]) || ((uint)b < (uint)land.Length && land[b]));

            void AddEdge(int x0, int y0, int x1, int y1, int divisions)
            {
                var size = data.size;
                var first = vertices.Count;
                for (var i = 0; i <= divisions; i++)
                {
                    var t = (float)i / divisions;
                    var u = Mathf.Lerp(x0, x1, t) / width;
                    var v = Mathf.Lerp(y0, y1, t) / height;
                    vertices.Add(new Vector3(u * size.x, data.GetInterpolatedHeight(u, v) + lift, v * size.z));
                    if (i == 0) continue;
                    indices.Add(first + i - 1);
                    indices.Add(first + i);
                }
            }
        }

        private static void DrawScenePreview(Terrain terrain, WorldHeightmap world)
        {
            var preview = terrain.transform.Find(ScenePreviewName);
            var enabled = preview != null && preview.gameObject.activeSelf;
            var next = EditorGUILayout.Toggle(new GUIContent("Water and Sun Preview",
                "Show sea level, river channels and directional light while editing the Terrain."), enabled);
            if (enabled && next && world != null && world.HasRivers && GUILayout.Button("Refresh River Preview"))
            {
                UpdateRiverPreview(terrain, world, preview, true);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                SceneView.RepaintAll();
            }
            if (next == enabled) return;

            if (preview == null)
            {
                var created = CreateScenePreview(terrain, world);
                if (created == null) return;
                Undo.RegisterCreatedObjectUndo(created, "Create Terrain Scene Preview");
                AssetDatabase.SaveAssets();
            }
            else
            {
                Undo.RecordObject(preview.gameObject, "Toggle Terrain Scene Preview");
                preview.gameObject.SetActive(next);
            }

            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            SceneView.RepaintAll();
        }

        // Keep the preview under the Terrain so it stays with the editing canvas and disappears
        // when Play Mode takes over. The EditorOnly tag also keeps the preview out of builds.
        private static GameObject CreateScenePreview(Terrain terrain, WorldHeightmap world)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("Could not find the URP Unlit shader for the sea-level preview.", terrain);
                return null;
            }

            var data = terrain.terrainData;
            var path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Save the TerrainData as an asset before creating its preview.", terrain);
                return null;
            }

            Material sea = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Material material && material.name == SeaMaterialName)
                {
                    sea = material;
                    break;
                }
            if (sea == null)
            {
                sea = new Material(shader) { name = SeaMaterialName };
                sea.SetColor("_BaseColor", new Color(.10f, .26f, .33f, 1f));
                AssetDatabase.AddObjectToAsset(sea, data);
            }

            var root = new GameObject(ScenePreviewName) { tag = "EditorOnly" };
            root.transform.SetParent(terrain.transform, false);
            try
            {
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                water.name = "Sea Level";
                water.transform.SetParent(root.transform, false);
                var size = data.size;
                // The built-in plane spans ten units. Place it slightly below sea level to avoid z-fighting.
                water.transform.localPosition = new Vector3(size.x * .5f, world.SeaLevel * size.y - .1f, size.z * .5f);
                water.transform.localScale = new Vector3(size.x * .1f, 1f, size.z * .1f);
                DestroyImmediate(water.GetComponent<Collider>());
                var renderer = water.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = sea;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var sun = new GameObject("Preview Sun", typeof(Light));
                sun.transform.SetParent(root.transform, false);
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -1.075f, -1f));
                var light = sun.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                light.shadows = LightShadows.Soft;
                UpdateRiverPreview(terrain, world, root.transform, false);
                return root;
            }
            catch
            {
                DestroyImmediate(root);
                throw;
            }
        }

        // The ribbon uses the world generator's widened river mask, so the scene shows the same
        // route as Play Mode. Its height follows the editable Terrain and can be refreshed after sculpting.
        private static void UpdateRiverPreview(Terrain terrain, WorldHeightmap world, Transform preview, bool recordUndo)
        {
            var rivers = world.ReadRivers();
            if (rivers == null) return;

            var data = terrain.terrainData;
            var water = WaterMap.Classify(rivers.Terrain);
            var mask = rivers.BuildSurfaceMask();
            var width = rivers.Terrain.Recipe.Width;
            var height = rivers.Terrain.Recipe.Height;
            var stride = width + 1;
            var size = data.size;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var corners = new Dictionary<int, int>();
            for (var cell = 0; cell < mask.Length; cell++)
            {
                if (mask[cell] == 0 || water[cell] != WaterKind.Land) continue;
                var x = cell % width;
                var y = cell / width;
                var southWest = Corner(x, y);
                var southEast = Corner(x + 1, y);
                var northWest = Corner(x, y + 1);
                var northEast = Corner(x + 1, y + 1);
                indices.Add(southWest);
                indices.Add(northWest);
                indices.Add(southEast);
                indices.Add(southEast);
                indices.Add(northWest);
                indices.Add(northEast);
            }

            var riverObject = preview.Find(RiverPreviewName);
            if (riverObject == null)
            {
                var child = new GameObject(RiverPreviewName, typeof(MeshFilter), typeof(MeshRenderer));
                riverObject = child.transform;
                riverObject.SetParent(preview, false);
                var renderer = child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = preview.Find("Sea Level").GetComponent<MeshRenderer>().sharedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (recordUndo) Undo.RegisterCreatedObjectUndo(child, "Add River Preview");
            }

            var filter = riverObject.GetComponent<MeshFilter>();
            var mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = "Scene Preview Rivers" };
                AssetDatabase.AddObjectToAsset(mesh, data);
                if (recordUndo) Undo.RecordObject(filter, "Assign River Preview");
                filter.sharedMesh = mesh;
            }
            else if (recordUndo)
                Undo.RegisterCompleteObjectUndo(mesh, "Refresh River Preview");

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            EditorUtility.SetDirty(mesh);

            int Corner(int x, int y)
            {
                var key = y * stride + x;
                if (corners.TryGetValue(key, out var index)) return index;
                var u = (float)x / width;
                var v = (float)y / height;
                var elevation = data.GetInterpolatedHeight(u, v);
                index = vertices.Count;
                // Lift the unlit overlay slightly so it remains visible over the terrain surface.
                vertices.Add(new Vector3(u * size.x, elevation + RiverPreviewLift, v * size.z));
                corners.Add(key, index);
                return index;
            }
        }

        // Layer order matches the runtime shader. The base leaves procedural ground showing, followed by
        // rock, snow and sparse grass.
        private static void AddLayers(TerrainData data, Material ground)
        {
            var layers = new[]
            {
                CreateLayer("Base (generated in Play Mode)", "_Grass", 16f),
                CreateLayer("Mountain Rock", "_Alpine", 24f),
                CreateLayer("Snow", "_Snow", 22f),
                CreateLayer("Sparse Grass", "_SparseGrass", 16f)
            };
            data.terrainLayers = layers;

            TerrainLayer CreateLayer(string label, string property, float tileSize)
            {
                var layer = new TerrainLayer
                {
                    name = label,
                    diffuseTexture = ground.GetTexture(property + "Colour") as Texture2D,
                    normalMapTexture = ground.GetTexture(property + "Normal") as Texture2D,
                    tileSize = new Vector2(tileSize, tileSize)
                };
                AssetDatabase.AddObjectToAsset(layer, data);
                return layer;
            }
        }

        // Measure slope in scene units and allocate the four paint weights from slope, altitude and local
        // variation.
        private static float[,,] BuildPaint(TerrainData data, float seaLevel, float snowLine)
        {
            var resolution = data.alphamapResolution;
            var weights = new float[resolution, resolution, 4];
            var lastHeight = data.heightmapResolution - 1;
            var height = data.GetHeights(0, 0, lastHeight + 1, lastHeight + 1);
            var step = data.heightmapScale;

            for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var u = (float)x / (resolution - 1);
                    var v = (float)y / (resolution - 1);
                    var hx = Mathf.RoundToInt(u * lastHeight);
                    var hy = Mathf.RoundToInt(v * lastHeight);
                    var left = Mathf.Max(0, hx - 1);
                    var right = Mathf.Min(lastHeight, hx + 1);
                    var lower = Mathf.Max(0, hy - 1);
                    var upper = Mathf.Min(lastHeight, hy + 1);
                    var altitude = (height[hy, hx] - seaLevel) / Mathf.Max(.0001f, 1f - seaLevel);
                    if (altitude <= 0f)
                    {
                        weights[y, x, 0] = 1f;
                        continue;
                    }

                    var eastSlope = (height[hy, right] - height[hy, left]) * step.y / ((right - left) * step.x);
                    var northSlope = (height[upper, hx] - height[lower, hx]) * step.y / ((upper - lower) * step.z);
                    var slope = Mathf.Atan(Mathf.Sqrt(eastSlope * eastSlope + northSlope * northSlope)) * Mathf.Rad2Deg;
                    var variation = Mathf.PerlinNoise(u * 46f + 12f, v * 46f + 31f) - .5f;
                    var rock = Fade(slope + 14f * variation, 21f, 43f) * Fade(altitude, .025f, .13f);
                    var snow = Fade(altitude + .15f * variation, snowLine - .06f, snowLine + .07f)
                        * (1f - Fade(slope, 24f, 51f));
                    var grass = Fade(slope + 9f * variation, 9f, 29f) * Fade(altitude, .05f, .25f) * .64f;
                    var bare = 1f - snow;
                    weights[y, x, 0] = bare * (1f - rock) * (1f - grass);
                    weights[y, x, 1] = bare * rock;
                    weights[y, x, 2] = snow;
                    weights[y, x, 3] = bare * (1f - rock) * grass;
                }

            return weights;

            static float Fade(float value, float start, float end)
                => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
        }

        // Convert the rectangular source grid to Unity Terrain's square height sample grid without changing
        // the map's physical aspect ratio.
        private static float[,] Resample(HeightmapResult source, int resolution)
        {
            var width = source.Recipe.Width;
            var height = source.Recipe.Height;
            var heights = new float[resolution, resolution];
            for (var y = 0; y < resolution; y++)
            {
                var sy = (float)y / (resolution - 1) * (height - 1);
                var south = Mathf.FloorToInt(sy);
                var north = Mathf.Min(south + 1, height - 1);
                for (var x = 0; x < resolution; x++)
                {
                    var sx = (float)x / (resolution - 1) * (width - 1);
                    var west = Mathf.FloorToInt(sx);
                    var east = Mathf.Min(west + 1, width - 1);
                    var lower = Mathf.Lerp(source.Elevations[south * width + west],
                        source.Elevations[south * width + east], sx - west);
                    var upper = Mathf.Lerp(source.Elevations[north * width + west],
                        source.Elevations[north * width + east], sx - west);
                    heights[y, x] = Mathf.Lerp(lower, upper, sy - south);
                }
            }
            return heights;
        }
    }
}
