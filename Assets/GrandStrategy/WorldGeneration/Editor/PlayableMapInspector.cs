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
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();
            var world = serializedObject.FindProperty("world").objectReferenceValue as WorldHeightmap;
            var terrain = serializedObject.FindProperty("authoringTerrain");
            var source = serializedObject.FindProperty("authoringWorld");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scene Terrain", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Sculpt and paint the scene Terrain in Edit Mode, then save the scene. Play Mode reads its heights and paint. " +
                "Keep the first Terrain Layer as the base. The next three layers blend over the generated ground. " +
                "The generated coastline and riverbanks are protected from height edits.", MessageType.Info);

            var assigned = terrain.objectReferenceValue as Terrain;
            if (assigned != null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Authoring Terrain", assigned, typeof(Terrain), true);
                if (GUILayout.Button("Select Terrain for Sculpting and Painting"))
                    Selection.activeGameObject = assigned.gameObject;

                if (source.objectReferenceValue != world)
                    EditorGUILayout.HelpBox("This Terrain was created for a different World asset.", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(world == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
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

        private void CreateTerrain(WorldHeightmap world, Terrain previous, SerializedProperty terrain, SerializedProperty source)
        {
            var map = (PlayableMap)target;
            var scene = map.gameObject.scene;
            if (string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("Save the scene before creating its Terrain.");

            var generated = world.Read();
            var width = generated.Recipe.Width;
            var height = generated.Recipe.Height;
            var resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height) - 1) + 1, 33, 2049);
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
                    AddLayers(data, ground);

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
            var resolution = data.alphamapResolution;
            var weights = new float[resolution, resolution, layers.Length];
            for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                    weights[y, x, 0] = 1f;
            data.SetAlphamaps(0, 0, weights);

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
