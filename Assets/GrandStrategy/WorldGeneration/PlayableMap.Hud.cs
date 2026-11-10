using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    public sealed partial class PlayableMap
    {
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
        private List<(int Province, Vector3 Point)> ownedMarkers;

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
                MapViewMode.Realm => campaign == null ? "No active realm" : campaign.TribeName + " | Capital shown",
                MapViewMode.Ownership => campaign == null ? "No provinces held" : "Gold labels mark provinces held",
                MapViewMode.Biomes => selected >= 0 ? "Dominant biome: " + DominantBiome(surface.Map.Provinces[selected])
                    : "Click a province to inspect its biome",
                MapViewMode.Territories => "Imported territory boundaries",
                MapViewMode.Provinces => "Land province boundaries",
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
            ownedMarkers = null;
            if (!hadExpanded) surface.SetCapital(settlement, campaign.HasGranary, true);
        }

        private void DrawOwnershipMarkers()
        {
            if (mapView != MapViewMode.Ownership || campaign == null || Event.current.type != EventType.Repaint) return;
            ownedMarkers ??= LocateOwnedProvinces();
            foreach (var marker in ownedMarkers)
            {
                var screen = mapCamera.WorldToScreenPoint(marker.Point);
                if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height)
                    continue;
                var label = new GUIContent(surface.Map.Provinces[marker.Province].Id + " | Held");
                var width = hudLabel.CalcSize(label).x + 14f;
                var rect = new Rect(screen.x - width * .5f, Screen.height - screen.y - 16f, width, 21f);
                DrawPanelFrame(rect);
                GUI.Label(rect, label, hudLabel);
            }
        }

        private List<(int Province, Vector3 Point)> LocateOwnedProvinces()
        {
            var result = new List<(int Province, Vector3 Point)>();
            var map = surface.Map;
            var owned = new bool[map.Provinces.Length];
            var hasAdditionalProvince = false;
            for (var i = 0; i < owned.Length; i++)
            {
                owned[i] = i != campaign.Capital && campaign.Owns(i);
                hasAdditionalProvince |= owned[i];
            }
            if (!hasAdditionalProvince) return result;

            var sumX = new long[owned.Length];
            var sumY = new long[owned.Length];
            var count = new int[owned.Length];
            for (var cell = 0; cell < map.ProvinceAt.Length; cell++)
            {
                var province = map.ProvinceAt[cell];
                if (province < 0 || !owned[province] || !surface.IsDryLand(cell)) continue;
                sumX[province] += cell % map.Width;
                sumY[province] += cell / map.Width;
                count[province]++;
            }

            var best = new int[owned.Length];
            var distance = new float[owned.Length];
            Array.Fill(best, -1);
            Array.Fill(distance, float.PositiveInfinity);
            for (var cell = 0; cell < map.ProvinceAt.Length; cell++)
            {
                var province = map.ProvinceAt[cell];
                if (province < 0 || count[province] == 0 || !surface.IsDryLand(cell)) continue;
                var dx = cell % map.Width - (float)sumX[province] / count[province];
                var dy = cell / map.Width - (float)sumY[province] / count[province];
                var squared = dx * dx + dy * dy;
                if (squared >= distance[province]) continue;
                distance[province] = squared;
                best[province] = cell;
            }
            for (var i = 0; i < best.Length; i++)
                if (best[i] >= 0) result.Add((i, surface.PointAtCell(best[i])));
            return result;
        }
    }
}
