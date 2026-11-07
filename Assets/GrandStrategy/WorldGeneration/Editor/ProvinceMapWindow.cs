using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed class ProvinceMapWindow : EditorWindow
    {
        [SerializeField] private string manifest;
        [SerializeField] private string provinceImage;
        [SerializeField] private string provinceDefinitions;
        [SerializeField] private string territoryImage;
        [SerializeField] private string territoryDefinitions;
        [SerializeField] private string territoryHistory;
        [SerializeField] private ProvinceMapAsset savedMap;
        [SerializeField] private bool show3D;
        private ProvinceMap map;
        private HeightmapResult terrain;
        private float heightScale;
        private Texture2D texture;
        private Heightmap3DPreview preview3D;
        private int selected = -1;
        private Vector2 scroll;
        private string status = "Select the exported world manifest and OpenGS province files.";

        [MenuItem("Tools/GrandStrategy/Province Map")]
        private static void Open()
        {
            var window = GetWindow<ProvinceMapWindow>("Province Map");
            window.minSize = new Vector2(880, 560);
        }

        private void OnDisable()
        {
            try { preview3D?.Dispose(); }
            finally
            {
                preview3D = null;
                if (texture != null) DestroyImmediate(texture);
                texture = null;
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(315)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawImport();
                    DrawProvince();
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawMap();
                    EditorGUILayout.HelpBox(status, MessageType.Info);
                }
            }
        }

        private void DrawImport()
        {
            EditorGUILayout.LabelField("IMPORT PROVINCES", EditorStyles.boldLabel);
            manifest = FileField("World manifest", manifest, "json");
            provinceImage = FileField("Province image", provinceImage, "png");
            provinceDefinitions = FileField("Province definitions", provinceDefinitions, "");
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Optional territory files", EditorStyles.miniBoldLabel);
            territoryImage = FileField("Territory image", territoryImage, "png");
            territoryDefinitions = FileField("Territory definitions", territoryDefinitions, "");
            territoryHistory = FileField("Territory history", territoryHistory, "");
            if (GUILayout.Button("Import OpenGS Map"))
                Run(() =>
                {
                    var next = OpenGsMapReader.Read(manifest, provinceImage, provinceDefinitions, territoryImage,
                        territoryDefinitions, territoryHistory, out var elevation, out var scale);
                    ShowMap(next, elevation, scale);
                });

            EditorGUILayout.Space(6);
            savedMap = (ProvinceMapAsset)EditorGUILayout.ObjectField(savedMap, typeof(ProvinceMapAsset), false);
            using (new EditorGUI.DisabledScope(savedMap == null))
                if (GUILayout.Button("Open Saved Map"))
                    Run(() => ShowMap(savedMap.Map, savedMap.ReadTerrain(), savedMap.HeightScale));
            using (new EditorGUI.DisabledScope(map == null))
                if (GUILayout.Button("Save Province Map as Asset")) Run(SaveMap);
        }

        private static string FileField(string label, string path, string extension)
        {
            EditorGUILayout.LabelField(label);
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(path ?? "");
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    var directory = string.IsNullOrWhiteSpace(path) ? "" : Path.GetDirectoryName(path);
                    var file = EditorUtility.OpenFilePanel(label, directory ?? "", extension);
                    if (!string.IsNullOrEmpty(file)) path = file;
                }
            }
            return path;
        }

        private void DrawProvince()
        {
            if (selected < 0) return;
            var province = map.Provinces[selected];
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(province.Id, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Type", province.Type);
            EditorGUILayout.LabelField("OpenGS terrain", string.IsNullOrEmpty(province.Terrain) ? "Unavailable" : province.Terrain);
            EditorGUILayout.LabelField("Territory", province.TerritoryId ?? "Unassigned");
            EditorGUILayout.LabelField("Samples", province.PixelCount.ToString("N0"));
            if (map.HasRiverData) EditorGUILayout.LabelField("River route samples", province.RiverSamples.ToString("N0"));
            if (map.HasCoastData && province.Type == "land")
                EditorGUILayout.LabelField("Borders open sea", province.TouchesOpenSea ? "Yes" : "No");
            if (province.BiomeCounts != null)
            {
                EditorGUILayout.LabelField("Biome mix", EditorStyles.boldLabel);
                for (var i = 0; i < province.BiomeCounts.Length; i++)
                    if (province.BiomeCounts[i] > 0)
                        EditorGUILayout.LabelField(((BiomeKind)i).ToString(), (province.BiomeCounts[i] / (float)province.PixelCount).ToString("P1"));
            }
            EditorGUILayout.LabelField($"Neighbours ({province.Neighbours.Length})", EditorStyles.boldLabel);
            foreach (var neighbour in province.Neighbours)
                if (GUILayout.Button(map.Provinces[neighbour].Id)) Run(() => Select(neighbour));
        }

        private void DrawMap()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                show3D = GUILayout.Toggle(show3D, "3D", EditorStyles.toolbarButton, GUILayout.Width(40));
                if (show3D && GUILayout.Button("Reset view", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    preview3D?.ResetView();
                GUILayout.FlexibleSpace();
                GUILayout.Label(show3D ? "Drag to orbit | Scroll to zoom | Right-click to select" : "Click a province to inspect it", EditorStyles.miniLabel);
            }
            var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(.035f, .045f, .055f));
            if (texture == null || area.width < 2 || area.height < 2) return;
            var input = Event.current;
            if (show3D)
            {
                try
                {
                    if (preview3D == null) preview3D = new Heightmap3DPreview();
                    preview3D.SetWorld(terrain, texture, heightScale, false);
                    if (input.type == EventType.MouseDown && input.button == 1 && preview3D.TryPick(area, input.mousePosition, out var uv))
                    {
                        SelectAt(uv);
                        input.Use();
                    }
                    if (preview3D.HandleInput(area)) Repaint();
                    preview3D.Draw(area);
                }
                catch (Exception ex) { show3D = false; status = ex.Message; }
                return;
            }
            if (preview3D != null) { preview3D.Dispose(); preview3D = null; }
            var scale = Mathf.Min(area.width / map.Width, area.height / map.Height);
            var width = map.Width * scale;
            var height = map.Height * scale;
            var image = new Rect(area.center.x - width / 2, area.center.y - height / 2, width, height);
            GUI.DrawTexture(image, texture);
            if (input.type == EventType.MouseDown && input.button == 0 && image.Contains(input.mousePosition))
            {
                var uv = new Vector2((input.mousePosition.x - image.x) / width, (image.yMax - input.mousePosition.y) / height);
                Run(() => SelectAt(uv));
                input.Use();
            }
        }

        private void SelectAt(Vector2 uv)
        {
            var x = Mathf.Clamp((int)(uv.x * map.Width), 0, map.Width - 1);
            var y = Mathf.Clamp((int)(uv.y * map.Height), 0, map.Height - 1);
            Select(map.ProvinceAt[y * map.Width + x]);
        }

        private void Select(int index)
        {
            if (index == selected) return;
            SetTexture(CreateTexture(map, index));
            selected = index;
            Repaint();
        }

        private void ShowMap(ProvinceMap provinces, HeightmapResult elevation, float scale)
        {
            SetTexture(CreateTexture(provinces, -1));
            map = provinces;
            terrain = elevation;
            heightScale = scale;
            selected = -1;
            status = $"Loaded {map.Provinces.Length:N0} provinces, {map.Territories.Length:N0} territories, {map.UnassignedPixels:N0} unassigned pixels.";
        }

        private void SetTexture(Texture2D next)
        {
            if (texture != null) DestroyImmediate(texture);
            texture = next;
        }

        private static Texture2D CreateTexture(ProvinceMap map, int selection)
        {
            var pixels = new Color32[map.ProvinceAt.Length];
            var colors = new Color32[map.Provinces.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                var rgb = map.Provinces[i].Rgb;
                colors[i] = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                var province = map.ProvinceAt[i];
                if (province < 0)
                {
                    pixels[i] = new Color32(36, 38, 41, 255);
                    continue;
                }
                var x = i % map.Width;
                var edge = (x > 0 && map.ProvinceAt[i - 1] != province)
                    || (x < map.Width - 1 && map.ProvinceAt[i + 1] != province)
                    || (i >= map.Width && map.ProvinceAt[i - map.Width] != province)
                    || (i < pixels.Length - map.Width && map.ProvinceAt[i + map.Width] != province);
                pixels[i] = province == selection
                    ? edge ? new Color32(255, 246, 190, 255) : new Color32(233, 183, 74, 255)
                    : edge ? new Color32(19, 23, 30, 255) : colors[province];
            }

            var next = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            try { next.SetPixels32(pixels); next.Apply(false, true); return next; }
            catch { DestroyImmediate(next); throw; }
        }

        private void SaveMap()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save province map", "ProvinceMap", "asset", "Choose a new asset path.");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) throw new IOException("Choose a new filename to preserve the existing map.");
            var asset = CreateInstance<ProvinceMapAsset>();
            try
            {
                asset.Initialize(map, terrain, heightScale);
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                savedMap = asset;
                status = "Saved " + path;
            }
            catch { if (!AssetDatabase.Contains(asset)) DestroyImmediate(asset); throw; }
        }

        private void Run(Action action)
        {
            try { action(); Repaint(); }
            catch (Exception ex) { status = ex.Message; }
        }
    }
}
