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
        private Texture2D ownership;
        private MapBorderOverlay borders;
        private GameObject surface;
        private WaterMap water;
        private byte[] rivers;

        public ProvinceMap Map { get; }
        public bool HasTerritories => borders.HasTerritories;
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
                var size = Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(Map.Provinces.Length + 1)));
                ownership = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "Province ownership", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                material.SetTexture("_Ownership", ownership);
                material.SetVector("_OwnershipSize", new Vector4(size, size, 1f / size, 1f / size));
                SetOwnership(null, Color.white);

                surface = new GameObject("Map surface", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))
                    { hideFlags = HideFlags.DontSave };
                surface.GetComponent<MeshFilter>().sharedMesh = mesh;
                surface.GetComponent<MeshRenderer>().sharedMaterial = material;
                var collider = surface.GetComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                Collider = collider;
                borders = new MapBorderOverlay(surface, mesh, Map);
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
            var raster = Map.ProvinceAt;
            var colourPixels = colours.GetRawTextureData<Color32>();
            var indexPixels = regions.GetRawTextureData<Color32>();
            for (var cell = 0; cell < raster.Length; cell++)
            {
                var province = raster[cell];
                if (province < -1 || province >= Map.Provinces.Length)
                    throw new InvalidOperationException("The saved map contains an invalid province index.");

                var rgb = BiomeMap.PreviewColour(SurfaceKind(cell, biomes));
                colourPixels[cell] = new Color32(rgb.R, rgb.G, rgb.B, 255);
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

        public Vector3 PointAtCell(int cell)
        {
            var x = ((cell % Map.Width + .5f) / Map.Width - .5f) * Aspect;
            var z = (cell / Map.Width + .5f) / Map.Height - .5f;
            var origin = new Vector3(x, mesh.bounds.max.y + 1f, z);
            if (!Collider.Raycast(new Ray(origin, Vector3.down), out var hit, origin.y + 1f))
                throw new InvalidOperationException("The capital is outside the playable terrain.");
            return hit.point;
        }

        public void SetView(MapViewMode mode)
        {
            borders.SetView(mode);
            material.SetFloat("_ShowPolitics", mode == MapViewMode.Terrain ? 0f : 1f);
        }

        public void SetSelection(int selected) => material.SetVector("_Selected", ShaderIndex(selected));

        public void SetOwnership(CampaignState campaign, Color tribeColour)
        {
            var pixels = ownership.GetRawTextureData<Color32>();
            pixels[0] = new Color32(0, 0, 0, 255); // Unassigned pixels use index zero.
            for (var i = 0; i < Map.Provinces.Length; i++)
                pixels[i + 1] = new Color32(campaign?.Owns(i) == true ? (byte)255 : (byte)0, 0, 0, 255);
            ownership.Apply(false, false);
            material.SetColor("_TribeColour", tribeColour);
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
            borders?.Dispose();
            UnityEngine.Object.Destroy(surface);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(colours);
            UnityEngine.Object.Destroy(regions);
            UnityEngine.Object.Destroy(ownership);
        }
    }
}
