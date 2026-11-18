// Stores the province and territory rasters alongside metadata used for selection, borders, construction
// and trade.
using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    [Serializable]
    public sealed class ProvinceInfo
    {
        public string Id;
        public string Type;
        public string Terrain;
        public string TerritoryId;
        public int Rgb;
        public float SourceX;
        public float SourceY;
        public int PixelCount;
        public int RiverSamples;
        public int RiverWaterSamples;
        public int RiverBankEdges;
        public bool TouchesOpenSea;
        public int[] Neighbours;
        public int[] BiomeCounts;
        [NonSerialized] public int[] TradeWaterways;

        internal ProvinceInfo Copy() => (ProvinceInfo)MemberwiseClone();
    }

    [Serializable]
    public sealed class TerritoryInfo
    {
        public string Id;
        public string Type;
        public int Rgb;
        public float SourceX;
        public float SourceY;
    }

    [Serializable]
    public sealed class ProvinceMap
    {
        public int Width;
        public int Height;
        public ProvinceInfo[] Provinces;
        public TerritoryInfo[] Territories;
        // Row-major, with the first row at the south edge. -1 denotes unassigned pixels.
        public int[] ProvinceAt;
        public int[] TerritoryAt;
        public bool HasTerritoryPreview => TerritoryAt != null && TerritoryAt.Length == Width * Height
            && Territories != null && Territories.Length > 0;
        public int UnassignedPixels;
        public bool HasRiverData;
        public bool HasRiverFootprint;
        public bool HasCoastData;

        private const byte MarchLand = 1;
        private const byte FordEastWest = 2;
        private const byte FordNorthSouth = 4;
        [NonSerialized] private byte[] marchTerrain;
        [NonSerialized] private int[] waterwayAt;
        internal bool HasDockWaterwayData => waterwayAt != null;

        internal int DockWaterwayAt(int cell)
        {
            if (waterwayAt == null || cell < 0 || cell >= waterwayAt.Length) return 0;
            var x = cell % Width;
            var y = cell / Width;
            var reach = (int)Math.Ceiling(Height * .001f) + 1;
            var nearest = reach * reach + 1;
            var waterway = 0;
            for (var row = Math.Max(0, y - reach); row <= Math.Min(Height - 1, y + reach); row++)
                for (var column = Math.Max(0, x - reach); column <= Math.Min(Width - 1, x + reach); column++)
                {
                    var channel = waterwayAt[row * Width + column];
                    var dx = x - column;
                    var dy = y - row;
                    var distance = dx * dx + dy * dy;
                    if (channel == 0 || distance == 0 || distance >= nearest) continue;
                    nearest = distance;
                    waterway = channel;
                }
            return waterway;
        }
        [NonSerialized] internal Func<int, SettlementBuilding, int?> LocalProductionYield;

        internal void BuildMarchTerrain(WaterMap water, byte[] rivers, float[] elevations)
        {
            marchTerrain = new byte[ProvinceAt.Length];
            for (var cell = 0; cell < marchTerrain.Length; cell++)
                if (water[cell] == WaterKind.Land && (rivers == null || rivers[cell] == 0)
                    && CampaignState.IsLand(this, ProvinceAt[cell]))
                    marchTerrain[cell] = MarchLand;

            if (rivers == null) return;
            for (var y = 1; y < Height - 1; y++)
                for (var x = 1; x < Width - 1; x++)
                {
                    var cell = y * Width + x;
                    if (water[cell] != WaterKind.Land || rivers[cell] == 0) continue;
                    if (FlatBanks(cell, 1)) marchTerrain[cell] |= FordEastWest;
                    if (FlatBanks(cell, Width)) marchTerrain[cell] |= FordNorthSouth;
                }

            bool FlatBanks(int centre, int step)
            {
                var a = centre - step;
                var b = centre + step;
                return marchTerrain[a] == MarchLand && marchTerrain[b] == MarchLand
                    && Math.Abs(elevations[a] - elevations[centre]) <= .025f
                    && Math.Abs(elevations[b] - elevations[centre]) <= .025f;
            }
        }

        internal bool IsFord(int cell) => marchTerrain != null && cell >= 0 && cell < marchTerrain.Length
            && (marchTerrain[cell] & (FordEastWest | FordNorthSouth)) != 0;

        internal bool IsMarchCell(int cell) => cell >= 0 && cell < ProvinceAt.Length
            && (marchTerrain == null ? CampaignState.IsLand(this, ProvinceAt[cell]) : marchTerrain[cell] != 0);

        internal bool CanMarchBetween(int from, int to)
        {
            if (!IsMarchCell(from) || !IsMarchCell(to)) return false;
            var horizontal = from / Width == to / Width && Math.Abs(from - to) == 1;
            var vertical = Math.Abs(from - to) == Width;
            if (!horizontal && !vertical) return false;
            if (marchTerrain == null) return true;
            var a = marchTerrain[from];
            var b = marchTerrain[to];
            if (a == MarchLand && b == MarchLand) return true;
            if (a != MarchLand && b != MarchLand) return false;
            return ((a | b) & (horizontal ? FordEastWest : FordNorthSouth)) != 0;
        }

        // Resolve imported colours to compact province indices and collect each province's neighbours and
        // biome counts.
        public static ProvinceMap Build(HeightmapRecipe recipe, int[] rgb, ProvinceInfo[] provinces,
            TerritoryInfo[] territories = null, byte[] biomes = null, byte[] rivers = null,
            WaterKind[] water = null, byte[] riverWidths = null, int[] territoryAt = null)
        {
            recipe.Validate();
            var width = recipe.Width;
            var height = recipe.Height;
            var length = width * height;
            if (rgb == null || rgb.Length != length || (biomes != null && biomes.Length != length)
                || (rivers != null && rivers.Length != length) || (water != null && water.Length != length)
                || (territoryAt != null && territoryAt.Length != length))
                throw new ArgumentException("Province and world layers must have identical dimensions.");
            if (provinces == null || provinces.Length == 0 || provinces.Length > length)
                throw new ArgumentException("Province definitions are missing or exceed the number of samples.");

            var riverSurface = rivers == null ? null : RiverMap.RasterizeSurface(recipe, rivers, riverWidths,
                cell => water == null || water[cell] == WaterKind.Land);
            var byColor = new Dictionary<int, int>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var neighbours = new HashSet<int>[provinces.Length];
            var records = new ProvinceInfo[provinces.Length];
            var biomeCount = Enum.GetValues(typeof(BiomeKind)).Length;
            for (var i = 0; i < provinces.Length; i++)
            {
                var p = provinces[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id))
                    throw new ArgumentException("Province IDs must be present and unique.");
                if (p.Rgb < 0 || p.Rgb > 0xffffff || !byColor.TryAdd(p.Rgb, i))
                    throw new ArgumentException($"Invalid or duplicate RGB for province {p.Id}.");
                records[i] = new ProvinceInfo
                {
                    Id = p.Id, Type = p.Type, Terrain = p.Terrain, TerritoryId = p.TerritoryId,
                    Rgb = p.Rgb, SourceX = p.SourceX, SourceY = p.SourceY,
                    BiomeCounts = biomes == null ? null : new int[biomeCount]
                };
                neighbours[i] = new HashSet<int>();
            }

            var unassigned = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var cell = y * width + x;
                    var index = FindRegionIndex(byColor, rgb[cell], cell, width, "province");
                    if (index < 0) unassigned++;
                    // Reuse the packed RGB buffer for the owned province-index raster.
                    rgb[cell] = index;
                    if (x > 0) Connect(index, rgb[cell - 1], cell, cell - 1, water, riverSurface, records, neighbours);
                    if (y > 0) Connect(index, rgb[cell - width], cell, cell - width, water, riverSurface, records, neighbours);
                    if (index < 0) continue;
                    var record = records[index];
                    record.PixelCount++;
                    if (rivers != null && rivers[cell] != 0) record.RiverSamples++;
                    if (riverSurface != null && riverSurface[cell] != 0) record.RiverWaterSamples++;
                    if (biomes == null) continue;
                    var biome = biomes[cell];
                    if (biome >= biomeCount) throw new ArgumentException($"Unknown biome ID {biome} at sample {cell}.");
                    record.BiomeCounts[biome]++;
                }

            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record.PixelCount == 0)
                    throw new ArgumentException($"Province {record.Id} has a definition but no pixels in the image.");
                record.Neighbours = new int[neighbours[i].Count];
                neighbours[i].CopyTo(record.Neighbours);
                Array.Sort(record.Neighbours);
            }

            return new ProvinceMap
            {
                Width = width, Height = height, Provinces = records,
                Territories = territories ?? Array.Empty<TerritoryInfo>(),
                ProvinceAt = rgb, TerritoryAt = territoryAt, UnassignedPixels = unassigned,
                HasRiverData = rivers != null, HasRiverFootprint = riverSurface != null, HasCoastData = water != null
            };
        }

        // Build gameplay geography from the same world layers that the playable map renders.
        // Enrich a copy of the map with playable biome and connected-waterway data so the imported asset
        // remains unchanged.
        internal ProvinceMap WithSettlementGeography(WaterMap water, byte[] rivers, byte[] biomes)
        {
            var result = (ProvinceMap)MemberwiseClone();
            result.Provinces = Array.ConvertAll(Provinces, p => p.Copy());
            var waterways = new HashSet<int>[Provinces.Length];
            var land = Array.ConvertAll(Provinces, p => string.Equals(p.Type, "land", StringComparison.OrdinalIgnoreCase));
            var length = ProvinceAt.Length;
            var channelsAt = new int[length];
            var queue = new int[length];
            var component = 0;
            if (biomes != null)
            {
                var biomeCount = Enum.GetValues(typeof(BiomeKind)).Length;
                for (var i = 0; i < land.Length; i++)
                    if (land[i]) result.Provinces[i].BiomeCounts = new int[biomeCount];
            }

            for (var cell = 0; cell < length; cell++)
            {
                var province = ProvinceAt[cell];
                if (!IsWaterway(cell))
                {
                    if (biomes != null && province >= 0 && land[province] && water[cell] == WaterKind.Land)
                        result.Provinces[province].BiomeCounts[biomes[cell]]++;
                    continue;
                }
                if (channelsAt[cell] != 0) continue;
                component++;
                var tail = 1;
                queue[0] = cell;
                channelsAt[cell] = component;
                for (var head = 0; head < tail; head++)
                {
                    var current = queue[head];
                    var x = current % Width;
                    var y = current / Width;
                    if (x > 0) Visit(current - 1);
                    if (x + 1 < Width) Visit(current + 1);
                    if (y > 0) Visit(current - Width);
                    if (y + 1 < Height) Visit(current + Width);
                    // Rivers can run diagonally across the raster.
                    if (!IsRiver(current)) continue;
                    if (y > 0 && x > 0 && IsRiver(current - Width - 1)) Visit(current - Width - 1);
                    if (y > 0 && x + 1 < Width && IsRiver(current - Width + 1)) Visit(current - Width + 1);
                    if (y + 1 < Height && x > 0 && IsRiver(current + Width - 1)) Visit(current + Width - 1);
                    if (y + 1 < Height && x + 1 < Width && IsRiver(current + Width + 1)) Visit(current + Width + 1);
                }

                void Visit(int next)
                {
                    if (IsWaterway(next))
                    {
                        if (channelsAt[next] != 0) return;
                        channelsAt[next] = component;
                        queue[tail++] = next;
                        return;
                    }
                    var shore = ProvinceAt[next];
                    if (water[next] == WaterKind.Land && shore >= 0 && land[shore])
                        (waterways[shore] ??= new HashSet<int>()).Add(component);
                }
            }

            for (var i = 0; i < result.Provinces.Length; i++)
            {
                var channels = waterways[i];
                var ids = channels == null ? Array.Empty<int>() : new int[channels.Count];
                channels?.CopyTo(ids);
                Array.Sort(ids);
                result.Provinces[i].TradeWaterways = ids;
            }
            // A dock can launch onto sea or an authored navigable river, while the component
            // IDs preserve the original trade network and community connections.
            for (var cell = 0; cell < length; cell++)
            {
                if (channelsAt[cell] == 0 || water[cell] == WaterKind.Ocean) continue;
                var province = ProvinceAt[cell];
                if (province < 0 || !string.Equals(Provinces[province].Type, "ocean", StringComparison.OrdinalIgnoreCase))
                    channelsAt[cell] = 0;
            }
            result.waterwayAt = channelsAt;
            return result;

            bool IsRiver(int cell) => water[cell] == WaterKind.Land && rivers != null && rivers[cell] != 0;
            bool IsWaterway(int cell) => water[cell] == WaterKind.Ocean || IsRiver(cell);
        }

        // The importer hands over the PNG buffer for conversion in place.
        // Resolve territory image colours to the indices used by the selection and border code.
        public static int[] IndexTerritoryColors(int[] rgb, TerritoryInfo[] territories, int width)
        {
            var byColor = new Dictionary<int, int>();
            for (var i = 0; i < territories.Length; i++)
            {
                var colour = territories[i].Rgb;
                if (colour < 0 || colour > 0xffffff || !byColor.TryAdd(colour, i))
                    throw new ArgumentException($"Invalid or duplicate territory RGB #{colour:X6}.");
            }
            for (var i = 0; i < rgb.Length; i++)
                rgb[i] = FindRegionIndex(byColor, rgb[i], i, width, "territory");
            return rgb;
        }

        private static int FindRegionIndex(Dictionary<int, int> byColor, int color, int cell, int width, string layer)
        {
            if (byColor.TryGetValue(color, out var index)) return index;
            if (color == 0) return -1;
            throw new ArgumentException($"Unknown {layer} RGB #{color:X6} at ({cell % width}, {cell / width}) from the south.");
        }

        // Record adjacency at a raster edge and collect the coast and river information needed by later
        // rules.
        private static void Connect(int a, int b, int aCell, int bCell, WaterKind[] water,
            byte[] riverSurface, ProvinceInfo[] records, HashSet<int>[] neighbours)
        {
            if (a == b) return;
            if (a >= 0 && b >= 0)
            {
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
            CountWaterBorder(a, aCell, bCell, water, riverSurface, records);
            CountWaterBorder(b, bCell, aCell, water, riverSurface, records);
        }

        private static void CountWaterBorder(int index, int inside, int outside, WaterKind[] water,
            byte[] riverSurface, ProvinceInfo[] records)
        {
            if (index < 0 || records[index].Type != "land") return;
            if (water != null && water[outside] == WaterKind.Ocean) records[index].TouchesOpenSea = true;
            if (riverSurface != null && riverSurface[inside] == 0 && riverSurface[outside] != 0)
                records[index].RiverBankEdges++;
        }
    }
}
