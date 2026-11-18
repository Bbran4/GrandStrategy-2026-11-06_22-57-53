// Builds and owns the runtime map mesh, collider, material textures, borders and scenery. It also maps
// clicks and placements back to the province raster.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal sealed class PlayableMapSurface : IDisposable
    {
        private const float Raw16Tolerance = 1f / 65535f;

        private Mesh mesh;
        private Material material;
        private Material footprintMaterial;
        private Material warbandMaterial;
        private GameObject warbandMarker;
        private LineRenderer marchLine;
        private IReadOnlyList<int> displayedMarchRoute;
        private int displayedMarchIndex = -1;
        private int markerCell = -1;
        private LineRenderer footprint;
        private int footprintCell = -1;
        private float footprintWidth;
        private Texture2D colours;
        private Texture2D regions;
        private Texture2D provinceCorners;
        private Texture2D ownership;
        private Texture2D productionSites;
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
        private readonly Dictionary<int, MapSettlement> villages = new Dictionary<int, MapSettlement>();
        private float groundDetail;
        private MapBorderOverlay borders;
        private GameObject surface;
        private WaterMap water;
        private byte[] rivers;
        private float[] elevations;
        private float seaLevel;
        private byte[] settlementBiomes;
        private CampaignState influenceCampaign;
        private int[] influenceRoots;
        private int highlightedInfluenceRoot;
        private float[] influenceCosts;
        private float displayedMilitaryPressure;
        private CampaignState resourceMapCampaign;
        private int influenceFoodPeriod = -1;

        public ProvinceMap Map { get; }
        public bool HasTerritories => borders.HasTerritories;
        public float Aspect => (float)Map.Width / Map.Height;
        public Collider Collider { get; private set; }

        // Validate the world and province pair first, then build rendering and geography from the same
        // underlying world samples.
        public PlayableMapSurface(WorldHeightmap world, ProvinceMapAsset provinces, MapTreeCatalog treeCatalog,
            TerrainData authored = null, int authoredMeshDetail = 1024)
        {
            var imported = provinces.ReadTerrain();
            var riverMap = world.ReadRivers();
            var terrain = riverMap?.Terrain ?? world.Read();
            ValidatePair(terrain, imported);
            elevations = terrain.Elevations;
            seaLevel = terrain.Recipe.SeaLevel;
            water = WaterMap.Classify(terrain);
            rivers = riverMap?.BuildSurfaceDirections();
            var settings = world.ReadClimateSettings();
            var biomes = world.HasBiomes && settings != null
                ? BiomeGenerator.Generate(ClimateGenerator.Generate(terrain, water, settings), water, riverMap)
                : null;
            settlementBiomes = biomes?.Samples;
            Map = provinces.Map.WithSettlementGeography(water, rivers, biomes?.Samples);
            Map.BuildMarchTerrain(water, rivers, elevations);
            Map.LocalProductionYield = LocalProductionYield;
            provinceShape = new ProvinceShape(Map.Width, Map.Height, Map.ProvinceAt);

            try
            {
                var groundMaterial = Resources.Load<Material>("PlayableMapGround");
                if (groundMaterial == null)
                    throw new InvalidOperationException("The playable map ground material is missing.");
                if (groundMaterial.shader == null || !groundMaterial.shader.isSupported)
                    throw new InvalidOperationException("The playable map shader requires Universal Render Pipeline.");

                mesh = HeightmapMesh.Create(terrain, world.HeightScale, authored == null ? 512 : Mathf.Clamp(authoredMeshDetail, 512, 2048),
                    authored, water, rivers);
                colours = CreateTexture("Base colours and land coverage", false, FilterMode.Trilinear, true);
                regions = CreateTexture("Province indices", true, FilterMode.Point, false);
                biomeSurface = CreateTexture("Biome IDs, ocean shoals, freshwater and snow", true, FilterMode.Trilinear, true);
                if (rivers != null)
                    riverFlow = CreateTexture("River flow and coverage", true, FilterMode.Trilinear, true);
                groundPatterns = MapGroundPatterns.Create();
                FillTextures(biomes, terrain);
                material = new Material(groundMaterial) { hideFlags = HideFlags.DontSave };
                material.SetTexture("_MapColours", colours);
                BindAuthoredPaint(material, authored, world.HeightScale);
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

        // Bind the editor Terrain's first control texture and its three paint layers. The base channel
        // leaves the generated material visible.
        private void BindAuthoredPaint(Material target, TerrainData authored, float heightScale)
        {
            var size = new Vector3(Aspect, heightScale, 1f) * PlayableMap.TerrainAuthoringScale;
            if (authored != null && (authored.size - size).sqrMagnitude > 0.0001f)
                throw new InvalidOperationException("Restore the scene Terrain's original size to keep it aligned with the map.");

            var layers = authored == null ? null : authored.terrainLayers;
            if (layers?.Length > 4)
                throw new InvalidOperationException("The scene Terrain supports the base layer and up to three painted layers.");

            var hasPaint = layers?.Length > 1 && authored.alphamapTextureCount > 0;
            target.SetTexture("_AuthoredControl", hasPaint ? authored.GetAlphamapTexture(0) : Texture2D.blackTexture);
            for (var i = 1; i <= 3; i++)
            {
                var layer = layers != null && i < layers.Length ? layers[i] : null;
                target.SetTexture($"_AuthoredColour{i}", layer?.diffuseTexture ?? Texture2D.grayTexture);
                target.SetTexture($"_AuthoredNormal{i}", layer?.normalMapTexture ?? Texture2D.normalTexture);
                var tile = layer?.tileSize ?? Vector2.one;
                var offset = layer?.tileOffset ?? Vector2.zero;
                tile = new Vector2(Mathf.Max(.0001f, tile.x), Mathf.Max(.0001f, tile.y));
                target.SetVector($"_AuthoredTile{i}", new Vector4(size.x / tile.x, size.z / tile.y,
                    offset.x / tile.x, offset.y / tile.y));
            }
            if (hasPaint) target.EnableKeyword("_AUTHORED_TERRAIN");
        }

        // Check that the province export describes the same terrain as the World asset, allowing for RAW16
        // rounding.
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

        // Pack the colour, province IDs, biome and water data used by the shader into separate textures.
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

        // Encode river directions and the river footprint for the animated water shading.
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

        // Upload corner offsets so the shader follows the same smoothed cells as CPU picking.
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

        // Convert a raster cell to map coordinates and find its height on the playable surface.
        public Vector3 PointAtCell(int cell)
        {
            var x = ((cell % Map.Width + .5f) / Map.Width - .5f) * Aspect;
            var z = (cell / Map.Width + .5f) / Map.Height - .5f;
            var origin = new Vector3(x, mesh.bounds.max.y + 1f, z);
            if (!Collider.Raycast(new Ray(origin, Vector3.down), out var hit, origin.y + 1f))
                throw new InvalidOperationException("The capital is outside the playable terrain.");
            return hit.point;
        }

        // Update render detail and scenery for the current camera scale.
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

        public void SetCityLimitsVisible(CampaignState campaign, int province, int selectedSettlement)
        {
            settlement?.SetBoundaryVisible(campaign != null
                && (province == campaign.Capital || selectedSettlement == campaign.CapitalCell));
            foreach (var village in villages)
                village.Value.SetBoundaryVisible(campaign != null
                    && (province == campaign.SettlementProvince(village.Key) || selectedSettlement == village.Key));
        }

        public void SetVillages(CampaignState campaign)
        {
            ClearVillages();
            foreach (var cell in campaign.SettlementCells) RefreshVillage(campaign, cell);
        }

        private void ClearVillages()
        {
            foreach (var village in villages.Values) village.Dispose();
            villages.Clear();
        }

        public void RefreshVillage(CampaignState campaign, int cell)
        {
            if (!villages.TryGetValue(cell, out var village))
            {
                village = new MapSettlement(surface.transform, geometry, terrainShadow, Aspect, IsDryLandAt);
                villages.Add(cell, village);
            }

            village.Show(geometry.Sample(CellCentre(cell), out _), false, false, null, campaign.SettlementBuildings(cell), building => ProductionPoint(campaign, cell, building), DockWaterPoint(campaign, cell));
        }

        public bool IsWithinVillageLimits(int cell, Vector2 uv)
            => IsWithinCityLimits(geometry.Sample(CellCentre(cell), out _), uv);

        public bool CanSiteVillage(Vector3 capital, int cell)
        {
            if (cell < 0 || !IsDryLand(cell)) return false;
            var uv = CellCentre(cell);
            var point = new Vector2(uv.x * Aspect, uv.y);
            var diameter = 2f * MapSettlement.BuildRadius;
            var capitalPoint = new Vector2(capital.x + Aspect * .5f, capital.z + .5f);
            if ((point - capitalPoint).sqrMagnitude < diameter * diameter) return false;
            foreach (var site in villages.Keys)
            {
                var other = CellCentre(site);
                var separation = point - new Vector2(other.x * Aspect, other.y);
                if (separation.sqrMagnitude < diameter * diameter) return false;
            }
            return true;
        }

        public SettlementBuilding SurveyProductionSites(int centreCell)
        {
            if (centreCell < 0 || !IsDryLand(centreCell)) return 0;
            var centreX = centreCell % Map.Width;
            var centreY = centreCell / Map.Width;
            var radius = MapSettlement.BuildRadius * Map.Height;
            var reach = Mathf.CeilToInt(radius);
            SettlementBuilding result = 0;

            for (var y = Mathf.Max(0, centreY - reach); y <= Mathf.Min(Map.Height - 1, centreY + reach); y++)
                for (var x = Mathf.Max(0, centreX - reach); x <= Mathf.Min(Map.Width - 1, centreX + reach); x++)
                {
                    var dx = x - centreX;
                    var dy = y - centreY;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    var cell = y * Map.Width + x;
                    result |= ProductionSitesAt(cell, x, y);
                    if ((result & SettlementBuilding.Docks) == 0 && IsDryLand(cell) && Map.DockWaterwayAt(cell) > 0)
                        result |= SettlementBuilding.Docks;
                }
            return result;
        }

        private SettlementBuilding ProductionSitesAt(int cell, int x, int y)
        {
            if (!IsDryLand(cell)) return 0;
            var biome = settlementBiomes == null ? BiomeKind.Grassland : (BiomeKind)settlementBiomes[cell];
            SettlementBuilding sites = 0;
            if (biome == BiomeKind.Grassland || biome == BiomeKind.Wetland || MapNatureRules.IsForest(biome))
                sites |= SettlementBuilding.Farm;
            if (settlementBiomes == null || MapNatureRules.IsForest(biome))
                sites |= SettlementBuilding.LumberCamp;
            if (biome == BiomeKind.Alpine || elevations[cell] >= Mathf.Lerp(seaLevel, 1f, .62f))
                sites |= SettlementBuilding.IronMine;
            if ((x > 0 && IsTradeWater(cell - 1)) || (x + 1 < Map.Width && IsTradeWater(cell + 1))
                || (y > 0 && IsTradeWater(cell - Map.Width))
                || (y + 1 < Map.Height && IsTradeWater(cell + Map.Width)))
                sites |= SettlementBuilding.Docks;
            return sites;
        }

        private int? LocalProductionYield(int cell, SettlementBuilding building)
        {
            if (building == SettlementBuilding.Docks) return null;
            if (cell < 0 || cell >= Map.ProvinceAt.Length || !IsDryLand(cell)) return 0;
            if (settlementBiomes == null && building == SettlementBuilding.LumberCamp) return null;
            if ((ProductionSitesAt(cell, cell % Map.Width, cell / Map.Width) & building) == 0) return 0;

            var biome = settlementBiomes == null ? BiomeKind.Grassland : (BiomeKind)settlementBiomes[cell];
            return building switch
            {
                SettlementBuilding.Farm => biome == BiomeKind.Wetland ? 3 : biome == BiomeKind.Grassland ? 2 : 1,
                SettlementBuilding.LumberCamp => biome == BiomeKind.TropicalForest ? 3 : 2,
                SettlementBuilding.IronMine => elevations[cell] >= Mathf.Lerp(seaLevel, 1f, .78f) ? 2 : 1,
                _ => null
            };
        }

        private bool IsTradeWater(int cell) => water[cell] == WaterKind.Ocean || IsNavigableRiverCell(cell);

        public void SetResourceFilter(SettlementBuilding building)
        {
            var filter = building switch
            {
                SettlementBuilding.LumberCamp => new Vector4(0, 1, 0, 0),
                SettlementBuilding.IronMine => new Vector4(0, 0, 1, 0),
                SettlementBuilding.Docks => new Vector4(0, 0, 0, 1),
                0 => Vector4.zero,
                _ => new Vector4(1, 0, 0, 0)
            };
            var colour = building switch
            {
                SettlementBuilding.LumberCamp => new Color(.28f, .83f, .33f),
                SettlementBuilding.IronMine => new Color(.87f, .43f, .29f),
                SettlementBuilding.Docks => new Color(.29f, .72f, 1f),
                0 => new Color(.82f, .90f, 1f),
                _ => new Color(.94f, .76f, .25f)
            };
            material.SetVector("_ResourceFilter", filter);
            material.SetColor("_ResourceTint", colour);
        }

        private void CreateProductionSiteTexture()
        {
            if (productionSites != null) return;
            productionSites = CreateTexture("Production sites", true, FilterMode.Point, false);
            var pixels = productionSites.GetRawTextureData<Color32>();
            for (var y = 0; y < Map.Height; y++)
                for (var x = 0; x < Map.Width; x++)
                {
                    var cell = y * Map.Width + x;
                    var sites = ProductionSitesAt(cell, x, y);
                    pixels[cell] = new Color32(
                        (sites & SettlementBuilding.Farm) != 0 ? (byte)255 : (byte)0,
                        (sites & SettlementBuilding.LumberCamp) != 0 ? (byte)255 : (byte)0,
                        (sites & SettlementBuilding.IronMine) != 0 ? (byte)255 : (byte)0,
                        (sites & SettlementBuilding.Docks) != 0 ? (byte)255 : Map.IsFord(cell) ? FordResourceCode(cell) : (byte)0);
                }
            productionSites.Apply(false, false);
            material.SetTexture("_ProductionSites", productionSites);
            resourceMapCampaign = influenceCampaign;
        }

        public bool IsWithinCityLimits(Vector3 capital, Vector2 uv)
            => IsDryLandAt(uv) && MapSettlement.Contains(capital,
                new Vector3((uv.x - .5f) * Aspect, 0f, uv.y - .5f));

        private byte FordResourceCode(int cell)
            => influenceCampaign?.IsFordSecured(cell) == true ? (byte)176 : (byte)128;

        public void RefreshSecuredFord(int cell)
        {
            if (productionSites == null) return;
            var pixels = productionSites.GetRawTextureData<Color32>();
            var pixel = pixels[cell];
            pixel.a = FordResourceCode(cell);
            pixels[cell] = pixel;
            productionSites.Apply(false, false);
        }

        public List<int> FindWarbandRoute(int start, int goal)
        {
            if (!Map.IsMarchCell(start) || !Map.IsMarchCell(goal) || start == goal) return null;
            var fordDestination = Map.IsFord(goal);

            var frontier = new SortedSet<(int Estimate, int Cell)> { (Distance(start), start) };
            var costs = new Dictionary<int, int> { [start] = 0 };
            var previous = new Dictionary<int, int>();
            while (frontier.Count > 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);
                var cell = current.Cell;
                if (fordDestination ? Map.CanMarchBetween(cell, goal) : cell == goal)
                {
                    var route = new List<int> { cell };
                    while (cell != start)
                    {
                        cell = previous[cell];
                        route.Add(cell);
                    }
                    route.Reverse();
                    return route;
                }

                var cost = costs[cell] + 1;
                var x = cell % Map.Width;
                if (x > 0) Visit(cell - 1);
                if (x + 1 < Map.Width) Visit(cell + 1);
                if (cell >= Map.Width) Visit(cell - Map.Width);
                if (cell + Map.Width < Map.ProvinceAt.Length) Visit(cell + Map.Width);

                void Visit(int next)
                {
                    if (!Map.CanMarchBetween(cell, next)) return;
                    var distance = Distance(next);
                    if (costs.TryGetValue(next, out var prior))
                    {
                        if (prior <= cost) return;
                        frontier.Remove((prior + distance, next));
                    }
                    costs[next] = cost;
                    previous[next] = cell;
                    frontier.Add((cost + distance, next));
                }
            }
            return null;

            int Distance(int cell) => Math.Abs(cell % Map.Width - goal % Map.Width)
                + Math.Abs(cell / Map.Width - goal / Map.Width);
        }

        public void ShowWarband(CampaignState campaign)
        {
            var cell = campaign?.WarbandCell ?? -1;
            UpdateMarchLine(campaign?.WarbandRoute, campaign?.WarbandRouteIndex ?? 0);
            if (cell == markerCell) return;
            if (cell < 0)
            {
                if (warbandMarker != null) warbandMarker.SetActive(false);
                markerCell = -1;
                return;
            }

            if (warbandMarker == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported) return;
                warbandMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                warbandMaterial.SetColor("_BaseColor", new Color(.86f, .18f, .13f));
                warbandMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                warbandMarker.name = "Warband marker";
                warbandMarker.hideFlags = HideFlags.DontSave;
                warbandMarker.transform.SetParent(surface.transform, false);
                warbandMarker.transform.localScale = new Vector3(.004f, .0025f, .004f);
                warbandMarker.GetComponent<MeshRenderer>().sharedMaterial = warbandMaterial;
                var collider = warbandMarker.GetComponent<Collider>();
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }

            warbandMarker.SetActive(true);
            UpdateMarchLine(campaign.WarbandRoute, campaign.WarbandRouteIndex);
            warbandMarker.transform.localPosition = geometry.Sample(CellCentre(cell), out _) + Vector3.up * .003f;
            warbandMarker.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            markerCell = cell;
        }

        private void UpdateMarchLine(IReadOnlyList<int> route, int index)
        {
            if (warbandMaterial == null) return;
            if (route == null || route.Count - index < 2)
            {
                if (marchLine != null) marchLine.enabled = false;
                displayedMarchRoute = null;
                displayedMarchIndex = -1;
                return;
            }

            if (ReferenceEquals(displayedMarchRoute, route) && displayedMarchIndex == index) return;
            if (marchLine == null)
            {
                var path = new GameObject("Warband march route", typeof(LineRenderer)) { hideFlags = HideFlags.DontSave };
                path.transform.SetParent(surface.transform, false);
                marchLine = path.GetComponent<LineRenderer>();
                marchLine.useWorldSpace = false;
                marchLine.widthMultiplier = .00034f;
                marchLine.sharedMaterial = warbandMaterial;
            }

            var positions = new Vector3[route.Count - index];
            for (var i = 0; i < positions.Length; i++)
                positions[i] = geometry.Sample(CellCentre(route[index + i]), out _) + Vector3.up * .0012f;
            marchLine.positionCount = positions.Length;
            marchLine.SetPositions(positions);
            marchLine.enabled = true;
            displayedMarchRoute = route;
            displayedMarchIndex = index;
        }

        public void ShowBuildFootprint(int cell, float halfWidth, Color colour)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length)
            {
                if (footprint != null) footprint.enabled = false;
                return;
            }

            if (footprint == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported) return;
                footprintMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                var outline = new GameObject("Building footprint", typeof(LineRenderer)) { hideFlags = HideFlags.DontSave };
                outline.transform.SetParent(surface.transform, false);
                footprint = outline.GetComponent<LineRenderer>();
                footprint.useWorldSpace = false;
                footprint.loop = true;
                footprint.widthMultiplier = .00019f;
                footprint.sharedMaterial = footprintMaterial;
            }

            if (cell != footprintCell || halfWidth != footprintWidth)
            {
                const int samplesPerSide = 8;
                footprint.positionCount = 4 * samplesPerSide;
                var centre = CellCentre(cell);
                var width = halfWidth / Aspect;
                var depth = halfWidth * .77f;
                for (var i = 0; i < footprint.positionCount; i++)
                {
                    var t = (i % samplesPerSide) / (float)samplesPerSide;
                    var offset = (i / samplesPerSide) switch
                    {
                        0 => new Vector2(Mathf.Lerp(-width, width, t), -depth),
                        1 => new Vector2(width, Mathf.Lerp(-depth, depth, t)),
                        2 => new Vector2(Mathf.Lerp(width, -width, t), depth),
                        _ => new Vector2(-width, Mathf.Lerp(depth, -depth, t))
                    };
                    var uv = centre + offset;
                    uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
                    footprint.SetPosition(i, geometry.Sample(uv, out _) + Vector3.up * .0011f);
                }
                footprintCell = cell;
                footprintWidth = halfWidth;
            }
            footprintMaterial.SetColor("_BaseColor", colour);
            footprint.enabled = true;
        }

        private Vector3? DockWaterPoint(CampaignState campaign, int site)
        {
            var dock = campaign.BuildingPlacement(site, SettlementBuilding.Docks);
            var water = Map.DockWaterCell(dock);
            return water < 0 ? (Vector3?)null : geometry.Sample(CellCentre(water), out _);
        }

        private Vector3? ProductionPoint(CampaignState campaign, int site, SettlementBuilding building)
        {
            var cell = campaign.BuildingPlacement(site, building);
            return cell < 0 ? (Vector3?)null : geometry.Sample(CellCentre(cell), out _);
        }

        public bool CanPlaceProduction(CampaignState campaign, int site, int cell, SettlementBuilding building)
        {
            if (cell < 0 || !IsDryLand(cell)) return false;
            var centre = geometry.Sample(CellCentre(site), out _);
            var anchor = geometry.Sample(CellCentre(cell), out _);
            if (!MapSettlement.FitsGranary(centre, anchor) || !HasDryFootprint(CellCentre(cell), MapSettlement.ProductionHalfWidth)
                || ConflictsWithProduction(campaign, site, cell, MapSettlement.ProductionHalfWidth)) return false;

            if (site == campaign.CapitalCell && campaign.HasGranary)
            {
                var granary = campaign.GranaryCell;
                var granaryUv = granary >= 0
                    ? CellCentre(granary)
                    : CellCentre(site) + new Vector2(MapSettlement.GranaryOffset.x / Aspect, MapSettlement.GranaryOffset.y);
                var offset = CellCentre(cell) - granaryUv;
                var clearance = MapSettlement.ProductionHalfWidth + MapSettlement.GranaryHalfWidth;
                if (Mathf.Abs(offset.x * Aspect) < clearance && Mathf.Abs(offset.y) < clearance * .77f)
                    return false;
            }

            return building == SettlementBuilding.Docks ? Map.DockWaterwayAt(cell) > 0
                : (ProductionSitesAt(cell, cell % Map.Width, cell / Map.Width) & building) != 0;
        }

        public bool ConflictsWithProduction(CampaignState campaign, int site, int cell, float halfWidth)
        {
            foreach (var building in new[] { SettlementBuilding.Farm, SettlementBuilding.LumberCamp,
                SettlementBuilding.IronMine, SettlementBuilding.Docks })
            {
                var occupied = campaign.BuildingPlacement(site, building);
                if (occupied >= 0 && Overlaps(cell, halfWidth, occupied, MapSettlement.ProductionHalfWidth)) return true;
            }
            return false;
        }

        private bool Overlaps(int first, float firstWidth, int second, float secondWidth)
        {
            var offset = CellCentre(first) - CellCentre(second);
            var width = firstWidth + secondWidth;
            return Mathf.Abs(offset.x * Aspect) < width && Mathf.Abs(offset.y) < width * .77f;
        }

        public bool CanPlaceGranary(Vector3 capital, int cell)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length || !IsDryLand(cell)) return false;
            var uv = CellCentre(cell);
            var site = geometry.Sample(uv, out _);
            return MapSettlement.FitsGranary(capital, site) && HasDryFootprint(uv, MapSettlement.GranaryHalfWidth);
        }

        private bool HasDryFootprint(Vector2 uv, float width)
        {
            var halfWidth = width / Aspect;
            var halfDepth = width * .77f;
            var min = uv - new Vector2(halfWidth, halfDepth);
            var max = uv + new Vector2(halfWidth, halfDepth);
            if (min.x < 0f || min.y < 0f || max.x >= 1f || max.y >= 1f) return false;

            var first = CellAt(min);
            var last = CellAt(max);
            for (var y = first / Map.Width; y <= last / Map.Width; y++)
                for (var x = first % Map.Width; x <= last % Map.Width; x++)
                    if (!IsDryLand(y * Map.Width + x)) return false;
            return true;
        }

        private Vector2 CellCentre(int cell) => new Vector2(
            (cell % Map.Width + .5f) / Map.Width, (cell / Map.Width + .5f) / Map.Height);

        public void SetCapital(Vector3 point, CampaignState campaign)
        {
            settlement ??= new MapSettlement(surface.transform, geometry, terrainShadow, Aspect, IsDryLandAt);
            Vector3? granarySite = null;
            if (campaign.HasGranary && CanPlaceGranary(point, campaign.GranaryCell))
                granarySite = geometry.Sample(CellCentre(campaign.GranaryCell), out _);
            settlement.Show(point, campaign.HasGranary, campaign.FirstExpansion != ExpansionApproach.None,
                granarySite, campaign.SettlementBuildings(campaign.CapitalCell),
                building => ProductionPoint(campaign, campaign.CapitalCell, building), DockWaterPoint(campaign, campaign.CapitalCell));
            nature.SetCapital(point);
            material.SetVector("_CapitalClearing", new Vector4(point.x, point.z, MapNature.CapitalRadius, 0f));
        }

        private bool IsDryLandAt(Vector2 uv) => IsDryLand(CellAt(uv));

        public void SetView(MapViewMode mode)
        {
            borders.SetView(mode);
            viewMode = mode;
            if (mode == MapViewMode.Resources) CreateProductionSiteTexture();
            material.SetFloat("_ShowResources", mode == MapViewMode.Resources ? 1f : 0f);
            RefreshInfluence();
            UpdateOwnershipVisibility();
            material.SetFloat("_ShowPolitics", mode == MapViewMode.Terrain || mode == MapViewMode.Biomes ? 0f : 1f);
            material.SetFloat("_ShowBiomes", mode == MapViewMode.Biomes ? 1f : 0f);
        }

        public void SetOwnership(CampaignState campaign, Color colour)
        {
            influenceCampaign = campaign;
            if (productionSites != null && resourceMapCampaign != campaign)
            {
                var pixels = productionSites.GetRawTextureData<Color32>();
                for (var cell = 0; cell < pixels.Length; cell++)
                {
                    if (!Map.IsFord(cell)) continue;
                    var pixel = pixels[cell];
                    pixel.a = FordResourceCode(cell);
                    pixels[cell] = pixel;
                }
                productionSites.Apply(false, false);
                resourceMapCampaign = campaign;
            }
            influenceFoodPeriod = -1;
            material.SetVector("_OwnershipColour", QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour);
            RefreshInfluence();
        }

        public int InfluenceProvinceAt(int cell)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length) return -1;
            RefreshInfluence(true);
            return influenceRoots == null ? -1 : influenceRoots[cell] - 1;
        }

        public void HighlightInfluenceAt(int cell)
        {
            if (viewMode != MapViewMode.Ownership || influenceRoots == null) return;
            var root = cell >= 0 && cell < influenceRoots.Length ? influenceRoots[cell] : 0;
            if (root == highlightedInfluenceRoot) return;

            var pixels = ownership.GetRawTextureData<Color32>();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (influenceRoots[i] == 0
                    || (influenceRoots[i] != highlightedInfluenceRoot && influenceRoots[i] != root)) continue;
                var pixel = pixels[i];
                pixel.b = influenceRoots[i] == root ? (byte)255 : (byte)0;
                pixels[i] = pixel;
            }
            highlightedInfluenceRoot = root;
            ownership.Apply(false, false);
        }

        public void RefreshInfluence(bool force = false)
        {
            var campaign = influenceCampaign;
            if (campaign == null || (!force && viewMode != MapViewMode.Ownership)) return;
            var foodPeriod = campaign.Day / CampaignState.FoodInterval;
            var pressure = campaign.MilitaryPressure;
            if (foodPeriod == influenceFoodPeriod && pressure == displayedMilitaryPressure) return;

            ownership ??= CreateTexture("Settlement influence", true, FilterMode.Bilinear, false);
            var pixels = ownership.GetRawTextureData<Color32>();
            influenceRoots ??= new int[pixels.Length];
            influenceCosts ??= new float[pixels.Length];
            Array.Clear(influenceRoots, 0, influenceRoots.Length);

            Spread(campaign.CapitalCell, campaign.CapitalCell);
            for (var i = 0; i < campaign.SettlementCells.Count; i++)
                Spread(campaign.SettlementCells[i], campaign.SettlementProvinceRoots[i]);

            for (var cell = 0; cell < pixels.Length; cell++)
            {
                var root = influenceRoots[cell];
                if (root == 0)
                {
                    pixels[cell] = default;
                    continue;
                }
                var x = cell % Map.Width;
                var boundary = (x > 0 && IsOtherProvince(cell - 1))
                    || (x + 1 < Map.Width && IsOtherProvince(cell + 1))
                    || (cell >= Map.Width && IsOtherProvince(cell - Map.Width))
                    || (cell + Map.Width < pixels.Length && IsOtherProvince(cell + Map.Width));
                pixels[cell] = new Color32(255, boundary ? (byte)255 : (byte)0,
                    root == highlightedInfluenceRoot ? (byte)255 : (byte)0, 0);

                bool IsOtherProvince(int next) => influenceRoots[next] != 0 && influenceRoots[next] != root;
            }

            if (pressure > 0f && campaign.WarbandCell >= 0
                && Map.ProvinceAt[campaign.WarbandCell] == campaign.IntegrationTarget)
            {
                var budget = Map.Height * Mathf.Lerp(.006f, .060f, pressure);
                Flood(campaign.WarbandCell, budget, campaign.IntegrationTarget, (cell, _) =>
                {
                    var pixel = pixels[cell];
                    pixel.a = 255;
                    pixels[cell] = pixel;
                });
            }

            ownership.Apply(false, false);
            material.SetTexture("_OwnershipMask", ownership);
            displayedMilitaryPressure = pressure;
            influenceFoodPeriod = foodPeriod;
            UpdateOwnershipVisibility();

            void Spread(int origin, int provinceRoot)
            {
                var population = campaign.SettlementPopulation(origin);
                // Express the reach in map units so the same world has comparable reach at higher raster resolutions.
                var budget = Map.Height * Mathf.Clamp(.014f + .0014f * Mathf.Sqrt(population), .014f, .085f);
                Flood(origin, budget, -1, (cell, cost) =>
                {
                    var distance = cost / budget;
                    if (influenceRoots[cell] != 0 && distance >= influenceCosts[cell]) return;
                    influenceRoots[cell] = provinceRoot + 1;
                    influenceCosts[cell] = distance;
                });
            }

            void Flood(int origin, float budget, int restrictedRegion, Action<int, float> paint)
            {
                var costs = new Dictionary<int, float> { [origin] = 0f };
                var frontier = new SortedSet<(float Cost, int Cell)> { (0f, origin) };
                while (frontier.Count > 0)
                {
                    var step = frontier.Min;
                    frontier.Remove(step);
                    var cell = step.Cell;
                    if (!IsRiver(cell)) paint(cell, step.Cost);

                    var x = cell % Map.Width;
                    var y = cell / Map.Width;
                    if (x > 0) Visit(cell - 1);
                    if (x + 1 < Map.Width) Visit(cell + 1);
                    if (y > 0) Visit(cell - Map.Width);
                    if (y + 1 < Map.Height) Visit(cell + Map.Width);

                    void Visit(int next)
                    {
                        if (water[next] != WaterKind.Land
                            || (restrictedRegion >= 0 && (Map.ProvinceAt[next] != restrictedRegion || IsRiver(next)))) return;
                        var securedCrossing = restrictedRegion < 0 && (Map.IsFord(cell) || Map.IsFord(next))
                            && Map.CanMarchBetween(cell, next)
                            && (campaign.IsFordSecured(cell) || campaign.IsFordSecured(next));
                        var cost = step.Cost + (securedCrossing ? 1f
                            : 1f + Mathf.Abs(elevations[next] - elevations[cell]) * (.16f * Map.Height)
                                + (IsRiver(next) ? 5f : 0f));
                        if (cost > budget) return;
                        if (costs.TryGetValue(next, out var previous))
                        {
                            if (previous <= cost) return;
                            frontier.Remove((previous, next));
                        }
                        costs[next] = cost;
                        frontier.Add((cost, next));
                    }
                }
            }
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

        // Release runtime-created meshes, materials and textures when the scene closes or setup fails.
        public void Dispose()
        {
            settlement?.Dispose();
            ClearVillages();
            nature?.Dispose();
            borders?.Dispose();
            UnityEngine.Object.Destroy(surface);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(footprintMaterial);
            UnityEngine.Object.Destroy(warbandMaterial);
            UnityEngine.Object.Destroy(colours);
            UnityEngine.Object.Destroy(regions);
            UnityEngine.Object.Destroy(provinceCorners);
            UnityEngine.Object.Destroy(ownership);
            UnityEngine.Object.Destroy(productionSites);
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
