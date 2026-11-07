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
        public int UnassignedPixels;
        public bool HasRiverData;
        public bool HasRiverFootprint;
        public bool HasCoastData;

        public static ProvinceMap Build(HeightmapRecipe recipe, int[] rgb, ProvinceInfo[] provinces,
            TerritoryInfo[] territories = null, byte[] biomes = null, byte[] rivers = null,
            WaterKind[] water = null, byte[] riverWidths = null)
        {
            recipe.Validate();
            var width = recipe.Width;
            var height = recipe.Height;
            var length = width * height;
            if (rgb == null || rgb.Length != length || (biomes != null && biomes.Length != length)
                || (rivers != null && rivers.Length != length) || (water != null && water.Length != length))
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
                    var color = rgb[cell];
                    if (!byColor.TryGetValue(color, out var index))
                    {
                        if (color != 0) throw new ArgumentException($"Unknown province RGB #{color:X6} at ({x}, {y}) from the south.");
                        index = -1;
                        unassigned++;
                    }
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
                ProvinceAt = rgb, UnassignedPixels = unassigned,
                HasRiverData = rivers != null, HasRiverFootprint = riverSurface != null, HasCoastData = water != null
            };
        }

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
