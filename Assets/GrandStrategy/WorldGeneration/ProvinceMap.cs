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
        private const byte PassEastWest = 8;
        private const byte PassNorthSouth = 16;
        private const float PassGradeMultiplier = .45f;
        [NonSerialized] private byte[] marchTerrain;
        [NonSerialized] private float[] marchElevations;
        [NonSerialized] private int[] waterwayAt;
        [NonSerialized] private byte[] waterwayRivers;
        internal bool HasDockWaterwayData => waterwayAt != null;

        internal int DockWaterwayAt(int cell)
        {
            var water = DockWaterCell(cell);
            return water < 0 ? 0 : waterwayAt[water];
        }

        internal int DockWaterCell(int cell)
        {
            if (waterwayAt == null || cell < 0 || cell >= waterwayAt.Length) return -1;
            var x = cell % Width;
            var y = cell / Width;
            var reach = (int)Math.Ceiling(Height * .001f) + 1;
            var nearest = reach * reach + 1;
            var waterCell = -1;
            for (var row = Math.Max(0, y - reach); row <= Math.Min(Height - 1, y + reach); row++)
                for (var column = Math.Max(0, x - reach); column <= Math.Min(Width - 1, x + reach); column++)
                {
                    var channel = waterwayAt[row * Width + column];
                    var dx = x - column;
                    var dy = y - row;
                    var distance = dx * dx + dy * dy;
                    if (channel <= 0 || distance == 0 || distance >= nearest) continue;
                    nearest = distance;
                    waterCell = row * Width + column;
                }
            return waterCell;
        }
        internal List<int> FindDockRoute(int loadingDock, int receivingDock)
        {
            var start = DockWaterCell(loadingDock);
            var goal = DockWaterCell(receivingDock);
            if (start < 0 || goal < 0 || waterwayAt[start] != waterwayAt[goal]) return null;

            var channel = waterwayAt[start];
            var frontier = new SortedSet<(int Estimate, int Remaining, int Cell)> { (Distance(start), Distance(start), start) };
            var costs = new Dictionary<int, int> { [start] = 0 };
            var previous = new Dictionary<int, int>();
            while (frontier.Count > 0)
            {
                var entry = frontier.Min;
                frontier.Remove(entry);
                var cell = entry.Cell;
                if (cell == goal)
                {
                    var route = new List<int> { receivingDock, goal };
                    while (cell != start)
                    {
                        cell = previous[cell];
                        route.Add(cell);
                    }
                    route.Add(loadingDock);
                    route.Reverse();
                    return route;
                }

                var x = cell % Width;
                var y = cell / Width;
                var cost = costs[cell] + 1;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;
                        var next = ny * Width + nx;
                        if (Math.Abs(waterwayAt[next]) != channel) continue;
                        // Connected coast uses cardinal edges. Diagonals connect river raster cells.
                        if (dx != 0 && dy != 0 && (waterwayRivers == null
                            || waterwayRivers[cell] == 0 || waterwayRivers[next] == 0)) continue;
                        var distance = Distance(next);
                        if (costs.TryGetValue(next, out var prior))
                        {
                            if (prior <= cost) continue;
                            frontier.Remove((prior + distance, distance, next));
                        }
                        costs[next] = cost;
                        previous[next] = cell;
                        frontier.Add((cost + distance, distance, next));
                    }
            }
            return null;

            int Distance(int cell) => Math.Max(Math.Abs(cell % Width - goal % Width), Math.Abs(cell / Width - goal / Width));
        }

        [NonSerialized] internal Func<int, SettlementBuilding, int?> LocalProductionYield;

        internal void BuildMarchTerrain(WaterMap water, byte[] rivers, float[] elevations)
        {
            marchElevations = elevations;
            marchTerrain = new byte[ProvinceAt.Length];
            for (var cell = 0; cell < marchTerrain.Length; cell++)
                if (water[cell] == WaterKind.Land && (rivers == null || rivers[cell] == 0)
                    && CampaignState.IsLand(this, ProvinceAt[cell]))
                    marchTerrain[cell] = MarchLand;

            if (rivers != null)
            {
                for (var y = 1; y < Height - 1; y++)
                    for (var x = 1; x < Width - 1; x++)
                    {
                        var cell = y * Width + x;
                        if (water[cell] != WaterKind.Land || rivers[cell] == 0) continue;
                        if (FlatBanks(cell, 1)) marchTerrain[cell] |= FordEastWest;
                        if (FlatBanks(cell, Width)) marchTerrain[cell] |= FordNorthSouth;
                    }
            }

            MarkMountainPasses(water.Source.Recipe.SeaLevel);

            bool FlatBanks(int centre, int step)
            {
                var a = centre - step;
                var b = centre + step;
                return marchTerrain[a] == MarchLand && marchTerrain[b] == MarchLand
                    && Math.Abs(elevations[a] - elevations[centre]) <= .025f
                    && Math.Abs(elevations[b] - elevations[centre]) <= .025f;
            }
        }

        private void MarkMountainPasses(float seaLevel)
        {
            const float minimumHeightAboveSea = .12f;
            const float minimumRidgeRelief = .03f;
            const float minimumExitDrop = .008f;
            var radius = Math.Max(2, Height / 64);
            for (var y = radius; y < Height - radius; y++)
                for (var x = radius; x < Width - radius; x++)
                {
                    var cell = y * Width + x;
                    if (marchTerrain[cell] != MarchLand || marchElevations[cell] < seaLevel + minimumHeightAboveSea)
                        continue;
                    if (Saddle(cell, travel: 1, ridge: Width)) marchTerrain[cell] |= PassEastWest;
                    else if (Saddle(cell, travel: Width, ridge: 1)) marchTerrain[cell] |= PassNorthSouth;
                }

            bool Saddle(int cell, int travel, int ridge)
            {
                var elevation = marchElevations[cell];
                var flank = ridge * radius;
                var exit = travel * radius;
                if (Math.Min(marchElevations[cell - flank], marchElevations[cell + flank]) - elevation < minimumRidgeRelief
                    || elevation - Math.Max(marchElevations[cell - exit], marchElevations[cell + exit]) < minimumExitDrop)
                    return false;

                // Both approaches and ridge shoulders must remain on land.
                for (var i = 1; i <= radius; i++)
                    if ((marchTerrain[cell - travel * i] & MarchLand) == 0
                        || (marchTerrain[cell + travel * i] & MarchLand) == 0
                        || (marchTerrain[cell - ridge * i] & MarchLand) == 0
                        || (marchTerrain[cell + ridge * i] & MarchLand) == 0)
                        return false;
                return true;
            }
        }

        internal bool HasMarchTerrain => marchTerrain != null;

        internal bool IsMountainPass(int cell) => marchTerrain != null && cell >= 0 && cell < marchTerrain.Length
            && (marchTerrain[cell] & (PassEastWest | PassNorthSouth)) != 0;

        internal bool UsesMountainPass(int from, int to)
        {
            if ((!IsMountainPass(from) && !IsMountainPass(to)) || !IsMarchCell(from) || !IsMarchCell(to))
                return false;
            if ((marchTerrain[from] & marchTerrain[to] & MarchLand) == 0) return false;
            var flags = marchTerrain[from] | marchTerrain[to];
            return (Math.Abs(from - to) == Width && (flags & PassNorthSouth) != 0)
                || (from / Width == to / Width && Math.Abs(from - to) == 1 && (flags & PassEastWest) != 0);
        }

        internal float InfluenceElevationCost(int from, int to) => ElevationGrade(from, to, .16f * Height);

        private float ElevationGrade(int from, int to, float scale)
        {
            if (marchElevations == null) return 0f;
            var grade = Math.Abs(marchElevations[from] - marchElevations[to]) * scale;
            return UsesMountainPass(from, to) ? grade * PassGradeMultiplier : grade;
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
            if (((a & b) & MarchLand) != 0) return true;
            if (((a | b) & MarchLand) == 0) return false;
            return ((a | b) & (horizontal ? FordEastWest : FordNorthSouth)) != 0;
        }

        internal bool TryGetFordBanks(int ford, out int first, out int second)
        {
            first = second = -1;
            if (!IsFord(ford)) return false;
            var offset = (marchTerrain[ford] & FordEastWest) != 0 ? 1 : Width;
            first = ford - offset;
            second = ford + offset;
            return true;
        }

        // Claims are encoded as the province seat's cell index plus one. River cells are unclaimed,
        // so an open ford connects its two banks without granting ownership of the river.
        internal void CollectConnectedClaim(int[] claims, int start, int seat, Func<int, bool> canUseFord,
            HashSet<int> connected, Queue<int> frontier)
        {
            var root = seat + 1;
            if (claims[start] != root || !connected.Add(start)) return;
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                var cell = frontier.Dequeue();
                var x = cell % Width;
                if (x > 0) VisitEdge(cell, -1);
                if (x + 1 < Width) VisitEdge(cell, 1);
                if (cell >= Width) VisitEdge(cell, -Width);
                if (cell + Width < claims.Length) VisitEdge(cell, Width);
            }

            void VisitEdge(int cell, int offset)
            {
                var next = LinkedClaimCell(cell, offset, canUseFord);
                if (next >= 0 && claims[next] == root) Enqueue(next);
            }

            void Enqueue(int cell)
            {
                if (connected.Add(cell)) frontier.Enqueue(cell);
            }
        }

        internal void CollectProvinceBorders(int[] claims, HashSet<int> connected, Func<int, bool> canUseFord,
            Dictionary<int, SortedSet<int>> borders)
        {
            foreach (var cell in connected)
            {
                if (cell % Width + 1 < Width) RecordEdge(cell, 1);
                if (cell + Width < claims.Length) RecordEdge(cell, Width);
            }

            void RecordEdge(int cell, int offset)
            {
                var next = LinkedClaimCell(cell, offset, canUseFord);
                if (next < 0 || claims[next] == claims[cell] || !connected.Contains(next)) return;

                var from = claims[cell] - 1;
                var to = claims[next] - 1;
                AddNeighbour(from, to);
                AddNeighbour(to, from);
            }

            void AddNeighbour(int from, int to)
            {
                if (!borders.TryGetValue(from, out var neighbours))
                    borders.Add(from, neighbours = new SortedSet<int>());
                neighbours.Add(to);
            }
        }

        private int LinkedClaimCell(int cell, int offset, Func<int, bool> canUseFord)
        {
            var next = cell + offset;
            if (!IsFord(next)) return next;
            var oppositeBank = next + offset;
            return canUseFord(next) && CanMarchBetween(cell, next) && CanMarchBetween(next, oppositeBank)
                ? oppositeBank : -1;
        }

        internal int FordForBridgeEdge(int from, int to)
        {
            var ford = IsFord(from) ? from : IsFord(to) ? to : -1;
            if (!TryGetFordBanks(ford, out var first, out var second)) return -1;
            var bank = ford == from ? to : from;
            return bank == first || bank == second ? ford : -1;
        }

        internal const int BaseMarchCost = 4;

        internal int MarchStepCost(int from, int to, Func<int, bool> hasBridge = null,
            Func<int, int, bool> hasRoad = null)
        {
            if (hasBridge != null)
            {
                var ford = FordForBridgeEdge(from, to);
                if (ford >= 0 && hasBridge(ford)) return BaseMarchCost;
            }
            if (!IsFord(from) && !IsFord(to) && hasRoad?.Invoke(from, to) == true) return BaseMarchCost;
            var grade = ElevationGrade(from, to, Height);
            var elevationCost = (int)Math.Min(16f, grade);
            return BaseMarchCost + elevationCost + (IsFord(from) || IsFord(to) ? 2 : 0);
        }

        internal List<int> FindWarbandRoute(int start, int goal, Func<int, bool> canUseFord = null,
            Func<int, bool> canVisit = null, Func<int, bool> hasBridge = null, Func<int, int, bool> hasRoad = null)
        {
            if (!IsMarchCell(start) || !IsMarchCell(goal) || start == goal) return null;
            var fordDestination = IsFord(goal);

            var frontier = new SortedSet<(int Estimate, int Cell)> { (Distance(start), start) };
            var costs = new Dictionary<int, int> { [start] = 0 };
            var previous = new Dictionary<int, int>();
            while (frontier.Count > 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);
                var cell = current.Cell;
                if (fordDestination ? CanMarchBetween(cell, goal) : cell == goal)
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

                var spent = costs[cell];
                var x = cell % Width;
                if (x > 0) Visit(cell - 1);
                if (x + 1 < Width) Visit(cell + 1);
                if (cell >= Width) Visit(cell - Width);
                if (cell + Width < ProvinceAt.Length) Visit(cell + Width);

                void Visit(int next)
                {
                    if (!CanMarchBetween(cell, next) || (canUseFord != null && IsFord(next) && !canUseFord(next))
                        || canVisit?.Invoke(next) == false) return;
                    var cost = spent + MarchStepCost(cell, next, hasBridge, hasRoad);
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

            int Distance(int cell) => BaseMarchCost * (Math.Abs(cell % Width - goal % Width)
                + Math.Abs(cell / Width - goal / Width));
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
                    channelsAt[cell] = -channelsAt[cell];
            }
            result.waterwayAt = channelsAt;
            result.waterwayRivers = rivers;
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
