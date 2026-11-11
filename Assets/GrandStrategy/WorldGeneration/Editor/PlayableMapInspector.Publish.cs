// Validates editable terrain and publishes an independent set of assets for the campaign.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed partial class PlayableMapInspector
    {
        private string publishStatus;

        private void DrawPublishControls(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinces)
        {
            var release = serializedObject.FindProperty("publishedWorld");
            EditorGUILayout.PropertyField(release, new GUIContent("Published World",
                "The campaign loads this version. Assign an older version here to switch back, then save the scene."));

            var current = release.objectReferenceValue as PublishedWorld;
            if (current != null)
                EditorGUILayout.LabelField("Campaign Version", current.Version.ToString());
            else
                EditorGUILayout.HelpBox("Publish a world to give the campaign a fixed terrain version.", MessageType.Info);

            using (new EditorGUI.DisabledScope(terrain == null || world == null || provinces == null
                || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Preview Playable Map"))
                    RunPublishAction(() => PreviewEditable(terrain, world, provinces));

                if (GUILayout.Button(new GUIContent("Publish World",
                    "Save a new world version beside this scene and save the scene's campaign assignment.")))
                    RunPublishAction(() => Publish(terrain, world, provinces, release));
            }

            EditorGUILayout.HelpBox("Preview uses the current comparison setting. Publishing includes both the sculpting and paint and saves the scene.", MessageType.None);
            if (!string.IsNullOrEmpty(publishStatus))
                EditorGUILayout.HelpBox(publishStatus, MessageType.Info);
        }

        private void RunPublishAction(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                publishStatus = exception.Message;
                Debug.LogException(exception, target);
            }
        }

        private void PreviewEditable(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinces)
        {
            ValidateWorldForPublish(terrain, world, provinces);
            var map = (PlayableMap)target;
            serializedObject.ApplyModifiedProperties();
            SessionState.SetString(PlayableMap.EditablePreviewSessionKey, map.gameObject.scene.path);
            EditorApplication.EnterPlaymode();
        }

        private void Publish(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinces, SerializedProperty selectedRelease)
        {
            ValidateWorldForPublish(terrain, world, provinces);
            var scene = ((PlayableMap)target).gameObject.scene;
            var directory = Path.GetDirectoryName(scene.path)?.Replace('\\', '/') ?? "Assets";
            var current = selectedRelease.objectReferenceValue as PublishedWorld;
            var version = current == null ? 1 : current.Version + 1;
            string path;
            do
            {
                path = $"{directory}/{scene.name} Published World {version:D3}.asset";
                if (!File.Exists(path) && AssetDatabase.LoadMainAssetAtPath(path) == null) break;
                version++;
            } while (true);

            var detail = serializedObject.FindProperty("authoredMeshDetail").intValue;
            var snapshot = CreatePublishedWorld(path, version, world, provinces, terrain.terrainData, detail);
            selectedRelease.objectReferenceValue = snapshot;
            serializedObject.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            publishStatus = EditorSceneManager.SaveScene(scene)
                ? $"Published version {version} to {path}"
                : $"Created {path}. Save the scene to keep this version assigned.";
        }

        private void ValidateWorldForPublish(Terrain terrain, WorldHeightmap world, ProvinceMapAsset provinces)
        {
            if (string.IsNullOrEmpty(((PlayableMap)target).gameObject.scene.path))
                throw new InvalidOperationException("Save the Playable Map scene first.");
            var source = world.Read();
            provinces.ValidateTerrain(source);
            var size = new Vector3((float)source.Recipe.Width / source.Recipe.Height, world.HeightScale, 1f)
                * PlayableMap.TerrainAuthoringScale;
            var origin = new Vector3(-size.x * .5f, -world.SeaLevel * size.y, -size.z * .5f);
            var transform = terrain.transform;
            if ((terrain.terrainData.size - size).sqrMagnitude > .0001f
                || (transform.position - origin).sqrMagnitude > .0001f
                || (transform.lossyScale - Vector3.one).sqrMagnitude > .0001f
                || Quaternion.Angle(transform.rotation, Quaternion.identity) > .01f)
                throw new InvalidOperationException("Restore the editable Terrain's original position, rotation and size so it aligns with the world.");

            var layers = terrain.terrainData.terrainLayers;
            if (layers.Length > 4 || Array.Exists(layers, layer => layer == null))
                throw new InvalidOperationException("Use up to four assigned Terrain Layers before publishing.");
        }

        private static PublishedWorld CreatePublishedWorld(string path, int version, WorldHeightmap world,
            ProvinceMapAsset provinces, TerrainData editable, int detail)
        {
            var objects = new List<UnityEngine.Object>();
            PublishedWorld snapshot = null;
            try
            {
                snapshot = Track(CreateInstance<PublishedWorld>());
                AssetDatabase.CreateAsset(snapshot, path);
                var copiedWorld = AddCopy(world, world.name);
                var copiedProvinces = AddCopy(provinces, provinces.name);

                // Copy the actual height and paint samples into new native storage. Referencing the editable
                // TerrainData would let future brush strokes change a previously published version.
                var terrain = Track(new TerrainData
                {
                    name = "Published Terrain",
                    heightmapResolution = editable.heightmapResolution,
                    alphamapResolution = editable.alphamapResolution,
                    size = editable.size
                });
                AssetDatabase.AddObjectToAsset(terrain, snapshot);
                var resolution = terrain.heightmapResolution;
                terrain.SetHeights(0, 0, editable.GetHeights(0, 0, resolution, resolution));

                var layers = Array.ConvertAll(editable.terrainLayers, layer => AddCopy(layer, layer.name));
                terrain.terrainLayers = layers;
                if (layers.Length > 0)
                    terrain.SetAlphamaps(0, 0, editable.GetAlphamaps(0, 0, editable.alphamapWidth, editable.alphamapHeight));
                MarkPaintDirty(terrain);

                snapshot.Initialize(version, copiedWorld, copiedProvinces, terrain, detail);
                EditorUtility.SetDirty(snapshot);
                AssetDatabase.SaveAssetIfDirty(snapshot);
                return snapshot;

                T AddCopy<T>(T source, string name) where T : UnityEngine.Object
                {
                    var copy = Track(Instantiate(source));
                    copy.name = name;
                    AssetDatabase.AddObjectToAsset(copy, snapshot);
                    return copy;
                }
            }
            catch
            {
                // Remove only the incomplete version created by this call.
                if (snapshot != null && AssetDatabase.GetAssetPath(snapshot) == path)
                    AssetDatabase.DeleteAsset(path);
                foreach (var item in objects)
                    if (item != null && !AssetDatabase.Contains(item)) DestroyImmediate(item);
                throw;
            }

            T Track<T>(T item) where T : UnityEngine.Object
            {
                objects.Add(item);
                return item;
            }
        }
    }
}
