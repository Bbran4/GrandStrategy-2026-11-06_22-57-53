using System;
using System.IO;

namespace GrandStrategy.WorldGeneration
{
    public static class HeightmapEncoding
    {
        /// <summary>Unsigned 16-bit little-endian samples, rows south to north, columns west to east.</summary>
        public static void WriteRaw16(HeightmapResult result, Stream destination)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            using (var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, true))
                foreach (var value in result.Elevations)
                    writer.Write((ushort)Math.Round(HeightmapGenerator.Clamp(value) * 65535, MidpointRounding.AwayFromZero));
        }

        public static HeightmapResult ReadRaw16(HeightmapRecipe recipe, Stream source)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (source == null) throw new ArgumentNullException(nameof(source));
            recipe.Validate();
            var length = recipe.Width * recipe.Height;
            if (!source.CanSeek || source.Length - source.Position != length * 2L)
                throw new InvalidDataException("RAW16 length does not match the manifest dimensions.");

            var elevations = new float[length];
            using (var reader = new BinaryReader(source, System.Text.Encoding.UTF8, true))
                for (var i = 0; i < elevations.Length; i++)
                    elevations[i] = reader.ReadUInt16() / 65535f;
            return new HeightmapResult(recipe.Copy(), elevations);
        }

        /// <summary>Little-endian float32 cut elevations in the same sample order as RAW16.</summary>
        public static void WriteRiverCuts(RiverMap rivers, Stream destination)
        {
            if (rivers == null) throw new ArgumentNullException(nameof(rivers));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            using (var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, true))
                foreach (var value in rivers.CutElevations)
                    writer.Write(value);
        }
    }
}
