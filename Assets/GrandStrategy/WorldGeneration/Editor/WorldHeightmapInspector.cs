using UnityEditor;

namespace GrandStrategy.WorldGeneration.Editor
{
    [CustomEditor(typeof(WorldHeightmap))]
    public sealed class WorldHeightmapInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var world = (WorldHeightmap)target;
            EditorGUILayout.LabelField("Authored world heightmap", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Dimensions", $"{world.Width} × {world.Height}");
            EditorGUILayout.LabelField("Seed", world.Seed.ToString());
            EditorGUILayout.LabelField("Sea level", world.SeaLevel.ToString("F3"));
            EditorGUILayout.HelpBox("The saved recipe describes these exact elevations. Open the World Generator to inspect them or generate and save a new world.", MessageType.Info);
            if (UnityEngine.GUILayout.Button("Open in World Generator")) WorldGeneratorWindow.OpenWorld(world);
        }
    }
}
