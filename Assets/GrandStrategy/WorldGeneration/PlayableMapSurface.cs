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
        private GameObject warbandFormation;
        private Material warbandArmour;
        private readonly Transform[] warbandFigures = new Transform[8];
        private bool showWarbandFigures;
        private int markerHeadingCell = -1;
        public Vector3 WarbandGroundPosition { get; private set; }
        private LineRenderer marchLine;
        private LineRenderer supplyLine;
        private Material supplyMaterial;
        private Material cargoMaterial;
        private Material bridgeMaterial;
        private CampaignState displayedBridgeCampaign;
        private readonly List<LineRenderer> displayedRoads = new List<LineRenderer>();
        private readonly List<LineRenderer> displayedBridges = new List<LineRenderer>();
        private GameObject cargoMarker;
        private IReadOnlyList<int> displayedSupplyRoute;
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
        private Texture2D independentInfluence;
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
        private MapSettlement independentVillage;
        private MapSettlement independentOutpost;
        private int displayedIndependentOutpost = -1;
        private int displayedIndependentVillage = -1;
        private int displayedIndependentFarm = -1;
        private readonly Dictionary<int, MapSettlement> villages = new Dictionary<int, MapSettlement>();
        private float groundDetail;
        private MapBorderOverlay borders;
        private GameObject surface;
        private WaterMap water;
        private byte[] rivers;
        private float[] elevations;
        private float seaLevel;
        private int depositSeed;
        private byte[] settlementBiomes;
        private CampaignState influenceCampaign;
        private int[] influenceRoots;
        private readonly List<int> influencedCells = new List<int>();
        private readonly HashSet<int> connectedClaims = new HashSet<int>();
        private readonly Queue<int> claimFrontier = new Queue<int>();
        private readonly Dictionary<int, SortedSet<int>> provinceBorders = new Dictionary<int, SortedSet<int>>();
        private int highlightedInfluenceRoot;
        private float[] influenceCosts;
        private float displayedMilitaryPressure;
        private CampaignState resourceMapCampaign;
        private int fordDisplayRevision = -1;
        private int influenceRevision = -1;
        private readonly Dictionary<int, SettlementBuilding> provinceProductionSites = new Dictionary<int, SettlementBuilding>();
        private readonly HashSet<int> militaryOccupation = new HashSet<int>();
        private readonly HashSet<int> landContacts = new HashSet<int>();

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
            depositSeed = terrain.Recipe.Seed;
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
                // Optional overlays receive their generated textures when first needed.
                material.SetTexture("_ProductionSites", Texture2D.blackTexture);
                material.SetTexture("_IndependentInfluence", Texture2D.blackTexture);
                material.SetTexture("_OwnershipMask", Texture2D.blackTexture);
                material.SetFloat("_ShowResources", 0f);
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
            var close = zoom <= .12f;
            if (close == showWarbandFigures) return;
            showWarbandFigures = close;
            UpdateWarbandVisibility();
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

            village.Show(geometry.Sample(CellCentre(cell), out _), campaign.GranaryCellAt(cell) >= 0, false,
                GranaryPoint(campaign, cell), campaign.SettlementBuildings(cell),
                building => ProductionPoint(campaign, cell, building), DockWaterPoint(campaign, cell), BuildRadius(campaign, cell));
        }

        private static float BuildRadius(CampaignState campaign, int cell)
            => campaign?.IsTown(cell) == true ? MapSettlement.TownBuildRadius : MapSettlement.BuildRadius;

        public bool IsWithinSettlementLimits(CampaignState campaign, int site, Vector2 uv)
            => IsDryLandAt(uv) && MapSettlement.Contains(geometry.Sample(CellCentre(site), out _),
                new Vector3((uv.x - .5f) * Aspect, 0f, uv.y - .5f), BuildRadius(campaign, site));

        public void ShowIndependentVillage(CampaignState campaign)
        {
            var outpost = campaign?.IndependentOutpostCell ?? -1;
            if (outpost != displayedIndependentOutpost)
            {
                independentOutpost?.Dispose();
                independentOutpost = null;
                displayedIndependentOutpost = outpost;
                if (outpost >= 0 && IsDryLand(outpost))
                {
                    independentOutpost = new MapSettlement(surface.transform, geometry, terrainShadow, Aspect, IsDryLandAt);
                    independentOutpost.Show(geometry.Sample(CellCentre(outpost), out _), false, false, null, 0,
                        _ => null, null, MapSettlement.BuildRadius, independent: true);
                }
            }
            var cell = campaign?.IndependentVillageCell ?? -1;
            var farm = campaign?.IndependentFarmCell ?? -1;
            if (cell == displayedIndependentVillage && farm == displayedIndependentFarm) return;
            independentVillage?.Dispose();
            independentVillage = null;
            displayedIndependentVillage = cell;
            displayedIndependentFarm = farm;
            if (cell < 0 || !IsDryLand(cell)) return;

            independentVillage = new MapSettlement(surface.transform, geometry, terrainShadow, Aspect, IsDryLandAt);
            Vector3? farmPoint = farm >= 0 ? geometry.Sample(CellCentre(farm), out _) : (Vector3?)null;
            independentVillage.Show(geometry.Sample(CellCentre(cell), out _), false, false, null,
                farmPoint.HasValue ? SettlementBuilding.Farm : 0,
                building => building == SettlementBuilding.Farm ? farmPoint : null, null, MapSettlement.BuildRadius, independent: true);
        }

        private int FindIndependentFarmSite(int village)
        {
            var centre = geometry.Sample(CellCentre(village), out _);
            var reach = Mathf.CeilToInt(MapSettlement.BuildRadius * Map.Height);
            var x = village % Map.Width;
            var y = village / Map.Width;
            var selected = -1;
            var bestYield = 0;
            for (var row = Mathf.Max(0, y - reach); row <= Mathf.Min(Map.Height - 1, y + reach); row++)
                for (var column = Mathf.Max(0, x - reach); column <= Mathf.Min(Map.Width - 1, x + reach); column++)
                {
                    var cell = row * Map.Width + column;
                    if (Map.ProvinceAt[cell] != Map.ProvinceAt[village]) continue;
                    var yield = LocalProductionYield(cell, SettlementBuilding.Farm) ?? 0;
                    if (yield <= bestYield) continue;
                    var uv = CellCentre(cell);
                    if (!MapSettlement.FitsGranary(centre, geometry.Sample(uv, out _), MapSettlement.BuildRadius)
                        || !HasDryFootprint(uv, MapSettlement.ProductionHalfWidth)
                        || IsBuildSiteTooSteep(cell, SettlementBuilding.Farm)) continue;
                    selected = cell;
                    bestYield = yield;
                }
            return selected;
        }

        private int FindIndependentOutpostSite(int village)
        {
            RefreshInfluence(true);
            var region = Map.ProvinceAt[village];
            var capital = CellCentre(influenceCampaign.CapitalCell);
            var nearest = float.PositiveInfinity;
            var candidate = -1;
            for (var cell = 0; cell < Map.ProvinceAt.Length; cell++)
            {
                if (Map.ProvinceAt[cell] != region || influenceRoots[cell] >= 0) continue;
                var offset = CellCentre(cell) - capital;
                offset.x *= Aspect;
                var distance = offset.sqrMagnitude;
                if (distance >= nearest || !HasRoom(influenceCampaign, cell, MapSettlement.BuildRadius)) continue;
                nearest = distance;
                candidate = cell;
            }
            return candidate;
        }

        private int FindIndependentVillageSite(int region)
        {
            RefreshInfluence(true);
            var capital = CellCentre(influenceCampaign.CapitalCell);
            var nearest = float.PositiveInfinity;
            var candidate = -1;
            for (var cell = 0; cell < Map.ProvinceAt.Length; cell++)
            {
                if (Map.ProvinceAt[cell] != region || influenceRoots[cell] != 0) continue;
                var offset = CellCentre(cell) - capital;
                offset.x *= Aspect;
                var distance = offset.sqrMagnitude;
                if (distance >= nearest || !HasRoom(influenceCampaign, cell, MapSettlement.BuildRadius)) continue;
                nearest = distance;
                candidate = cell;
            }
            return candidate;
        }

        public bool CanSiteVillage(CampaignState campaign, int cell)
            => HasRoom(campaign, cell, MapSettlement.BuildRadius);

        public bool CanExpandTown(CampaignState campaign, int cell)
            => HasRoom(campaign, cell, MapSettlement.TownBuildRadius);

        private bool HasRoom(CampaignState campaign, int cell, float radius)
        {
            if (cell < 0 || !IsDryLand(cell)) return false;
            var centre = CellCentre(cell);
            if (Conflicts(campaign.CapitalCell)) return false;
            var independent = campaign.IndependentVillageCell;
            if (independent >= 0 && (cell == independent || Conflicts(independent))) return false;
            var outpost = campaign.IndependentOutpostCell;
            if (outpost >= 0 && (cell == outpost || Conflicts(outpost))) return false;
            var proposed = campaign.IntegrationSettlementCell;
            if (proposed >= 0 && proposed != campaign.CapitalCell && !villages.ContainsKey(proposed)
                && Conflicts(proposed)) return false;
            foreach (var other in campaign.SettlementCells)
                if (Conflicts(other)) return false;
            return true;

            bool Conflicts(int other)
            {
                if (cell == other) return false;
                var offset = centre - CellCentre(other);
                offset.x *= Aspect;
                var required = radius + BuildRadius(campaign, other);
                return offset.sqrMagnitude < required * required;
            }
        }

        public SettlementBuilding SurveyProductionSites(int centreCell, CampaignState campaign = null)
        {
            if (centreCell < 0 || !IsDryLand(centreCell)) return 0;
            var centreX = centreCell % Map.Width;
            var centreY = centreCell / Map.Width;
            var radius = BuildRadius(campaign, centreCell) * Map.Height;
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
            if (IronDepositYield(cell, x, y) > 0)
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
                SettlementBuilding.IronMine => IronDepositYield(cell, cell % Map.Width, cell / Map.Width),
                _ => null
            };
        }

        private int IronDepositYield(int cell, int x, int y)
        {
            if (elevations[cell] < Mathf.Lerp(seaLevel, 1f, .42f)) return 0;

            // Each square can contain one compact deposit. The world seed determines its location and grade.
            var spacing = Mathf.Max(4f, Map.Height / 58f);
            var scaledX = (x + .5f) / spacing;
            var scaledY = (y + .5f) / spacing;
            var squareX = Mathf.FloorToInt(scaledX);
            var squareY = Mathf.FloorToInt(scaledY);
            uint hash;
            unchecked
            {
                hash = (uint)depositSeed ^ (uint)squareX * 0x9E3779B9u ^ (uint)squareY * 0x85EBCA6Bu;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
            }
            var offsetX = .27f + ((hash >> 8) & 255u) * (.46f / 255f);
            var offsetY = .27f + ((hash >> 16) & 255u) * (.46f / 255f);
            var dx = scaledX - squareX - offsetX;
            var dy = scaledY - squareY - offsetY;
            if ((hash & 3u) == 0 || dx * dx + dy * dy > .24f * .24f) return 0;
            return (hash & 12u) == 12u ? 2 : 1;
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
                        CrossingResourceCode(cell, (sites & SettlementBuilding.Docks) != 0));
                }
            productionSites.Apply(false, false);
            material.SetTexture("_ProductionSites", productionSites);
            resourceMapCampaign = influenceCampaign;
        }

        // Alpha stores crossing codes and whether this cell offers waterfront construction.
        private byte CrossingResourceCode(int cell, bool waterfront)
        {
            if (Map.IsFord(cell)) return FordResourceCode(cell);
            if (Map.IsMountainPass(cell)) return waterfront ? (byte)224 : (byte)48;
            return waterfront ? (byte)255 : (byte)0;
        }

        private byte FordResourceCode(int cell)
            => influenceCampaign?.IsFordHeldByRival(cell) == true ? (byte)110
                : influenceCampaign?.IsFordClosed(cell) == true ? (byte)92
                : influenceCampaign?.IsFordSecured(cell) == true ? (byte)176
                : influenceCampaign?.HasFordPassage(cell) == true ? (byte)145 : (byte)128;

        public void RefreshPassageDisplay()
        {
            if (productionSites == null || influenceCampaign == null) return;
            var revision = influenceCampaign.FordAccessRevision;
            if (revision == fordDisplayRevision) return;
            fordDisplayRevision = revision;
            if (influenceCampaign.PassageFords.Count == 0 && influenceCampaign.RivalHeldFords.Count == 0) return;
            var pixels = productionSites.GetRawTextureData<Color32>();
            foreach (var ford in influenceCampaign.PassageFords) Paint(ford);
            foreach (var ford in influenceCampaign.RivalHeldFords) Paint(ford);
            productionSites.Apply(false, false);

            void Paint(int ford)
            {
                var pixel = pixels[ford];
                pixel.a = FordResourceCode(ford);
                pixels[ford] = pixel;
            }
        }

        public void RefreshSecuredFord(int cell)
        {
            if (productionSites == null) return;
            var pixels = productionSites.GetRawTextureData<Color32>();
            var pixel = pixels[cell];
            pixel.a = FordResourceCode(cell);
            pixels[cell] = pixel;
            productionSites.Apply(false, false);
        }

        public void ShowRoadsAndBridges(CampaignState campaign)
        {
            if (campaign != displayedBridgeCampaign)
            {
                foreach (var bridge in displayedBridges) UnityEngine.Object.Destroy(bridge.gameObject);
                displayedBridges.Clear();
                foreach (var road in displayedRoads) UnityEngine.Object.Destroy(road.gameObject);
                displayedRoads.Clear();
                displayedBridgeCampaign = campaign;
            }
            if (campaign == null || (displayedBridges.Count >= campaign.BridgedFords.Count
                && displayedRoads.Count >= campaign.Roads.Count)) return;
            if (bridgeMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported) return;
                bridgeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                bridgeMaterial.SetColor("_BaseColor", new Color(.48f, .29f, .15f));
            }

            for (var i = displayedRoads.Count; i < campaign.Roads.Count; i++)
            {
                var path = campaign.Roads[i].Path;
                var road = new GameObject("Settlement road", typeof(LineRenderer)) { hideFlags = HideFlags.DontSave };
                road.transform.SetParent(surface.transform, false);
                var line = road.GetComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.widthMultiplier = .00028f;
                line.sharedMaterial = bridgeMaterial;
                var positions = new Vector3[path.Length];
                for (var cell = 0; cell < path.Length; cell++)
                    positions[cell] = geometry.Sample(CellCentre(path[cell]), out _) + Vector3.up * .00020f;
                line.positionCount = positions.Length;
                line.SetPositions(positions);
                displayedRoads.Add(line);
            }

            for (var i = displayedBridges.Count; i < campaign.BridgedFords.Count; i++)
            {
                var ford = campaign.BridgedFords[i];
                if (!Map.TryGetFordBanks(ford, out var first, out var second)) break;
                var start = geometry.Sample(CellCentre(first), out _);
                var end = geometry.Sample(CellCentre(second), out _);
                var deck = new GameObject("Timber bridge", typeof(LineRenderer)) { hideFlags = HideFlags.DontSave };
                deck.transform.SetParent(surface.transform, false);
                var line = deck.GetComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.widthMultiplier = .00075f;
                line.sharedMaterial = bridgeMaterial;
                line.positionCount = 2;
                line.SetPosition(0, start + Vector3.up * .00042f);
                line.SetPosition(1, end + Vector3.up * .00042f);
                line.alignment = LineAlignment.View;
                displayedBridges.Add(line);
            }
        }

        public void ShowWarband(CampaignState campaign)
        {
            var cell = campaign?.WarbandCell ?? -1;
            var route = campaign?.WarbandRoute;
            var routeIndex = campaign?.WarbandRouteIndex ?? 0;
            var next = route != null && routeIndex + 1 < route.Count ? route[routeIndex + 1] : -1;
            UpdateMarchLine(route, routeIndex);
            if (cell == markerCell && next == markerHeadingCell) return;
            if (cell < 0)
            {
                markerCell = markerHeadingCell = -1;
                UpdateWarbandVisibility();
                return;
            }

            var centre = geometry.Sample(CellCentre(cell), out _);
            WarbandGroundPosition = centre;
            if (warbandMarker == null && !CreateWarbandVisuals()) return;
            UpdateMarchLine(route, routeIndex);
            var headingCell = next >= 0 ? next : markerCell;
            var forward = headingCell >= 0 && headingCell != cell
                ? geometry.Sample(CellCentre(headingCell), out _) - centre : Vector3.forward;
            if (next < 0 && headingCell >= 0 && headingCell != cell) forward = -forward;
            forward.y = 0;
            var facing = Quaternion.LookRotation(forward.normalized, Vector3.up);
            warbandMarker.transform.localPosition = centre + Vector3.up * .003f;
            warbandMarker.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            for (var i = 0; i < warbandFigures.Length; i++)
            {
                var offset = facing * new Vector3((i % 4 - 1.5f) * .00088f, 0f, (i / 4 - .5f) * .00105f);
                var point = centre + offset;
                var uv = new Vector2(Mathf.Clamp01(point.x / Aspect + .5f), Mathf.Clamp01(point.z + .5f));
                warbandFigures[i].localPosition = geometry.Sample(uv, out _);
                warbandFigures[i].localRotation = facing;
            }

            markerCell = cell;
            markerHeadingCell = next;
            UpdateWarbandVisibility();
        }

        private void UpdateWarbandVisibility()
        {
            if (warbandMarker != null) warbandMarker.SetActive(markerCell >= 0 && !showWarbandFigures);
            if (warbandFormation != null) warbandFormation.SetActive(markerCell >= 0 && showWarbandFigures);
        }

        private bool CreateWarbandVisuals()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) return false;
            warbandMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            warbandMaterial.SetColor("_BaseColor", new Color(.86f, .18f, .13f));
            warbandArmour = new Material(warbandMaterial) { hideFlags = HideFlags.DontSave };
            warbandArmour.SetColor("_BaseColor", new Color(.33f, .30f, .28f));
            warbandMarker = CreatePiece("Warband marker", surface.transform, warbandMaterial,
                Vector3.zero, new Vector3(.004f, .0025f, .004f));
            warbandFormation = new GameObject("Warband formation") { hideFlags = HideFlags.DontSave };
            warbandFormation.transform.SetParent(surface.transform, false);

            for (var i = 0; i < warbandFigures.Length; i++)
            {
                var soldier = new GameObject("Warband soldier") { hideFlags = HideFlags.DontSave };
                var figure = soldier.transform;
                figure.SetParent(warbandFormation.transform, false);
                warbandFigures[i] = figure;
                CreatePiece("Body", figure, warbandArmour, new Vector3(0f, .00055f, 0f),
                    new Vector3(.00045f, .0011f, .00035f));
                CreatePiece("Helmet", figure, warbandArmour, new Vector3(0f, .00123f, 0f),
                    new Vector3(.00028f, .00028f, .00028f));
                CreatePiece("Shield", figure, warbandMaterial, new Vector3(-.00019f, .00065f, .00021f),
                    new Vector3(.00034f, .00053f, .00010f));
            }
            return true;

            GameObject CreatePiece(string name, Transform parent, Material shared, Vector3 position, Vector3 scale)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = name;
                piece.hideFlags = HideFlags.DontSave;
                piece.transform.SetParent(parent, false);
                piece.transform.localPosition = position;
                piece.transform.localScale = scale;
                piece.GetComponent<MeshRenderer>().sharedMaterial = shared;
                var collider = piece.GetComponent<Collider>();
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
                return piece;
            }
        }

        public void ShowSupplyRoute(IReadOnlyList<int> route, bool waterway, bool food)
        {
            if (route == null || route.Count < 2)
            {
                if (supplyLine != null) supplyLine.enabled = false;
                displayedSupplyRoute = null;
                return;
            }

            if (supplyLine == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported) return;
                supplyMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                var path = new GameObject("Supply route", typeof(LineRenderer)) { hideFlags = HideFlags.DontSave };
                path.transform.SetParent(surface.transform, false);
                supplyLine = path.GetComponent<LineRenderer>();
                supplyLine.useWorldSpace = false;
                supplyLine.widthMultiplier = .00038f;
                supplyLine.sharedMaterial = supplyMaterial;
            }

            supplyMaterial.SetColor("_BaseColor", food ? new Color(1f, .81f, .39f)
                : waterway ? new Color(.30f, .73f, 1f) : new Color(.37f, .91f, .56f));
            if (ReferenceEquals(route, displayedSupplyRoute)) return;

            var positions = new Vector3[route.Count];
            for (var i = 0; i < route.Count; i++)
                positions[i] = geometry.Sample(CellCentre(route[i]), out _) + Vector3.up * .00135f;
            supplyLine.positionCount = positions.Length;
            supplyLine.SetPositions(positions);
            supplyLine.enabled = true;
            displayedSupplyRoute = route;
        }

        public Vector3? ShowSupplyCargo(IReadOnlyList<int> path, float progress, bool waterway, bool food, bool held)
        {
            if (path == null || path.Count < 2)
            {
                if (cargoMarker != null) cargoMarker.SetActive(false);
                return null;
            }

            if (cargoMarker == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported) return null;
                cargoMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                cargoMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cargoMarker.name = "Supply shipment";
                cargoMarker.hideFlags = HideFlags.DontSave;
                cargoMarker.transform.SetParent(surface.transform, false);
                cargoMarker.GetComponent<MeshRenderer>().sharedMaterial = cargoMaterial;
                var collider = cargoMarker.GetComponent<Collider>();
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }

            var step = Mathf.Clamp01(progress) * (path.Count - 1);
            var index = Mathf.Min(Mathf.FloorToInt(step), path.Count - 2);
            var a = CellCentre(path[index]);
            var b = CellCentre(path[index + 1]);
            var direction = new Vector3((b.x - a.x) * Aspect, 0f, b.y - a.y);
            if (direction.sqrMagnitude > 0f)
                cargoMarker.transform.localRotation = Quaternion.LookRotation(direction.normalized);
            var ground = geometry.Sample(Vector2.Lerp(a, b, step - index), out _);
            cargoMarker.transform.localPosition = ground + Vector3.up * .0018f;
            cargoMaterial.SetColor("_BaseColor", held ? new Color(.96f, .36f, .24f)
                : food ? new Color(1f, .90f, .48f) : new Color(1f, .72f, .31f));
            cargoMarker.transform.localScale = waterway
                ? new Vector3(.0008f, .00052f, .0022f) : new Vector3(.0010f, .0008f, .0014f);
            cargoMarker.SetActive(true);
            return surface.transform.TransformPoint(ground);
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
            if (!MapSettlement.FitsGranary(centre, anchor, BuildRadius(campaign, site)) || !HasDryFootprint(CellCentre(cell), MapSettlement.ProductionHalfWidth)
                || ConflictsWithProduction(campaign, site, cell, MapSettlement.ProductionHalfWidth)) return false;

            var granary = campaign.GranaryCellAt(site);
            if (granary >= 0 || (site == campaign.CapitalCell && campaign.HasGranary))
            {
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

        public bool CanPlaceGranary(CampaignState campaign, int settlementCell, int cell)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length || !IsDryLand(cell)) return false;
            var uv = CellCentre(cell);
            var centre = geometry.Sample(CellCentre(settlementCell), out _);
            var anchor = geometry.Sample(uv, out _);
            return MapSettlement.FitsGranary(centre, anchor, BuildRadius(campaign, settlementCell))
                && HasDryFootprint(uv, MapSettlement.GranaryHalfWidth);
        }

        private Vector3? GranaryPoint(CampaignState campaign, int site)
        {
            var cell = campaign.GranaryCellAt(site);
            return CanPlaceGranary(campaign, site, cell) ? geometry.Sample(CellCentre(cell), out _) : (Vector3?)null;
        }

        public bool IsBuildSiteTooSteep(int cell, SettlementBuilding? building = null)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length) return false;
            var limit = building switch
            {
                SettlementBuilding.Farm => .55f,
                SettlementBuilding.IronMine => 1.35f,
                SettlementBuilding.Docks => .70f,
                _ => .85f
            };
            var centre = CellCentre(cell);
            var halfWidth = building.HasValue ? MapSettlement.ProductionHalfWidth : MapSettlement.GranaryHalfWidth;
            for (var column = -1; column <= 1; column++)
                for (var row = -1; row <= 1; row++)
                {
                    var uv = centre + new Vector2(column * halfWidth / Aspect, row * halfWidth * .77f);
                    uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
                    geometry.Sample(uv, out var slope);
                    if (slope > limit) return true;
                }
            return false;
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
            settlement.Show(point, campaign.HasGranary, campaign.FirstExpansion != ExpansionApproach.None,
                GranaryPoint(campaign, campaign.CapitalCell), campaign.SettlementBuildings(campaign.CapitalCell),
                building => ProductionPoint(campaign, campaign.CapitalCell, building), DockWaterPoint(campaign, campaign.CapitalCell), BuildRadius(campaign, campaign.CapitalCell));
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
            campaign.LandContactResolver = HasLandContact;
            campaign.IndependentVillageSiteResolver = FindIndependentVillageSite;
            campaign.IndependentOutpostSiteResolver = FindIndependentOutpostSite;
            campaign.IndependentFarmSiteResolver = FindIndependentFarmSite;
            campaign.IndependentTerritoryResolver = IsIndependentTerritory;
            campaign.MilitaryOccupationResolver = IsMilitaryOccupied;
            campaign.DetachedClaimResolver = DetachedClaimAt;
            campaign.ProvinceBorderResolver = HasProvinceBorder;
            campaign.ProvinceSeatConnectionResolver = HasConnectionToProvinceSeat;
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
            influenceRevision = -1;
            material.SetVector("_OwnershipColour", QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour);
            RefreshInfluence();
        }

        private bool HasLandContact(int province)
        {
            RefreshInfluence(true);
            return landContacts.Contains(province);
        }

        public SettlementBuilding InfluenceProvinceResources(int root)
        {
            if (root < 0) return 0;
            RefreshInfluence(true);
            return provinceProductionSites.TryGetValue(root + 1, out var sites) ? sites : 0;
        }

        public bool IsMilitaryOccupied(int cell)
        {
            if (cell < 0 || influenceCampaign?.WarbandCommitted != true) return false;
            RefreshInfluence(true);
            return militaryOccupation.Contains(cell);
        }

        public bool IsIndependentTerritory(int cell)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length || influenceCampaign == null || influenceCampaign.IndependentVillageCell < 0) return false;
            RefreshInfluence(true);
            return influenceRoots != null && influenceRoots[cell] < 0;
        }

        public int InfluenceProvinceAt(int cell)
        {
            if (cell < 0 || cell >= Map.ProvinceAt.Length) return -1;
            RefreshInfluence(true);
            return influenceRoots == null || influenceRoots[cell] <= 0 ? -1 : influenceRoots[cell] - 1;
        }

        public bool HasConnectionToProvinceSeat(int cell)
        {
            RefreshInfluence(true);
            return connectedClaims.Contains(cell);
        }

        private HashSet<int> DetachedClaimAt(int cell)
        {
            var root = InfluenceProvinceAt(cell);
            if (root < 0 || connectedClaims.Contains(cell)) return null;

            var fragment = new HashSet<int>();
            Map.CollectConnectedClaim(influenceRoots, cell, root, influenceCampaign.CanUseSupplyFord, fragment, claimFrontier);
            return fragment;
        }

        public IEnumerable<int> ProvinceNeighbours(int seat)
        {
            RefreshInfluence(true);
            if (provinceBorders.TryGetValue(seat, out var neighbours)) return neighbours;
            return Array.Empty<int>();
        }

        private bool HasProvinceBorder(int first, int second)
        {
            RefreshInfluence(true);
            return provinceBorders.TryGetValue(first, out var neighbours) && neighbours.Contains(second);
        }

        public void HighlightInfluenceAt(int cell)
        {
            if (viewMode != MapViewMode.Ownership || influenceRoots == null) return;
            var root = cell >= 0 && cell < influenceRoots.Length ? Mathf.Max(0, influenceRoots[cell]) : 0;
            if (root == highlightedInfluenceRoot) return;

            var pixels = ownership.GetRawTextureData<Color32>();
            foreach (var influenced in influencedCells)
            {
                var owner = influenceRoots[influenced];
                if (owner <= 0 || (owner != highlightedInfluenceRoot && owner != root)) continue;
                var pixel = pixels[influenced];
                pixel.b = owner == root ? (byte)255 : (byte)0;
                pixels[influenced] = pixel;
            }
            highlightedInfluenceRoot = root;
            ownership.Apply(false, false);
        }

        public void RefreshInfluence(bool force = false)
        {
            var campaign = influenceCampaign;
            if (campaign == null || (!force && viewMode != MapViewMode.Ownership)) return;
            var revision = campaign.InfluenceRevision;
            var pressure = campaign.MilitaryPressure;
            if (revision == influenceRevision && pressure == displayedMilitaryPressure) return;

            var firstUpload = ownership == null;
            ownership ??= CreateTexture("Settlement influence", true, FilterMode.Bilinear, false);
            var pixels = ownership.GetRawTextureData<Color32>();
            if (firstUpload)
                for (var i = 0; i < pixels.Length; i++) pixels[i] = default;
            var firstIndependentUpload = campaign.IndependentVillageCell >= 0 && independentInfluence == null;
            if (firstIndependentUpload)
            {
                independentInfluence = new Texture2D(Map.Width, Map.Height, TextureFormat.R8, false, true)
                {
                    name = "Independent village influence", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                material.SetTexture("_IndependentInfluence", independentInfluence);
                var newPixels = independentInfluence.GetRawTextureData<byte>();
                for (var i = 0; i < newPixels.Length; i++) newPixels[i] = 0;
            }
            var independentPixels = independentInfluence == null ? default : independentInfluence.GetRawTextureData<byte>();
            influenceRoots ??= new int[pixels.Length];
            influenceCosts ??= new float[pixels.Length];
            var independentMaskChanged = firstIndependentUpload;
            foreach (var cell in influencedCells)
            {
                if (influenceRoots[cell] < 0 && independentPixels.IsCreated)
                {
                    independentPixels[cell] = 0;
                    independentMaskChanged = true;
                }
                influenceRoots[cell] = 0;
                pixels[cell] = default;
            }
            influencedCells.Clear();
            foreach (var cell in militaryOccupation) pixels[cell] = default;
            militaryOccupation.Clear();
            landContacts.Clear();
            provinceProductionSites.Clear();
            if (pressure > 0f && campaign.WarbandCell >= 0
                && Map.ProvinceAt[campaign.WarbandCell] == campaign.IntegrationTarget)
            {
                var budget = Map.Height * Mathf.Lerp(.006f, .060f, pressure);
                Flood(campaign.WarbandCell, budget, campaign.IntegrationTarget, (cell, _) => militaryOccupation.Add(cell));
            }

            Spread(campaign.CapitalCell, campaign.CapitalCell);
            for (var i = 0; i < campaign.SettlementCells.Count; i++)
                Spread(campaign.SettlementCells[i], campaign.SettlementProvinceRoots[i]);

            if (campaign.IndependentVillageCell >= 0)
            {
                var residents = campaign.IndependentPopulation(campaign.IndependentVillageRegion);
                var outpostResidents = campaign.IndependentOutpostCell >= 0 ? residents / 3 : 0;
                Spread(campaign.IndependentVillageCell, -1, residents - outpostResidents);
                if (campaign.IndependentOutpostCell >= 0) Spread(campaign.IndependentOutpostCell, -1, outpostResidents);
            }

            connectedClaims.Clear();
            provinceBorders.Clear();
            Func<int, bool> canUseFord = campaign.CanUseSupplyFord;
            Map.CollectConnectedClaim(influenceRoots, campaign.CapitalCell, campaign.CapitalCell, canUseFord,
                connectedClaims, claimFrontier);
            var hasOtherProvince = false;
            for (var i = 0; i < campaign.SettlementCells.Count; i++)
            {
                var site = campaign.SettlementCells[i];
                if (campaign.SettlementProvinceRoots[i] != site) continue;
                hasOtherProvince = true;
                Map.CollectConnectedClaim(influenceRoots, site, site, canUseFord, connectedClaims, claimFrontier);
            }
            if (hasOtherProvince) Map.CollectProvinceBorders(influenceRoots, connectedClaims, canUseFord, provinceBorders);

            const SettlementBuilding allSites = SettlementBuilding.Farm | SettlementBuilding.LumberCamp
                | SettlementBuilding.IronMine | SettlementBuilding.Docks;
            foreach (var cell in influencedCells)
            {
                var root = influenceRoots[cell];
                if (root < 0)
                {
                    independentPixels[cell] = 255;
                    independentMaskChanged = true;
                    continue;
                }
                var x = cell % Map.Width;
                provinceProductionSites.TryGetValue(root, out var sites);
                if (sites != allSites) provinceProductionSites[root] = sites | ProductionSitesAt(cell, x, cell / Map.Width);
                landContacts.Add(Map.ProvinceAt[cell]);
                if (x > 0) RecordContact(cell - 1);
                if (x + 1 < Map.Width) RecordContact(cell + 1);
                if (cell >= Map.Width) RecordContact(cell - Map.Width);
                if (cell + Map.Width < pixels.Length) RecordContact(cell + Map.Width);

                void RecordContact(int adjacent)
                {
                    if (IsDryLand(adjacent)) landContacts.Add(Map.ProvinceAt[adjacent]);
                }

                var west = x > 0 ? influenceRoots[cell - 1] : 0;
                var east = x + 1 < Map.Width ? influenceRoots[cell + 1] : 0;
                var south = cell >= Map.Width ? influenceRoots[cell - Map.Width] : 0;
                var north = cell + Map.Width < pixels.Length ? influenceRoots[cell + Map.Width] : 0;
                var frontier = west <= 0 || east <= 0 || south <= 0 || north <= 0;
                var internalBorder = west != root || east != root || south != root || north != root;
                var border = frontier ? (byte)255 : internalBorder ? (byte)115 : (byte)0;
                pixels[cell] = new Color32(255, border, root == highlightedInfluenceRoot ? (byte)255 : (byte)0, 0);
            }

            foreach (var cell in militaryOccupation)
            {
                var pixel = pixels[cell];
                pixel.a = 255;
                pixels[cell] = pixel;
            }

            ownership.Apply(false, false);
            if (independentMaskChanged) independentInfluence.Apply(false, false);
            material.SetTexture("_OwnershipMask", ownership);
            displayedMilitaryPressure = pressure;
            influenceRevision = revision;
            UpdateOwnershipVisibility();

            void Spread(int origin, int provinceRoot, int? residents = null)
            {
                var population = residents ?? campaign.SettlementPopulation(origin);
                if (population <= 0) return;
                // Express the reach in map units so the same world has comparable reach at higher raster resolutions.
                var budget = Map.Height * Mathf.Clamp(.014f + .0014f * Mathf.Sqrt(population), .014f, .085f);
                Flood(origin, budget, -1, (cell, cost) =>
                {
                    if (provinceRoot < 0 && militaryOccupation.Contains(cell)) return;
                    var distance = cost / budget;
                    if (influenceRoots[cell] == 0) influencedCells.Add(cell);
                    else if (distance >= influenceCosts[cell]) return;
                    influenceRoots[cell] = provinceRoot >= 0 ? provinceRoot + 1 : -1;
                    influenceCosts[cell] = distance;
                }, useClanFords: provinceRoot >= 0);
            }

            void Flood(int origin, float budget, int restrictedRegion, Action<int, float> paint, bool useClanFords = false)
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
                            || (restrictedRegion < 0 && Map.IsFord(next) && campaign.IsFordClosed(next))
                            || (restrictedRegion >= 0 && (Map.ProvinceAt[next] != restrictedRegion || IsRiver(next)))) return;
                        var securedCrossing = useClanFords && (Map.IsFord(cell) || Map.IsFord(next))
                            && Map.CanMarchBetween(cell, next)
                            && (campaign.CanUseSupplyFord(cell) || campaign.CanUseSupplyFord(next));
                        var infrastructureCost = useClanFords ? campaign.InfluenceInfrastructureCost(cell, next) : null;
                        var travelCost = infrastructureCost ?? (securedCrossing ? 1f
                            : 1f + Map.InfluenceElevationCost(cell, next) + (IsRiver(next) ? 5f : 0f));
                        var cost = step.Cost + travelCost;
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
            independentVillage?.Dispose();
            independentOutpost?.Dispose();
            ClearVillages();
            nature?.Dispose();
            borders?.Dispose();
            UnityEngine.Object.Destroy(surface);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(footprintMaterial);
            UnityEngine.Object.Destroy(warbandMaterial);
            UnityEngine.Object.Destroy(warbandArmour);
            UnityEngine.Object.Destroy(supplyMaterial);
            UnityEngine.Object.Destroy(cargoMaterial);
            UnityEngine.Object.Destroy(bridgeMaterial);
            UnityEngine.Object.Destroy(colours);
            UnityEngine.Object.Destroy(regions);
            UnityEngine.Object.Destroy(provinceCorners);
            UnityEngine.Object.Destroy(ownership);
            UnityEngine.Object.Destroy(independentInfluence);
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
