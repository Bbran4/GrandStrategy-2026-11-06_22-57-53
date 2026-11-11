// Coordinates world authoring in the Unity Editor, from recipe editing through generation, preview, saving
// and export.
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
        [SerializeField] private bool show3D;
        [SerializeField] private float heightScale = WorldHeightmap.DefaultHeightScale;
        [SerializeField] private float riverCatchment = RiverGenerator.DefaultCatchmentPercent;
        [SerializeField] private float riverDepth = RiverGenerator.DefaultCarveDepth;
        [SerializeField] private float maximumNavalRise = RiverNavigation.DefaultMaximumRise;
        [SerializeField] private bool exportNavalMask;
        private RiverNavigation navigation;
        [SerializeField] private ClimateSettings climateSettings = new ClimateSettings();
        private ClimateMap climate;
        private BiomeMap biomes;
        private HeightmapResult result;
        [SerializeField] private float riverWidthScale = RiverGenerator.DefaultWidthScale;
        private RiverMap rivers;
        private WaterMap water;
        private Texture2D preview;
        private Heightmap3DPreview preview3D;
        private Vector2 scroll;
        private bool settingsChanged;
        private string status = "Choose a seed and generate your first Old World heightmap.";
        private static readonly string[] SizeNames = { "1024 x 512 | Prototype", "2048 x 1024 | Full map", "3072 x 2048 | Large map" };
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

        private void OnEnable()
        {
            if (recipe == null) recipe = HeightmapRecipe.OldWorld();
            if (climateSettings == null) climateSettings = new ClimateSettings();
        }
        private void OnDisable()
        {
            try { if (preview3D != null) { preview3D.Dispose(); preview3D = null; } }
            finally { if (preview != null) { DestroyImmediate(preview); preview = null; } }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("GRANDSTRATEGY  /  WORLD AUTHORING", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("OLD WORLD  |  Heightmap foundation", EditorStyles.largeLabel);
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

        // Expose world dimensions and recipe steps before generating a new elevation field.
        private void DrawSettings()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("WORLD SETTINGS", EditorStyles.boldLabel);
            var sizeIndex = -1;
            for (var i = 0; i < Widths.Length; i++)
                if (recipe.Width == Widths[i] && recipe.Height == Heights[i]) sizeIndex = i;
            var names = sizeIndex < 0 ? new[] { SizeNames[0], SizeNames[1], SizeNames[2], $"{recipe.Width} x {recipe.Height} | Loaded recipe" } : SizeNames;
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
            DrawRiverSettings();
            EditorGUILayout.Space(10);
            DrawClimateSettings();
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
                        if (GUILayout.Button("Up", GUILayout.Width(25))) { Move(i, i - 1); break; }
                    using (new EditorGUI.DisabledScope(i == recipe.Steps.Count - 1))
                        if (GUILayout.Button("Dn", GUILayout.Width(25))) { Move(i, i + 1); break; }
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
                if (GUILayout.Button("Load & Generate Recipe")) LoadRecipe();
            }
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("SAVED WORLD", EditorStyles.boldLabel);
            savedWorld = (WorldHeightmap)EditorGUILayout.ObjectField(savedWorld, typeof(WorldHeightmap), false);
            using (new EditorGUI.DisabledScope(savedWorld == null))
                if (GUILayout.Button("Load saved world")) LoadWorld();
            using (new EditorGUI.DisabledScope(result == null))
            {
                if (GUILayout.Button("Save displayed world as asset...")) SaveWorld();
                if (GUILayout.Button("Export displayed world...")) Export();
            }
            EditorGUILayout.HelpBox("LandAndWater shows ocean, lakes, and land. Naval Access adds full-width navigable rivers in the ocean colour.", MessageType.Info);
        }

        private void DrawClimateSettings()
        {
            EditorGUILayout.LabelField("CLIMATE", EditorStyles.boldLabel);
            climateSettings.Equator = EditorGUILayout.Slider("Equator from south (%)", climateSettings.Equator * 100f, 0f, 100f) / 100f;
            climateSettings.ElevationCooling = EditorGUILayout.Slider("Elevation cooling (C)", climateSettings.ElevationCooling, 0f, 50f);
            climateSettings.MoistureReach = EditorGUILayout.Slider("Coastal moisture reach (%)", climateSettings.MoistureReach * 100f, 5f, 100f) / 100f;
            climateSettings.Wind = (PrevailingWind)EditorGUILayout.EnumPopup("Prevailing wind", climateSettings.Wind);
            using (new EditorGUI.DisabledScope(result == null))
            {
                if (GUILayout.Button("Generate Climate")) GenerateClimate();
                if (GUILayout.Button("Generate Biomes"))
                {
                    try
                    {
                        SetPreview(result, rivers, HeightmapPreviewMode.Biomes);
                        status = "Biomes generated. Export the world to get the OpenGS Terrain image.";
                    }
                    catch (Exception ex) { ShowError(ex); }
                }
            }
        }

        // Build the climate and biome preview layers for the current terrain.
        private void GenerateClimate()
        {
            try
            {
                var next = ClimateGenerator.Generate(result, water, climateSettings,
                    (fraction, label) => !EditorUtility.DisplayCancelableProgressBar("Generating climate", label, fraction));
                var mode = HeightmapPreview.IsClimateMode(previewMode) ? previewMode : HeightmapPreviewMode.Temperature;
                SetPreview(result, rivers, mode, next);
                status = "Climate generated. Use Temperature or Moisture to inspect it. Save or export to keep this climate.";
            }
            catch (OperationCanceledException) { status = "Climate generation cancelled."; }
            catch (Exception ex) { ShowError(ex); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        private void DrawRiverSettings()
        {
            EditorGUILayout.LabelField("RIVERS", EditorStyles.boldLabel);
            riverCatchment = EditorGUILayout.Slider(new GUIContent("Catchment (%)", "Smaller thresholds generate more tributaries."),
                riverCatchment, RiverGenerator.MinimumCatchmentPercent, RiverGenerator.MaximumCatchmentPercent);
            riverDepth = EditorGUILayout.Slider(new GUIContent("Channel depth", "Typical cut in normalized height units. Saddles may need deeper cuts."),
                riverDepth, RiverGenerator.MinimumCarveDepth, RiverGenerator.MaximumCarveDepth);
            riverWidthScale = EditorGUILayout.Slider(new GUIContent("Channel width", "Multiplier for river water and carved banks. Tributaries widen the river downstream."),
                riverWidthScale, RiverGenerator.MinimumWidthScale, RiverGenerator.MaximumWidthScale);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(result == null))
                    if (GUILayout.Button("Generate Rivers")) GenerateRivers();
                using (new EditorGUI.DisabledScope(rivers == null))
                    if (GUILayout.Button("Clear Rivers")) ClearRivers();
            }
            if (rivers != null)
            {
                EditorGUILayout.LabelField($"{rivers.ChannelCells:N0} channel samples", EditorStyles.miniLabel);
                var rise = EditorGUILayout.DelayedFloatField(
                    new GUIContent("Naval rise limit", "Maximum normalized riverbed elevation above sea level."),
                    maximumNavalRise);
                if (!float.IsNaN(rise))
                    SetNavalRise(Mathf.Clamp(rise, 0f, RiverNavigation.MaximumRiseLimit));
                exportNavalMask = EditorGUILayout.Toggle("Export naval mask", exportNavalMask);
                if (GUILayout.Button("Preview Naval Access"))
                {
                    try { SetPreview(result, rivers, HeightmapPreviewMode.NavalAccess); }
                    catch (Exception ex) { ShowError(ex); }
                }
                if (navigation != null)
                    EditorGUILayout.LabelField($"{navigation.ChannelCount:N0} ship-route samples", EditorStyles.miniLabel);
            }
        }

        private void SetNavalRise(float rise)
        {
            if (rise == maximumNavalRise) return;
            var previousRise = maximumNavalRise;
            maximumNavalRise = rise;
            try
            {
                if (previewMode == HeightmapPreviewMode.NavalAccess) SetPreview(result, rivers, previewMode);
            }
            catch (Exception ex)
            {
                maximumNavalRise = previousRise;
                ShowError(ex);
            }
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
                var selectedMode = (HeightmapPreviewMode)EditorGUILayout.EnumPopup(previewMode, EditorStyles.toolbarPopup, GUILayout.Width(150));
                if (selectedMode != previewMode)
                {
                    try
                    {
                        if (result == null) previewMode = selectedMode;
                        else SetPreview(result, rivers, selectedMode);
                    }
                    catch (Exception ex) { ShowError(ex); }
                }
                var oldShow3D = show3D; var oldHeightScale = heightScale;
                show3D = GUILayout.Toggle(show3D, "3D", EditorStyles.toolbarButton, GUILayout.Width(35));
                if (show3D)
                {
                    GUILayout.Label(new GUIContent("Height", "Terrain height scale. Saved with the world and included in exports."), GUILayout.Width(40));
                    heightScale = GUILayout.HorizontalSlider(heightScale, WorldHeightmap.MinimumHeightScale, WorldHeightmap.MaximumHeightScale, GUILayout.Width(95));
                    if (GUILayout.Button("Reset view", EditorStyles.toolbarButton, GUILayout.Width(70))) preview3D?.ResetView();
                }
                else if (preview3D != null) { preview3D.Dispose(); preview3D = null; }
                if (oldShow3D != show3D || !Mathf.Approximately(oldHeightScale, heightScale)) Repaint();
                GUILayout.FlexibleSpace();
                GUILayout.Label(show3D ? "North = +Z" : "North up", EditorStyles.miniLabel);
            }
            var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(.035f, .045f, .055f));
            if (preview != null && show3D)
            {
                try
                {
                    if (preview3D == null) preview3D = new Heightmap3DPreview();
                    preview3D.SetWorld(result, preview, heightScale,
                        previewMode == HeightmapPreviewMode.LandAndWater || previewMode == HeightmapPreviewMode.NavalAccess
                        || HeightmapPreview.IsClimateMode(previewMode) || previewMode == HeightmapPreviewMode.Biomes);
                    if (preview3D.HandleInput(area)) Repaint();
                    preview3D.Draw(area);
                    GUI.Label(new Rect(area.x + 12, area.yMax - 25, area.width - 24, 20), "Drag to orbit | Scroll to zoom | Height is saved with the world", EditorStyles.whiteMiniLabel);
                }
                catch (Exception ex) { show3D = false; ShowError(ex); }
            }
            else if (preview != null)
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
                    var sample = y * result.Recipe.Width + x;
                    var elevation = result.Elevations[sample];
                    var detail = HeightmapPreview.IsClimateMode(previewMode) && climate != null
                        ? $"Temperature {climate.TemperatureCelsius(sample):F1} C | Moisture {climate.Moisture[sample] / 255f:P0}"
                        : $"Elevation {elevation:F4} | {(elevation >= result.Recipe.SeaLevel ? "Land" : "Water")}";
                    if (previewMode == HeightmapPreviewMode.Biomes && biomes != null)
                        detail = $"{biomes[sample]} | {climate.TemperatureCelsius(sample):F1} C | Moisture {climate.Moisture[sample] / 255f:P0}";
                    GUI.Label(new Rect(area.x + 12, area.yMax - 25, area.width - 24, 20), $"X {x} | Y {y} | {detail}", EditorStyles.whiteMiniLabel);
                    if (Event.current.type == EventType.MouseMove) Repaint();
                }
            }
            else GUI.Label(area, "Generate an Old World to preview its elevation.", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
            if (result != null)
                EditorGUILayout.LabelField($"{result.Recipe.Width} x {result.Recipe.Height}   |   Seed {result.Recipe.Seed}   |   Land {result.LandFraction:P1}   |   Elevation {result.Minimum:F3}-{result.Maximum:F3}", EditorStyles.miniLabel);
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
                SetPreview(next, null, previewMode == HeightmapPreviewMode.Rivers ? HeightmapPreviewMode.Relief : previewMode);
                settingsChanged = false;
                status = $"Generated in {timer.Elapsed.TotalSeconds:F2}s. Save an asset to preserve this world across editor reloads.";
            }
            catch (OperationCanceledException) { status = "Generation cancelled. The previous completed world is retained."; }
            catch (Exception ex) { ShowError(ex); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        // Build river routes and carved elevations from the current world before updating the previews.
        private void GenerateRivers()
        {
            try
            {
                var next = RiverGenerator.Generate(rivers?.Base ?? result, riverCatchment, riverDepth,
                    (fraction, label) => !EditorUtility.DisplayCancelableProgressBar("Generating rivers", label, fraction), riverWidthScale);
                if (next.ChannelCells == 0)
                {
                    SetPreview(next.Base, null, HeightmapPreviewMode.Relief);
                    status = "No channels reached the catchment threshold. Try a lower threshold or a world with water outlets.";
                    return;
                }
                SetPreview(next.Terrain, next, HeightmapPreviewMode.Rivers);
                status = $"Carved {next.ChannelCells:N0} river samples. Save or export to retain the carved terrain.";
            }
            catch (OperationCanceledException) { status = "River generation cancelled. The previous terrain is still displayed."; }
            catch (Exception ex) { ShowError(ex); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        private void ClearRivers()
        {
            try
            {
                SetPreview(rivers.Base, null, HeightmapPreviewMode.Relief);
                status = "Base terrain restored.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // Choose which generated layer the editor is displaying.
        private void SetPreview(HeightmapResult terrain, RiverMap network, HeightmapPreviewMode mode,
            ClimateMap climateOverride = null, ClimateSettings settingsOverride = null)
        {
            var nextWater = water != null && ReferenceEquals(water.Source, terrain) ? water : WaterMap.Classify(terrain);
            var nextClimate = climateOverride ?? (ReferenceEquals(climate?.Source, terrain) ? climate : null);
            if ((HeightmapPreview.IsClimateMode(mode) || mode == HeightmapPreviewMode.Biomes) && nextClimate == null)
                nextClimate = ClimateGenerator.Generate(terrain, nextWater, settingsOverride ?? climateSettings);
            var nextBiomes = ReferenceEquals(biomes?.Climate, nextClimate) ? biomes : null;
            if (mode == HeightmapPreviewMode.Biomes && nextBiomes == null)
                nextBiomes = BiomeGenerator.Generate(nextClimate, nextWater, network);
            PublishPreview(terrain, network, mode, nextWater, nextClimate, nextBiomes);
        }

        private void PublishPreview(HeightmapResult terrain, RiverMap network, HeightmapPreviewMode mode,
            WaterMap nextWater, ClimateMap nextClimate, BiomeMap nextBiomes)
        {
            var nextNavigation = mode == HeightmapPreviewMode.NavalAccess && network != null
                ? RiverNavigation.Build(network, nextWater, maximumNavalRise) : null;
            var next = HeightmapPreview.Create(terrain, mode, nextWater, network, nextNavigation, nextClimate,
                biomes: nextBiomes);
            if (preview != null) DestroyImmediate(preview);
            result = terrain;
            rivers = network;
            navigation = nextNavigation;
            climate = nextClimate;
            biomes = nextBiomes;
            previewMode = mode;
            water = nextWater;
            preview = next;
            wantsMouseMove = true;
        }

        // Write a Unity asset that can be assigned to the playable scene.
        private void SaveWorld()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save world heightmap", $"OldWorld-{result.Recipe.Seed}", "asset", "Save the terrain, river data, generation recipe, and height scale.");
            if (string.IsNullOrEmpty(path)) return;
            // New assets only: never silently replace an authored world or its GUID.
            if (File.Exists(path)) { status = "Choose a new filename to preserve the existing world."; return; }
            WorldHeightmap asset = null;
            try
            {
                asset = CreateInstance<WorldHeightmap>(); asset.Initialize(result, heightScale, rivers, climate, biomes);
                AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
                savedWorld = asset; EditorGUIUtility.PingObject(asset); status = "Saved " + path;
            }
            catch (Exception ex) { if (asset != null && !AssetDatabase.Contains(asset)) DestroyImmediate(asset); ShowError(ex); }
        }

        private void LoadWorld()
        {
            try
            {
                var network = savedWorld.ReadRivers();
                var terrain = network?.Terrain ?? savedWorld.Read();
                var nextRecipe = terrain.Recipe.Copy();
                var savedClimate = savedWorld.ReadClimateSettings();
                var mode = savedWorld.HasBiomes ? HeightmapPreviewMode.Biomes
                    : savedClimate != null ? HeightmapPreviewMode.Temperature
                    : network == null ? HeightmapPreviewMode.Relief : HeightmapPreviewMode.Rivers;
                SetPreview(terrain, network, mode, settingsOverride: savedClimate);
                if (savedClimate != null) climateSettings = savedClimate;
                if (network != null)
                {
                    riverCatchment = network.CatchmentPercent;
                    riverDepth = network.CarveDepth;
                    riverWidthScale = network.WidthScale;
                }
                heightScale = savedWorld.HeightScale;
                recipe = nextRecipe;
                selectedStep = 0;
                settingsChanged = false;
                status = "Loaded saved world.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // Export generation settings so the world can be reproduced without relying on the Unity asset.
        private void SaveRecipe()
        {
            var path = EditorUtility.SaveFilePanel("Save world recipe", "", "OldWorld-recipe", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var file = new WorldRecipe
                {
                    Format = WorldRecipe.FormatId,
                    Version = WorldRecipe.CurrentVersion,
                    Recipe = recipe.Copy(),
                    HeightScale = heightScale,
                    BuildRivers = rivers != null,
                    RiverCatchment = riverCatchment,
                    RiverDepth = riverDepth,
                    RiverWidthScale = riverWidthScale,
                    BuildClimate = climate != null,
                    Climate = climateSettings.Copy(),
                    BuildBiomes = biomes != null,
                    MaximumNavalRise = maximumNavalRise,
                    ExportNavalMask = exportNavalMask
                };
                file.Validate();
                File.WriteAllText(path, JsonUtility.ToJson(file, true));
                status = "Saved the current world controls and enabled generation stages.";
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void LoadRecipe()
        {
            var path = EditorUtility.OpenFilePanel("Load and generate world recipe", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (new FileInfo(path).Length > 262144) throw new InvalidDataException("Recipe file exceeds the 256 KiB limit.");
                var file = JsonUtility.FromJson<WorldRecipe>(File.ReadAllText(path));
                if (file == null) throw new InvalidDataException("The recipe file is empty.");
                var next = file.Rebuild((fraction, label) =>
                    !EditorUtility.DisplayCancelableProgressBar("Rebuilding world recipe", label, fraction));
                var mode = next.Biomes != null ? HeightmapPreviewMode.Biomes
                    : next.Climate != null ? HeightmapPreviewMode.Temperature
                    : next.Rivers != null ? HeightmapPreviewMode.Rivers : HeightmapPreviewMode.Relief;

                PublishPreview(next.Terrain, next.Rivers, mode, next.Water, next.Climate, next.Biomes);
                recipe = file.Recipe.Copy();
                if (!file.IsLegacy)
                {
                    heightScale = file.HeightScale;
                    riverCatchment = file.RiverCatchment;
                    riverDepth = file.RiverDepth;
                    riverWidthScale = file.RiverWidthScale;
                    climateSettings = file.Climate.Copy();
                    maximumNavalRise = file.MaximumNavalRise;
                    exportNavalMask = file.ExportNavalMask;
                }
                selectedStep = 0;
                settingsChanged = false;
                status = file.IsLegacy ? "Loaded a terrain-only recipe. Generate rivers and climate, then save a new recipe to retain them."
                    : "Loaded the recipe and rebuilt its saved generation stages.";
            }
            catch (OperationCanceledException) { status = "Recipe generation cancelled. The previous world is still displayed."; }
            catch (Exception ex) { ShowError(ex); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        [Serializable] private sealed class ExportManifest
        {
            public string Format = "GrandStrategy.HeightmapExport";
            public int Version = 2;
            public string Coordinates = "X west to east; Y south to north; row-major. PNG top corresponds to north.";
            public string RawEncoding = "unsigned 16-bit little-endian; no header; first row south; normalized 0-65535";
            public string ElevationUnits = "normalized 0-1, not metres";
            public float TerrainHeightScale;
            public string WaterMask = "OpenGS Land Image. Ocean RGB (5,20,18), lakes (0,255,0), land (255,255,255). The optional naval image adds full-width ship corridors in the ocean colour.";
            public string WaterClassification = "Below-sea-level samples connected to any map edge by four-neighbour paths are ocean. Other submerged samples are lakes. The optional naval image adds navigable river water above sea level and preserves lake pixels.";
            public string Preview = "8-bit PNGs are inspection images; RAW16 and the Unity asset preserve higher precision";
            public HeightmapRecipe Recipe;
            public bool HasBiomes;
            public BiomeExport Biomes;
            public bool HasClimate;
            public ClimateExport Climate;
            public bool HasRivers;
            public RiverExport Rivers;
        }

        [Serializable] private sealed class BiomeExport
        {
            public string Raster = "biomes.raw";
            public string Encoding = "Unsigned byte per sample in RAW16 order. Each byte indexes the legend.";
            public string[] Legend = Enum.GetNames(typeof(BiomeKind));
            public string TerrainImage = "terrain.png";
            public string NavalTerrainImage;
        }

        [Serializable] private sealed class ClimateExport
        {
            public string TemperatureImage = "temperature.png";
            public string MoistureImage = "moisture.png";
            public string Encoding = "Opaque grayscale PNGs, north at top. Temperature 0 to 255 maps linearly to -40 to 45 C. Moisture 0 to 255 maps to the dry-to-wet index.";
            public ClimateSettings Settings;
        }

        [Serializable] private sealed class RiverExport
        {
            public string BaseHeightmap = "heightmap-base.raw";
            public string CutElevations = "river-cuts.f32";
            public string CutEncoding = "Little-endian float32 per sample in RAW16 order. Zero keeps the base sample. Positive values replace it with the normalized cut elevation before RAW16 quantization.";
            public string Directions = "river-directions.raw";
            public string DirectionEncoding = "One byte per sample, aligned with heightmap.raw. 0=none; 1=E, 2=NE, 3=N, 4=NW, 5=W, 6=SW, 7=S, 8=SE.";
            public int ChannelCells;
            public float CatchmentPercent;
            public float CarveDepth;
            public float WidthScale;
            public string Widths;
            public string WidthEncoding;
            public string NavalLandMask;
            public string NavalAccessRaster;
            public string NavalAccessEncoding;
            public float NavalMaximumRise;
            public int NavalChannelCells;
        }

        // Write the raster layers consumed by OpenGS and the corresponding files needed to import provinces
        // back into Unity.
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
                WritePng(Path.Combine(folder, "land-mask.png"), HeightmapPreviewMode.LandAndWater);
                var manifest = new ExportManifest { Recipe = result.Recipe, TerrainHeightScale = heightScale };
                RiverNavigation exportedAccess = null;
                if (rivers != null)
                {
                    manifest.HasRivers = true;
                    manifest.Rivers = new RiverExport
                    {
                        ChannelCells = rivers.ChannelCells,
                        CatchmentPercent = rivers.CatchmentPercent,
                        CarveDepth = rivers.CarveDepth,
                        WidthScale = rivers.WidthScale
                    };
                    using (var stream = new FileStream(Path.Combine(folder, manifest.Rivers.BaseHeightmap), FileMode.CreateNew))
                        HeightmapEncoding.WriteRaw16(rivers.Base, stream);
                    using (var stream = new FileStream(Path.Combine(folder, manifest.Rivers.CutElevations), FileMode.CreateNew))
                        HeightmapEncoding.WriteRiverCuts(rivers, stream);
                    File.WriteAllBytes(Path.Combine(folder, manifest.Rivers.Directions), rivers.Directions);
                    if (rivers.Widths != null)
                    {
                        manifest.Rivers.Widths = "river-widths.raw";
                        manifest.Rivers.WidthEncoding = "Unsigned byte per sample in RAW16 order. Zero is off-channel. Positive values give the full river width in raster samples.";
                        File.WriteAllBytes(Path.Combine(folder, manifest.Rivers.Widths), rivers.Widths);
                    }
                    WritePng(Path.Combine(folder, "rivers.png"), HeightmapPreviewMode.Rivers);
                    if (exportNavalMask)
                    {
                        var access = navigation ?? RiverNavigation.Build(rivers, water, maximumNavalRise);
                        exportedAccess = access;
                        manifest.Rivers.NavalLandMask = "naval-land-mask.png";
                        manifest.Rivers.NavalAccessRaster = "river-naval-access.raw";
                        manifest.Rivers.NavalAccessEncoding = "One byte per sample in RAW16 order. Bit 0 marks a ship route. Bit 1 marks a mask-only connector. Bit 2 marks the shared river water footprint.";
                        manifest.Rivers.NavalMaximumRise = maximumNavalRise;
                        manifest.Rivers.NavalChannelCells = access.ChannelCount;
                        WritePng(Path.Combine(folder, manifest.Rivers.NavalLandMask), HeightmapPreviewMode.NavalAccess, access);
                        File.WriteAllBytes(Path.Combine(folder, manifest.Rivers.NavalAccessRaster), access.CopyCells());
                    }
                }
                if (climate != null)
                {
                    manifest.HasClimate = true;
                    manifest.Climate = new ClimateExport { Settings = climate.Settings };
                    WritePng(Path.Combine(folder, manifest.Climate.TemperatureImage), HeightmapPreviewMode.Temperature, dataColors: true);
                    WritePng(Path.Combine(folder, manifest.Climate.MoistureImage), HeightmapPreviewMode.Moisture, dataColors: true);
                }
                if (biomes != null)
                {
                    manifest.HasBiomes = true;
                    manifest.Biomes = new BiomeExport();
                    File.WriteAllBytes(Path.Combine(folder, manifest.Biomes.Raster), biomes.Samples);
                    WritePng(Path.Combine(folder, manifest.Biomes.TerrainImage), HeightmapPreviewMode.Biomes, dataColors: true);
                    if (exportedAccess != null)
                    {
                        manifest.Biomes.NavalTerrainImage = "terrain-naval.png";
                        WritePng(Path.Combine(folder, manifest.Biomes.NavalTerrainImage), HeightmapPreviewMode.Biomes,
                            exportedAccess, dataColors: true);
                    }
                }
                File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonUtility.ToJson(manifest, true));
                status = "Exported to " + folder;
                EditorUtility.RevealInFinder(folder);
            }
            catch (Exception ex) { status = "Export incomplete at " + folder + ". " + ex.Message; Debug.LogException(ex); }
        }

        private void WritePng(string path, HeightmapPreviewMode mode, RiverNavigation access = null, bool dataColors = false)
        {
            var texture = HeightmapPreview.Create(result, mode, water, rivers, access, climate, dataColors, biomes);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { DestroyImmediate(texture); }
        }

        private void ShowError(Exception ex) { status = ex.Message; Debug.LogException(ex); }
    }
}
