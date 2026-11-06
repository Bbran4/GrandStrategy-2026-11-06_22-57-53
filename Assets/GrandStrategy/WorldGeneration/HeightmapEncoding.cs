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
    }
}
