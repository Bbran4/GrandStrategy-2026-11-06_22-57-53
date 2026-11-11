// Stages river edits before replacing the editable map with a reviewed revision.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed partial class PlayableMapInspector
    {
        private int authoredRiverWidth = 3;
        private float authoredRiverDepth = .012f;
        private Terrain geographyTerrain;
        private WorldHeightmap geographyWorld;
        private ProvinceMapAsset geographyProvinces;
        private HeightmapResult geographyOriginal;
        private RiverMap geographyRivers;
        private bool geographyCapturing;
        private readonly List<int> geographyWaypoints = new List<int>();
        private GeographyRevision geographyReview;
        private string geographyStatus;

        private void DrawGeography(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinces)
        {
            if (provinces == null)
            {
                StopGeographyCapture();
                EditorGUILayout.HelpBox("Assign a matching Province Map before editing geography.", MessageType.Info);
                return;
            }
            if (terrain != geographyTerrain || world != geographyWorld || provinces != geographyProvinces)
            {
                ClearGeographyDraft();
                geographyTerrain = terrain;
                geographyWorld = world;
                geographyProvinces = provinces;
            }

            EditorGUILayout.HelpBox("Draw a route from an inland source to an ocean or lake. Review it, then apply to carve the channel and update the river network.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            authoredRiverWidth = EditorGUILayout.IntSlider("River Width (samples)", authoredRiverWidth, 1, 24);
            authoredRiverDepth = EditorGUILayout.Slider("River Depth", authoredRiverDepth,
                RiverGenerator.MinimumCarveDepth, RiverGenerator.MaximumCarveDepth);
            if (EditorGUI.EndChangeCheck()) geographyReview = null;

            if (GUILayout.Button(geographyCapturing ? "Release River Tool" : "Draw River Waypoints"))
            {
                if (geographyCapturing) StopGeographyCapture();
                else RunGeographyAction(BeginGeographyCapture);
            }
            if (geographyCapturing)
                EditorGUILayout.HelpBox("Left-click in the Scene view to stage an edit. Review and apply to update the terrain.", MessageType.Info);
            EditorGUILayout.LabelField($"River waypoints: {geographyWaypoints.Count}");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(geographyWaypoints.Count == 0))
                    if (GUILayout.Button("Remove Last Waypoint"))
                    {
                        geographyWaypoints.RemoveAt(geographyWaypoints.Count - 1);
                        geographyReview = null;
                        SceneView.RepaintAll();
                    }
                if (GUILayout.Button("Discard Draft")) ClearGeographyDraft();
            }

            using (new EditorGUI.DisabledScope(geographyWaypoints.Count < 2))
                if (GUILayout.Button("Review River Cut"))
                    RunGeographyAction(ReviewGeography);

            if (geographyReview != null)
            {
                var review = geographyReview;
                EditorGUILayout.LabelField("Changed Water or River Samples", review.ChangedWaterCells.ToString("N0"));
                EditorGUILayout.LabelField("Sea-Connected Channel Samples", review.NavalChannelCells.ToString("N0"));
                EditorGUILayout.LabelField("Provinces to Review", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Names(review.AffectedProvinces), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Territories to Review", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Names(review.AffectedTerritories), EditorStyles.wordWrappedLabel);
                if (review.UnassignedProvinceCells > 0 || review.UnassignedTerritoryCells > 0)
                    EditorGUILayout.HelpBox($"{review.UnassignedProvinceCells:N0} province samples and {review.UnassignedTerritoryCells:N0} territory samples have no adjoining compatible region. Review these areas before publishing.", MessageType.Warning);
                if (GUILayout.Button("Apply River Cut"))
                    RunGeographyAction(() => ApplyGeography(review));
            }
            if (!string.IsNullOrEmpty(geographyStatus))
                EditorGUILayout.HelpBox(geographyStatus, MessageType.Info);

            static string Names(string[] names) => names.Length == 0 ? "None" : string.Join(", ", names);
        }

        private void RunGeographyAction(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                geographyStatus = exception.Message;
                Debug.LogException(exception, target);
            }
        }

        private void BeginGeographyCapture()
        {
            if (!PrepareSceneTool(geographyTerrain)) return;
            if (geographyOriginal == null)
            {
                geographyRivers = geographyWorld.ReadRivers();
                geographyOriginal = geographyRivers?.Terrain ?? geographyWorld.Read();
                geographyProvinces.ValidateTerrain(geographyOriginal);
            }
            geographyCapturing = true;
            SceneView.duringSceneGui += DrawGeographyInScene;
            SceneView.lastActiveSceneView?.Focus();
            SceneView.RepaintAll();
        }

        private void StopGeographyCapture()
        {
            SceneView.duringSceneGui -= DrawGeographyInScene;
            geographyCapturing = false;
            SceneView.RepaintAll();
        }

        private void ClearGeographyDraft()
        {
            StopGeographyCapture();
            geographyOriginal = null;
            geographyRivers = null;
            geographyWaypoints.Clear();
            geographyReview = null;
            geographyStatus = null;
        }

        private void DrawGeographyInScene(SceneView view)
        {
            var input = Event.current;
            if (geographyTerrain == null || geographyTerrain.terrainData == null || geographyOriginal == null
                || EditorApplication.isPlayingOrWillChangePlaymode
                || (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape))
            {
                if (input.type == EventType.KeyDown) input.Use();
                StopGeographyCapture();
                return;
            }

            var control = GUIUtility.GetControlID(FocusType.Passive);
            if (input.type == EventType.Layout && !input.alt) HandleUtility.AddDefaultControl(control);
            if (input.type == EventType.MouseMove) view.Repaint();
            var terrain = geographyTerrain;
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            var size = data.size;
            var recipe = geographyOriginal.Recipe;
            var collider = terrain.GetComponent<TerrainCollider>();
            RaycastHit hit = default;
            var hasHit = collider != null && collider.Raycast(HandleUtility.GUIPointToWorldRay(input.mousePosition),
                out hit, float.PositiveInfinity);

            if (input.type == EventType.Repaint)
            {
                if (geographyWaypoints.Count > 0)
                {
                    var points = new Vector3[geographyWaypoints.Count + (hasHit ? 1 : 0)];
                    for (var i = 0; i < geographyWaypoints.Count; i++) points[i] = Position(geographyWaypoints[i]);
                    if (points.Length > geographyWaypoints.Count) points[points.Length - 1] = hit.point + Vector3.up * 2f;
                    using (new Handles.DrawingScope(new Color(.40f, .83f, 1f)))
                    {
                        if (points.Length > 1) Handles.DrawAAPolyLine(3f, points);
                        foreach (var point in points)
                            Handles.DotHandleCap(0, point, Quaternion.identity, HandleUtility.GetHandleSize(point) * .035f, EventType.Repaint);
                    }
                }
                return;
            }

            if (!hasHit || input.alt || input.type != EventType.MouseDown || input.button != 0
                || HandleUtility.nearestControl != control) return;
            var local = hit.point - origin;
            var x = Mathf.Clamp((int)(local.x / size.x * recipe.Width), 0, recipe.Width - 1);
            var y = Mathf.Clamp((int)(local.z / size.z * recipe.Height), 0, recipe.Height - 1);
            var cell = y * recipe.Width + x;
            if (geographyWaypoints.Count == 0 || geographyWaypoints[geographyWaypoints.Count - 1] != cell)
                geographyWaypoints.Add(cell);
            geographyReview = null;
            input.Use();
            view.Repaint();
            Repaint();

            Vector3 Position(int index)
            {
                var u = (index % recipe.Width + .5f) / recipe.Width;
                var v = (index / recipe.Width + .5f) / recipe.Height;
                var altitude = Mathf.Max(data.GetInterpolatedHeight(u, v), recipe.SeaLevel * size.y);
                return origin + new Vector3(u * size.x, altitude + 2f, v * size.z);
            }
        }

        private void ReviewGeography()
        {
            StopGeographyCapture();
            geographyReview = null;
            geographyReview = GeographyAuthoring.Review(geographyOriginal, (geographyRivers?.Base ?? geographyOriginal).Elevations, geographyRivers,
                geographyWaypoints, (byte)authoredRiverWidth, authoredRiverDepth, geographyProvinces.Map,
                geographyWorld.ReadClimateSettings(), geographyWorld.HasBiomes);
            geographyStatus = "Review complete. Apply to create new editable assets.";
        }

        private void ApplyGeography(GeographyRevision review)
        {
            StopGeographyCapture();
            var map = (PlayableMap)target;
            var scene = map.gameObject.scene;
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the scene before applying geography.");
            var directory = Path.GetDirectoryName(scene.path)?.Replace('\\', '/') ?? "Assets";
            var worldPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{scene.name} Geography.asset");
            var terrainPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{scene.name} Geography Terrain.asset");
            var originalData = geographyTerrain.terrainData;
            var originalPath = AssetDatabase.GetAssetPath(originalData);
            if (string.IsNullOrEmpty(originalPath)) throw new InvalidOperationException("Save the editable TerrainData before applying geography.");
            var revisedWorld = CreateInstance<WorldHeightmap>();
            var revisedProvinces = CreateInstance<ProvinceMapAsset>();
            GameObject revisedObject = null;
            Terrain revisedTerrain;
            try
            {
                revisedWorld.Initialize(review.Terrain, geographyWorld.HeightScale, review.Rivers, review.Climate, review.Biomes);
                revisedProvinces.name = "Province Map";
                revisedProvinces.Initialize(review.Provinces, review.Terrain, revisedWorld.HeightScale);
                AssetDatabase.CreateAsset(revisedWorld, worldPath);
                AssetDatabase.AddObjectToAsset(revisedProvinces, revisedWorld);

                // Duplicate the editable asset, including its layers and stored paint baseline.
                AssetDatabase.SaveAssetIfDirty(originalData);
                if (!AssetDatabase.CopyAsset(originalPath, terrainPath))
                    throw new InvalidOperationException("Could not copy the editable TerrainData.");
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
                RebaseSculpt(data, review);
                EditorUtility.SetDirty(data);

                revisedObject = Terrain.CreateTerrainGameObject(data);
                revisedObject.name = "Map Terrain (Authoring)";
                if (revisedObject.scene != scene) SceneManager.MoveGameObjectToScene(revisedObject, scene);
                revisedObject.transform.position = new Vector3(-data.size.x * .5f, -revisedWorld.SeaLevel * data.size.y, -data.size.z * .5f);
                revisedTerrain = revisedObject.GetComponent<Terrain>();
                revisedTerrain.heightmapPixelError = geographyTerrain.heightmapPixelError;
                revisedTerrain.drawInstanced = geographyTerrain.drawInstanced;
                CreateScenePreview(revisedTerrain, revisedWorld);
                AssetDatabase.SaveAssetIfDirty(revisedWorld);
                AssetDatabase.SaveAssetIfDirty(data);
            }
            catch
            {
                if (revisedObject != null) DestroyImmediate(revisedObject);
                if (AssetDatabase.LoadMainAssetAtPath(terrainPath) != null) AssetDatabase.DeleteAsset(terrainPath);
                if (AssetDatabase.GetAssetPath(revisedWorld) == worldPath) AssetDatabase.DeleteAsset(worldPath);
                if (revisedWorld != null && !AssetDatabase.Contains(revisedWorld)) DestroyImmediate(revisedWorld);
                if (revisedProvinces != null && !AssetDatabase.Contains(revisedProvinces)) DestroyImmediate(revisedProvinces);
                throw;
            }

            Undo.SetCurrentGroupName("Apply Editable Geography");
            Undo.RegisterCreatedObjectUndo(revisedObject, "Apply Editable Geography");
            serializedObject.FindProperty("world").objectReferenceValue = revisedWorld;
            serializedObject.FindProperty("provinceMap").objectReferenceValue = revisedProvinces;
            serializedObject.FindProperty("authoringWorld").objectReferenceValue = revisedWorld;
            serializedObject.FindProperty("authoringTerrain").objectReferenceValue = revisedTerrain;
            serializedObject.ApplyModifiedProperties();
            Undo.DestroyObjectImmediate(geographyTerrain.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);

            ClearGeographyDraft();
            geographyStatus = $"Created {worldPath}. Save the scene, then preview and publish when ready.";
            geographyTerrain = revisedTerrain;
            geographyWorld = revisedWorld;
            geographyProvinces = revisedProvinces;
        }

        private void RebaseSculpt(TerrainData data, GeographyRevision review)
        {
            var oldWater = WaterMap.Classify(geographyOriginal);
            var newWater = WaterMap.Classify(review.Terrain);
            var oldMask = geographyRivers?.BuildSurfaceMask();
            var newMask = review.Rivers?.BuildSurfaceMask();
            var resolution = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, resolution, resolution);
            var recipe = geographyOriginal.Recipe;
            for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var u = (float)x / (resolution - 1);
                    var v = (float)y / (resolution - 1);
                    var baseX = Mathf.FloorToInt(u * (recipe.Width - 1));
                    var baseY = Mathf.FloorToInt(v * (recipe.Height - 1));
                    var before = SampleGeneratedHeight(geographyOriginal, u, v);
                    var after = SampleGeneratedHeight(review.Terrain, u, v);
                    var editableBefore = oldWater.AllowsSculpt(baseX, baseY, oldMask);
                    var editableAfter = newWater.AllowsSculpt(baseX, baseY, newMask);
                    if (before == after && editableBefore == editableAfter) continue;
                    heights[y, x] = editableBefore && editableAfter
                        ? Mathf.Clamp(heights[y, x] + after - before, recipe.SeaLevel, 1f) : after;
                }
            data.SetHeights(0, 0, heights);
        }
    }
}
