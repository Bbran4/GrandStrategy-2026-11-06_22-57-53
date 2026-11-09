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
        private Texture2D provinceCorners;
        private readonly ProvinceShape provinceShape;
        private Texture2D ownership;
        private Texture2D biomeSurface;
        private Texture2D groundPatterns;
        private Texture2D landTint;
        private Texture2D landMaterialsLow;
        private Texture2D landMaterialsHigh;
        private Texture2D terrainShadow;
        private MapNature nature;
        private float groundDetail;
        private MapBorderOverlay borders;
        private GameObject surface;
        private WaterMap water;
        private byte[] rivers;

        public ProvinceMap Map { get; }
        public bool HasTerritories => borders.HasTerritories;
        public float Aspect => (float)Map.Width / Map.Height;
        public Collider Collider { get; private set; }

        public PlayableMapSurface(WorldHeightmap world, ProvinceMapAsset provinces, MapTreeCatalog treeCatalog)
        {
            var imported = provinces.ReadTerrain();
            var riverMap = world.ReadRivers();
            var terrain = riverMap?.Terrain ?? world.Read();
            ValidatePair(terrain, imported);
            Map = provinces.Map;
            provinceShape = new ProvinceShape(Map.Width, Map.Height, Map.ProvinceAt);
            water = WaterMap.Classify(terrain);
            rivers = riverMap?.BuildSurfaceMask();
            var settings = world.ReadClimateSettings();
            var biomes = world.HasBiomes && settings != null
                ? BiomeGenerator.Generate(ClimateGenerator.Generate(terrain, water, settings), water, riverMap)
                : null;

            try
            {
                var groundMaterial = Resources.Load<Material>("PlayableMapGround");
                if (groundMaterial == null)
                    throw new InvalidOperationException("The playable map ground material is missing.");
                if (groundMaterial.shader == null || !groundMaterial.shader.isSupported)
                    throw new InvalidOperationException("The playable map shader requires Universal Render Pipeline.");

                mesh = HeightmapMesh.Create(terrain, world.HeightScale, 512);
                colours = CreateTexture("Base colours and land coverage", false, FilterMode.Trilinear, true);
                regions = CreateTexture("Province indices", true, FilterMode.Point, false);
                biomeSurface = CreateTexture("Biome IDs, ocean shoals, freshwater and snow", true, FilterMode.Trilinear, true);
                groundPatterns = MapGroundPatterns.Create();
                FillTextures(biomes, terrain);
                material = new Material(groundMaterial) { hideFlags = HideFlags.DontSave };
                material.SetTexture("_MapColours", colours);
                material.SetTexture("_BiomeSurface", biomeSurface);
                material.SetTexture("_GroundPatterns", groundPatterns);
                material.SetTexture("_LandTint", landTint);
                material.SetTexture("_LandMaterialsLow", landMaterialsLow);
                material.SetTexture("_LandMaterialsHigh", landMaterialsHigh);
                material.SetVector("_GroundGrid", new Vector4(Map.Width, Map.Height, 1f / Aspect, 0f));
                material.SetTexture("_ProvinceIndices", regions);
                CreateProvinceCornerTexture();
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
                var collider = surface.GetComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                Collider = collider;
                var geometry = new MapTerrainGeometry(mesh);
                terrainShadow = geometry.CreateSunShadow();
                material.SetTexture("_TerrainShadow", terrainShadow);
                var sun = geometry.SunDirection;
                material.SetVector("_MapSun", new Vector4(sun.x, sun.y, sun.z, 0f));
                borders = new MapBorderOverlay(surface, geometry, Map, provinceShape, IsDryLand);
                nature = new MapNature(surface, geometry, this, terrain.Recipe.Seed, treeCatalog,
                    cell => SurfaceKind(cell, biomes), cell => SnowAt(cell, terrain, biomes),
                    cell => biomes?.Climate.TemperatureCelsius(cell) ?? 12f,
                    cell => biomes == null ? .55f : biomes.Climate.Moisture[cell] / 255f);
                nature.SetSunShadow(terrainShadow, sun, Aspect);
                material.SetTexture("_ForestCanopy", nature.Canopy);
                foreach (var slot in material.GetTexturePropertyNames())
                {
                    if (material.GetTexture(slot) == null)
                        throw new InvalidOperationException($"The playable map material is missing a texture for {slot}.");
                }
                surface.GetComponent<MeshRenderer>().sharedMaterial = material;
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

        private void FillTextures(BiomeMap biomes, HeightmapResult terrain)
        {
            var raster = Map.ProvinceAt;
            var colourPixels = colours.GetRawTextureData<Color32>();
            var indexPixels = regions.GetRawTextureData<Color32>();
            var surfacePixels = biomeSurface.GetRawTextureData<Color32>();
            var deep = MapGroundPatterns.Palette(BiomeKind.DeepOcean);
            var shallow = MapGroundPatterns.Palette(BiomeKind.ShallowSea);
            var deepWater = new Color32(deep.R, deep.G, deep.B, 0);
            var shallowWater = new Color32(shallow.R, shallow.G, shallow.B, 0);
            var seaLevel = terrain.Recipe.SeaLevel;
            var inverseSeaLevel = 1f / Mathf.Max(seaLevel, .0001f);
            for (var cell = 0; cell < raster.Length; cell++)
            {
                var province = raster[cell];
                if (province < -1 || province >= Map.Provinces.Length)
                    throw new InvalidOperationException("The saved map contains an invalid province index.");

                var kind = SurfaceKind(cell, biomes);
                var dry = IsDryLand(cell);
                // Ocean shading uses depth. The discrete biome ID still describes the saved map.
                var shoal = 0f;
                if (water[cell] == WaterKind.Ocean)
                {
                    var depth = (seaLevel - terrain.Elevations[cell]) * inverseSeaLevel;
                    shoal = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.025f, .58f, depth));
                    colourPixels[cell] = Color32.Lerp(deepWater, shallowWater, shoal);
                }
                else
                {
                    var rgb = MapGroundPatterns.Palette(kind);
                    colourPixels[cell] = new Color32(rgb.R, rgb.G, rgb.B, dry ? (byte)255 : (byte)0);
                }
                surfacePixels[cell] = new Color32(
                    (byte)kind,
                    (byte)Mathf.RoundToInt(shoal * 255f),
                    kind == BiomeKind.Lake || kind == BiomeKind.River ? (byte)255 : (byte)0,
                    (byte)Mathf.RoundToInt((dry ? SnowAt(cell, terrain, biomes) :
                        water[cell] == WaterKind.Lake ? MapNatureRules.LakeIce(biomes?.Climate.TemperatureCelsius(cell)) : 0f) * 255f));
                var indexColour = EncodeProvince(province);
                indexColour.a = provinceShape.CellMoved(cell % Map.Width, cell / Map.Width) ? (byte)255 : (byte)0;
                indexPixels[cell] = indexColour;
            }

            MapGroundBlend.Bake(Map.Width, Map.Height, colourPixels, surfacePixels,
                out landTint, out landMaterialsLow, out landMaterialsHigh);
            colours.Apply(true, true);
            regions.Apply(false, true);
            biomeSurface.Apply(true, true);
        }

        private void CreateProvinceCornerTexture()
        {
            var width = Map.Width + 1;
            var height = Map.Height + 1;
            provinceCorners = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "Province corner offsets", hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = provinceCorners.GetRawTextureData<Color32>();
            for (var i = 0; i < provinceShape.CornerCount; i++)
            {
                var offset = provinceShape.PackedCorner(i);
                pixels[i] = new Color32((byte)(offset & 255), (byte)(offset >> 8), 0, 255);
            }

            provinceCorners.Apply(false, true);
            material.SetTexture("_ProvinceCorners", provinceCorners);
            material.SetVector("_ProvinceGrid", new Vector4(Map.Width, Map.Height, 1f / width, 1f / height));
        }

        private static float SnowAt(int cell, HeightmapResult terrain, BiomeMap biomes)
            => MapNatureRules.Snow(terrain.Elevations[cell], terrain.Recipe.SeaLevel, biomes?.Climate.TemperatureCelsius(cell));

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

        public int ProvinceCellAt(Vector2 uv)
            => provinceShape.CellAt(Mathf.Clamp01(uv.x) * Map.Width, Mathf.Clamp01(uv.y) * Map.Height);

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

        public void SetZoom(float zoom)
        {
            // Retain broad albedo variation in the overview. Mipmaps remove fine texture detail.
            var next = Mathf.SmoothStep(1f, .25f, Mathf.InverseLerp(.28f, 1f, zoom));
            if (groundDetail != next)
            {
                groundDetail = next;
                material.SetFloat("_GroundDetail", groundDetail);
            }
            nature.SetZoom(zoom);
        }

        public void SetCapital(Vector3 point)
        {
            nature.SetCapital(point);
            material.SetVector("_CapitalClearing", new Vector4(point.x, point.z, MapNature.CapitalRadius, 0f));
        }

        public void SetView(MapViewMode mode)
        {
            borders.SetView(mode);
            material.SetFloat("_ShowPolitics", mode == MapViewMode.Terrain ? 0f : 1f);
            material.SetFloat("_RealmFill", mode == MapViewMode.Realms ? .68f : .41f);
        }

        public void SetSelection(int selected)
            => material.SetVector("_Selected", ShaderIndex(CampaignState.IsLand(Map, selected) ? selected : -1));

        public void SetOwnership(CampaignState campaign, Color tribeColour)
        {
            var pixels = ownership.GetRawTextureData<Color32>();
            pixels[0] = new Color32(0, 0, 0, 255); // Unassigned pixels use index zero.
            for (var i = 0; i < Map.Provinces.Length; i++)
                pixels[i + 1] = new Color32(campaign?.Owns(i) == true ? (byte)255 : (byte)0, 0, 0, 255);
            ownership.Apply(false, false);
            material.SetColor("_TribeColour", tribeColour);
            material.SetFloat("_HasCampaign", campaign == null ? 0f : 1f);
            borders?.SetRealm(campaign, tribeColour);
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
            nature?.Dispose();
            borders?.Dispose();
            UnityEngine.Object.Destroy(surface);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(colours);
            UnityEngine.Object.Destroy(regions);
            UnityEngine.Object.Destroy(provinceCorners);
            UnityEngine.Object.Destroy(ownership);
            UnityEngine.Object.Destroy(biomeSurface);
            UnityEngine.Object.Destroy(groundPatterns);
            UnityEngine.Object.Destroy(landTint);
            UnityEngine.Object.Destroy(landMaterialsLow);
            UnityEngine.Object.Destroy(landMaterialsHigh);
            UnityEngine.Object.Destroy(terrainShadow);
        }
    }
}
