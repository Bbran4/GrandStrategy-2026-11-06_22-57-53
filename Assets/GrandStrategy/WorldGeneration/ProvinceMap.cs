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
        public bool HasCoastData;

        public static ProvinceMap Build(HeightmapRecipe recipe, int[] rgb, ProvinceInfo[] provinces,
            TerritoryInfo[] territories = null, byte[] biomes = null, byte[] rivers = null, byte[] ocean = null)
        {
            recipe.Validate();
            var width = recipe.Width;
            var height = recipe.Height;
            var length = width * height;
            if (rgb == null || rgb.Length != length || (biomes != null && biomes.Length != length)
                || (rivers != null && rivers.Length != length) || (ocean != null && ocean.Length != length))
                throw new ArgumentException("Province and world layers must have identical dimensions.");
            if (provinces == null || provinces.Length == 0 || provinces.Length > length)
                throw new ArgumentException("Province definitions are missing or exceed the number of samples.");

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
                    if (x > 0) Connect(index, rgb[cell - 1], cell, cell - 1, ocean, records, neighbours);
                    if (y > 0) Connect(index, rgb[cell - width], cell, cell - width, ocean, records, neighbours);
                    if (index < 0) continue;
                    var record = records[index];
                    record.PixelCount++;
                    if (rivers != null && rivers[cell] != 0) record.RiverSamples++;
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
                HasRiverData = rivers != null, HasCoastData = ocean != null
            };
        }

        private static void Connect(int a, int b, int aCell, int bCell, byte[] ocean,
            ProvinceInfo[] records, HashSet<int>[] neighbours)
        {
            if (a == b) return;
            if (a >= 0 && b >= 0)
            {
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
            if (ocean == null) return;
            if (a >= 0 && records[a].Type == "land" && ocean[bCell] != 0) records[a].TouchesOpenSea = true;
            if (b >= 0 && records[b].Type == "land" && ocean[aCell] != 0) records[b].TouchesOpenSea = true;
        }
    }
}
