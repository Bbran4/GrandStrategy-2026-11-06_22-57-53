using System;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal sealed class PlayableMapSurface : IDisposable
    {
        private const float Raw16Tolerance = 1f / 65535f;

        private Mesh mesh;
        private Material material;
        private Texture2D colours;
        private Texture2D regions;
        private GameObject surface;
        private WaterMap water;
        private byte[] rivers;

        public ProvinceMap Map { get; }
        public float Aspect => (float)Map.Width / Map.Height;
        public Collider Collider { get; private set; }

        public PlayableMapSurface(WorldHeightmap world, ProvinceMapAsset provinces)
        {
            var imported = provinces.ReadTerrain();
            var riverMap = world.ReadRivers();
            var terrain = riverMap?.Terrain ?? world.Read();
            ValidatePair(terrain, imported);
            Map = provinces.Map;
            water = WaterMap.Classify(terrain);
            rivers = riverMap?.BuildSurfaceMask();
            var settings = world.ReadClimateSettings();
            var biomes = world.HasBiomes && settings != null
                ? BiomeGenerator.Generate(ClimateGenerator.Generate(terrain, water, settings), water, riverMap)
                : null;

            try
            {
                var shader = Resources.Load<Shader>("PlayableMap");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The playable map shader requires Universal Render Pipeline.");

                mesh = HeightmapMesh.Create(terrain, world.HeightScale, 512);
                colours = CreateTexture("Map colours", false, FilterMode.Bilinear, true);
                regions = CreateTexture("Province indices", true, FilterMode.Point, false);
                FillTextures(biomes);
                material = new Material(shader) { hideFlags = HideFlags.DontSave };
                material.SetTexture("_MapColours", colours);
                material.SetTexture("_ProvinceIndices", regions);

                surface = new GameObject("Map surface", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))
                    { hideFlags = HideFlags.DontSave };
                surface.GetComponent<MeshFilter>().sharedMesh = mesh;
                surface.GetComponent<MeshRenderer>().sharedMaterial = material;
                var collider = surface.GetComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                Collider = collider;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static void ValidatePair(HeightmapResult world, HeightmapResult imported)
        {
            var a = world.Recipe;
            var b = imported.Recipe;
            if (a.Width != b.Width || a.Height != b.Height || a.Seed != b.Seed || a.SeaLevel != b.SeaLevel)
                throw new InvalidOperationException("Use the World asset that produced the OpenGS export for this Province Map.");
            for (var i = 0; i < world.Elevations.Length; i++)
                if (Mathf.Abs(world.Elevations[i] - imported.Elevations[i]) > Raw16Tolerance)
                    throw new InvalidOperationException("The World asset's final terrain differs from the Province Map's source export.");
        }

        private Texture2D CreateTexture(string label, bool linear, FilterMode filter, bool mipmaps)
        {
            return new Texture2D(Map.Width, Map.Height, TextureFormat.RGBA32, mipmaps, linear)
            {
                name = label, hideFlags = HideFlags.DontSave, filterMode = filter, wrapMode = TextureWrapMode.Clamp
            };
        }

        private void FillTextures(BiomeMap biomes)
        {
            var width = Map.Width;
            var height = Map.Height;
            var raster = Map.ProvinceAt;
            var colourPixels = colours.GetRawTextureData<Color32>();
            var indexPixels = regions.GetRawTextureData<Color32>();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var cell = y * width + x;
                    var province = raster[cell];
                    if (province < -1 || province >= Map.Provinces.Length)
                        throw new InvalidOperationException("The saved map contains an invalid province index.");

                    // Draw each shared edge on one side. Also outline the northern and eastern map limits.
                    var border = (x > 0 && raster[cell - 1] != province)
                        || (y > 0 && raster[cell - width] != province)
                        || (province >= 0 && (x == 0 || y == 0 || x == width - 1 || y == height - 1));
                    var kind = SurfaceKind(cell, biomes);
                    var rgb = BiomeMap.PreviewColour(kind);
                    colourPixels[cell] = new Color32(rgb.R, rgb.G, rgb.B, border ? (byte)255 : (byte)0);
                    indexPixels[cell] = EncodeProvince(province);
                }

            colours.Apply(true, true);
            regions.Apply(false, true);
        }

        private BiomeKind SurfaceKind(int cell, BiomeMap biomes)
        {
            if (water[cell] == WaterKind.Lake) return BiomeKind.Lake;
            if (water[cell] == WaterKind.Ocean) return biomes?[cell] ?? BiomeKind.DeepOcean;
            if (rivers != null && rivers[cell] != 0) return BiomeKind.River;
            return biomes?[cell] ?? BiomeKind.Grassland;
        }

        public int CellAt(Vector2 uv)
        {
            var x = Mathf.Clamp(Mathf.FloorToInt(uv.x * Map.Width), 0, Map.Width - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt(uv.y * Map.Height), 0, Map.Height - 1);
            return y * Map.Width + x;
        }

        public bool IsDryLand(int cell) => water[cell] == WaterKind.Land && (rivers == null || rivers[cell] == 0);

        public void SetSelection(int selected, int founded, Color tribeColour)
        {
            material.SetColor("_TribeColour", tribeColour);
            material.SetVector("_Selected", ShaderIndex(selected));
            material.SetVector("_Founded", ShaderIndex(founded));
        }

        private static Vector4 ShaderIndex(int province)
        {
            var rgb = EncodeProvince(province);
            return new Vector4(rgb.r / 255f, rgb.g / 255f, rgb.b / 255f, province >= 0 ? 1f : 0f);
        }

        private static Color32 EncodeProvince(int province)
        {
            var id = province + 1;
            return new Color32((byte)(id & 255), (byte)((id >> 8) & 255), (byte)((id >> 16) & 255), 255);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(surface);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(colours);
            UnityEngine.Object.Destroy(regions);
        }
    }
}
