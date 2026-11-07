using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    internal static class OpenGsMapReader
    {
        [Serializable]
        private sealed class WorldExport
        {
            public string Format;
            public int Version;
            public float TerrainHeightScale;
            public HeightmapRecipe Recipe;
            public bool HasBiomes;
            public bool HasRivers;
            public RiverExport Rivers;
        }

        [Serializable]
        private sealed class RiverExport
        {
            public string Widths;
        }

        public static ProvinceMap Read(string worldManifest, string provinceImage, string provinceDefinitions,
            string territoryImage, string territoryDefinitions, string territoryHistory,
            out HeightmapResult terrain, out float heightScale)
        {
            var export = JsonUtility.FromJson<WorldExport>(ReadText(worldManifest));
            if (export == null || export.Format != "GrandStrategy.HeightmapExport" || export.Version != 2 || export.Recipe == null)
                throw new InvalidDataException("Select a version 2 GrandStrategy world export manifest.");
            export.Recipe.Validate();
            var folder = Path.GetDirectoryName(Path.GetFullPath(worldManifest));
            var width = export.Recipe.Width;
            var height = export.Recipe.Height;
            var length = width * height;
            heightScale = export.TerrainHeightScale == 0f ? WorldHeightmap.DefaultHeightScale : export.TerrainHeightScale;
            if (!(heightScale >= WorldHeightmap.MinimumHeightScale && heightScale <= WorldHeightmap.MaximumHeightScale))
                throw new InvalidDataException("The world manifest contains an invalid height scale.");

            var provinces = ReadRows(provinceDefinitions).Select(row => new ProvinceInfo
            {
                Id = Required(row, "id"),
                Type = RegionType(row, "province_type"),
                Terrain = row.TryGetValue("province_terrain", out var type) ? type : null,
                Rgb = Rgb(row),
                SourceX = Coordinate(row, "x"),
                SourceY = Coordinate(row, "y")
            }).ToArray();
            var territories = ReadTerritories(territoryImage, territoryDefinitions, territoryHistory,
                provinces, width, height, out var territoryAt);

            using (var source = File.OpenRead(Path.Combine(folder, "heightmap.raw")))
                terrain = HeightmapEncoding.ReadRaw16(export.Recipe, source);
            var biomes = export.HasBiomes ? ReadBytes(Path.Combine(folder, "biomes.raw"), length) : null;
            var rivers = export.HasRivers ? ReadBytes(Path.Combine(folder, "river-directions.raw"), length) : null;
            var widths = rivers != null && export.Rivers?.Widths == "river-widths.raw"
                ? ReadBytes(Path.Combine(folder, "river-widths.raw"), length) : null;

            var water = ReadWater(Path.Combine(folder, "land-mask.png"), width, height);
            return ProvinceMap.Build(export.Recipe, ReadPng(provinceImage, width, height),
                provinces, territories, biomes, rivers, water, widths, territoryAt);
        }

        private static TerritoryInfo[] ReadTerritories(string image, string definitions, string history,
            ProvinceInfo[] provinces, int width, int height, out int[] territoryAt)
        {
            territoryAt = null;
            if (string.IsNullOrWhiteSpace(image) && string.IsNullOrWhiteSpace(definitions) && string.IsNullOrWhiteSpace(history))
                return null;
            if (string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(definitions) || string.IsNullOrWhiteSpace(history))
                throw new InvalidDataException("Select all three territory files, or leave them all blank.");

            var territories = ReadRows(definitions).Select(row => new TerritoryInfo
            {
                Id = Required(row, "id"),
                Type = RegionType(row, "territory_type"),
                Rgb = Rgb(row),
                SourceX = Coordinate(row, "x"),
                SourceY = Coordinate(row, "y")
            }).ToArray();
            var byId = territories.ToDictionary(t => t.Id, StringComparer.Ordinal);
            territoryAt = ProvinceMap.IndexTerritoryColors(ReadPng(image, width, height), territories, width);

            var provinceById = provinces.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var historyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in ReadRows(history))
            {
                var territoryId = Required(row, "id");
                if (!byId.ContainsKey(territoryId) || !historyIds.Add(territoryId))
                    throw new InvalidDataException($"Unknown or duplicate territory history ID {territoryId}.");
                if (!row.TryGetValue("provinces", out var members))
                    throw new InvalidDataException($"Missing province list for territory {territoryId}.");
                foreach (var member in members.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var id = member.Trim();
                    if (!provinceById.TryGetValue(id, out var province))
                        throw new InvalidDataException($"Territory {territoryId} refers to unknown province {id}.");
                    if (province.TerritoryId != null)
                        throw new InvalidDataException($"Province {id} appears more than once in territory history.");
                    province.TerritoryId = territoryId;
                }
            }
            return territories;
        }

        private static IEnumerable<Dictionary<string, string>> ReadRows(string path)
        {
            var contents = ReadText(path);
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".json":
                    var root = JObject.Parse(contents, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    foreach (var item in root.Properties())
                    {
                        if (!(item.Value is JObject fields))
                            throw new InvalidDataException($"Expected an object for {item.Name}.");
                        var row = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = item.Name };
                        foreach (var field in fields.Properties())
                        {
                            if (field.Name == "provinces" && field.Value is JArray members)
                                row.Add(field.Name, string.Join(",", members.Select(member =>
                                    member.Type == JTokenType.String ? member.Value<string>()
                                        : throw new InvalidDataException("Province IDs in history must be strings."))));
                            else if (field.Value is JValue value)
                                row.Add(field.Name, Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                        }
                        yield return row;
                    }
                    yield break;
                case ".csv":
                    using (var reader = new StringReader(contents))
                    {
                        var header = reader.ReadLine()?.Split(';');
                        if (header == null) throw new InvalidDataException("Empty OpenGS CSV.");
                        string line;
                        var lineNumber = 1;
                        while ((line = reader.ReadLine()) != null)
                        {
                            lineNumber++;
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var values = line.Split(';');
                            if (values.Length != header.Length)
                                throw new InvalidDataException($"Invalid OpenGS CSV row {lineNumber} in {Path.GetFileName(path)}.");
                            var row = new Dictionary<string, string>(StringComparer.Ordinal);
                            for (var i = 0; i < header.Length; i++)
                                row.Add(header[i].Trim(), values[i].Trim().Trim('"'));
                            yield return row;
                        }
                    }
                    yield break;
                default:
                    throw new InvalidDataException("OpenGS definitions and history must be exported as JSON or semicolon CSV.");
            }
        }

        private static string Required(Dictionary<string, string> row, string field)
        {
            if (!row.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Missing OpenGS field {field}.");
            return value;
        }

        private static string RegionType(Dictionary<string, string> row, string field)
        {
            var type = Required(row, field);
            if (type != "land" && type != "ocean" && type != "lake")
                throw new InvalidDataException($"Unknown OpenGS region type {type}.");
            return type;
        }

        private static float Coordinate(Dictionary<string, string> row, string field)
        {
            if (!float.TryParse(Required(row, field), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException($"Invalid OpenGS coordinate {field}.");
            return value;
        }

        private static int Rgb(Dictionary<string, string> row)
        {
            return Channel(row, "R") << 16 | Channel(row, "G") << 8 | Channel(row, "B");
        }

        private static int Channel(Dictionary<string, string> row, string field)
        {
            if (!byte.TryParse(Required(row, field), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"Invalid OpenGS colour channel {field}.");
            return value;
        }

        private static string ReadText(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Select the required input files.");
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
                throw new InvalidDataException($"Metadata file exceeds 16 MiB: {Path.GetFileName(path)}.");
            return File.ReadAllText(path);
        }

        private static byte[] ReadBytes(string path, int length)
        {
            if (new FileInfo(path).Length != length)
                throw new InvalidDataException($"Incorrect raster length: {Path.GetFileName(path)}.");
            return File.ReadAllBytes(path);
        }

        private static WaterKind[] ReadWater(string path, int width, int height)
        {
            var rgb = ReadPng(path, width, height);
            var result = new WaterKind[rgb.Length];
            var ocean = PackRgb(OpenGsLandPalette.Color(WaterKind.Ocean));
            var lake = PackRgb(OpenGsLandPalette.Color(WaterKind.Lake));
            for (var i = 0; i < rgb.Length; i++)
                result[i] = rgb[i] == ocean ? WaterKind.Ocean : rgb[i] == lake ? WaterKind.Lake : WaterKind.Land;
            return result;
        }

        private static int PackRgb(Rgb24 colour) => colour.R << 16 | colour.G << 8 | colour.B;

        private static int[] ReadPng(string path, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Select the required PNG images.");
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("PNG exceeds 64 MiB.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 33 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71
                || bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
                throw new InvalidDataException($"Use a lossless PNG for {info.Name}.");
            if (ReadBigEndian(bytes, 16) != width || ReadBigEndian(bytes, 20) != height)
                throw new InvalidDataException($"{info.Name} must match the world dimensions {width} × {height}.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes) || texture.width != width || texture.height != height)
                    throw new InvalidDataException($"Could not decode {info.Name}.");
                // Unity returns rows from the bottom, matching the RAW16 and biome rasters.
                var pixels = texture.GetPixels32();
                var rgb = new int[pixels.Length];
                for (var i = 0; i < rgb.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.a != 255) throw new InvalidDataException($"{info.Name} must have opaque data pixels.");
                    rgb[i] = pixel.r << 16 | pixel.g << 8 | pixel.b;
                }
                return rgb;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static int ReadBigEndian(byte[] bytes, int offset) =>
            bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
    }
}
