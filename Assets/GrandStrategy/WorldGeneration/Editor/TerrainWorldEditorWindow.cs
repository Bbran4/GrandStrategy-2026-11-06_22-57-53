// Keeps terrain authoring controls open while the Scene view is used for brushes and stamps.
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed class TerrainWorldEditorWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Sculpt", "Paint", "Stamps", "Preview", "Publish", "Geography" };
        [SerializeField] private PlayableMap map;
        [SerializeField] private int tab;
        private Vector2 scroll;
        private PlayableMapInspector workspace;

        [MenuItem("Tools/GrandStrategy/World Sculptor", false, 101)]
        private static void OpenFromMenu() => Open(Selection.activeGameObject?.GetComponent<PlayableMap>());

        internal static void Open(PlayableMap selected)
        {
            var window = GetWindow<TerrainWorldEditorWindow>("World Sculptor");
            window.minSize = new Vector2(320f, 240f);
            if (selected != null) window.SetMap(selected);
            else if (window.map == null) window.FindMap();
        }

        [InitializeOnLoadMethod]
        private static void ClearPreviewAfterPlay()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    SessionState.EraseString(PlayableMap.EditablePreviewSessionKey);
            };
        }

        private void OnEnable() => EditorApplication.hierarchyChanged += OnHierarchyChanged;

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            ReleaseWorkspace();
        }

        private void OnHierarchyChanged()
        {
            if (map == null) FindMap();
            Repaint();
        }

        private void FindMap()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<PlayableMap>(true);
                if (found == null) continue;
                SetMap(found);
                return;
            }
        }

        private void SetMap(PlayableMap next)
        {
            if (map == next) return;
            ReleaseWorkspace();
            map = next;
            Repaint();
        }

        private void ReleaseWorkspace()
        {
            if (workspace != null) DestroyImmediate(workspace);
            workspace = null;
        }

        private void OnGUI()
        {
            SetMap((PlayableMap)EditorGUILayout.ObjectField("Playable Map", map, typeof(PlayableMap), true));
            if (map == null)
            {
                EditorGUILayout.HelpBox("Open PlayableMap.unity or assign a Playable Map from an open scene.", MessageType.Info);
                if (GUILayout.Button("Find Playable Map in Active Scene")) FindMap();
                return;
            }

            if (workspace == null)
                workspace = (PlayableMapInspector)UnityEditor.Editor.CreateEditor(map, typeof(PlayableMapInspector));
            tab = GUILayout.Toolbar(tab, Tabs);
            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;
                workspace.DrawWorkspace((PlayableMapInspector.WorkspaceTab)tab);
            }
        }
    }
}
