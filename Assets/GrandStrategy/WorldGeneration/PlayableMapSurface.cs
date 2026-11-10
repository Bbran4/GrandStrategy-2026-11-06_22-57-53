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
        private Texture2D ownership;
        private MapViewMode viewMode = MapViewMode.Provinces;
        private readonly ProvinceShape provinceShape;
        private Texture2D biomeSurface;
        private Texture2D riverFlow;
        private bool[] navigableRiverProvinces;
        private Texture2D groundPatterns;
        private Texture2D landTint;
        private Texture2D landMaterialsLow;
        private Texture2D landMaterialsHigh;
        private Texture2D terrainShadow;
        private MapNature nature;
        private MapTerrainGeometry geometry;
        private MapSettlement settlement;
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
            water = WaterMap.Classify(terrain);
            rivers = riverMap?.BuildSurfaceDirections();
            var settings = world.ReadClimateSettings();
            var biomes = world.HasBiomes && settings != null
                ? BiomeGenerator.Generate(ClimateGenerator.Generate(terrain, water, settings), water, riverMap)
                : null;
            Map = provinces.Map.WithSettlementGeography(water, rivers, biomes?.Samples);
            provinceShape = new ProvinceShape(Map.Width, Map.Height, Map.ProvinceAt);

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
                if (rivers != null)
                    riverFlow = CreateTexture("River flow and coverage", true, FilterMode.Trilinear, true);
                groundPatterns = MapGroundPatterns.Create();
                FillTextures(biomes, terrain);
                material = new Material(groundMaterial) { hideFlags = HideFlags.DontSave };
                material.SetTexture("_MapColours", colours);
                material.SetTexture("_BiomeSurface", biomeSurface);
                var flowTexture = riverFlow != null ? riverFlow : Texture2D.blackTexture;
                material.SetTexture("_RiverFlow", flowTexture);
                material.SetTexture("_GroundPatterns", groundPatterns);
                material.SetTexture("_LandTint", landTint);
                material.SetTexture("_LandMaterialsLow", landMaterialsLow);
                material.SetTexture("_LandMaterialsHigh", landMaterialsHigh);
                var biomeColours = new Vector4[8];
                for (var i = 0; i < biomeColours.Length; i++)
                {
                    var rgb = BiomeMap.PreviewColour((BiomeKind)((int)BiomeKind.Wetland + i));
                    Color colour = new Color32(rgb.R, rgb.G, rgb.B, 255);
                    biomeColours[i] = QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour;
                }
                material.SetVectorArray("_BiomeViewPalette", biomeColours);
                var shallowSea = MapGroundPatterns.Palette(BiomeKind.ShallowSea);
                Color coastalWater = new Color32(shallowSea.R, shallowSea.G, shallowSea.B, 255);
                material.SetVector("_CoastalWaterColour",
                    QualitySettings.activeColorSpace == ColorSpace.Linear ? coastalWater.linear : coastalWater);
                material.SetVector("_GroundGrid", new Vector4(Map.Width, Map.Height, 1f / Aspect, 0f));
                var maximumReliefHeight = world.HeightScale * (1f - terrain.Recipe.SeaLevel);
                material.SetFloat("_InverseReliefHeight", 1f / Mathf.Max(maximumReliefHeight, .0001f));
                material.SetTexture("_ProvinceIndices", regions);
                material.SetTexture("_OwnershipMask", Texture2D.blackTexture);
                material.SetFloat("_ShowOwnership", 0f);
                CreateProvinceCornerTexture();
                surface = new GameObject("Map surface", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))
                    { hideFlags = HideFlags.DontSave };
                surface.GetComponent<MeshFilter>().sharedMesh = mesh;
                var collider = surface.GetComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                Collider = collider;
                geometry = new MapTerrainGeometry(mesh);
                terrainShadow = geometry.CreateSunShadow();
                material.SetTexture("_TerrainShadow", terrainShadow);
                var sun = geometry.SunDirection;
                material.SetVector("_MapSun", new Vector4(sun.x, sun.y, sun.z, 0f));
                borders = new MapBorderOverlay(surface, geometry, Map, provinceShape, IsDryLand, IsRiver,
                    cell => water[cell] == WaterKind.Lake, IsNavigableRiverCell);
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
            if (riverFlow != null) FillRiverFlow();
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
                // Centre the filtered channel at 128: land snow is positive and lake ice is negative.
                // River and ocean texels are neutral, so filtered land snow cannot produce river ice.
                var snowOrIce = dry ? SnowAt(cell, terrain, biomes)
                    : kind == BiomeKind.Lake ? -MapNatureRules.LakeIce(biomes?.Climate.TemperatureCelsius(cell)) : 0f;
                surfacePixels[cell] = new Color32(
                    (byte)kind,
                    (byte)Mathf.RoundToInt(shoal * 255f),
                    kind == BiomeKind.Lake || kind == BiomeKind.River ? (byte)255 : (byte)0,
                    (byte)(128 + Mathf.RoundToInt(snowOrIce * 127f)));
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

        private void FillRiverFlow()
        {
            var pixels = riverFlow.GetRawTextureData<Color32>();
            navigableRiverProvinces = new bool[Map.Provinces.Length];
            var directions = new Color32[9];
            directions[0] = new Color32(128, 128, 0, 0);
            for (var i = 1; i < directions.Length; i++)
            {
                var angle = (i - 1) * Mathf.PI * .25f;
                directions[i] = new Color32((byte)(128 + Mathf.RoundToInt(Mathf.Cos(angle) * 127f)),
                    (byte)(128 + Mathf.RoundToInt(Mathf.Sin(angle) * 127f)), 255, 0);
            }
            for (var cell = 0; cell < rivers.Length; cell++)
            {
                var pixel = directions[IsRiver(cell) ? rivers[cell] : 0];
                if (IsNavigableRiverCell(cell))
                {
                    pixel.a = 255;
                    navigableRiverProvinces[Map.ProvinceAt[cell]] = true;
                }
                pixels[cell] = pixel;
            }
            riverFlow.Apply(true, true);
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

        private bool IsRiver(int cell) => rivers != null && rivers[cell] != 0 && water[cell] == WaterKind.Land;

        private bool IsNavigableRiverCell(int cell)
        {
            if (!IsRiver(cell)) return false;
            var province = Map.ProvinceAt[cell];
            return province >= 0 && province < Map.Provinces.Length
                && string.Equals(Map.Provinces[province].Type, "ocean", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsNavigableRiverProvince(int province)
            => navigableRiverProvinces != null && province >= 0 && province < navigableRiverProvinces.Length
                && navigableRiverProvinces[province];

        public bool IsDryLand(int cell) => water[cell] == WaterKind.Land && !IsRiver(cell);

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

        public void SetCapital(Vector3 point, bool granary, bool expanded)
        {
            settlement ??= new MapSettlement(surface.transform, geometry, terrainShadow, Aspect, IsDryLandAt);
            settlement.Show(point, granary, expanded);
            nature.SetCapital(point);
            material.SetVector("_CapitalClearing", new Vector4(point.x, point.z, MapNature.CapitalRadius, 0f));
        }

        private bool IsDryLandAt(Vector2 uv) => IsDryLand(CellAt(uv));

        public void SetView(MapViewMode mode)
        {
            borders.SetView(mode);
            viewMode = mode;
            UpdateOwnershipVisibility();
            material.SetFloat("_ShowPolitics", mode == MapViewMode.Terrain || mode == MapViewMode.Biomes ? 0f : 1f);
            material.SetFloat("_ShowBiomes", mode == MapViewMode.Biomes ? 1f : 0f);
        }

        public void SetOwnership(CampaignState campaign, Color colour)
        {
            if (ownership == null)
            {
                var size = Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(Map.Provinces.Length + 1)));
                ownership = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "Active campaign ownership", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = ownership.GetRawTextureData<Color32>();
            // Index zero is unassigned. Clear every entry before publishing a loaded or updated campaign.
            for (var index = 0; index < pixels.Length; index++)
            {
                var province = index - 1;
                pixels[index] = new Color32(campaign.Owns(province) && CampaignState.IsLand(Map, province)
                    ? (byte)255 : (byte)0, 0, 0, 255);
            }
            ownership.Apply(false, false);
            var width = ownership.width;
            material.SetTexture("_OwnershipMask", ownership);
            material.SetVector("_OwnershipGrid", new Vector4(width, 1f / width, Map.Provinces.Length, 0f));
            material.SetVector("_OwnershipColour", QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour);
            UpdateOwnershipVisibility();
        }

        private void UpdateOwnershipVisibility()
            => material.SetFloat("_ShowOwnership", viewMode == MapViewMode.Ownership && ownership != null ? 1f : 0f);

        public void SetSelection(int selected)
            => material.SetVector("_Selected", ShaderIndex(CampaignState.IsLand(Map, selected) ? selected : -1));

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
            settlement?.Dispose();
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
            UnityEngine.Object.Destroy(riverFlow);
            UnityEngine.Object.Destroy(groundPatterns);
            UnityEngine.Object.Destroy(landTint);
            UnityEngine.Object.Destroy(landMaterialsLow);
            UnityEngine.Object.Destroy(landMaterialsHigh);
            UnityEngine.Object.Destroy(terrainShadow);
        }
    }
}
