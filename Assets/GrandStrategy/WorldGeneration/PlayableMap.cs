using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GrandStrategy.WorldGeneration
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class PlayableMap : MonoBehaviour
    {
        [SerializeField] private WorldHeightmap world;
        [SerializeField] private ProvinceMapAsset provinceMap;
        [SerializeField] private MapTreeCatalog treeCatalog;

        private static readonly string[] ColourNames = { "Blue", "Red", "Green", "Violet", "Ochre" };
        private static readonly Color32[] TribeColours =
        {
            new Color32(48, 161, 230, 255), new Color32(219, 64, 51, 255),
            new Color32(84, 161, 107, 255), new Color32(156, 112, 184, 255),
            new Color32(242, 186, 51, 255)
        };
        private static readonly int[] Speeds = { 1, 2, 4 };
        private const float MinimumZoom = .045f;
        private const float TiltedZoom = .28f;
        private const float TiltedPitch = 48f;
        private const float ZoomResponse = 12f;
        private const float CampaignDaysPerSecond = .25f;

        private Camera mapCamera;
        private PlayableMapSurface surface;
        private Vector3 focus;
        private float zoom = 1f;
        private float targetZoom = 1f;
        private bool dragging;
        private MapViewMode mapView = MapViewMode.Provinces;
        private bool editingName;
        private int selected = -1;
        private int selectedCell = -1;
        private int clickedCell = -1;
        private CampaignState campaign;
        private string mapKey;
        private string savePath;
        private bool hasSave;
        private string saveStatus;
        private Vector3 settlement;
        private string tribeName = "My tribe";
        private string failure;
        private int colourIndex;
        private string provinceDetails;
        private Vector2 detailsScroll;
        private Vector2 campaignScroll;

        private void Start()
        {
            try
            {
                if (world == null || provinceMap == null)
                    throw new InvalidOperationException("Assign a World asset and its matching Province Map asset.");
                mapCamera = GetComponent<Camera>();
                // The map is anchored at the scene origin, independently of the camera.
                surface = new PlayableMapSurface(world, provinceMap, treeCatalog);
                mapKey = CampaignState.MapKey(surface.Map, world.Seed);
                savePath = CampaignSaveFile.PathFor(mapKey);
                hasSave = File.Exists(savePath);
                mapCamera.orthographic = true;
                mapCamera.clearFlags = CameraClearFlags.SolidColor;
                mapCamera.backgroundColor = new Color(.045f, .058f, .075f);
                mapCamera.nearClipPlane = .01f;
                mapCamera.farClipPlane = 2f * (surface.Aspect + 4f);
                UpdateCamera(0f);
            }
            catch (Exception error)
            {
                failure = error.Message;
                Debug.LogException(error, this);
            }
        }

        private void OnDestroy() => surface?.Dispose();

        private void Update()
        {
            if (surface == null || failure != null) return;
            var deltaTime = Time.unscaledDeltaTime;
            if (campaign != null)
            {
                var hadExpanded = campaign.FirstExpansion != ExpansionApproach.None;
                if (campaign.Advance(deltaTime * CampaignDaysPerSecond))
                    OnProvinceJoined(hadExpanded);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && !editingName)
            {
                if (keyboard.homeKey.wasPressedThisFrame)
                {
                    focus = Vector3.zero;
                    targetZoom = 1f;
                }

                var movement = new Vector3(
                    Axis(keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
                        keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed),
                    0f,
                    Axis(keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
                        keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed));
                focus += Vector3.ClampMagnitude(movement, 1f) * mapCamera.orthographicSize * deltaTime;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var position = mouse.position.ReadValue();
                var guiPosition = new Vector2(position.x, Screen.height - position.y);
                var overPanel = TopBar.Contains(guiPosition) || Panel.Contains(guiPosition)
                    || CampaignPanel.Contains(guiPosition) || MapDock.Contains(guiPosition);
                if (mouse.middleButton.wasPressedThisFrame) dragging = !overPanel;
                if (!mouse.middleButton.isPressed) dragging = false;
                if (dragging)
                {
                    var delta = mouse.delta.ReadValue();
                    var units = 2f * mapCamera.orthographicSize / Mathf.Max(1, Screen.height);
                    var groundProjection = mapCamera.transform.up.z;
                    focus -= new Vector3(delta.x, 0f, delta.y / groundProjection) * units;
                }
                var scroll = mouse.scroll.ReadValue().y;
                if (!overPanel && scroll != 0f)
                {
                    var step = Mathf.Clamp(Mathf.Abs(scroll) * .0015f, .045f, .25f);
                    targetZoom = Mathf.Clamp(targetZoom * Mathf.Exp(-Mathf.Sign(scroll) * step), MinimumZoom, 1f);
                }
                UpdateCamera(deltaTime);
                if (!overPanel && !dragging && mouse.leftButton.wasPressedThisFrame)
                    Pick(position);
            }
            else
            {
                dragging = false;
                UpdateCamera(deltaTime);
            }
        }

        private static int Axis(bool negative, bool positive) => (positive ? 1 : 0) - (negative ? 1 : 0);

        private void UpdateCamera(float deltaTime)
        {
            zoom = Mathf.Lerp(zoom, targetZoom, 1f - Mathf.Exp(-ZoomResponse * deltaTime));
            if (Mathf.Abs(zoom - targetZoom) < .0001f) zoom = targetZoom;

            var tilt = 1f - Mathf.InverseLerp(TiltedZoom, 1f, zoom);
            var pitch = Mathf.SmoothStep(90f, TiltedPitch, tilt);
            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            var groundProjection = Mathf.Sin(pitch * Mathf.Deg2Rad);

            var mapAspect = surface.Aspect;
            var viewAspect = Mathf.Max(.001f, mapCamera.aspect);
            // At full zoom-out the padded fit covers both map axes, including in a tall Game view.
            var overviewSize = .54f * Mathf.Max(1f, mapAspect / viewAspect);
            mapCamera.orthographicSize = overviewSize * zoom;
            surface.SetZoom(zoom);

            var halfWidth = mapCamera.orthographicSize * viewAspect;
            var halfDepth = mapCamera.orthographicSize / groundProjection;
            var panX = Mathf.Max(0f, mapAspect * .5f - halfWidth);
            var panZ = Mathf.Max(0f, .5f - halfDepth);
            focus.x = Mathf.Clamp(focus.x, -panX, panX);
            focus.z = Mathf.Clamp(focus.z, -panZ, panZ);

            var distance = mapAspect + 4f;
            mapCamera.transform.SetPositionAndRotation(focus + rotation * Vector3.back * distance, rotation);
        }

        private void Pick(Vector2 position)
        {
            var previous = selected;
            selected = selectedCell = clickedCell = -1;
            editingName = false;
            var ray = mapCamera.ScreenPointToRay(position);
            if (surface.Collider.Raycast(ray, out var hit, mapCamera.farClipPlane))
            {
                clickedCell = surface.CellAt(hit.textureCoord);
                selectedCell = surface.ProvinceCellAt(hit.textureCoord);
                selected = surface.Map.ProvinceAt[selectedCell];
            }

            if (selected != previous)
            {
                detailsScroll = Vector2.zero;
                provinceDetails = selected < 0 ? null : Describe(surface.Map.Provinces[selected]);
            }
            surface.SetSelection(selected);
        }

        private string SelectedTerritoryId()
        {
            var map = surface.Map;
            if (map.HasTerritoryPreview)
            {
                var index = map.TerritoryAt[clickedCell];
                return index >= 0 && index < map.Territories.Length ? map.Territories[index]?.Id : null;
            }

            // Older maps can store territory membership without a separate territory raster.
            return selected >= 0 ? map.Provinces[selected].TerritoryId : null;
        }

        private string Describe(ProvinceInfo province)
        {
            var text = new StringBuilder();
            text.AppendLine("Type: " + (province.Type ?? "Unknown"));
            if (surface.IsNavigableRiverProvince(selected))
                text.AppendLine("Navigable river province (ship access)");
            text.AppendLine("Dominant biome: " + DominantBiome(province));
            if (string.Equals(province.Type, "land", StringComparison.OrdinalIgnoreCase))
            {
                text.AppendLine($"Food yield: +{CampaignState.Fertility(province)} per 5 days before winter");
                text.AppendLine(CampaignState.HasTradeAccess(province) ? "River or coastal trade: cheaper food deal" : "Inland trade: standard price");
                text.AppendLine(CampaignState.IsDefensible(province) ? "Defensible terrain: heavier losses for attackers" : "Open terrain: lighter losses for attackers");
            }
            if (!string.IsNullOrEmpty(province.Terrain)) text.AppendLine("OpenGS terrain: " + province.Terrain);
            text.AppendLine("Neighbours:");
            var neighbours = province.Neighbours ?? Array.Empty<int>();
            if (neighbours.Length == 0) text.Append("None");
            foreach (var index in neighbours)
                if (index >= 0 && index < surface.Map.Provinces.Length)
                    text.Append(surface.Map.Provinces[index].Id).Append("  ");
            return text.ToString();
        }

        private static string DominantBiome(ProvinceInfo province)
        {
            var counts = province.BiomeCounts;
            var best = -1;
            var maximum = 0;
            if (counts != null)
                for (var i = 0; i < counts.Length; i++)
                    if (counts[i] > maximum)
                    {
                        best = i;
                        maximum = counts[i];
                    }
            return best < 0 ? "Unavailable" : ((BiomeKind)best).ToString();
        }

        private void ApplyCampaign(CampaignState next)
        {
            if (!surface.IsDryLand(next.CapitalCell))
                throw new InvalidOperationException("The capital must be on dry land.");
            var capitalPoint = surface.PointAtCell(next.CapitalCell);
            surface.SetCapital(capitalPoint, next.HasGranary, next.FirstExpansion != ExpansionApproach.None);
            surface.SetOwnership(next, TribeColours[next.Colour]);
            campaign = next;
            settlement = capitalPoint;
            editingName = false;
            GUI.FocusControl(null);
        }

        private void SaveCampaign()
        {
            try
            {
                CampaignSaveFile.Write(savePath, campaign.ToSave());
                hasSave = true;
                saveStatus = "Campaign saved.";
            }
            catch (Exception error)
            {
                saveStatus = "Save failed. " + error.Message;
            }
        }

        private void LoadCampaign()
        {
            try
            {
                var save = CampaignSaveFile.Read(savePath);
                ApplyCampaign(CampaignState.Restore(surface.Map, mapKey, save, ColourNames.Length));
                saveStatus = "Campaign loaded and paused.";
            }
            catch (Exception error)
            {
                saveStatus = "Load failed. " + error.Message;
            }
        }

        private void OnGUI()
        {
            if (failure != null)
            {
                GUI.Box(new Rect(12, 12, Mathf.Max(1, Screen.width - 24), 76), GUIContent.none);
                GUI.Label(new Rect(22, 18, Mathf.Max(1, Screen.width - 44), 60),
                    "Map could not load. " + failure, GUI.skin.GetStyle("box"));
                return;
            }
            if (surface == null) return;

            PrepareHudStyles();
            DrawSettlement();
            DrawTopBar();
            DrawMapDock();
            DrawCampaign();
            var panel = Panel;
            DrawPanelFrame(panel);
            GUILayout.BeginArea(new Rect(panel.x + 9, panel.y + 7, Mathf.Max(1, panel.width - 18), Mathf.Max(1, panel.height - 14)));
            GUILayout.Label("Province dossier", hudHeading);
            GUILayout.Label("WASD to pan | Scroll to zoom | Home to reset");
            if (selectedCell >= 0)
            {
                var territory = SelectedTerritoryId();
                GUILayout.Label("Territory: " + (string.IsNullOrEmpty(territory) ? "None" : territory));
            }
            if (selected >= 0)
                DrawProvince(surface.Map.Provinces[selected]);
            else if (selectedCell >= 0)
                GUILayout.Label("No province at this location.");
            else
                GUILayout.Label(campaign == null ? "Choose a land province to establish a tribe." : "Click a province to inspect it.");
            GUILayout.EndArea();
            editingName = GUI.GetNameOfFocusedControl() == "TribeName";
        }

        private void DrawProvince(ProvinceInfo province)
        {
            GUILayout.Space(5);
            GUILayout.Label("Province " + province.Id);
            detailsScroll = GUILayout.BeginScrollView(detailsScroll, GUILayout.Height(115));
            GUILayout.Label(provinceDetails);
            GUILayout.EndScrollView();

            if (campaign != null)
            {
                DrawProvinceCampaign();
                return;
            }
            if (!CampaignState.IsLand(surface.Map, selected))
            {
                GUILayout.Label("Choose a land province to establish a tribe.");
                return;
            }

            GUILayout.Label("Tribe name");
            GUI.SetNextControlName("TribeName");
            tribeName = GUILayout.TextField(tribeName, CampaignState.MaxNameLength);
            if (GUILayout.Button("Tribe colour: " + ColourNames[colourIndex]))
                colourIndex = (colourIndex + 1) % ColourNames.Length;

            var dry = surface.IsDryLand(clickedCell) && surface.IsDryLand(selectedCell);
            if (!dry) GUILayout.Label("Click dry land to place the settlement.");
            else if (Array.FindIndex(province.Neighbours ?? Array.Empty<int>(), i => CampaignState.IsLand(surface.Map, i)) < 0)
                GUILayout.Label("Isolated start: no neighbouring land communities.");
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && dry && !string.IsNullOrWhiteSpace(tribeName);
            if (GUILayout.Button("Establish tribe here"))
            {
                try
                {
                    ApplyCampaign(CampaignState.Found(surface.Map, mapKey, selectedCell, tribeName, colourIndex));
                    saveStatus = "Tribe founded. The campaign is paused.";
                }
                catch (Exception error)
                {
                    saveStatus = "Founding failed. " + error.Message;
                }
            }
            GUI.enabled = wasEnabled;
        }

        private void DrawProvinceCampaign()
        {
            if (!CampaignState.IsLand(surface.Map, selected)) return;

            GUILayout.Label("Pop: " + campaign.Population(selected).ToString("N0"));
            if (campaign.Owns(selected))
            {
                GUILayout.Label(selected == campaign.Capital
                    ? campaign.TribeName + " | Capital" : campaign.TribeName + " | Integrated province");
                return;
            }

            GUILayout.Label("Unclaimed | Independent community");
            if (selected == campaign.IntegrationTarget)
            {
                GUILayout.Label($"{campaign.PendingApproach}: joins in {campaign.IntegrationDaysLeft} days");
                return;
            }
            if (!campaign.CanReachIndependentProvince(selected))
            {
                GUILayout.Label("Integration requires a neighbouring province you own.");
                return;
            }

            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.CanTradeWith(selected);
            if (GUILayout.Button($"Trade for 36 food ({campaign.TradeCost} coin)"))
            {
                campaign.TryTradeForFood(selected);
                saveStatus = "Food delivered. This neighbour is open to a pact.";
            }
            GUI.enabled = wasEnabled;
            DrawExpansionButton(ExpansionApproach.Diplomacy, $"Negotiate ({CampaignState.IntegrationCost} coin)");
            if (campaign.TradePartner == selected)
                DrawExpansionButton(ExpansionApproach.Trade, $"Sign trade pact ({CampaignState.PactCost} coin)");
            if (campaign.HasWarband)
                DrawExpansionButton(ExpansionApproach.Force, $"Use force ({CampaignState.ForceFood} food, casualties)");
        }

        private void DrawExpansionButton(ExpansionApproach approach, string label)
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.CanStartIntegration(selected, approach);
            if (GUILayout.Button(label))
                campaign.TryStartIntegration(selected, approach);
            GUI.enabled = wasEnabled;
        }

        private void DrawCampaign()
        {
            var panel = CampaignPanel;
            DrawPanelFrame(panel);
            GUILayout.BeginArea(new Rect(panel.x + 9, panel.y + 7, Mathf.Max(1, panel.width - 18), Mathf.Max(1, panel.height - 14)));
            campaignScroll = GUILayout.BeginScrollView(campaignScroll);
            GUILayout.Label(campaign == null ? "Campaign" : "Settlement", hudHeading);
            if (campaign != null)
            {
                if (campaign.Day < CampaignState.WinterStart)
                    GUILayout.Label($"First winter in {CampaignState.WinterStart - campaign.Day} days");
                else if (campaign.Day < CampaignState.WinterEnd)
                    GUILayout.Label($"Winter | {CampaignState.WinterEnd - campaign.Day} days to thaw | {campaign.WinterShortfall} food missed");
                else
                    GUILayout.Label(campaign.WinterShortfall == 0 ? "First winter survived without hunger."
                        : $"First winter survived | {campaign.WinterShortfall} food missed");
                if (campaign.WinterFoodRemaining > 0)
                    GUILayout.Label($"Winter stores needed: {campaign.WinterFoodRemaining} food");
                if (campaign.FirstExpansion != ExpansionApproach.None)
                    GUILayout.Label($"First expansion: {campaign.FirstExpansion}");
                if (campaign.Paused && campaign.DaysToNextDecision > 0
                    && GUILayout.Button($"Advance to next milestone ({campaign.DaysToNextDecision} days)"))
                {
                    var hadExpanded = campaign.FirstExpansion != ExpansionApproach.None;
                    if (campaign.AdvanceToNextDecision()) OnProvinceJoined(hadExpanded);
                }
                if (campaign.IntegrationTarget >= 0)
                {
                    var target = surface.Map.Provinces[campaign.IntegrationTarget].Id;
                    GUILayout.Label($"{target} joins in {campaign.IntegrationDaysLeft} days");
                }
                DrawSettlementChoices();
            }

            GUILayout.BeginHorizontal();
            if (campaign != null && GUILayout.Button("Save campaign")) SaveCampaign();
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && hasSave;
            if (GUILayout.Button("Load campaign")) LoadCampaign();
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(saveStatus)) GUILayout.Label(saveStatus);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSettlementChoices()
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.CanBuildGranary;
            if (!campaign.HasGranary && GUILayout.Button($"Build granary ({CampaignState.GranaryWood} wood, {CampaignState.GranaryCoin} coin)"))
            {
                campaign.TryBuildGranary();
                surface.SetCapital(settlement, true, campaign.FirstExpansion != ExpansionApproach.None);
                saveStatus = "Granary built. Winter food use drops from 14 to 8 per five days.";
            }
            GUI.enabled = wasEnabled && campaign.CanRecruitWarband;
            if (!campaign.HasWarband && GUILayout.Button($"Recruit warband ({CampaignState.WarbandWood} wood, {CampaignState.WarbandCoin} coin)"))
            {
                campaign.TryRecruitWarband();
                saveStatus = "Warband raised. It uses 1 food per five days.";
            }
            GUI.enabled = wasEnabled;

            var map = surface.Map;
            var neighbours = map.Provinces[campaign.Capital].Neighbours ?? Array.Empty<int>();
            var listed = 0;
            foreach (var neighbour in neighbours)
            {
                if (!campaign.CanReachIndependentProvince(neighbour)) continue;
                if (listed++ == 0) GUILayout.Label("Nearby independent communities");
                if (GUILayout.Button(map.Provinces[neighbour].Id))
                {
                    selected = neighbour;
                    selectedCell = clickedCell = -1;
                    detailsScroll = Vector2.zero;
                    provinceDetails = Describe(map.Provinces[neighbour]);
                    surface.SetSelection(neighbour);
                }
                if (listed == 3) break;
            }
        }

        private void DrawSettlement()
        {
            if (campaign == null) return;
            var point = mapCamera.WorldToScreenPoint(settlement + Vector3.up * .003f);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height)
                return;
            var y = Screen.height - point.y;
            var label = new GUIContent(mapView == MapViewMode.Realm || mapView == MapViewMode.Ownership
                ? campaign.TribeName + " | Capital" : campaign.TribeName);
            var width = GUI.skin.box.CalcSize(label).x + 12f;
            GUI.Box(new Rect(point.x - width * .5f, y - 35f, width, 23f), label);
            GUI.Label(new Rect(point.x - 5f, y - 13f, 14f, 18f), "▼");
        }

        private const float HudMargin = 12f;
        private const float DockHeight = 104f;
        private static readonly Color HudGold = new Color(.83f, .69f, .42f);
        private static readonly Color HudPaper = new Color(.91f, .87f, .77f);
        private static readonly Color HudInk = new Color(.09f, .08f, .075f, .96f);
        private static readonly Color HudWine = new Color(.22f, .09f, .09f, .98f);
        private static readonly Color HudButtonFill = new Color(.17f, .14f, .115f, .98f);
        private static readonly Color HudBorder = new Color(.48f, .38f, .24f);

        private GUIStyle hudHeading;
        private GUIStyle hudTitle;
        private GUIStyle hudLabel;
        private GUIStyle hudValue;
        private GUIStyle hudButton;

        private bool CompactHud => Screen.width < 800;
        private Rect TopBar => new Rect(0, 0, Screen.width, CompactHud ? 84 : 56);
        private float PanelWidth => Mathf.Max(1f, Mathf.Min(325f, (Screen.width - 36f) * .5f));
        private float PanelTop => TopBar.yMax + HudMargin;
        private Rect Panel => new Rect(HudMargin, PanelTop, PanelWidth,
            Mathf.Max(1f, Mathf.Min(selected >= 0 ? 412 : 110, Screen.height - PanelTop - HudMargin)));
        private Rect CampaignPanel => new Rect(Screen.width - PanelWidth - HudMargin, PanelTop, PanelWidth,
            Mathf.Max(1f, Mathf.Min(campaign == null ? 118 : 350, MapDock.yMin - PanelTop - HudMargin)));
        private Rect MapDock
        {
            get
            {
                var width = Mathf.Max(1f, Mathf.Min(340f, Screen.width - HudMargin * 2f));
                return new Rect(Screen.width - HudMargin - width, Screen.height - HudMargin - DockHeight, width, DockHeight);
            }
        }

        private void PrepareHudStyles()
        {
            if (hudTitle != null) return;
            hudHeading = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            hudHeading.normal.textColor = HudGold;
            hudTitle = new GUIStyle(hudHeading) { fontSize = 15, clipping = TextClipping.Clip };
            hudLabel = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            hudLabel.normal.textColor = HudGold;
            hudValue = new GUIStyle(hudLabel) { fontSize = 15, fontStyle = FontStyle.Bold };
            hudValue.normal.textColor = HudPaper;
            hudButton = new GUIStyle(hudLabel) { fontSize = 12, fontStyle = FontStyle.Bold };
            hudButton.normal.textColor = HudPaper;
            hudButton.hover.textColor = HudGold;
            hudButton.active.textColor = HudGold;
        }

        private static void PaintHudRect(Rect rect, Color colour)
        {
            if (Event.current.type != EventType.Repaint) return;
            var previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawPanelFrame(Rect rect)
        {
            PaintHudRect(rect, HudBorder);
            PaintHudRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), HudInk);
        }

        private bool DrawHudButton(Rect rect, string label, bool enabled = true, bool selectedView = false)
        {
            var previous = GUI.enabled;
            var available = previous && enabled;
            var hovered = available && rect.Contains(Event.current.mousePosition);
            PaintHudRect(rect, selectedView || hovered ? HudGold : HudBorder);
            PaintHudRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2),
                selectedView || hovered ? HudWine : HudButtonFill);
            GUI.enabled = available;
            var pressed = GUI.Button(rect, label, hudButton);
            GUI.enabled = previous;
            return pressed;
        }

        private void DrawTopBar()
        {
            var bar = TopBar;
            PaintHudRect(bar, HudInk);
            PaintHudRect(new Rect(0, bar.yMax - 2, bar.width, 2), HudGold);

            var compact = CompactHud;
            var titleWidth = compact ? Mathf.Max(70, bar.width - 180) : 178;
            PaintHudRect(new Rect(0, 0, titleWidth, compact ? 36 : 54), HudWine);
            GUI.Label(new Rect(HudMargin, compact ? 6 : 15, Mathf.Max(1, titleWidth - 18), 26),
                campaign?.TribeName ?? "GrandStrategy", hudTitle);

            var clockWidth = compact ? 166f : 258f;
            var clockX = bar.width - clockWidth - HudMargin;
            var clockY = compact ? 6f : 14f;
            var controlWidth = compact ? 42f : 62f;
            var day = campaign == null ? "Day —" : $"Day {campaign.Day:N0}";
            GUI.Label(new Rect(clockX, clockY, clockWidth - controlWidth * 2 - 8, 26), day, hudValue);
            if (DrawHudButton(new Rect(bar.width - HudMargin - controlWidth * 2 - 4, clockY, controlWidth, 26),
                campaign == null || campaign.Paused ? (compact ? "Play" : "Resume") : "Pause", campaign != null))
                campaign.Paused = !campaign.Paused;
            if (DrawHudButton(new Rect(bar.width - HudMargin - controlWidth, clockY, controlWidth, 26),
                campaign == null ? "1×" : $"{campaign.Speed}×", campaign != null))
                campaign.SetSpeed(Speeds[(Array.IndexOf(Speeds, campaign.Speed) + 1) % Speeds.Length]);

            var resourceX = compact ? HudMargin : titleWidth + 8f;
            var available = compact ? bar.width - HudMargin * 2f : clockX - resourceX - 8f;
            var slot = Mathf.Max(1f, Mathf.Min(116f, available / 4f));
            var resourceY = compact ? 38f : 8f;
            DrawResource("Coin", campaign?.Coin, resourceX, resourceY, slot);
            DrawResource("Food", campaign?.Food, resourceX + slot, resourceY, slot);
            DrawResource("Wood", campaign?.Wood, resourceX + slot * 2, resourceY, slot);
            DrawResource("Population", campaign == null ? (int?)null : campaign.Population(campaign.Capital),
                resourceX + slot * 3, resourceY, slot);
        }

        private void DrawResource(string label, int? value, float x, float y, float width)
        {
            PaintHudRect(new Rect(x + width - 1, y + 2, 1, 32), HudBorder);
            GUI.Label(new Rect(x, y, width, 16), label, hudLabel);
            GUI.Label(new Rect(x, y + 15, width, 22), value.HasValue ? value.Value.ToString("N0") : "—", hudValue);
        }

        private void DrawMapDock()
        {
            var dock = MapDock;
            DrawPanelFrame(dock);
            GUI.Label(new Rect(dock.x + 8, dock.y + 3, dock.width - 16, 19), "Map views", hudHeading);
            var slot = (dock.width - 24) / 3f;
            var x = dock.x + 7;
            var y = dock.y + 23;
            DrawMapViewButton("Terrain", MapViewMode.Terrain, x, y, slot);
            DrawMapViewButton("Biomes", MapViewMode.Biomes, x + slot + 4, y, slot);
            DrawMapViewButton("Provinces", MapViewMode.Provinces, x + (slot + 4) * 2, y, slot);
            DrawMapViewButton("Territories", MapViewMode.Territories, x, y + 26, slot, surface.HasTerritories);
            DrawMapViewButton("Realm", MapViewMode.Realm, x + slot + 4, y + 26, slot);
            DrawMapViewButton("Ownership", MapViewMode.Ownership, x + (slot + 4) * 2, y + 26, slot);

            var status = mapView switch
            {
                MapViewMode.Realm => "Province and territory borders",
                MapViewMode.Ownership => campaign == null ? "No provinces held" : "Your colour marks provinces held",
                MapViewMode.Biomes => selected >= 0 ? "Dominant biome: " + DominantBiome(surface.Map.Provinces[selected])
                    : "Click a province to inspect its biome",
                MapViewMode.Territories => "Land, sea & navigable river territories",
                MapViewMode.Provinces => "Land, sea & navigable river provinces",
                _ => "Terrain and settlement"
            };
            GUI.Label(new Rect(dock.x + 6, dock.y + 78, dock.width - 12, 21), status, hudLabel);
        }

        private void DrawMapViewButton(string label, MapViewMode mode, float x, float y, float width, bool available = true)
        {
            if (DrawHudButton(new Rect(x, y, width, 23), label, available, mapView == mode) && mapView != mode)
            {
                surface.SetView(mode);
                mapView = mode;
            }
        }

        private void OnProvinceJoined(bool hadExpanded)
        {
            surface.SetOwnership(campaign, TribeColours[campaign.Colour]);
            if (!hadExpanded) surface.SetCapital(settlement, campaign.HasGranary, true);
        }
    }
}
