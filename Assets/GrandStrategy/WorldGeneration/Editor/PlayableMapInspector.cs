using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrandStrategy.WorldGeneration.Editor
{
    [CustomEditor(typeof(PlayableMap))]
    public sealed class PlayableMapInspector : UnityEditor.Editor
    {
        private const float DefaultSnowLine = .55f;
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
        private float stampRise = 34f;
        private float stampAngle;
        private Terrain stampTerrain;
        private HeightmapResult stampSource;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();
            var world = serializedObject.FindProperty("world").objectReferenceValue as WorldHeightmap;
            var terrain = serializedObject.FindProperty("authoringTerrain");
            var source = serializedObject.FindProperty("authoringWorld");
            var meshDetail = serializedObject.FindProperty("authoredMeshDetail");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scene Terrain", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Sculpt and paint the scene Terrain in Edit Mode, then save the scene. Play Mode reads its heights and paint. " +
                "Keep the first Terrain Layer as the base. The next three layers blend over the generated ground. " +
                "The generated coastline and riverbanks are protected from height edits.", MessageType.Info);
            snowLine = EditorGUILayout.Slider(new GUIContent("Snow Line",
                "Fraction of the elevation range above sea level. Steep faces retain less snow."),
                snowLine, .25f, .9f);

            var assigned = terrain.objectReferenceValue as Terrain;
            if (assigned != null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Authoring Terrain", assigned, typeof(Terrain), true);
                if (GUILayout.Button("Select Terrain for Sculpting and Painting"))
                    Selection.activeGameObject = assigned.gameObject;

                if (assigned.terrainData != null && world != null)
                {
                    meshDetail.intValue = EditorGUILayout.IntPopup(new GUIContent("Play Mode Mesh Detail",
                        "Subdivisions along the longer side. Finer meshes take longer to build and use more memory."),
                        meshDetail.intValue, MeshDetailLabels, MeshDetailValues);
                    var samples = assigned.terrainData.heightmapResolution;
                    var grid = HeightmapMesh.GridSize(world.Width, world.Height, meshDetail.intValue, samples);
                    EditorGUILayout.LabelField("Playable Mesh", $"{grid.x:N0} × {grid.y:N0} vertices");
                    if (meshDetail.intValue >= samples)
                        EditorGUILayout.HelpBox($"The scene Terrain's {samples:N0} height samples limit usable detail.",
                            MessageType.Info);
                    serializedObject.ApplyModifiedProperties();
                }

                if (source.objectReferenceValue != world)
                    EditorGUILayout.HelpBox("This Terrain was created for a different World asset.", MessageType.Warning);

                using (new EditorGUI.DisabledScope(world == null || source.objectReferenceValue != world
                    || assigned.terrainData == null || EditorApplication.isPlayingOrWillChangePlaymode))
                    DrawMountainStamp(assigned, world);

                using (new EditorGUI.DisabledScope(world == null || source.objectReferenceValue != world
                    || assigned.terrainData == null || assigned.terrainData.terrainLayers.Length != 4
                    || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Repaint from Height and Slope") && EditorUtility.DisplayDialog(
                        "Repaint Scene Terrain?", "This replaces the current paint, including brush strokes. Sculpted heights are kept.",
                        "Repaint", "Cancel"))
                    {
                        var data = assigned.terrainData;
                        var weights = BuildPaint(data, world.SeaLevel, snowLine);
                        const string undoName = "Repaint Scene Terrain";
                        Undo.RegisterCompleteObjectUndo(data, undoName);
                        Undo.RegisterCompleteObjectUndo(data.alphamapTextures, undoName);
                        data.SetAlphamaps(0, 0, weights);
                        EditorUtility.SetDirty(data);
                        foreach (var texture in data.alphamapTextures) EditorUtility.SetDirty(texture);
                        SceneView.RepaintAll();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(world == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                heightmapResolution = EditorGUILayout.IntPopup(new GUIContent("New Terrain Heightmap",
                    "The fine heightmap gives brushes more samples to work with."),
                    heightmapResolution, HeightmapLabels, HeightmapValues);
                var label = assigned == null ? "Create Scene Terrain" : "Create Another Scene Terrain";
                if (GUILayout.Button(label))
                {
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
        }

        private void DrawMountainStamp(Terrain terrain, WorldHeightmap world)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mountain Range Stamp", EditorStyles.boldLabel);
            stampLength = EditorGUILayout.Slider("Length", stampLength, 40f, 440f);
            stampWidth = EditorGUILayout.Slider("Width", stampWidth, 25f, 220f);
            stampRise = EditorGUILayout.Slider("Rise", stampRise, 5f, 90f);
            stampAngle = EditorGUILayout.Slider("Rotation", stampAngle, 0f, 180f);
            if (stampTerrain != null)
            {
                EditorGUILayout.HelpBox("Click an inland slope in the Scene view. Escape cancels.", MessageType.Info);
                if (GUILayout.Button("Cancel Stamp")) StopStamp();
                SceneView.RepaintAll();
            }
            else if (GUILayout.Button("Place Mountain Range"))
            {
                stampSource = world.Read();
                stampTerrain = terrain;
                SceneView.duringSceneGui += DrawStampInScene;
                SceneView.lastActiveSceneView?.Focus();
                SceneView.RepaintAll();
            }
        }

        private void OnDisable() => StopStamp();

        private void StopStamp()
        {
            SceneView.duringSceneGui -= DrawStampInScene;
            stampTerrain = null;
            stampSource = null;
            SceneView.RepaintAll();
        }

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
                using (new Handles.DrawingScope(new Color(.95f, .7f, .28f)))
                    Handles.DrawAAPolyLine(3f, points);
            }
            if (input.type != EventType.MouseDown || input.button != 0 || input.alt) return;

            input.Use();
            try
            {
                ApplyMountainStamp(hit.point - stampTerrain.transform.position, axis);
            }
            finally
            {
                StopStamp();
                Repaint();
            }
        }

        private void ApplyMountainStamp(Vector3 centre, Vector2 axis)
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
            var recipe = stampSource.Recipe;
            var changed = false;

            for (var y = bottom; y <= top; y++)
                for (var x = left; x <= right; x++)
                {
                    var offset = new Vector2(x * step.x - centre.x, y * step.z - centre.z);
                    var along = Vector2.Dot(offset, axis) * 2f / stampLength;
                    var across = Vector2.Dot(offset, new Vector2(-axis.y, axis.x)) * 2f / stampWidth;
                    var footprint = along * along + across * across;
                    if (footprint >= 1f) continue;
                    var sourceX = Mathf.RoundToInt((float)x / last * (recipe.Width - 1));
                    var sourceY = Mathf.RoundToInt((float)y / last * (recipe.Height - 1));
                    var altitude = stampSource.Elevations[sourceY * recipe.Width + sourceX];
                    var inland = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(recipe.SeaLevel + .015f, recipe.SeaLevel + .07f, altitude));
                    if (inland <= 0f) continue;

                    var envelope = (1f - footprint) * (1f - footprint);
                    var ridge = Mathf.Exp(-4.5f * Mathf.Abs(across - .18f * Mathf.Sin(along * 6f)));
                    var peaks = .52f + .48f * Mathf.PerlinNoise(along * 4.5f + 16f, 7.3f);
                    var gullies = Mathf.PerlinNoise(along * 13f + across * across * 4f + 22f, across * 2.5f + 9f);
                    var relief = envelope * peaks * (.36f + .64f * ridge) * (.64f + .36f * gullies);
                    var old = heights[y - bottom, x - left];
                    var raised = Mathf.Min(1f, old + relief * inland * stampRise / step.y);
                    heights[y - bottom, x - left] = raised;
                    changed |= raised > old;
                }

            if (!changed) return;
            Undo.RegisterCompleteObjectUndo(data, "Stamp Mountain Range");
            data.SetHeights(left, bottom, heights);
            EditorUtility.SetDirty(data);
        }

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
