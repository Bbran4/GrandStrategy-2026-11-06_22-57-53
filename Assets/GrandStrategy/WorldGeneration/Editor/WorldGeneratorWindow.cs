using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed class WorldGeneratorWindow : EditorWindow
    {
        [SerializeField] private HeightmapRecipe recipe;
        [SerializeField] private WorldHeightmap savedWorld;
        [SerializeField] private HeightmapPreviewMode previewMode;
        [SerializeField] private int selectedStep;
        private HeightmapResult result;
        private Texture2D preview;
        private Vector2 scroll;
        private bool settingsChanged;
        private string status = "Choose a seed and generate your first Old World heightmap.";
        private static readonly string[] SizeNames = { "1024 × 512 · Prototype", "2048 × 1024 · Full map", "3072 × 2048 · Large map" };
        private static readonly int[] Widths = { 1024, 2048, 3072 };
        private static readonly int[] Heights = { 512, 1024, 2048 };

        [MenuItem("Tools/GrandStrategy/World Generator")]
        public static void Open()
        {
            var window = GetWindow<WorldGeneratorWindow>("World Generator");
            window.minSize = new Vector2(900, 600);
        }

        public static void OpenWorld(WorldHeightmap world)
        {
            Open();
            var window = GetWindow<WorldGeneratorWindow>();
            window.savedWorld = world; window.LoadWorld();
        }

        private void OnEnable() { if (recipe == null) recipe = HeightmapRecipe.OldWorld(); }
        private void OnDisable() { if (preview != null) DestroyImmediate(preview); }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("GRANDSTRATEGY  /  WORLD AUTHORING", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("OLD WORLD  ·  Heightmap foundation", EditorStyles.largeLabel);
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(335)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawSettings();
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope()) DrawPreview();
            }
        }

        private void DrawSettings()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("WORLD SETTINGS", EditorStyles.boldLabel);
            var sizeIndex = -1;
            for (var i = 0; i < Widths.Length; i++)
                if (recipe.Width == Widths[i] && recipe.Height == Heights[i]) sizeIndex = i;
            var names = sizeIndex < 0 ? new[] { SizeNames[0], SizeNames[1], SizeNames[2], $"{recipe.Width} × {recipe.Height} · Loaded recipe" } : SizeNames;
            var currentIndex = sizeIndex < 0 ? 3 : sizeIndex;
            var newIndex = EditorGUILayout.Popup("Resolution", currentIndex, names);
            if (newIndex != currentIndex && newIndex < Widths.Length) { recipe.Width = Widths[newIndex]; recipe.Height = Heights[newIndex]; }
            recipe.Seed = EditorGUILayout.IntField("Seed", recipe.Seed);
            recipe.SeaLevel = EditorGUILayout.Slider(new GUIContent("Sea level", "Normalized elevation threshold; changing this requires regeneration."), recipe.SeaLevel, .05f, .85f);
            recipe.CoastWarp = EditorGUILayout.Slider("Coast irregularity", recipe.CoastWarp, 0, .15f);
            recipe.EdgeFalloff = EditorGUILayout.Slider("Ocean border", recipe.EdgeFalloff, .01f, .2f);
            if (EditorGUI.EndChangeCheck()) settingsChanged = true;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(30))) Generate();
                if (GUILayout.Button("New seed + generate", GUILayout.Height(30)))
                { recipe.Seed = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0); settingsChanged = true; Generate(); }
            }
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("TEMPLATE STEPS", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Executed from top to bottom. Positions are %.", EditorStyles.miniLabel);
            for (var i = 0; i < recipe.Steps.Count; i++)
            {
                var step = recipe.Steps[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var enabled = EditorGUILayout.Toggle(step.Enabled, GUILayout.Width(18));
                    if (enabled != step.Enabled) { step.Enabled = enabled; settingsChanged = true; }
                    if (GUILayout.Toggle(selectedStep == i, $"{i + 1:00}   {step.Operation}", "Button")) selectedStep = i;
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("↑", GUILayout.Width(25))) { Move(i, i - 1); break; }
                    using (new EditorGUI.DisabledScope(i == recipe.Steps.Count - 1))
                        if (GUILayout.Button("↓", GUILayout.Width(25))) { Move(i, i + 1); break; }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(recipe.Steps.Count >= 48))
                    if (GUILayout.Button("Add step"))
                    { recipe.Steps.Add(HeightmapRecipe.Step(HeightmapOperation.Hill)); selectedStep = recipe.Steps.Count - 1; settingsChanged = true; }
                using (new EditorGUI.DisabledScope(recipe.Steps.Count <= 1))
                    if (GUILayout.Button("Remove selected"))
                    { recipe.Steps.RemoveAt(selectedStep); selectedStep = Mathf.Clamp(selectedStep, 0, recipe.Steps.Count - 1); settingsChanged = true; }
            }
            if (recipe.Steps.Count > 0) DrawStep(recipe.Steps[Mathf.Clamp(selectedStep, 0, recipe.Steps.Count - 1)]);
            EditorGUILayout.Space(10);
            if (GUILayout.Button("Reset Old World recipe") && EditorUtility.DisplayDialog("Reset recipe?", "Replace the current settings and steps? The displayed result remains available until you generate again.", "Reset", "Cancel"))
            { recipe = HeightmapRecipe.OldWorld(); selectedStep = 0; settingsChanged = true; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save recipe JSON")) SaveRecipe();
                if (GUILayout.Button("Load recipe JSON")) LoadRecipe();
            }
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("SAVED WORLD", EditorStyles.boldLabel);
            savedWorld = (WorldHeightmap)EditorGUILayout.ObjectField(savedWorld, typeof(WorldHeightmap), false);
            using (new EditorGUI.DisabledScope(savedWorld == null))
                if (GUILayout.Button("Load saved world")) LoadWorld();
            using (new EditorGUI.DisabledScope(result == null))
            {
                if (GUILayout.Button("Save displayed world as asset…")) SaveWorld();
                if (GUILayout.Button("Export displayed world…")) Export();
            }
            EditorGUILayout.HelpBox("Relief colours indicate elevation, not biomes or simulated snow. Rivers, lakes, climate, and provinces come later.", MessageType.Info);
        }

        private void DrawStep(HeightmapStep step)
        {
            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            step.Operation = (HeightmapOperation)EditorGUILayout.EnumPopup("Operation", step.Operation);
            var repeated = step.Operation != HeightmapOperation.MultiplyLand && step.Operation != HeightmapOperation.Detail;
            if (repeated)
            {
                step.CountMin = EditorGUILayout.IntSlider("Minimum count / passes", step.CountMin, 1, 32);
                step.CountMax = EditorGUILayout.IntSlider("Maximum count / passes", step.CountMax, step.CountMin, 32);
            }
            step.StrengthMin = EditorGUILayout.Slider("Minimum strength", step.StrengthMin, 0, 1);
            var positioned = step.Operation == HeightmapOperation.Hill || step.Operation == HeightmapOperation.Pit
                || step.Operation == HeightmapOperation.Range || step.Operation == HeightmapOperation.Trough || step.Operation == HeightmapOperation.Strait;
            if (positioned)
            {
                step.StrengthMax = EditorGUILayout.Slider("Maximum strength", step.StrengthMax, step.StrengthMin, 1);
                step.Radius = EditorGUILayout.Slider("Radius (% map height)", step.Radius * 100, .5f, 50) / 100;
                if (step.Operation == HeightmapOperation.Range || step.Operation == HeightmapOperation.Trough)
                    step.Length = EditorGUILayout.Slider("Length (% map height)", step.Length * 100, 2, 100) / 100;
                step.XMin = EditorGUILayout.Slider("West limit (%)", step.XMin * 100, 0, 100) / 100;
                step.XMax = EditorGUILayout.Slider("East limit (%)", step.XMax * 100, step.XMin * 100, 100) / 100;
                step.YMin = EditorGUILayout.Slider("South limit (%)", step.YMin * 100, 0, 100) / 100;
                step.YMax = EditorGUILayout.Slider("North limit (%)", step.YMax * 100, step.YMin * 100, 100) / 100;
                if (step.Operation == HeightmapOperation.Strait) step.Horizontal = EditorGUILayout.Toggle("Horizontal channel", step.Horizontal);
            }
            else step.StrengthMax = step.StrengthMin;
            if (EditorGUI.EndChangeCheck()) settingsChanged = true;
        }

        private void DrawPreview()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                previewMode = (HeightmapPreviewMode)EditorGUILayout.EnumPopup(previewMode, EditorStyles.toolbarPopup, GUILayout.Width(150));
                if (EditorGUI.EndChangeCheck() && result != null) RefreshPreview();
                GUILayout.FlexibleSpace();
                GUILayout.Label("NORTH ↑", EditorStyles.miniLabel);
            }
            var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(.035f, .045f, .055f));
            if (preview != null)
            {
                var aspect = (float)preview.width / preview.height;
                var w = Mathf.Min(area.width - 24, (area.height - 24) * aspect); var h = w / aspect;
                var imageRect = new Rect(area.center.x - w / 2, area.center.y - h / 2, w, h);
                GUI.DrawTexture(imageRect, preview, ScaleMode.StretchToFill, false);
                if (imageRect.Contains(Event.current.mousePosition))
                {
                    var point = Event.current.mousePosition;
                    var x = Mathf.Clamp((int)((point.x - imageRect.x) / imageRect.width * result.Recipe.Width), 0, result.Recipe.Width - 1);
                    var y = Mathf.Clamp((int)((imageRect.yMax - point.y) / imageRect.height * result.Recipe.Height), 0, result.Recipe.Height - 1);
                    var elevation = result.Elevations[y * result.Recipe.Width + x];
                    GUI.Label(new Rect(area.x + 12, area.yMax - 25, area.width - 24, 20), $"X {x} · Y {y} · Elevation {elevation:F4} · {(elevation >= result.Recipe.SeaLevel ? "Land" : "Water")}", EditorStyles.whiteMiniLabel);
                    if (Event.current.type == EventType.MouseMove) Repaint();
                }
            }
            else GUI.Label(area, "Generate an Old World to preview its elevation.", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
            if (result != null)
                EditorGUILayout.LabelField($"{result.Recipe.Width} × {result.Recipe.Height}   |   Seed {result.Recipe.Seed}   |   Land {result.LandFraction:P1}   |   Elevation {result.Minimum:F3}–{result.Maximum:F3}", EditorStyles.miniLabel);
            if (settingsChanged && result != null)
                EditorGUILayout.HelpBox("Settings changed. Generate again to apply them. Saving and exporting use the displayed result and its original settings.", MessageType.Warning);
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void Move(int from, int to)
        { var step = recipe.Steps[from]; recipe.Steps.RemoveAt(from); recipe.Steps.Insert(to, step); selectedStep = to; settingsChanged = true; }

        private void Generate()
        {
            try
            {
                var timer = Stopwatch.StartNew();
                var next = HeightmapGenerator.Generate(recipe, (fraction, label) => !EditorUtility.DisplayCancelableProgressBar("Generating Old World", label, fraction));
                result = next; settingsChanged = false; RefreshPreview();
                status = $"Generated in {timer.Elapsed.TotalSeconds:F2}s. Save an asset to preserve this world across editor reloads.";
            }
            catch (OperationCanceledException) { status = "Generation cancelled. The previous completed world is retained."; }
            catch (Exception ex) { ShowError(ex); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        private void RefreshPreview()
        {
            var next = HeightmapPreview.Create(result, previewMode);
            if (preview != null) DestroyImmediate(preview);
            preview = next; wantsMouseMove = true;
        }

        private void SaveWorld()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save world heightmap", $"OldWorld-{result.Recipe.Seed}", "asset", "Save the displayed elevations and their generation recipe.");
            if (string.IsNullOrEmpty(path)) return;
            // New assets only: never silently replace an authored world or its GUID.
            if (File.Exists(path)) { status = "Choose a new filename to preserve the existing world."; return; }
            WorldHeightmap asset = null;
            try
            {
                asset = CreateInstance<WorldHeightmap>(); asset.Initialize(result);
                AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
                savedWorld = asset; EditorGUIUtility.PingObject(asset); status = "Saved " + path;
            }
            catch (Exception ex) { if (asset != null && !AssetDatabase.Contains(asset)) DestroyImmediate(asset); ShowError(ex); }
        }

        private void LoadWorld()
        {
            try
            {
                var next = savedWorld.Read();
                result = next; recipe = next.Recipe.Copy(); selectedStep = 0; settingsChanged = false; RefreshPreview();
                status = "Loaded saved world. Its elevations are preserved; Generate explicitly replaces the displayed result.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [Serializable] private sealed class RecipeFile
        { public string Format = "GrandStrategy.HeightmapRecipe"; public HeightmapRecipe Recipe; }

        private void SaveRecipe()
        {
            var path = EditorUtility.SaveFilePanel("Save generation recipe", "", "OldWorld-recipe", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { recipe.Validate(); File.WriteAllText(path, JsonUtility.ToJson(new RecipeFile { Recipe = recipe }, true)); status = "Saved recipe settings (not elevation data)."; }
            catch (Exception ex) { ShowError(ex); }
        }

        private void LoadRecipe()
        {
            var path = EditorUtility.OpenFilePanel("Load generation recipe", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (new FileInfo(path).Length > 262144) throw new InvalidDataException("Recipe file exceeds the 256 KiB limit.");
                var file = JsonUtility.FromJson<RecipeFile>(File.ReadAllText(path));
                if (file == null || file.Format != "GrandStrategy.HeightmapRecipe" || file.Recipe == null)
                    throw new InvalidDataException("Not a GrandStrategy heightmap recipe.");
                file.Recipe.Validate(); recipe = file.Recipe; selectedStep = 0; settingsChanged = true;
                status = "Loaded recipe. Generate to apply it.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        [Serializable] private sealed class ExportManifest
        {
            public string Format = "GrandStrategy.HeightmapExport";
            public int Version = 1;
            public string Coordinates = "X west to east; Y south to north; row-major. PNG top corresponds to north.";
            public string RawEncoding = "unsigned 16-bit little-endian; no header; first row south; normalized 0–65535";
            public string ElevationUnits = "normalized 0–1, not metres";
            public string WaterMask = "white land, black below sea level; lakes and oceans not classified";
            public string Preview = "8-bit PNGs are inspection images; RAW16 and the Unity asset preserve higher precision";
            public HeightmapRecipe Recipe;
        }

        private void Export()
        {
            var parent = EditorUtility.OpenFolderPanel("Choose export parent folder", "", "");
            if (string.IsNullOrEmpty(parent)) return;
            var folder = Path.Combine(parent, $"OldWorld-{result.Recipe.Seed}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}");
            try
            {
                Directory.CreateDirectory(folder);
                using (var stream = new FileStream(Path.Combine(folder, "heightmap.raw"), FileMode.CreateNew)) HeightmapEncoding.WriteRaw16(result, stream);
                WritePng(Path.Combine(folder, "heightmap-preview.png"), HeightmapPreviewMode.Grayscale);
                WritePng(Path.Combine(folder, "relief.png"), HeightmapPreviewMode.Relief);
                WriteMask(Path.Combine(folder, "land-mask.png"));
                File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonUtility.ToJson(new ExportManifest { Recipe = result.Recipe }, true));
                status = "Exported to " + folder;
                EditorUtility.RevealInFinder(folder);
            }
            catch (Exception ex) { status = "Export incomplete at " + folder + ". " + ex.Message; Debug.LogException(ex); }
        }

        private void WritePng(string path, HeightmapPreviewMode mode)
        {
            var texture = HeightmapPreview.Create(result, mode);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { DestroyImmediate(texture); }
        }

        private void WriteMask(string path)
        {
            var texture = new Texture2D(result.Recipe.Width, result.Recipe.Height, TextureFormat.RGBA32, false, true);
            try
            {
                var pixels = new Color32[result.Elevations.Length];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = result.Elevations[i] >= result.Recipe.SeaLevel ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { DestroyImmediate(texture); }
        }

        private void ShowError(Exception ex) { status = ex.Message; Debug.LogException(ex); }
    }
}
