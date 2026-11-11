// Reclassifies coast and river edits and reconciles the existing political raster for review.
using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    public sealed class GeographyRevision
    {
        public HeightmapResult Terrain { get; }
        public RiverMap Rivers { get; }
        public ProvinceMap Provinces { get; }
        public ClimateMap Climate { get; }
        public BiomeMap Biomes { get; }
        public int ChangedWaterCells { get; }
        public int NavalChannelCells { get; }
        public int UnassignedProvinceCells { get; }
        public int UnassignedTerritoryCells { get; }
        public string[] AffectedProvinces { get; }
        public string[] AffectedTerritories { get; }

        internal GeographyRevision(HeightmapResult terrain, RiverMap rivers, ProvinceMap provinces,
            ClimateMap climate, BiomeMap biomes, int changedWaterCells, int navalChannelCells,
            int unassignedProvinceCells, int unassignedTerritoryCells, string[] affectedProvinces, string[] affectedTerritories)
        {
            Terrain = terrain;
            Rivers = rivers;
            Provinces = provinces;
            Climate = climate;
            Biomes = biomes;
            ChangedWaterCells = changedWaterCells;
            NavalChannelCells = navalChannelCells;
            UnassignedProvinceCells = unassignedProvinceCells;
            UnassignedTerritoryCells = unassignedTerritoryCells;
            AffectedProvinces = affectedProvinces;
            AffectedTerritories = affectedTerritories;
        }
    }

    public static class GeographyAuthoring
    {
        public static GeographyRevision Review(HeightmapResult original, float[] editedBase, RiverMap previousRivers,
            IReadOnlyList<int> riverWaypoints, byte riverWidth, float riverDepth, ProvinceMap map,
            ClimateSettings settings, bool useBiomes)
        {
            if (original == null || editedBase == null || map == null || riverWaypoints == null)
                throw new ArgumentException("World, elevation and province data are required.");
            var recipe = original.Recipe;
            var length = original.Elevations.Length;
            if (editedBase.Length != length || map.Width != recipe.Width || map.Height != recipe.Height)
                throw new ArgumentException("The geography draft must use the existing map grid.");

            var edited = new HeightmapResult(recipe.Copy(), (float[])editedBase.Clone());
            WaterMap.Classify(edited); // Validate sample ranges before river grading.
            var rivers = RiverGenerator.RebuildAuthored(edited, previousRivers, riverWaypoints, riverWidth, riverDepth);
            var final = rivers?.Terrain ?? edited;
            var oldWater = WaterMap.Classify(original);
            var newWater = WaterMap.Classify(final);
            var oldNavigation = previousRivers == null ? null
                : RiverNavigation.Build(previousRivers, ReferenceEquals(original, previousRivers.Terrain)
                    ? oldWater : WaterMap.Classify(previousRivers.Terrain), RiverNavigation.DefaultMaximumRise);
            var newNavigation = rivers == null ? null : RiverNavigation.Build(rivers, newWater, RiverNavigation.DefaultMaximumRise);
            var oldRivers = previousRivers?.BuildSurfaceMask();
            var newRivers = rivers?.BuildSurfaceMask();
            var kinds = new WaterKind[length];
            var physicalWater = new WaterKind[length];
            var changed = new bool[length];
            var changedCells = 0;
            for (var cell = 0; cell < length; cell++)
            {
                var before = RegionKind(oldWater, oldNavigation, cell);
                var after = RegionKind(newWater, newNavigation, cell);
                kinds[cell] = after;
                physicalWater[cell] = newWater[cell];
                changed[cell] = before != after || (oldRivers?[cell] ?? 0) != (newRivers?[cell] ?? 0)
                    || (previousRivers?.Directions[cell] ?? 0) != (rivers?.Directions[cell] ?? 0);
                if (changed[cell]) changedCells++;
            }

            var provinces = Remap(map.ProvinceAt, Array.ConvertAll(map.Provinces, p => p.Type),
                Array.ConvertAll(map.Provinces, p => p.Id), out var affectedProvinces, out var unassignedProvinces);
            var unassignedTerritories = 0;
            var affectedTerritories = Array.Empty<string>();
            var territories = map.HasTerritoryPreview
                ? Remap(map.TerritoryAt, Array.ConvertAll(map.Territories, t => t.Type),
                    Array.ConvertAll(map.Territories, t => t.Id), out affectedTerritories, out unassignedTerritories)
                : null;

            var climate = settings == null ? null : ClimateGenerator.Generate(final, newWater, settings);
            var biomes = useBiomes && climate != null ? BiomeGenerator.Generate(climate, newWater, rivers) : null;
            var revisedMap = ProvinceMap.RebuildIndexed(recipe, provinces, map.Provinces, map.Territories,
                biomes?.Samples, rivers?.Directions, physicalWater, rivers?.Widths, territories);
            return new GeographyRevision(final, rivers, revisedMap, climate, biomes, changedCells,
                newNavigation?.ChannelCount ?? 0, unassignedProvinces, unassignedTerritories, affectedProvinces, affectedTerritories);

            int[] Remap(int[] originalIds, string[] types, string[] names, out string[] affectedNames, out int unassigned)
            {
                var ids = (int[])originalIds.Clone();
                var replacement = new bool[length];
                var affected = new SortedSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < length; i++)
                {
                    if (!changed[i]) continue;
                    if (ids[i] >= 0) affected.Add(names[ids[i]]);
                    if (Compatible(ids[i], kinds[i])) continue;
                    replacement[i] = true;
                    ids[i] = -1;
                }

                // Seed each changed area from compatible adjoining regions. A FIFO flood gives the
                // closest available region by raster distance, with stable ties in raster order.
                var queue = new Queue<int>();
                for (var i = 0; i < length; i++)
                {
                    if (!replacement[i]) continue;
                    foreach (var neighbour in Neighbours(i, recipe.Width, recipe.Height))
                    {
                        if (replacement[neighbour] || !Compatible(ids[neighbour], kinds[i])) continue;
                        ids[i] = ids[neighbour];
                        queue.Enqueue(i);
                        break;
                    }
                }
                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    affected.Add(names[ids[cell]]);
                    foreach (var neighbour in Neighbours(cell, recipe.Width, recipe.Height))
                    {
                        if (!replacement[neighbour] || ids[neighbour] >= 0 || kinds[neighbour] != kinds[cell]) continue;
                        ids[neighbour] = ids[cell];
                        queue.Enqueue(neighbour);
                    }
                }

                var counts = new int[types.Length];
                unassigned = 0;
                for (var i = 0; i < length; i++)
                {
                    if (ids[i] >= 0) counts[ids[i]]++;
                    else if (replacement[i]) unassigned++;
                }
                for (var region = 0; region < counts.Length; region++)
                    if (counts[region] == 0 && affected.Contains(names[region]))
                        throw new InvalidOperationException($"The edit removes every sample of region {names[region]}. Adjust the edit or reimport its boundaries.");
                affectedNames = new string[affected.Count];
                affected.CopyTo(affectedNames);
                return ids;

                bool Compatible(int region, WaterKind kind) => region >= 0
                    && string.Equals(types[region], kind.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static WaterKind RegionKind(WaterMap water, RiverNavigation navigation, int cell)
        {
            if (water[cell] == WaterKind.Land && navigation != null
                && (navigation[cell] & (RiverNavigation.WaterFootprint | RiverNavigation.Connector)) != 0)
                return WaterKind.Ocean;
            return water[cell];
        }

        private static IEnumerable<int> Neighbours(int cell, int width, int height)
        {
            if (cell % width > 0) yield return cell - 1;
            if (cell % width + 1 < width) yield return cell + 1;
            if (cell >= width) yield return cell - width;
            if (cell < width * (height - 1)) yield return cell + width;
        }
    }
}
