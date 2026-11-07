using System;
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

        private static readonly Color[] TribeColours =
        {
            new Color(.19f, .63f, .90f), new Color(.86f, .28f, .23f),
            new Color(.33f, .74f, .42f), new Color(.66f, .44f, .83f),
            new Color(.95f, .57f, .20f)
        };
        private static readonly string[] ColourNames = { "Blue", "Red", "Green", "Violet", "Ochre" };
        private static readonly Quaternion ViewAngle = Quaternion.Euler(65f, 0f, 0f);

        private Camera mapCamera;
        private PlayableMapSurface surface;
        private Vector3 focus;
        private float zoom = 1f;
        private bool dragging;
        private bool editingName;
        private int selected = -1;
        private int selectedCell = -1;
        private Vector3 selectedPoint;
        private int founded = -1;
        private Vector3 settlement;
        private string tribeName = "My tribe";
        private string failure;
        private int colourIndex;
        private string provinceDetails;
        private Vector2 detailsScroll;

        private Rect Panel => new Rect(12, 12, Mathf.Min(325, Screen.width - 24),
            Mathf.Min(selected >= 0 ? 365 : 112, Screen.height - 24));

        private void Start()
        {
            try
            {
                if (world == null || provinceMap == null)
                    throw new InvalidOperationException("Assign a World asset and its matching Province Map asset.");
                mapCamera = GetComponent<Camera>();
                // The map is anchored at the scene origin, independently of the camera.
                surface = new PlayableMapSurface(world, provinceMap);
                mapCamera.orthographic = true;
                mapCamera.clearFlags = CameraClearFlags.SolidColor;
                mapCamera.backgroundColor = new Color(.045f, .058f, .075f);
                mapCamera.nearClipPlane = .01f;
                mapCamera.farClipPlane = 2f * (surface.Aspect + 4f);
                UpdateCamera();
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
            var keyboard = Keyboard.current;
            if (keyboard != null && !editingName)
            {
                if (keyboard.homeKey.wasPressedThisFrame)
                {
                    focus = Vector3.zero;
                    zoom = 1f;
                }

                var movement = new Vector3(
                    Axis(keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
                        keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed),
                    0f,
                    Axis(keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
                        keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed));
                focus += Vector3.ClampMagnitude(movement, 1f) * mapCamera.orthographicSize * Time.unscaledDeltaTime;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var position = mouse.position.ReadValue();
                var overPanel = Panel.Contains(new Vector2(position.x, Screen.height - position.y));
                if (mouse.middleButton.wasPressedThisFrame) dragging = !overPanel;
                if (!mouse.middleButton.isPressed) dragging = false;
                if (dragging)
                {
                    var delta = mouse.delta.ReadValue();
                    var units = 2f * mapCamera.orthographicSize / Mathf.Max(1, Screen.height);
                    focus -= new Vector3(delta.x, 0f, delta.y / Mathf.Sin(65f * Mathf.Deg2Rad)) * units;
                }
                var scroll = mouse.scroll.ReadValue().y;
                if (!overPanel && scroll != 0f)
                {
                    var step = Mathf.Clamp(Mathf.Abs(scroll) * .0015f, .045f, .25f);
                    zoom = Mathf.Clamp(zoom * Mathf.Exp(-Mathf.Sign(scroll) * step), .045f, 1.25f);
                }
                UpdateCamera();
                if (!overPanel && !dragging && mouse.leftButton.wasPressedThisFrame)
                    Pick(position);
            }
            else
            {
                dragging = false;
                UpdateCamera();
            }
        }

        private static int Axis(bool negative, bool positive) => (positive ? 1 : 0) - (negative ? 1 : 0);

        private void UpdateCamera()
        {
            var aspect = surface.Aspect;
            focus.x = Mathf.Clamp(focus.x, -.5f * aspect, .5f * aspect);
            focus.z = Mathf.Clamp(focus.z, -.5f, .5f);
            mapCamera.orthographicSize = Mathf.Max(.60f, .58f * aspect / Mathf.Max(.1f, mapCamera.aspect)) * zoom;
            var distance = aspect + 4f;
            mapCamera.transform.SetPositionAndRotation(focus + ViewAngle * Vector3.back * distance, ViewAngle);
        }

        private void Pick(Vector2 position)
        {
            var previous = selected;
            selected = selectedCell = -1;
            editingName = false;
            var ray = mapCamera.ScreenPointToRay(position);
            if (surface.Collider.Raycast(ray, out var hit, mapCamera.farClipPlane))
            {
                selectedCell = surface.CellAt(hit.textureCoord);
                selected = surface.Map.ProvinceAt[selectedCell];
                selectedPoint = hit.point;
            }

            if (selected != previous)
            {
                detailsScroll = Vector2.zero;
                provinceDetails = selected < 0 ? null : Describe(surface.Map.Provinces[selected]);
            }
            UpdateHighlight();
        }

        private string Describe(ProvinceInfo province)
        {
            var text = new StringBuilder();
            text.AppendLine("Type: " + (province.Type ?? "Unknown"));
            text.AppendLine("Territory: " + (string.IsNullOrEmpty(province.TerritoryId) ? "None" : province.TerritoryId));
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

        private void UpdateHighlight() => surface.SetSelection(selected, founded, TribeColours[colourIndex]);

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
            var panel = Panel;
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 10, panel.y + 7, panel.width - 20, panel.height - 14));
            GUILayout.Label("GrandStrategy | Playable Map");
            GUILayout.Label("WASD or middle drag to pan. Scroll to zoom.");
            GUILayout.Label("Click a province. Home resets the view.");
            if (selected < 0)
                GUILayout.Label(founded < 0 ? "Choose a land province to establish a tribe." : "Founded " + tribeName);
            else
                DrawProvince(surface.Map.Provinces[selected]);
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

            if (founded >= 0)
            {
                GUILayout.Label(selected == founded ? tribeName + " | Starting settlement" : "Unclaimed");
                return;
            }
            if (!string.Equals(province.Type, "land", StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.Label("Choose a land province to establish a tribe.");
                return;
            }

            GUILayout.Label("Tribe name");
            GUI.SetNextControlName("TribeName");
            tribeName = GUILayout.TextField(tribeName, 32);
            if (GUILayout.Button("Tribe colour: " + ColourNames[colourIndex]))
            {
                colourIndex = (colourIndex + 1) % TribeColours.Length;
                UpdateHighlight();
            }

            var dry = surface.IsDryLand(selectedCell);
            if (!dry) GUILayout.Label("Click dry land to place the settlement.");
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && dry && !string.IsNullOrWhiteSpace(tribeName);
            if (GUILayout.Button("Establish tribe here"))
            {
                tribeName = tribeName.Trim();
                founded = selected;
                settlement = selectedPoint;
                UpdateHighlight();
            }
            GUI.enabled = wasEnabled;
        }

        private void DrawSettlement()
        {
            if (founded < 0) return;
            var point = mapCamera.WorldToScreenPoint(settlement);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height)
                return;
            var y = Screen.height - point.y;
            var label = new GUIContent(tribeName);
            var width = GUI.skin.box.CalcSize(label).x + 12f;
            GUI.Box(new Rect(point.x - width * .5f, y - 35f, width, 23f), label);
            GUI.Label(new Rect(point.x - 5f, y - 13f, 14f, 18f), "▼");
        }
    }
}
