using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    public enum HeightmapPreviewMode { Relief, Grayscale, LandAndWater }

    internal static class HeightmapPreview
    {
        public static Texture2D Create(HeightmapResult result, HeightmapPreviewMode mode)
        {
            var width = result.Recipe.Width; var height = result.Recipe.Height;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                { name = "Heightmap preview", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var colours = new Color32[width * height];
            var sea = result.Recipe.SeaLevel;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var h = result.Elevations[index];
                    Color colour;
                    if (mode == HeightmapPreviewMode.Grayscale) colour = new Color(h, h, h);
                    else if (mode == HeightmapPreviewMode.LandAndWater)
                        colour = h >= sea ? new Color(.85f, .86f, .77f) : new Color(.045f, .13f, .2f);
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
                    colour.a = 1; colours[index] = colour;
                }
            texture.SetPixels32(colours); texture.Apply(false, false);
            return texture;
        }
    }
}
