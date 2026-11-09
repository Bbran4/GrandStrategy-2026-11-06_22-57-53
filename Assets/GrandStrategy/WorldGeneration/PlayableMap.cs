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

        private static readonly Color[] TribeColours =
        {
            new Color(.19f, .63f, .90f), new Color(.86f, .28f, .23f),
            new Color(.33f, .74f, .42f), new Color(.66f, .44f, .83f),
            new Color(.95f, .57f, .20f)
        };
        private static readonly string[] ColourNames = { "Blue", "Red", "Green", "Violet", "Ochre" };
        private static readonly int[] Speeds = { 1, 2, 4 };
        private const float MinimumZoom = .045f;
        private const float TiltedZoom = .28f;
        private const float TiltedPitch = 48f;
        private const float ZoomResponse = 12f;

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

        private float PanelWidth => Mathf.Max(1f, Mathf.Min(325f, (Screen.width - 36f) * .5f));
        private Rect Panel => new Rect(12, 12, PanelWidth, Mathf.Min(selected >= 0 ? 412 : 160, Screen.height - 24));
        private Rect CampaignPanel => new Rect(Screen.width - PanelWidth - 12, 12, PanelWidth,
            Mathf.Min(campaign == null ? 118 : 230, Screen.height - 24));

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
            if (campaign?.Advance(deltaTime) == true)
                surface.SetOwnership(campaign, TribeColours[campaign.Colour]);

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
                var overPanel = Panel.Contains(guiPosition) || CampaignPanel.Contains(guiPosition);
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
            text.AppendLine("Dominant biome: " + DominantBiome(province));
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
            surface.SetOwnership(next, TribeColours[next.Colour]);
            campaign = next;
            settlement = capitalPoint;
            surface.SetCapital(capitalPoint);
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
                ApplyCampaign(CampaignState.Restore(surface.Map, mapKey, save, TribeColours.Length));
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

            DrawSettlement();
            DrawCampaign();
            var panel = Panel;
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 10, panel.y + 7, panel.width - 20, panel.height - 14));
            GUILayout.Label("GrandStrategy | Playable Map");
            GUILayout.Label("WASD or middle drag to pan. Scroll to zoom.");
            GUILayout.Label("Click a province. Home resets the view.");
            GUILayout.BeginHorizontal();
            MapViewButton("Terrain", MapViewMode.Terrain);
            MapViewButton("Realms", MapViewMode.Realms);
            MapViewButton("Territories", MapViewMode.Territories, surface.HasTerritories);
            MapViewButton("Provinces", MapViewMode.Provinces);
            GUILayout.EndHorizontal();
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

        private void MapViewButton(string label, MapViewMode mode, bool available = true)
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && available && mapView != mode;
            if (GUILayout.Button(label))
            {
                surface.SetView(mode);
                mapView = mode;
            }
            GUI.enabled = wasEnabled;
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
                colourIndex = (colourIndex + 1) % TribeColours.Length;

            var dry = surface.IsDryLand(clickedCell) && surface.IsDryLand(selectedCell);
            if (!dry) GUILayout.Label("Click dry land to place the settlement.");
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
                var progress = CampaignState.IntegrationDuration - campaign.IntegrationDaysLeft;
                GUILayout.Label($"Integration {progress}/{CampaignState.IntegrationDuration} days");
                return;
            }
            if (!campaign.CanReachIndependentProvince(selected))
            {
                GUILayout.Label("Integration requires a neighbouring province you own.");
                return;
            }

            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && campaign.IntegrationTarget == -1 && campaign.Coin >= CampaignState.IntegrationCost;
            if (GUILayout.Button($"Start integration ({CampaignState.IntegrationCost} coin)"))
                campaign.TryStartIntegration(selected);
            GUI.enabled = wasEnabled;
        }

        private void DrawCampaign()
        {
            var panel = CampaignPanel;
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 10, panel.y + 7, panel.width - 20, panel.height - 14));
            GUILayout.Label(campaign?.TribeName ?? "Campaign");
            if (campaign != null)
            {
                GUILayout.Label($"Day {campaign.Day:N0} | Coin {campaign.Coin:N0}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(campaign.Paused ? "Resume" : "Pause"))
                    campaign.Paused = !campaign.Paused;
                var speedIndex = Array.IndexOf(Speeds, campaign.Speed);
                if (GUILayout.Button($"Speed {campaign.Speed}×"))
                    campaign.SetSpeed(Speeds[(speedIndex + 1) % Speeds.Length]);
                GUILayout.EndHorizontal();
                if (campaign.IntegrationTarget >= 0)
                {
                    var target = surface.Map.Provinces[campaign.IntegrationTarget].Id;
                    GUILayout.Label($"{target} joins in {campaign.IntegrationDaysLeft} days");
                }
            }

            GUILayout.BeginHorizontal();
            if (campaign != null && GUILayout.Button("Save campaign")) SaveCampaign();
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && hasSave;
            if (GUILayout.Button("Load campaign")) LoadCampaign();
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(saveStatus)) GUILayout.Label(saveStatus);
            GUILayout.EndArea();
        }

        private void DrawSettlement()
        {
            if (campaign == null) return;
            var point = mapCamera.WorldToScreenPoint(settlement);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height)
                return;
            var y = Screen.height - point.y;
            var label = new GUIContent(campaign.TribeName);
            var width = GUI.skin.box.CalcSize(label).x + 12f;
            GUI.Box(new Rect(point.x - width * .5f, y - 35f, width, 23f), label);
            GUI.Label(new Rect(point.x - 5f, y - 13f, 14f, 18f), "▼");
        }
    }
}
