using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    /// <summary>Sea-connected ship-route candidates and their OpenGS raster footprint.</summary>
    public sealed class RiverNavigation
    {
        public const float DefaultMaximumRise = .08f;
        public const float MaximumRiseLimit = .25f;
        public const byte Channel = 1;
        public const byte Connector = 2;
        public const byte WaterFootprint = 4;

        private readonly byte[] cells;
        public int ChannelCount { get; }
        public byte this[int index] => cells[index];

        private RiverNavigation(byte[] cells, int channelCount)
        {
            this.cells = cells;
            ChannelCount = channelCount;
        }

        public byte[] CopyCells() => (byte[])cells.Clone();

        public static RiverNavigation Build(RiverMap rivers, WaterMap water, float maximumRise)
        {
            if (rivers == null) throw new ArgumentNullException(nameof(rivers));
            if (water == null || !ReferenceEquals(water.Source, rivers.Terrain))
                throw new ArgumentException("Water classification must describe the carved terrain.", nameof(water));
            if (float.IsNaN(maximumRise) || maximumRise < 0 || maximumRise > MaximumRiseLimit)
                throw new ArgumentOutOfRangeException(nameof(maximumRise));

            var terrain = rivers.Terrain;
            var width = terrain.Recipe.Width;
            var height = terrain.Recipe.Height;
            var ceiling = terrain.Recipe.SeaLevel + maximumRise;
            var cells = new byte[terrain.Elevations.Length];
            var upstream = new List<int>();

            // Start from mouths that reach the ocean. Lake outlets are not sea access.
            for (var cell = 0; cell < cells.Length; cell++)
            {
                if (rivers.Directions[cell] == 0 || terrain.Elevations[cell] > ceiling) continue;
                var next = rivers.DownstreamIndex(cell);
                if (next >= 0 && water[next] == WaterKind.Ocean)
                    AddChannel(cell, next, width, water, cells, upstream);
            }

            // Reverse the downstream links so every accepted cell has a route back to sea.
            for (var head = 0; head < upstream.Count; head++)
            {
                var cell = upstream[head];
                for (byte direction = 1; direction <= 8; direction++)
                {
                    var candidate = RiverGenerator.Neighbor(cell, direction, width, height);
                    if (candidate < 0 || (cells[candidate] & Channel) != 0
                        || terrain.Elevations[candidate] > ceiling || rivers.DownstreamIndex(candidate) != cell)
                        continue;
                    AddChannel(candidate, cell, width, water, cells, upstream);
                }
            }

            foreach (var cell in upstream)
                rivers.StampSurface(cells, cell, WaterFootprint);

            return new RiverNavigation(cells, upstream.Count);
        }

        private static void AddChannel(int cell, int downstream, int width, WaterMap water,
            byte[] cells, List<int> upstream)
        {
            var x = cell % width;
            var y = cell / width;
            var downstreamX = downstream % width;
            var downstreamY = downstream / width;
            if (x != downstreamX && y != downstreamY)
            {
                // Bridge diagonals for the four-connected mask while preserving lake pixels.
                // A bridge pixel is not a ship movement node.
                var bridge = y * width + downstreamX;
                var other = downstreamY * width + x;
                if (water[bridge] == WaterKind.Lake || water[other] == WaterKind.Ocean)
                    bridge = other;
                if (water[bridge] == WaterKind.Lake) return;
                if (water[bridge] == WaterKind.Land) cells[bridge] |= Connector;
            }
            cells[cell] |= Channel;
            upstream.Add(cell);
        }
    }
}
