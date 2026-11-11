// Builds the World Generator's 2D preview texture for elevation, water, rivers, climate or biomes.
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    public enum HeightmapPreviewMode { Relief, Grayscale, LandAndWater, Rivers, NavalAccess, Temperature, Moisture, Biomes }

    internal static class HeightmapPreview
    {
        public static bool IsClimateMode(HeightmapPreviewMode mode) =>
            mode == HeightmapPreviewMode.Temperature || mode == HeightmapPreviewMode.Moisture;

        // Paint a preview from the currently available layers. These display colours are separate from
        // exported game data.
        public static Texture2D Create(HeightmapResult result, HeightmapPreviewMode mode, WaterMap water = null, RiverMap rivers = null, RiverNavigation navigation = null,
            ClimateMap climate = null, bool dataColors = false, BiomeMap biomes = null)
        {
            var showMask = mode == HeightmapPreviewMode.LandAndWater || mode == HeightmapPreviewMode.NavalAccess;
            if (showMask)
            {
                if (water == null) water = WaterMap.Classify(result);
                if (!ReferenceEquals(water.Source, result)) throw new System.ArgumentException("Water classification belongs to a different world.");
            }
            var showRiverSurface = mode == HeightmapPreviewMode.Rivers;
            if (showRiverSurface && rivers != null && !ReferenceEquals(rivers.Terrain, result))
                throw new System.ArgumentException("River data belongs to a different world.");
            var showBiomes = mode == HeightmapPreviewMode.Biomes;
            if (showBiomes && (biomes == null || !ReferenceEquals(biomes.Source, result)))
                throw new System.ArgumentException("Biome data must describe the displayed terrain.");
            var showClimate = IsClimateMode(mode);
            if (showClimate && (climate == null || !ReferenceEquals(climate.Source, result)))
                throw new System.ArgumentException("Climate data must describe the displayed terrain.");
            var climateSamples = !showClimate ? null
                : mode == HeightmapPreviewMode.Temperature ? climate.Temperature : climate.Moisture;
            var width = result.Recipe.Width; var height = result.Recipe.Height;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { name = "Heightmap preview", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp,
                    filterMode = showMask || showBiomes || dataColors ? FilterMode.Point : FilterMode.Bilinear };
            try
            {
                var colours = new Color32[width * height];
                var riverSurface = showRiverSurface ? rivers?.BuildSurfaceMask() : null;
                var sea = result.Recipe.SeaLevel;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var index = y * width + x;
                        if (showMask)
                        {
                            var riverWater = mode == HeightmapPreviewMode.NavalAccess
                                && navigation != null && navigation[index] != 0;
                            var rgb = OpenGsLandPalette.Color(water[index], riverWater);
                            colours[index] = new Color32(rgb.R, rgb.G, rgb.B, 255);
                            continue;
                        }
                        if (showBiomes)
                        {
                            var rgb = dataColors
                                ? biomes.OpenGsTerrainColour(index, navigation != null && navigation[index] != 0)
                                : BiomeMap.PreviewColour(biomes[index]);
                            colours[index] = new Color32(rgb.R, rgb.G, rgb.B, 255);
                            continue;
                        }
                        if (showClimate)
                        {
                            var value = climateSamples[index];
                            colours[index] = dataColors ? new Color32(value, value, value, 255) : ClimateColour(mode, value);
                            continue;
                        }
                        var h = result.Elevations[index];
                        Color colour;
                        if (mode == HeightmapPreviewMode.Grayscale) colour = new Color(h, h, h);
                        else if (h < sea)
                            colour = Color.Lerp(new Color(.025f, .065f, .12f), new Color(.12f, .38f, .43f), Mathf.Pow(h / sea, 3));
                        else
                        {
                            var t = (h - sea) / (1 - sea);
                            if (t < .045f) colour = Color.Lerp(new Color(.7f, .69f, .47f), new Color(.32f, .43f, .26f), t / .045f);
                            else if (t < .32f) colour = Color.Lerp(new Color(.32f, .43f, .26f), new Color(.47f, .43f, .3f), (t - .045f) / .275f);
                            else if (t < .7f) colour = Color.Lerp(new Color(.47f, .43f, .3f), new Color(.61f, .59f, .54f), (t - .32f) / .38f);
                            else colour = Color.Lerp(new Color(.61f, .59f, .54f), new Color(.91f, .91f, .87f), (t - .7f) / .3f);
                            var west = result.Elevations[y * width + Mathf.Max(0, x - 1)];
                            var east = result.Elevations[y * width + Mathf.Min(width - 1, x + 1)];
                            var south = result.Elevations[Mathf.Max(0, y - 1) * width + x];
                            var north = result.Elevations[Mathf.Min(height - 1, y + 1) * width + x];
                            var normal = new Vector3((west - east) * width * .10f, (south - north) * height * .10f, 1).normalized;
                            var light = Vector3.Dot(normal, new Vector3(-.5f, .6f, .8f).normalized);
                            colour *= Mathf.Clamp(.7f + .4f * light, .4f, 1.1f);
                        }
                        if (riverSurface != null && riverSurface[index] != 0)
                            colour = new Color(.08f, .59f, .79f);
                        colour.a = 1; colours[index] = colour;
                    }
                texture.SetPixels32(colours); texture.Apply(false, false);
                return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
        }

        // Convert a climate sample to its editor display colour.
        private static Color32 ClimateColour(HeightmapPreviewMode mode, byte value)
        {
            var t = value / 255f;
            if (mode == HeightmapPreviewMode.Moisture)
                return Color.Lerp(new Color(.70f, .40f, .16f), new Color(.05f, .55f, .85f), t);
            var midpoint = new Color(.96f, .92f, .68f);
            return t < .5f ? Color.Lerp(new Color(.16f, .37f, .79f), midpoint, t * 2f)
                : Color.Lerp(midpoint, new Color(.82f, .22f, .12f), t * 2f - 1f);
        }
    }
}
