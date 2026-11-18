// Controls the playable scene. It links input, camera and HUD to CampaignState for rules and
// PlayableMapSurface for rendering and picking.
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
        // Native brush sizes are in scene units. Runtime height and paint use normalized map coordinates.
        public const float TerrainAuthoringScale = 1000f;

        [SerializeField] private WorldHeightmap world;
        [SerializeField] private ProvinceMapAsset provinceMap;
        [SerializeField] private MapTreeCatalog treeCatalog;
        [SerializeField, HideInInspector] private Terrain authoringTerrain;
        [SerializeField, HideInInspector] private WorldHeightmap authoringWorld;
        [SerializeField, HideInInspector] private int authoredMeshDetail = 1024;
        private bool hidAuthoringTerrain;

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
        private bool followingWarband;
        private SettlementBuilding resourceFilter = SettlementBuilding.Farm;
        private MapViewMode mapView = MapViewMode.Provinces;
        private bool editingName;
        private bool showProvinceSettlements;
        private bool showHappinessDetails;
        private bool showCampaignMilestones;
        private bool showClanSkills;
        private bool showIronSupplyRoute;
        private System.Collections.Generic.List<int> plannedMarch;
        private int plannedMarchDestination = -1;
        private int selected = -1;
        private int selectedCell = -1;
        private int clickedCell = -1;
        private Vector2 clickedMapUv;
        private float previewHalfWidth;
        private Color previewColour;
        private bool pointerOverProvincePanel;
        private CampaignState campaign;
        private string mapKey;
        private string savePath;
        private bool hasSave;
        private string saveStatus;
        private Vector3 settlement;
        private ClanTradition foundingTradition;
        private string tribeName = "My tribe";
        private string failure;
        private int colourIndex;
        private string provinceDetails;
        private Vector2 provinceScroll;
        private bool showProvinceDetails;
        private bool showTrade;
        private bool showTradeContacts;
        private TradeResource tradeGive = TradeResource.Coin;
        private TradeResource tradeWant = TradeResource.Food;
        private string tradeGiveAmount = "20";
        private string tradeWantAmount = "10";
        private Vector2 campaignScroll;

        // Build the playable surface, derive the campaign save path and hand off from the editor Terrain
        // preview to the runtime renderer.
        private void Start()
        {
            try
            {
                if (world == null || provinceMap == null)
                    throw new InvalidOperationException("Assign a World asset and its matching Province Map asset.");
                mapCamera = GetComponent<Camera>();
                // The map is anchored at the scene origin, independently of the camera.
                if (authoringTerrain != null && (authoringWorld != world || authoringTerrain.terrainData == null
                    || transform.IsChildOf(authoringTerrain.transform)))
                    throw new InvalidOperationException("Recreate the scene Terrain for the assigned World asset.");
                surface = new PlayableMapSurface(world, provinceMap, treeCatalog,
                    authoringTerrain == null ? null : authoringTerrain.terrainData, authoredMeshDetail);
                mapKey = CampaignState.MapKey(surface.Map, world.Seed);
                savePath = CampaignSaveFile.PathFor(mapKey);
                hasSave = File.Exists(savePath);
                mapCamera.orthographic = true;
                mapCamera.clearFlags = CameraClearFlags.SolidColor;
                mapCamera.backgroundColor = new Color(.045f, .058f, .075f);
                mapCamera.nearClipPlane = .01f;
                mapCamera.farClipPlane = 2f * (surface.Aspect + 4f);
                UpdateCamera(0f);
                if (authoringTerrain != null && authoringTerrain.gameObject.activeSelf)
                {
                    authoringTerrain.gameObject.SetActive(false);
                    hidAuthoringTerrain = true;
                }
            }
            catch (Exception error)
            {
                failure = error.Message;
                Debug.LogException(error, this);
            }
        }

        private void OnDestroy()
        {
            surface?.Dispose();
            if (hidAuthoringTerrain && authoringTerrain != null)
                authoringTerrain.gameObject.SetActive(true);
        }

        // Advance the simulation using unscaled frame time and route keyboard and mouse input to the camera
        // or map.
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

            surface.ShowWarband(campaign);
            surface.ShowRoadsAndBridges(campaign);
            surface.ShowIndependentVillage(campaign);
            surface.RefreshPassageDisplay();
            surface.RefreshInfluence();
            surface.HighlightInfluenceAt(clickedCell);
            var constructionSite = campaign == null ? -1 : SelectedConstructionSite;
            surface.SetCityLimitsVisible(campaign, selected, constructionSite);
            var showSupply = showIronSupplyRoute && constructionSite >= 0
                && campaign.HasSettlementBuilding(constructionSite, SettlementBuilding.IronMine);
            var supply = showSupply ? campaign.GetIronSupplyRoute(constructionSite) : IronSupplyRoute.None;
            var supplyPath = supply switch
            {
                IronSupplyRoute.Overland => campaign.GetOverlandIronRoute(constructionSite),
                IronSupplyRoute.Waterway => campaign.GetWaterwayIronRoute(constructionSite),
                _ => null
            };
            var journey = showSupply ? campaign.IronShipmentJourney(constructionSite) : default;
            surface.ShowIronSupplyRoute(journey.Path ?? supplyPath,
                (journey.Path != null ? journey.Transport : supply) == IronSupplyRoute.Waterway);
            surface.ShowIronCargo(journey.Path, journey.Progress, journey.Transport == IronSupplyRoute.Waterway);

            var keyboard = Keyboard.current;
            if (keyboard != null && !editingName)
            {
                if (keyboard.homeKey.wasPressedThisFrame)
                {
                    followingWarband = false;
                    focus = Vector3.zero;
                    targetZoom = 1f;
                }

                var movement = new Vector3(
                    Axis(keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
                        keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed),
                    0f,
                    Axis(keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
                        keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed));
                if (movement.sqrMagnitude > 0f) followingWarband = false;
                focus += Vector3.ClampMagnitude(movement, 1f) * mapCamera.orthographicSize * deltaTime;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var position = mouse.position.ReadValue();
                var guiPosition = new Vector2(position.x, Screen.height - position.y);
                var overPanel = TopBar.Contains(guiPosition) || ProvincePanel.Contains(guiPosition)
                    || CampaignPanel.Contains(guiPosition) || MapDock.Contains(guiPosition);
                if (mouse.middleButton.wasPressedThisFrame) dragging = !overPanel;
                if (!mouse.middleButton.isPressed) dragging = false;
                if (dragging)
                {
                    var delta = mouse.delta.ReadValue();
                    if (delta.sqrMagnitude > 0f) followingWarband = false;
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

        // Ease the camera toward the requested zoom, tilt it as the player zooms in and constrain the view
        // to the map.
        private void UpdateCamera(float deltaTime)
        {
            if (followingWarband && campaign?.HasWarband == true)
                focus = Vector3.Lerp(focus, surface.WarbandGroundPosition, 1f - Mathf.Exp(-ZoomResponse * deltaTime));
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

        // Raycast the rendered surface and resolve the clicked position to the corresponding smoothed
        // province cell.
        private void Pick(Vector2 position)
        {
            var previous = selected;
            selected = selectedCell = clickedCell = -1;
            editingName = false;
            var ray = mapCamera.ScreenPointToRay(position);
            if (surface.Collider.Raycast(ray, out var hit, mapCamera.farClipPlane))
            {
                clickedMapUv = hit.textureCoord;
                clickedCell = surface.CellAt(clickedMapUv);
                selectedCell = surface.ProvinceCellAt(hit.textureCoord);
                selected = mapView == MapViewMode.Ownership && surface.IsIndependentTerritory(clickedCell)
                    ? campaign.IndependentVillageRegion : surface.Map.ProvinceAt[selectedCell];
            }

            if (selected != previous || mapView == MapViewMode.Ownership)
            {
                provinceScroll = Vector2.zero;
                showProvinceDetails = showTrade = false;
                provinceDetails = selected < 0 ? null : Describe(surface.Map.Provinces[selected]);
            }
            surface.SetSelection(selected);
            surface.HighlightInfluenceAt(clickedCell);
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

        // Assemble the selected province's geography and economy information for the HUD.
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
                text.AppendLine(CampaignState.HasTradeAccess(province) ? "River or coastal trade: lower trade markup" : "Inland trade: standard markup");
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

        // Update the visible capital and ownership overlay after founding a tribe or loading a save.
        private void ApplyCampaign(CampaignState next)
        {
            if (!surface.IsDryLand(next.CapitalCell))
                throw new InvalidOperationException("The capital must be on dry land.");
            var capitalPoint = surface.PointAtCell(next.CapitalCell);
            surface.SetCapital(capitalPoint, next);
            foreach (var site in next.SettlementCells)
                if (!surface.IsDryLand(site)) throw new InvalidOperationException("Settlements must be on dry land.");
            surface.SetVillages(next);
            surface.SetOwnership(next, TribeColours[next.Colour]);
            campaign = next;
            followingWarband = false;
            plannedMarch = null;
            plannedMarchDestination = -1;
            showTrade = showTradeContacts = false;
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

        // Draw the HUD and panels using Unity's immediate-mode GUI. Button handlers pass decisions to the
        // campaign.
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
            DrawTopBar();
            DrawMapDock();
            DrawCampaign();
            if (Event.current.type == EventType.Repaint)
            {
                previewHalfWidth = 0f;
                pointerOverProvincePanel = ProvincePanel.Contains(Event.current.mousePosition);
            }
            DrawProvincePanel();
            if (Event.current.type == EventType.Repaint)
                surface.ShowBuildFootprint(previewHalfWidth > 0f ? clickedCell : -1, previewHalfWidth, previewColour);
            var focused = GUI.GetNameOfFocusedControl();
            editingName = focused == "TribeName" || focused == "TradeGive" || focused == "TradeWant";
        }

        private void DrawProvincePanel()
        {
            var panel = ProvincePanel;
            DrawPanelFrame(panel);
            GUILayout.BeginArea(new Rect(panel.x + 9, panel.y + 7, Mathf.Max(1, panel.width - 18), Mathf.Max(1, panel.height - 14)));
            provinceScroll = GUILayout.BeginScrollView(provinceScroll);
            if (mapView == MapViewMode.Ownership && campaign != null)
                DrawInfluenceProvince();
            else
            {
                GUILayout.Label(selected >= 0 ? "Province " + surface.Map.Provinces[selected].Id : "Province", hudHeading);
                if (selectedCell >= 0)
                {
                    var territory = SelectedTerritoryId();
                    GUILayout.Label("Territory: " + (string.IsNullOrEmpty(territory) ? "None" : territory));
                }
            }
            if (surface.Map.IsFord(clickedCell)) DrawFordDetails();
            var constructionSite = campaign == null ? -1 : SelectedConstructionSite;
            if (constructionSite >= 0)
                DrawSettlement(constructionSite);
            else if (selected >= 0)
                DrawProvince(surface.Map.Provinces[selected]);
            else if (selectedCell >= 0)
                GUILayout.Label("No province at this location.");
            else
            {
                GUILayout.Label(campaign == null ? "Choose a land province to establish a tribe." : "Click a province to inspect it.");
                GUILayout.Label("WASD to pan | Scroll to zoom | Home to reset");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawFordDetails()
        {
            var holder = campaign?.RivalFordHolder(clickedCell) ?? -1;
            GUILayout.Label(holder >= 0 ? $"Ford held by {surface.Map.Provinces[holder].Id}"
                : campaign?.IsPassageSuspended(clickedCell) == true ? "Community suspended civilian passage"
                : campaign?.IsFordClosed(clickedCell) == true ? "Ford closed to civilian traffic"
                : campaign?.IsFordSecured(clickedCell) == true ? "Secured ford | Open to civilian traffic"
                : campaign?.HasFordPassage(clickedCell) == true ? "Ford | Negotiated civilian passage"
                : "River ford | Warbands can cross here");
            if (campaign == null) return;

            if (campaign.IsFordSecured(clickedCell) && campaign.CanChangeFordAccess(clickedCell))
            {
                var closed = campaign.IsFordClosed(clickedCell);
                var guard = campaign.HasGarrisonAtFord(clickedCell) ? "Garrison" : "Warband";
                if (GUILayout.Button(closed ? $"{guard}: Reopen ford" : $"{guard}: Close ford")
                    && campaign.TryChangeFordAccess(clickedCell))
                {
                    surface.RefreshSecuredFord(clickedCell);
                    saveStatus = closed ? "Ford reopened." : "Ford closed to civilian traffic.";
                }
            }

            if (campaign.HasBridge(clickedCell)) GUILayout.Label("Timber bridge | Faster marches and stronger influence when open");
            else if (campaign.IsFordSecured(clickedCell))
            {
                var enabled = GUI.enabled;
                GUI.enabled = enabled && campaign.CanBuildBridge(clickedCell);
                if (GUILayout.Button($"Build timber bridge ({CampaignState.BridgeWood} wood, {CampaignState.BridgeCoin} coin)")
                    && campaign.TryBuildBridge(clickedCell))
                    saveStatus = "Timber bridge built. Marches and influence cross more easily.";
                GUI.enabled = enabled;
            }

            var partner = campaign.FordPassagePartner(clickedCell);
            if (partner < 0) return;
            GUILayout.Label($"Passage available from {surface.Map.Provinces[partner].Id}");
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.Coin >= CampaignState.FordPassageCoin;
            if (GUILayout.Button($"Negotiate passage ({CampaignState.FordPassageCoin} coin)")
                && campaign.TryNegotiateFordPassage(clickedCell))
            {
                surface.SetOwnership(campaign, TribeColours[campaign.Colour]);
                surface.RefreshSecuredFord(clickedCell);
                saveStatus = "Civilian passage secured by agreement. Hostilities can suspend it.";
            }
            GUI.enabled = wasEnabled;
        }

        private void DrawSettlement(int site)
        {
            GUILayout.Label($"Settlement population: {campaign.SettlementPopulation(site):N0}");
            GUILayout.Label($"Settlement happiness: {campaign.SettlementHappiness(site)}/100");
            showHappinessDetails = GUILayout.Toggle(showHappinessDetails, "Show happiness and growth");
            if (showHappinessDetails)
            {
                var factors = campaign.SettlementHappinessBreakdown(site);
                GUILayout.Label($"Base {factors.Base} | {campaign.Taxes} taxes {factors.Tax:+0;-0;0}");
                if (factors.Farm != 0) GUILayout.Label($"Local farm +{factors.Farm}");
                if (factors.Granary != 0) GUILayout.Label($"Capital granary +{factors.Granary}");
                if (factors.Food != 0) GUILayout.Label($"Food shortage {factors.Food}");
                var isolated = campaign.IsFoodIsolated(site);
                var food = isolated ? campaign.LocalFoodStock(site) : campaign.Food;
                var upkeep = isolated ? campaign.LocalFoodUpkeep(site) : campaign.FoodUpkeep;
                GUILayout.Label($"{(isolated ? "Isolated village food stock" : "Clan food stock")} {food:N0} | Next upkeep {upkeep:N0}");
                if (isolated && campaign.LocalFoodShortfall(site) > 0)
                    GUILayout.Label($"Last meal shortfall: {campaign.LocalFoodShortfall(site):N0} food");
                GUILayout.Label(food > upkeep ? "Surplus food supports growth at current stock."
                    : food == upkeep ? "Current stock covers upkeep with no surplus for growth."
                    : "Current stock falls short of upkeep. Population may decline.");
                if (site != campaign.CapitalCell) DrawLocalFoodReserve(site, isolated);
            }
            DrawTownUpgrade(site);
            DrawSettlementGarrison(site);
            DrawSettlementRoad(site);
            DrawConstruction(site);
            if (site == campaign.CapitalCell) DrawCapitalActions();
        }

        private void DrawLocalFoodReserve(int site, bool isolated)
        {
            const int step = 5;
            var target = campaign.LocalFoodTarget(site);
            GUILayout.Label($"Local reserve {campaign.LocalFoodStock(site):N0}/{target:N0}");
            GUILayout.BeginHorizontal();
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && target > 0;
            if (GUILayout.Button("Reserve −5")) campaign.TrySetLocalFoodTarget(site, Math.Max(0, target - step));
            GUI.enabled = wasEnabled && target < CampaignState.MaxLocalFoodTarget;
            if (GUILayout.Button("Reserve +5"))
                campaign.TrySetLocalFoodTarget(site, Math.Min(CampaignState.MaxLocalFoodTarget, target + step));
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
            if (target > 0)
                GUILayout.Label(isolated ? "Reserve replenishment waits for an open route."
                    : "Filled from clan food after upkeep and protected reserves.");
        }

        private void DrawSettlementRoad(int site)
        {
            if (site == campaign.CapitalCell) return;
            if (campaign.HasRoadToCapital(site))
            {
                GUILayout.Label("Road to the capital | Easier marches and stronger influence");
                return;
            }

            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.Wood >= CampaignState.RoadWood && campaign.Coin >= CampaignState.RoadCoin;
            if (GUILayout.Button($"Build road to capital ({CampaignState.RoadWood} wood, {CampaignState.RoadCoin} coin)"))
                saveStatus = campaign.TryBuildRoad(site) ? "Road opened to the capital."
                    : "No open land route. Secure or negotiate access at the crossing.";
            GUI.enabled = wasEnabled;
        }

        private void DrawSettlementGarrison(int site)
        {
            if (campaign.HasGarrison(site))
            {
                GUILayout.Label($"Garrison | {CampaignState.GarrisonRecruits} guards | 1 food / {CampaignState.FoodInterval} days");
                if (GUILayout.Button("Disband garrison") && campaign.TryDisbandGarrison(site))
                    saveStatus = "Guards returned to the settlement.";
                return;
            }

            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.CanRaiseGarrison(site);
            if (GUILayout.Button($"Raise garrison ({CampaignState.GarrisonCoin} coin, {CampaignState.GarrisonRecruits} people)")
                && campaign.TryRaiseGarrison(site))
                saveStatus = "Garrison raised. It can manage secured fords with a bank inside city limits.";
            GUI.enabled = wasEnabled;
        }

        private void DrawTownUpgrade(int site)
        {
            if (campaign.IsTown(site))
            {
                GUILayout.Label("Town | Expanded city limits");
                DrawTownSpecialisation(site);
                return;
            }

            var enoughPeople = campaign.SettlementPopulation(site) >= CampaignState.TownPopulation;
            var hasRoom = surface.CanExpandTown(campaign, site);
            if (!enoughPeople) GUILayout.Label($"Town upgrade requires {CampaignState.TownPopulation} people.");
            else if (!hasRoom) GUILayout.Label("A nearby settlement limits expansion.");
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && enoughPeople && hasRoom
                && campaign.Coin >= CampaignState.TownCoin && campaign.Wood >= campaign.TownWoodCost;
            if (GUILayout.Button($"Upgrade to town ({CampaignState.TownCoin} coin, {campaign.TownWoodCost} wood)")
                && campaign.TryUpgradeTown(site))
            {
                if (site == campaign.CapitalCell) surface.SetCapital(settlement, campaign);
                else surface.RefreshVillage(campaign, site);
            }
            GUI.enabled = wasEnabled;
        }

        private void DrawTownSpecialisation(int site)
        {
            var selectedRole = campaign.TownSpeciality(site);
            if (selectedRole != 0)
            {
                GUILayout.Label(TownRoleLabel(selectedRole));
                return;
            }

            GUILayout.Label("Choose one permanent specialisation");
            foreach (var building in new[] { SettlementBuilding.Farm, SettlementBuilding.LumberCamp,
                SettlementBuilding.IronMine, SettlementBuilding.Docks })
                if (campaign.HasSettlementBuilding(site, building) && GUILayout.Button(TownRoleLabel(building)))
                {
                    campaign.TrySpecialiseTown(site, building);
                    break;
                }
        }

        private static string TownRoleLabel(SettlementBuilding building) => building switch
        {
            SettlementBuilding.Farm => "Farming town | +1 food per production cycle",
            SettlementBuilding.LumberCamp => "Timber town | +1 wood per production cycle",
            SettlementBuilding.IronMine => "Mining town | +1 iron per production cycle",
            SettlementBuilding.Docks => "Port town | +1 trade agreement slot",
            _ => throw new ArgumentOutOfRangeException(nameof(building))
        };

        private void DrawInfluenceProvince()
        {
            var root = surface.InfluenceProvinceAt(clickedCell);
            if (root < 0)
            {
                GUILayout.Label(clickedCell < 0 ? "Select a province"
                    : surface.IsMilitaryOccupied(clickedCell) ? "Under military occupation"
                    : surface.IsIndependentTerritory(clickedCell) ? "Independent village territory"
                    : "Outside settlement influence", hudHeading);
                return;
            }

            var number = 1;
            var count = 0;
            long population = 0;
            SettlementBuilding reachableSites = 0;
            if (root == campaign.CapitalCell)
            {
                count++;
                population += campaign.SettlementPopulation(root);
                reachableSites |= surface.SurveyProductionSites(root, campaign);
            }

            var nextNumber = 1;
            for (var i = 0; i < campaign.SettlementCells.Count; i++)
            {
                var site = campaign.SettlementCells[i];
                var province = campaign.SettlementProvinceRoots[i];
                if (site == province)
                {
                    nextNumber++;
                    if (root == site) number = nextNumber;
                }
                if (province != root) continue;
                count++;
                population += campaign.SettlementPopulation(site);
                reachableSites |= surface.SurveyProductionSites(site, campaign);
            }

            GUILayout.Label(root == campaign.CapitalCell ? "Capital province" : $"Province {number}", hudHeading);
            GUILayout.Label($"{campaign.TribeName} | {count} {(count == 1 ? "settlement" : "settlements")} | {population:N0} people");
            var claimSites = surface.InfluenceProvinceResources(root);
            GUILayout.Label($"Province terrain: {(claimSites == 0 ? "No production sites" : DescribeProductionSites(claimSites))}");
            GUILayout.Label($"Within city limits: {(reachableSites == 0 ? "No production sites" : DescribeProductionSites(reachableSites))}");
            if (selected >= 0)
                GUILayout.Label($"Local economy region: {surface.Map.Provinces[selected].Id}");

            showProvinceSettlements = GUILayout.Toggle(showProvinceSettlements, "Show settlements");
            if (!showProvinceSettlements) return;
            if (root == campaign.CapitalCell) DrawSettlementLink(root, "Capital");
            for (var i = 0; i < campaign.SettlementCells.Count; i++)
                if (campaign.SettlementProvinceRoots[i] == root)
                    DrawSettlementLink(campaign.SettlementCells[i], $"Village {i + 1}");
        }

        private void DrawSettlementLink(int cell, string label)
        {
            if (GUILayout.Button($"{label} | {campaign.SettlementPopulation(cell):N0} people")) FocusMapCell(cell);
        }

        private void FocusMapCell(int cell)
        {
            var map = surface.Map;
            clickedCell = selectedCell = cell;
            clickedMapUv = new Vector2((cell % map.Width + .5f) / map.Width, (cell / map.Width + .5f) / map.Height);
            selected = map.ProvinceAt[cell];
            followingWarband = false;
            focus = surface.PointAtCell(cell);
            targetZoom = MinimumZoom;
            showProvinceDetails = showTrade = false;
            provinceScroll = Vector2.zero;
            provinceDetails = selected >= 0 ? Describe(map.Provinces[selected]) : null;
            surface.SetSelection(selected);
            surface.HighlightInfluenceAt(cell);
        }

        private void DrawProvince(ProvinceInfo province)
        {
            showProvinceDetails = GUILayout.Toggle(showProvinceDetails, "Terrain and neighbours");
            if (showProvinceDetails) GUILayout.Label(provinceDetails);

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

            var traditionName = foundingTradition switch
            {
                ClanTradition.River => "River Clan",
                ClanTradition.Highland => "Highland Clan",
                _ => "Standard"
            };
            if (GUILayout.Button($"Tradition: {traditionName}"))
                foundingTradition = foundingTradition switch
                {
                    ClanTradition.Standard => ClanTradition.River,
                    ClanTradition.River => ClanTradition.Highland,
                    _ => ClanTradition.Standard
                };
            if (foundingTradition == ClanTradition.River)
                GUILayout.Label("Start with 10 extra wood and 20 less coin. Docks cost 10 less wood.");
            else if (foundingTradition == ClanTradition.Highland)
                GUILayout.Label("Start with 20 extra coin and 10 less wood. Warbands cost 10 less coin and wood.");
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
                    ApplyCampaign(CampaignState.Found(surface.Map, mapKey, selectedCell, tribeName, colourIndex, localCapitalClaim: true, tradition: foundingTradition));
                    saveStatus = "Tribe founded. The campaign is paused.";
                }
                catch (Exception error)
                {
                    saveStatus = "Founding failed. " + error.Message;
                }
            }
            GUI.enabled = wasEnabled;
        }

        // Show construction for owned land, or trade and integration actions for reachable independent
        // provinces.
        private void DrawProvinceCampaign()
        {
            if (!CampaignState.IsLand(surface.Map, selected)) return;

            var population = campaign.Owns(selected) ? campaign.Population(selected) : campaign.IndependentPopulation(selected);
            GUILayout.Label($"Population: {population:N0}");
            if (campaign.Owns(selected))
            {
                GUILayout.Label(selected == campaign.Capital
                    ? campaign.TribeName + " | Capital" : campaign.TribeName + " | Integrated province");
                GUILayout.Label($"Subsistence harvest: +{campaign.SubsistenceFood(selected)} food / {CampaignState.ProductionInterval} days outside winter");
                if (selected == campaign.Capital || HasVillageHere)
                    GUILayout.Label("Select dry land within a settlement's gold city limits to see its buildings.");
                else
                    GUILayout.Label("Construction requires a settlement.");
                DrawFoundSettlement();
                if (selected == campaign.Capital) DrawCapitalActions();
                return;
            }

            if (selected == campaign.IndependentVillageRegion)
            {
                GUILayout.Label(campaign.IndependentVillageOccupied ? "Independent village occupied | Growth and development paused"
                    : campaign.IndependentFarmCell >= 0 ? "Independent farming village | Larger food market"
                    : "Independent village | Population growing");
                if (campaign.IndependentVillageGuarded)
                    GUILayout.Label("Village guards | Hold the objective for 3 consecutive days to capture it");
            }
            GUILayout.Label(population == 0 ? "No independent community here"
                : selected == campaign.Capital || HasVillageHere ? "Clan settlement nearby | Independent community"
                : "Independent community");
            DrawFoundSettlement();
            if (population == 0) return;
            if (campaign.CanTradeWith(selected))
            {
                showTrade = GUILayout.Toggle(showTrade, "Trade with this community");
                if (showTrade) DrawTrade();
            }
            if (!campaign.CanReachIndependentProvince(selected))
            {
                GUILayout.Label("Settlement influence must reach this region for land trade or integration.");
                return;
            }
            if (selected == campaign.IntegrationTarget)
            {
                GUILayout.Label(campaign.WarbandCommitted
                    ? $"Military operation resolves in {campaign.IntegrationDaysLeft} days"
                    : $"{campaign.PendingApproach}: joins in {campaign.IntegrationDaysLeft} days");
                return;
            }
            DrawExpansionButton(ExpansionApproach.Diplomacy, $"Negotiate ({campaign.NegotiationCoinCost} coin)");
            if (campaign.HasTradedWith(selected))
                DrawExpansionButton(ExpansionApproach.Trade, $"Sign trade pact ({CampaignState.PactCost} coin)");
            if (campaign.HasWarband)
            {
                if (!campaign.WarbandAt(selected))
                    GUILayout.Label("March the warband into this region to use force.");
                DrawExpansionButton(ExpansionApproach.Force, $"Use force ({CampaignState.ForceFood} food, casualties)");
            }
        }

        // Build an offer from the player's inputs and display its quote before asking the campaign to
        // execute or schedule it.
        private void DrawTrade()
        {
            DrawTradeLine("Offer", "TradeGive", ref tradeGive, ref tradeGiveAmount);
            DrawTradeLine("Request", "TradeWant", ref tradeWant, ref tradeWantAmount);
            int.TryParse(tradeGiveAmount, out var give);
            int.TryParse(tradeWantAmount, out var want);
            var offer = new TradeOffer(tradeGive, give, tradeWant, want);
            var quote = campaign.EvaluateTrade(selected, offer);

            var meter = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            PaintHudRect(meter, HudButtonFill);
            PaintHudRect(new Rect(meter.x, meter.y, meter.width * quote.Willingness / 100f, meter.height),
                quote.Willingness == 100 ? new Color(.22f, .36f, .23f) : HudWine);
            GUI.Label(meter, $"Willingness {quote.Willingness}%", hudLabel);
            GUILayout.Label($"Available: {quote.Stock:N0} {tradeWant.ToString().ToLowerInvariant()}");
            GUILayout.Label(quote.Status ?? "This neighbour accepts the exchange.");

            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && quote.Status == null;
            if (GUILayout.Button("Complete trade") && campaign.TryTrade(selected, offer))
                saveStatus = $"Traded {give} {tradeGive.ToString().ToLowerInvariant()} for {want} {tradeWant.ToString().ToLowerInvariant()}.";
            var agreementIssue = campaign.AgreementIssue(selected, offer);
            GUI.enabled = wasEnabled && agreementIssue == null;
            if (GUILayout.Button("Repeat every 15 days") && campaign.TryStartTradeAgreement(selected, offer))
                saveStatus = "Agreement signed. First shipment on the next 15-day cycle.";
            GUI.enabled = wasEnabled;
            if (quote.Status == null && agreementIssue != null) GUILayout.Label(agreementIssue);
        }

        private static void DrawTradeLine(string label, string control, ref TradeResource resource, ref string amount)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(58));
            GUI.SetNextControlName(control);
            amount = GUILayout.TextField(amount, 3, GUILayout.Width(48));
            if (GUILayout.Button(resource.ToString() + " ›"))
                resource = (TradeResource)(((int)resource + 1) % ((int)TradeResource.Iron + 1));
            GUILayout.EndHorizontal();
        }

        private void DrawExpansionButton(ExpansionApproach approach, string label)
        {
            var wasEnabled = GUI.enabled;
            var existing = campaign.IntegrationCommunitySite(selected);
            var needsSite = campaign.UsesLocalIntegration && existing < 0;
            var validSite = !needsSite || (clickedCell >= 0 && campaign.SettlementProvince(clickedCell) == selected
                && surface.CanSiteVillage(campaign, clickedCell));
            GUI.enabled = wasEnabled && validSite && campaign.CanStartIntegration(selected, approach);
            if (GUILayout.Button(label))
            {
                var root = surface.InfluenceProvinceAt(needsSite ? clickedCell : existing);
                campaign.TryStartIntegration(selected, approach, needsSite ? clickedCell : existing, root);
            }
            if (!validSite && approach == ExpansionApproach.Diplomacy)
                GUILayout.Label("Select clear ground for the community's settlement.");

            GUI.enabled = wasEnabled;
        }

        // Show seasonal milestones, food and tax information, trade contacts and save controls.
        private void DrawCampaign()
        {
            var panel = CampaignPanel;
            DrawPanelFrame(panel);
            GUILayout.BeginArea(new Rect(panel.x + 9, panel.y + 7, Mathf.Max(1, panel.width - 18), Mathf.Max(1, panel.height - 14)));
            campaignScroll = GUILayout.BeginScrollView(campaignScroll);
            GUILayout.Label("Campaign", hudHeading);
            if (campaign?.Tradition == ClanTradition.River)
                GUILayout.Label("River Clan | Docks cost 10 less wood");
            else if (campaign?.Tradition == ClanTradition.Highland)
                GUILayout.Label("Highland Clan | Reduced warband cost");
            if (campaign != null)
            {
                DrawCrossingAlerts();
                if (campaign.Day < CampaignState.WinterStart)
                    GUILayout.Label($"First winter in {CampaignState.WinterStart - campaign.Day} days");
                else if (campaign.Day < CampaignState.WinterEnd)
                    GUILayout.Label($"Winter | {CampaignState.WinterEnd - campaign.Day} days to thaw | {campaign.WinterShortfall} food missed");
                else
                    GUILayout.Label(campaign.WinterShortfall == 0 ? "First winter survived without hunger."
                        : $"First winter survived | {campaign.WinterShortfall} food missed");
                var winterStores = campaign.WinterFoodRemaining;
                if (winterStores > 0)
                    GUILayout.Label($"Estimated winter reserves: {winterStores:N0} food");
                GUILayout.Label($"Food upkeep: {campaign.FoodUpkeep:N0} every {CampaignState.FoodInterval} days");
                if (campaign.LastPopulationChange != 0)
                    GUILayout.Label($"Last food tick: {campaign.LastPopulationChange:+#,0;-#,0} population");
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
                    GUILayout.Label(campaign.WarbandCommitted
                        ? $"{target} | Operation resolves in {campaign.IntegrationDaysLeft} days"
                        : $"{target} joins in {campaign.IntegrationDaysLeft} days");
                }
                DrawWarbandOrders();
                GUILayout.Label("Treasury", hudHeading);
                GUILayout.Label("Tax law");
                GUILayout.BeginHorizontal();
                DrawTaxPolicy(TaxPolicy.Relief);
                DrawTaxPolicy(TaxPolicy.Standard);
                DrawTaxPolicy(TaxPolicy.Heavy);
                GUILayout.EndHorizontal();
                GUILayout.Label($"Tax rate: +{campaign.TaxIncome:N0} coin every {CampaignState.TaxInterval} days");
                GUILayout.Label($"Recorded taxes +{campaign.TaxCollected:N0} | Trade +{campaign.TradeReceipts:N0} / −{campaign.TradeSpending:N0}");
                showCampaignMilestones = GUILayout.Toggle(showCampaignMilestones, "Show campaign milestones");
                if (showCampaignMilestones) DrawCampaignMilestones();
                showClanSkills = GUILayout.Toggle(showClanSkills, $"Clan skills | {campaign.SkillPoints} points");
                if (showClanSkills)
                {
                    GUILayout.Label("Earn one point for each completed campaign milestone.");
                    DrawClanPerk(CampaignPerk.Stewardship, "Stewardship | Villages cost 5 less wood");
                    DrawClanPerk(CampaignPerk.TownPlanning, "Town planning | Upgrades cost 5 less wood");
                    DrawClanPerk(CampaignPerk.Envoys, "Envoys | Negotiation costs 5 less coin");
                    if (!campaign.HasPerk(CampaignPerk.Stewardship))
                        GUILayout.Label("Town planning and Envoys require Stewardship.");
                }
                DrawGovernment();
                DrawTradeAgreements();
                DrawNearbyCommunities();
                showTradeContacts = GUILayout.Toggle(showTradeContacts, "Show all trade contacts");
                if (showTradeContacts)
                    for (var province = 0; province < surface.Map.Provinces.Length; province++)
                        if (campaign.CanTradeWith(province) && GUILayout.Button(surface.Map.Provinces[province].Id))
                            SelectCommunity(province);
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

        // Show each agreement's latest outcome and provide reserve and cancellation controls.
        private void DrawCrossingAlerts()
        {
            var threat = campaign.FrontierFordThreat();
            if (threat.Ford >= 0)
            {
                var rival = surface.Map.Provinces[threat.Region].Id;
                GUILayout.Label($"{rival} threatens an unguarded ford", hudHeading);
                GUILayout.Label($"Possible takeover in {threat.Days} {(threat.Days == 1 ? "day" : "days")}. Station a warband or garrison to protect it.");
                if (GUILayout.Button("View threatened ford")) FocusFord(threat.Ford);
            }

            if (campaign.RivalHeldFords.Count == 0) return;
            GUILayout.Label("Crossings held by rivals", hudHeading);
            foreach (var ford in campaign.RivalHeldFords)
            {
                var holder = surface.Map.Provinces[campaign.RivalFordHolder(ford)].Id;
                if (GUILayout.Button($"View ford held by {holder}")) FocusFord(ford);
                var held = campaign.SuppliesHeldAtFord(ford);
                if (held.Shipments > 0)
                    GUILayout.Label($"{held.Shipments:N0} {(held.Shipments == 1 ? "shipment" : "shipments")} waiting | {held.Cargo:N0} iron");
            }
        }

        private void FocusFord(int ford)
        {
            resourceFilter = 0;
            surface.SetResourceFilter(resourceFilter);
            surface.SetView(MapViewMode.Resources);
            mapView = MapViewMode.Resources;
            FocusMapCell(ford);
        }

        private void DrawCampaignMilestones()
        {
            var deliveries = campaign.CompletedTradeDeliveries;
            var negotiations = campaign.NegotiatedIntegrations;
            var operations = campaign.SuccessfulOperations;
            var fords = campaign.HasEverSecuredFord ? 1 : 0;
            GUILayout.Label(campaign.TradeMilestoneComplete ? "Trade network complete"
                : $"Trade network | {deliveries}/{CampaignState.TradeMilestoneDeliveries} recurring deliveries");
            GUILayout.Label(campaign.DiplomacyMilestoneComplete ? "Regional diplomacy complete"
                : $"Regional diplomacy | {negotiations}/{CampaignState.DiplomacyMilestoneIntegrations} negotiated integrations");
            var target = CampaignState.MilitaryMilestoneOperations;
            GUILayout.Label(campaign.MilitaryMilestoneComplete ? "Military foothold complete"
                : $"Military foothold | {Math.Min(operations, target)}/{target} operations, {Math.Min(fords, 1)}/1 secured ford");
        }

        private void DrawClanPerk(CampaignPerk perk, string label)
        {
            if (campaign.HasPerk(perk))
            {
                GUILayout.Label(label + " | Unlocked");
                return;
            }
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.CanUnlockPerk(perk);
            if (GUILayout.Button(label)) campaign.TryUnlockPerk(perk);
            GUI.enabled = wasEnabled;
        }

        private void DrawGovernment()
        {
            GUILayout.Label(campaign.HasRegionalCouncil ? "Regional council | +1 trade agreement slot" : "Founding clan");
            if (campaign.HasRegionalCouncil)
            {
                GUILayout.Label($"Research {campaign.ResearchPoints} | +1 every {CampaignState.TaxInterval} days");
                DrawResearch(CampaignTechnology.Agronomy, "Agronomy | +1 food per farm");
                DrawResearch(CampaignTechnology.MarchLogistics, "March logistics | Faster warband movement");
                return;
            }
            var issue = campaign.RegionalCouncilIssue;
            if (issue != null) GUILayout.Label(issue);
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && issue == null;
            if (GUILayout.Button($"Establish regional council ({CampaignState.RegionalCouncilCoin} coin)")
                && campaign.TryEstablishRegionalCouncil())
                saveStatus = "Regional council established. One additional trade agreement is available.";
            GUI.enabled = wasEnabled;
        }

        private void DrawResearch(CampaignTechnology technology, string label)
        {
            if (campaign.HasTechnology(technology))
            {
                GUILayout.Label(label + " | Researched");
                return;
            }
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.ResearchPoints >= CampaignState.ResearchCost;
            if (GUILayout.Button($"{label} ({CampaignState.ResearchCost} research)"))
                campaign.TryResearch(technology);
            GUI.enabled = wasEnabled;
        }

        private void DrawTradeAgreements()
        {
            var agreements = campaign.TradeAgreements;
            GUILayout.Label($"Trade agreements {agreements.Count}/{campaign.TradeCapacity}", hudHeading);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Food reserve: {campaign.FoodReserve} + next upkeep");
            if (GUILayout.Button("−5", GUILayout.Width(32))) campaign.SetFoodReserve(campaign.FoodReserve - 5);
            if (GUILayout.Button("+5", GUILayout.Width(32))) campaign.SetFoodReserve(campaign.FoodReserve + 5);
            GUILayout.EndHorizontal();
            for (var i = 0; i < agreements.Count; i++)
            {
                var agreement = agreements[i];
                var offer = agreement.Offer;
                GUILayout.Label($"{surface.Map.Provinces[agreement.Province].Id} · {offer.GiveAmount} {offer.Give} for {offer.WantAmount} {offer.Want}");
                GUILayout.Label(agreement.LastAttemptDay == 0 ? "Waiting for the next cycle."
                    : agreement.LastIssue == null ? $"Last shipment on day {agreement.LastAttemptDay}."
                    : $"Paused on day {agreement.LastAttemptDay}. {agreement.LastIssue}");
                if (GUILayout.Button("Cancel agreement"))
                {
                    campaign.CancelTradeAgreement(agreement.Province);
                    break;
                }
            }
        }

        private void DrawWarbandOrders()
        {
            if (!campaign.HasWarband) return;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(followingWarband ? "Stop following" : "Follow warband"))
                followingWarband = !followingWarband;
            if (GUILayout.Button("View troops"))
            {
                followingWarband = true;
                targetZoom = MinimumZoom;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Military supplies | {campaign.WarbandFoodCost} food / {CampaignState.ProductionInterval} days");
            GUILayout.BeginHorizontal();
            foreach (var policy in new[] { WarbandSupplyPolicy.Forage, WarbandSupplyPolicy.Standard, WarbandSupplyPolicy.Provisioned })
            {
                var canSelect = GUI.enabled;
                GUI.enabled = canSelect && campaign.WarbandSupply != policy;
                if (GUILayout.Button(policy.ToString())) campaign.SetWarbandSupply(policy);
                GUI.enabled = canSelect;
            }
            GUILayout.EndHorizontal();
            if (campaign.WarbandCommitted)
            {
                GUILayout.Label("Warband committed | Trade with the target is suspended");
                if (campaign.MilitaryObjectiveCell >= 0)
                {
                    GUILayout.Label(campaign.IndependentVillageOccupied
                        ? $"Objective occupied | {Math.Min(campaign.ObjectiveOccupiedDays, campaign.ObjectiveHoldDaysRequired)}/{campaign.ObjectiveHoldDaysRequired} days held"
                        : $"Objective beyond occupation | Requires {campaign.ObjectiveHoldDaysRequired} consecutive days held");
                    if (GUILayout.Button("View objective village"))
                    {
                        followingWarband = false;
                        focus = surface.PointAtCell(campaign.MilitaryObjectiveCell);
                        targetZoom = MinimumZoom;
                    }
                }
                if (campaign.WarbandTravelDays > 0)
                    GUILayout.Label($"Repositioning | {campaign.WarbandTravelDays} days");
                var destination = campaign.CanRepositionWarband(clickedCell) ? clickedCell : campaign.MilitaryObjectiveCell;
                var canReposition = campaign.CanRepositionWarband(destination);
                var orderEnabled = GUI.enabled;
                GUI.enabled = orderEnabled && canReposition;
                var order = destination == clickedCell ? "Reposition to selected ground" : "Reposition to objective";
                if (GUILayout.Button(order))
                    saveStatus = campaign.TryRepositionWarband(destination) ? "Warband repositioning. The operation continues."
                        : "No route within the operation region.";
                GUI.enabled = orderEnabled;
                if (GUILayout.Button("Withdraw and return to capital") && campaign.TryWithdrawWarband())
                {
                    var route = campaign.PlanWarbandRoute(campaign.CapitalCell);
                    saveStatus = campaign.TryMarchWarband(route)
                        ? "Operation ended. Warband returning to the capital."
                        : "Operation ended. Warband holding position.";
                }
                return;
            }
            if (campaign.OperationMissedObjective)
                GUILayout.Label("Last operation failed. The objective was not held long enough.");
            if (surface.Map.IsFord(clickedCell) && !campaign.IsFordSecured(clickedCell))
            {
                var rival = campaign.IsFordHeldByRival(clickedCell);
                var canIssueOrder = campaign.CanSecureFord(clickedCell);
                var enabled = GUI.enabled;
                GUI.enabled = enabled && canIssueOrder;
                var order = rival ? $"Retake ford ({CampaignState.FordRetakeFood} food)" : "Secure ford";
                if (GUILayout.Button(order) && campaign.TrySecureFord(clickedCell))
                {
                    surface.SetOwnership(campaign, TribeColours[campaign.Colour]);
                    surface.RefreshSecuredFord(clickedCell);
                    saveStatus = rival ? "Ford retaken. Civilian traffic can resume." : "Ford secured.";
                }
                GUI.enabled = enabled;
                if (!canIssueOrder)
                    GUILayout.Label(rival && campaign.Food < CampaignState.FordRetakeFood
                        ? $"Bring {CampaignState.FordRetakeFood} food to retake this crossing."
                        : "Stop the warband on a bank beside this ford.");
            }
            GUILayout.Label(campaign.WarbandTravelDays > 0
                ? $"Warband marching | {campaign.WarbandTravelDays} days"
                : "Warband ready");
            if (campaign.WarbandTravelDays > 0 && !surface.Map.IsFord(campaign.WarbandCell)
                && GUILayout.Button("Halt march") && campaign.TryHaltWarband())
            {
                plannedMarch = null;
                plannedMarchDestination = -1;
                saveStatus = "Warband holding position.";
            }

            var wasEnabled = GUI.enabled;
            var fordSelected = surface.Map.IsFord(clickedCell);
            var canPlan = clickedCell != campaign.WarbandCell && surface.Map.IsMarchCell(clickedCell)
                && (!fordSelected || !surface.Map.CanMarchBetween(campaign.WarbandCell, clickedCell));
            GUI.enabled = wasEnabled && canPlan;
            if (GUILayout.Button(fordSelected ? "Plan march to ford bank" : "Plan march to selected ground"))
            {
                plannedMarchDestination = clickedCell;
                plannedMarch = campaign.PlanWarbandRoute(clickedCell);
                if (plannedMarch == null) saveStatus = "No land route to the selected ground.";
            }
            GUI.enabled = wasEnabled;
            if (plannedMarchDestination == clickedCell && plannedMarch != null)
            {
                var days = campaign.EstimateMarchDays(plannedMarch);
                if (days == 0) GUILayout.Label("Warband position changed. Plan the march again.");
                else if (GUILayout.Button($"Confirm march | {days} {(days == 1 ? "day" : "days")}"))
                {
                    if (campaign.TryMarchWarband(plannedMarch)) saveStatus = "March ordered.";
                    plannedMarch = null;
                    plannedMarchDestination = -1;
                }
            }
        }

        private void DrawTaxPolicy(TaxPolicy policy)
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.Taxes != policy;
            if (GUILayout.Button(policy.ToString())) campaign.SetTaxPolicy(policy);
            GUI.enabled = wasEnabled;
        }

        private bool HasVillageHere
        {
            get
            {
                foreach (var site in campaign.SettlementCells)
                    if (campaign.SettlementProvince(site) == selected) return true;
                return false;
            }
        }

        private bool InCapitalLimits => clickedCell >= 0
            && surface.IsWithinSettlementLimits(campaign, campaign.CapitalCell, clickedMapUv);

        private int SelectedConstructionSite
        {
            get
            {
                if (clickedCell < 0) return -1;
                if (InCapitalLimits) return campaign.CapitalCell;
                foreach (var site in campaign.SettlementCells)
                    if (surface.IsWithinSettlementLimits(campaign, site, clickedMapUv))
                        return site;
                return -1;
            }
        }

        private void DrawFoundSettlement()
        {
            var wasEnabled = GUI.enabled;
            var rivalTerritory = surface.IsIndependentTerritory(clickedCell);
            if (rivalTerritory) GUILayout.Label("An independent village claims this land.");
            var validSite = !rivalTerritory && clickedCell >= 0 && surface.Map.ProvinceAt[clickedCell] == selected
                && surface.CanSiteVillage(campaign, clickedCell);
            var provinceRoot = validSite ? surface.InfluenceProvinceAt(clickedCell) : -1;
            var party = validSite ? campaign.FoundingParty(clickedCell, provinceRoot) : (Origin: -1, People: 0);
            if (clickedCell >= 0 && surface.IsDryLand(clickedCell) && SelectedConstructionSite < 0)
            {
                var sites = surface.SurveyProductionSites(clickedCell);
                GUILayout.Label("Nearby production sites");
                GUILayout.Label(sites == 0 ? "None within city limits" : DescribeProductionSites(sites));
            }
            if (validSite)
            {
                GUILayout.Label(party.People == 0 ? "No settlement can send people to this site."
                    : $"{party.People:N0} settlers from {(party.Origin == campaign.CapitalCell ? "the capital" : "a village")} | "
                        + (provinceRoot < 0 ? "New province" : "Joins existing province"));
            }
            GUI.enabled = wasEnabled && validSite && party.People > 0 && campaign.CanFoundSettlement(clickedCell);
            var action = provinceRoot < 0 ? "Found new province" : "Found village";
            if (GUILayout.Button($"{action} ({CampaignState.SettlementCoin} coin, {campaign.SettlementWoodCost} wood)"))
            {
                if (campaign.TryFoundSettlement(clickedCell, provinceRoot))
                {
                    surface.RefreshVillage(campaign, clickedCell);
                    surface.SetOwnership(campaign, TribeColours[campaign.Colour]);
                    saveStatus = provinceRoot < 0 ? "Settlement founded a new province." : "Settlement joined an existing province.";
                }
                else
                    saveStatus = "This province needs more people before it can send settlers.";
            }
            GUI.enabled = wasEnabled;
        }

        private static string DescribeProductionSites(SettlementBuilding sites)
        {
            var description = new System.Collections.Generic.List<string>();
            if ((sites & SettlementBuilding.Farm) != 0) description.Add("Farmland");
            if ((sites & SettlementBuilding.LumberCamp) != 0) description.Add("Timber");
            if ((sites & SettlementBuilding.IronMine) != 0) description.Add("Iron deposits");
            if ((sites & SettlementBuilding.Docks) != 0) description.Add("Trade waterfront");
            return string.Join(", ", description);
        }

        private void DrawConstruction(int site)
        {
            GUILayout.Label(site == campaign.CapitalCell ? "Capital buildings" : "Village buildings", hudHeading);
            if (Event.current.type == EventType.Repaint)
            {
                previewHalfWidth = MapSettlement.ProductionHalfWidth;
                previewColour = HudGold;
            }
            var sites = surface.SurveyProductionSites(site, campaign);
            DrawBuilding(site, SettlementBuilding.Farm, sites);
            DrawBuilding(site, SettlementBuilding.LumberCamp, sites);
            DrawBuilding(site, SettlementBuilding.IronMine, sites);
            DrawBuilding(site, SettlementBuilding.Docks, sites);
        }

        private void DrawBuilding(int site, SettlementBuilding building, SettlementBuilding availableSites)
        {
            var built = campaign.HasSettlementBuilding(site, building);
            var placement = built ? campaign.BuildingPlacement(site, building) : clickedCell;
            var info = campaign.SettlementBuildingInfo(site, building, placement);
            if (built && info.Yield == 0) info = campaign.BuildingInfo(campaign.SettlementProvince(site), building);
            var bonus = (campaign.TownSpeciality(site) == building ? 1 : 0) + campaign.ResearchProductionBonus(building);
            var output = building == SettlementBuilding.Docks ? $"Water trade access and +{1 + bonus} agreement slots"
                : $"+{info.Yield + bonus} {info.Resource} / {CampaignState.ProductionInterval} days";
            if (built)
            {
                GUILayout.Label($"{info.Name} | {output}");
                if (building == SettlementBuilding.IronMine && placement >= 0)
                {
                    var supply = campaign.GetIronSupplyRoute(site);
                    var route = supply switch
                    {
                        IronSupplyRoute.Local => "Feeds capital stock directly",
                        IronSupplyRoute.Overland => "Overland route to capital open",
                        IronSupplyRoute.Waterway => "Waterway route to capital open",
                        _ => "No supply route to the capital"
                    };
                    GUILayout.Label($"Local iron {campaign.SettlementIronStock(site):N0} | {route}");
                    var shipment = campaign.IronShipment(site);
                    if (shipment.Amount > 0)
                        GUILayout.Label(campaign.IsIronShipmentHeld(site)
                            ? $"{shipment.Amount:N0} iron held by a ford closure"
                            : $"{shipment.Amount:N0} iron in transit | Next unload in {shipment.Days} days");
                    if (campaign.IsIronShipmentHeld(site) && GUILayout.Button("Reroute held shipment"))
                        saveStatus = campaign.TryRerouteHeldIronShipment(site) ? "Shipment rerouted overland."
                            : "No alternate secured route. The shipment is still waiting.";
                    if (supply == IronSupplyRoute.Overland || supply == IronSupplyRoute.Waterway || shipment.Amount > 0)
                        showIronSupplyRoute = GUILayout.Toggle(showIronSupplyRoute, "Show iron supply route");
                }
                return;
            }

            var wasEnabled = GUI.enabled;
            var localIssue = (availableSites & building) != 0 ? null : building switch
            {
                SettlementBuilding.Farm => "Requires workable land within city limits.",
                SettlementBuilding.LumberCamp => "Requires forest within city limits.",
                SettlementBuilding.IronMine => "Requires an iron deposit within city limits.",
                SettlementBuilding.Docks => "Requires an ocean shore or navigable riverbank within city limits.",
                _ => null
            };
            var fits = localIssue == null && surface.CanPlaceProduction(campaign, site, clickedCell, building);
            if (fits && surface.IsBuildSiteTooSteep(clickedCell, building))
            {
                localIssue = "The building footprint is too steep.";
                fits = false;
            }
            var canBuild = fits && campaign.CanBuildAtSettlement(site, building, clickedCell);
            GUI.enabled = wasEnabled && canBuild;
            if (GUILayout.Button($"Build {info.Name.ToLowerInvariant()} ({info.Coin} coin, {info.Wood} wood)")
                && campaign.TryBuildAtSettlement(site, building, clickedCell))
            {
                if (site == campaign.CapitalCell) surface.SetCapital(settlement, campaign);
                else surface.RefreshVillage(campaign, site);
            }
            PreviewWhenHovered(MapSettlement.ProductionHalfWidth, canBuild);
            GUI.enabled = wasEnabled;
            GUILayout.Label(localIssue ?? info.SiteIssue ?? (fits ? output : "Select clear ground suitable for this building."));
        }

        private void PreviewWhenHovered(float halfWidth, bool canBuild)
        {
            if (clickedCell < 0 || Event.current.type != EventType.Repaint || !pointerOverProvincePanel
                || !GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) return;
            previewHalfWidth = halfWidth;
            previewColour = canBuild ? new Color(.34f, .95f, .47f) : new Color(1f, .32f, .28f);
        }

        private void DrawCapitalActions()
        {
            var wasEnabled = GUI.enabled;
            var fitsGranary = InCapitalLimits && surface.CanPlaceGranary(campaign, settlement, clickedCell)
                && !surface.ConflictsWithProduction(campaign, campaign.CapitalCell, clickedCell, MapSettlement.GranaryHalfWidth);
            if (!campaign.HasGranary && fitsGranary && surface.IsBuildSiteTooSteep(clickedCell))
            {
                GUILayout.Label("The granary footprint is too steep.");
                fitsGranary = false;
            }
            else if (!campaign.HasGranary && InCapitalLimits && !fitsGranary)
                GUILayout.Label("Select open ground for the granary.");
            GUI.enabled = wasEnabled && fitsGranary && campaign.CanBuildGranary;
            if (!campaign.HasGranary && GUILayout.Button($"Build granary ({CampaignState.GranaryWood} wood, {CampaignState.GranaryCoin} coin)")
                && campaign.TryBuildGranary(clickedCell))
            {
                surface.SetCapital(settlement, campaign);
                saveStatus = "Granary built. Stored food is protected from winter spoilage.";
            }
            if (!campaign.HasGranary)
                PreviewWhenHovered(MapSettlement.GranaryHalfWidth, fitsGranary && campaign.CanBuildGranary);
            GUI.enabled = wasEnabled && campaign.CanRecruitWarband;
            if (!campaign.HasWarband && GUILayout.Button($"Recruit warband ({campaign.WarbandRecruitWood} wood, {campaign.WarbandRecruitCoin} coin, {CampaignState.WarbandRecruits} people)"))
            {
                campaign.TryRecruitWarband();
                saveStatus = $"{CampaignState.WarbandRecruits} people recruited from the capital.";
            }
            GUI.enabled = wasEnabled && campaign.CanUpgradeWarband;
            if (campaign.HasWarband && !campaign.HasIronWarband && GUILayout.Button(
                $"Equip with iron ({CampaignState.IronWarbandIron} iron, {CampaignState.IronWarbandWood} wood, {CampaignState.IronWarbandCoin} coin)"))
            {
                campaign.TryUpgradeWarband();
                saveStatus = "Iron-equipped warband. Your tribe takes half the losses when expanding by force.";
            }
            GUI.enabled = wasEnabled;
            if (campaign.HasIronWarband) GUILayout.Label("Iron-equipped warband");
        }

        private void DrawNearbyCommunities()
        {
            var map = surface.Map;
            var listed = 0;
            for (var neighbour = 0; neighbour < map.Provinces.Length; neighbour++)
            {
                if (!campaign.CanReachIndependentProvince(neighbour)) continue;
                if (listed++ == 0) GUILayout.Label("Nearby independent communities");
                if (GUILayout.Button(map.Provinces[neighbour].Id)) SelectCommunity(neighbour);
                if (listed == 3) break;
            }
        }

        private void SelectCommunity(int province)
        {
            selected = province;
            selectedCell = clickedCell = -1;
            provinceScroll = Vector2.zero;
            showProvinceDetails = showTrade = false;
            provinceDetails = Describe(surface.Map.Provinces[province]);
            surface.SetSelection(province);
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
        private Rect ProvincePanel
        {
            get
            {
                var height = Mathf.Max(1f, Mathf.Min(selected >= 0 ? 390 : 80, Screen.height - PanelTop - HudMargin));
                return new Rect(HudMargin, Screen.height - HudMargin - height, PanelWidth, height);
            }
        }
        private Rect CampaignPanel => new Rect(Screen.width - PanelWidth - HudMargin, PanelTop, PanelWidth,
            Mathf.Max(1f, Mathf.Min(campaign == null ? 118 : 350, MapDock.yMin - PanelTop - HudMargin)));
        private Rect MapDock
        {
            get
            {
                var width = Mathf.Max(1f, Mathf.Min(340f, Screen.width - PanelWidth - HudMargin * 3f));
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
            var slot = Mathf.Max(1f, Mathf.Min(116f, available / 5f));
            var resourceY = compact ? 38f : 8f;
            DrawResource("Coin", campaign?.Coin, resourceX, resourceY, slot);
            DrawResource("Food", campaign?.Food, resourceX + slot, resourceY, slot);
            DrawResource("Wood", campaign?.Wood, resourceX + slot * 2, resourceY, slot);
            DrawResource("Iron", campaign?.Iron, resourceX + slot * 3, resourceY, slot);
            DrawResource("Population", campaign?.OwnedPopulation, resourceX + slot * 4, resourceY, slot);
        }

        private void DrawResource(string label, long? value, float x, float y, float width)
        {
            PaintHudRect(new Rect(x + width - 1, y + 2, 1, 32), HudBorder);
            GUI.Label(new Rect(x, y, width, 16), label, hudLabel);
            GUI.Label(new Rect(x, y + 15, width, 22), value.HasValue ? value.Value.ToString("N0") : "—", hudValue);
        }

        // Switch map views through the surface while keeping the same camera and campaign.
        private void DrawMapDock()
        {
            var dock = MapDock;
            DrawPanelFrame(dock);
            GUI.Label(new Rect(dock.x + 8, dock.y + 3, dock.width - 16, 19), "Map views", hudHeading);
            var slot = (dock.width - 26) / 4f;
            var x = dock.x + 7;
            var y = dock.y + 23;
            DrawMapViewButton("Terrain", MapViewMode.Terrain, x, y, slot);
            DrawMapViewButton("Biomes", MapViewMode.Biomes, x + slot + 4, y, slot);
            DrawMapViewButton("Provinces", MapViewMode.Provinces, x + (slot + 4) * 2, y, slot);
            DrawMapViewButton("Resources", MapViewMode.Resources, x + (slot + 4) * 3, y, slot);
            DrawMapViewButton("Territories", MapViewMode.Territories, x, y + 26, slot, surface.HasTerritories);
            DrawMapViewButton("Realm", MapViewMode.Realm, x + slot + 4, y + 26, slot);
            DrawMapViewButton("Influence", MapViewMode.Ownership, x + (slot + 4) * 2, y + 26, slot);

            if (mapView == MapViewMode.Resources)
            {
                DrawResourceFilter("Fords", 0, x + (slot + 4) * 3, y + 26, slot);
                DrawResourceFilter("Farmland", SettlementBuilding.Farm, x, y + 52, slot);
                DrawResourceFilter("Timber", SettlementBuilding.LumberCamp, x + slot + 4, y + 52, slot);
                DrawResourceFilter("Iron", SettlementBuilding.IronMine, x + (slot + 4) * 2, y + 52, slot);
                DrawResourceFilter("Waterfront", SettlementBuilding.Docks, x + (slot + 4) * 3, y + 52, slot);
            }

            var status = mapView switch
            {
                MapViewMode.Resources when resourceFilter == 0 => "White: ford | Blue: passage | Green: secured | Purple: rival | Red: closed",
                MapViewMode.Resources => "Highlighted ground suits the selected resource",
                MapViewMode.Realm => "Province and territory borders",
                MapViewMode.Ownership when campaign?.WarbandCommitted == true => "Red shows military occupation",
                MapViewMode.Ownership => campaign == null ? "Found a settlement to see its influence" : "Dark frontier | Lighter internal province borders",
                MapViewMode.Biomes => selected >= 0 ? "Dominant biome: " + DominantBiome(surface.Map.Provinces[selected])
                    : "Click a province to inspect its biome",
                MapViewMode.Territories => "Land, sea & navigable river territories",
                MapViewMode.Provinces => "Land, sea & navigable river provinces",
                _ => "Terrain and settlement"
            };
            GUI.Label(new Rect(dock.x + 6, dock.y + 78, dock.width - 12, 21), status, hudLabel);
        }

        private void DrawResourceFilter(string label, SettlementBuilding filter, float x, float y, float width)
        {
            if (DrawHudButton(new Rect(x, y, width, 23), label, true, filter == resourceFilter))
            {
                resourceFilter = filter;
                surface.SetResourceFilter(filter);
            }
        }

        private void DrawMapViewButton(string label, MapViewMode mode, float x, float y, float width, bool available = true)
        {
            if (DrawHudButton(new Rect(x, y, width, 23), label, available, mapView == mode) && mapView != mode)
            {
                if (mode == MapViewMode.Resources) surface.SetResourceFilter(resourceFilter);
                surface.SetView(mode);
                mapView = mode;
                surface.HighlightInfluenceAt(clickedCell);
            }
        }

        private void OnProvinceJoined(bool hadExpanded)
        {
            surface.SetVillages(campaign);
            surface.SetOwnership(campaign, TribeColours[campaign.Colour]);
            if (!hadExpanded) surface.SetCapital(settlement, campaign);
        }
    }
}
