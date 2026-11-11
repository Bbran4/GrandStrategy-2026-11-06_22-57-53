// Routes drainage, selects river channels and carves them into the elevation field. River direction data
// also drives flow rendering and navigation.
using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    public sealed class RiverMap
    {
        public HeightmapResult Base { get; }
        public HeightmapResult Terrain { get; }
        // Zero leaves the base sample intact. A positive value is the exact carved elevation.
        public float[] CutElevations { get; }
        // Zero is outside the network. 1–8 run counterclockwise from east, in 45-degree increments.
        public byte[] Directions { get; }
        // Full water width in raster samples. Zero is off-channel. Older assets omit the layer.
        public byte[] Widths { get; }
        public float WidthScale { get; }
        public int ChannelCells { get; }
        public float CatchmentPercent { get; }
        public float CarveDepth { get; }

        private RiverMap(HeightmapResult world, HeightmapResult terrain, float[] cuts, byte[] directions,
            int channelCells, float catchmentPercent, float carveDepth, byte[] widths, float widthScale)
        {
            Base = world;
            Terrain = terrain;
            CutElevations = cuts;
            Directions = directions;
            ChannelCells = channelCells;
            CatchmentPercent = catchmentPercent;
            CarveDepth = carveDepth;
            Widths = widths;
            WidthScale = widthScale;
        }

        public int DownstreamIndex(int index) => RiverGenerator.Neighbor(index, Directions[index],
            Base.Recipe.Width, Base.Recipe.Height);

        public byte[] BuildSurfaceMask() => RasterizeSurface(Base.Recipe, Directions, Widths, IsBaseLand);

        // Expand the centreline direction codes across the river footprint so the renderer can animate
        // flow.
        internal byte[] BuildSurfaceDirections()
        {
            var recipe = Base.Recipe;
            var surface = new byte[Directions.Length];
            Func<int, bool> isLand = IsBaseLand;
            for (var cell = 0; cell < Directions.Length; cell++)
                if (Directions[cell] != 0)
                    StampSurface(surface, cell, Directions[cell], recipe.Width, recipe.Height,
                        Widths?[cell] ?? 1, isLand, writeDirection: true);
            for (var cell = 0; cell < Directions.Length; cell++)
                if (Directions[cell] != 0 && surface[cell] != 0)
                    surface[cell] = Directions[cell];
            return surface;
        }

        // Create a widened river mask from the centreline and channel widths.
        public static byte[] RasterizeSurface(HeightmapRecipe recipe, byte[] directions, byte[] widths, Func<int, bool> isLand)
        {
            recipe.Validate();
            var length = recipe.Width * recipe.Height;
            if (directions == null || directions.Length != length || (widths != null && widths.Length != length))
                throw new ArgumentException("River layers do not match the surface dimensions.");
            if (isLand == null) throw new ArgumentNullException(nameof(isLand));
            var surface = new byte[length];
            for (var cell = 0; cell < length; cell++)
            {
                var direction = directions[cell];
                if (direction > 8 || (widths != null && (widths[cell] == 0) != (direction == 0)))
                    throw new ArgumentException("Invalid river directions or widths.");
                if (direction != 0)
                    StampSurface(surface, cell, 1, recipe.Width, recipe.Height, widths?[cell] ?? 1, isLand);
            }
            return surface;
        }

        internal void StampSurface(byte[] destination, System.Collections.Generic.IEnumerable<int> cells, byte flag)
        {
            Func<int, bool> isLand = IsBaseLand;
            foreach (var cell in cells)
                StampSurface(destination, cell, flag, Base.Recipe.Width, Base.Recipe.Height, Widths?[cell] ?? 1, isLand);
        }

        private bool IsBaseLand(int index) => Base.Elevations[index] >= Base.Recipe.SeaLevel;

        private static void StampSurface(byte[] destination, int cell, byte flag, int width, int height,
            byte diameter, Func<int, bool> isLand, bool writeDirection = false)
        {
            var radius = diameter * .5f;
            var reach = (int)Math.Floor(radius);
            var x = cell % width;
            var y = cell / width;
            var radiusSquared = radius * radius;
            var firstColumn = Math.Max(0, x - reach);
            var lastColumn = Math.Min(width - 1, x + reach);
            var lastRow = Math.Min(height - 1, y + reach);
            for (var row = Math.Max(0, y - reach); row <= lastRow; row++)
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var dx = column - x;
                    var dy = row - y;
                    var index = row * width + column;
                    if (dx * dx + dy * dy <= radiusSquared && isLand(index))
                        destination[index] = writeDirection ? flag : (byte)(destination[index] | flag);
                }
        }

        internal static float ApplyCut(float original, float cut) => cut == 0f ? original : cut;

        // Combine the original heights and stored cuts into the carved terrain used by other layers.
        internal static RiverMap Compose(HeightmapResult world, float[] cuts, byte[] directions,
            float catchmentPercent, float carveDepth, byte[] widths = null,
            float widthScale = RiverGenerator.DefaultWidthScale)
        {
            RiverGenerator.ValidateSettings(catchmentPercent, carveDepth, widthScale);
            var width = world.Recipe.Width;
            var height = world.Recipe.Height;
            var sea = world.Recipe.SeaLevel;
            var source = world.Elevations;
            if (cuts == null || directions == null || cuts.Length != source.Length || directions.Length != source.Length)
                throw new InvalidOperationException("River layers do not match the heightmap dimensions.");
            if (widths != null && widths.Length != source.Length)
                throw new InvalidOperationException("River widths do not match the heightmap dimensions.");

            float[] final = null;
            var channels = 0;
            for (var i = 0; i < source.Length; i++)
            {
                var cut = cuts[i];
                if (cut != 0f)
                {
                    if (float.IsNaN(cut) || cut < sea || cut >= source[i])
                        throw new InvalidOperationException("River cuts must lower land without changing its water classification.");
                    if (final == null) final = (float[])source.Clone();
                    final[i] = cut;
                }

                if (widths != null && (widths[i] == 0) != (directions[i] == 0))
                    throw new InvalidOperationException("River widths must align with channel directions.");
                if (directions[i] == 0) continue;
                channels++;
                var next = RiverGenerator.Neighbor(i, directions[i], width, height);
                if (source[i] < sea || next < 0 || ApplyCut(source[next], cuts[next]) > ApplyCut(source[i], cut)
                    || (source[next] >= sea && directions[next] == 0))
                    throw new InvalidOperationException("The river network contains an invalid flow direction.");
            }

            var terrain = final == null ? world : new HeightmapResult(world.Recipe.Copy(), final);
            return new RiverMap(world, terrain, cuts, directions, channels, catchmentPercent, carveDepth, widths, widthScale);
        }
    }

    public static class RiverGenerator
    {
        public const float MinimumCatchmentPercent = .05f;
        public const float MaximumCatchmentPercent = 2f;
        public const float DefaultCatchmentPercent = .35f;
        public const float MinimumCarveDepth = .001f;
        public const float MaximumCarveDepth = .04f;
        public const float DefaultCarveDepth = .012f;
        public const float MinimumWidthScale = .5f;
        public const float MaximumWidthScale = 2f;
        public const float DefaultWidthScale = 1f;

        private const int PriorityLevels = 65536;
        private static readonly int[] DeltaX = { 0, 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] DeltaY = { 0, 0, 1, 1, 1, 0, -1, -1, -1 };

        // Build a downstream graph, accumulate catchment area and carve the channels that pass the river
        // threshold.
        public static RiverMap Generate(HeightmapResult world, float catchmentPercent = DefaultCatchmentPercent,
            float carveDepth = DefaultCarveDepth, Func<float, string, bool> progress = null,
            float widthScale = DefaultWidthScale)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            ValidateSettings(catchmentPercent, carveDepth, widthScale);
            world.Recipe.Validate();
            var width = world.Recipe.Width;
            var height = world.Recipe.Height;
            var sea = world.Recipe.SeaLevel;
            var source = world.Elevations;
            var length = width * height;
            if (source.Length != length) throw new ArgumentException("Heightmap dimensions do not match the samples.");

            var landCount = 0;
            for (var i = 0; i < length; i++)
            {
                ReportRow(progress, i, length, 0f, .05f, "Checking elevations");
                var value = source[i];
                if (float.IsNaN(value) || value < 0f || value > 1f)
                    throw new ArgumentException("River generation requires finite normalized elevations.");
                if (value >= sea) landCount++;
            }

            var directions = new byte[length];
            if (landCount == 0 || landCount == length)
            {
                Report(progress, 1f, "No land-to-water drainage");
                return RiverMap.Compose(world, new float[length], directions, catchmentPercent, carveDepth, widthScale: widthScale);
            }

            // Priority-Flood from existing water. The 16-bit priority is routing-only. Each land
            // cell points to an earlier settled cell, even when elevations tie on a flat.
            var flow = new int[length];
            var order = new int[landCount];
            var heads = new int[PriorityLevels];
            var tails = new int[PriorityLevels];
            Array.Fill(heads, -1);

            for (var i = 0; i < length; i++)
            {
                ReportRow(progress, i, length, .05f, .15f, "Finding ocean and lake outlets");
                if (source[i] < sea) continue;
                for (byte direction = 1; direction <= 8; direction++)
                {
                    var neighbor = Neighbor(i, direction, width, height);
                    if (neighbor < 0 || source[neighbor] >= sea) continue;
                    directions[i] = direction;
                    Enqueue(i, Priority(source[i]), heads, tails, flow);
                    break;
                }
            }

            var settled = 0;
            for (var priority = 0; priority < PriorityLevels; priority++)
            {
                while (heads[priority] >= 0)
                {
                    ReportRow(progress, settled, landCount, .2f, .35f, "Routing drainage across terrain");
                    var cell = heads[priority];
                    heads[priority] = flow[cell];
                    order[settled++] = cell;
                    // Once removed from the queue, this slot stores contributing area.
                    flow[cell] = 1;
                    for (byte direction = 1; direction <= 8; direction++)
                    {
                        var next = Neighbor(cell, direction, width, height);
                        if (next < 0 || directions[next] != 0 || source[next] < sea) continue;
                        directions[next] = (byte)((direction + 3) % 8 + 1);
                        Enqueue(next, Math.Max(priority, Priority(source[next])), heads, tails, flow);
                    }
                }
            }

            for (var i = settled - 1; i >= 0; i--)
            {
                ReportRow(progress, settled - 1 - i, settled, .55f, .15f, "Accumulating catchments");
                var cell = order[i];
                var downstream = Neighbor(cell, directions[cell], width, height);
                if (source[downstream] >= sea) flow[downstream] += flow[cell];
            }

            var threshold = Math.Max(8, (int)Math.Ceiling(landCount * (double)catchmentPercent / 100));
            var cuts = new float[length];
            var widths = new byte[length];
            var resolutionScale = Math.Sqrt(length / (1024d * 512d));
            for (var i = 0; i < settled; i++)
            {
                ReportRow(progress, i, settled, .7f, .22f, "Carving river channels");
                var cell = order[i];
                if (flow[cell] < threshold)
                {
                    directions[cell] = 0;
                    continue;
                }
                var size = Math.Sqrt((double)flow[cell] / threshold);
                // Contributing area grows downstream, including at tributary junctions.
                var diameter = Math.Min(10d, size) * resolutionScale * widthScale;
                widths[cell] = (byte)Math.Max(1d, Math.Round(diameter, MidpointRounding.AwayFromZero));
                var bankRadius = .5d + widths[cell] * .8d;
                var depthFactor = .4 + .15 * Math.Min(4d, size);
                CutChannel(cell, width, height, sea, bankRadius, carveDepth * (float)depthFactor, source, cuts);
            }

            // Propagate the bed level from tributaries toward outlets. Where drainage crosses
            // an uphill saddle, this can cut deeper than the ordinary channel-depth setting.
            for (var i = settled - 1; i >= 0; i--)
            {
                ReportRow(progress, settled - 1 - i, settled, .92f, .08f, "Grading riverbeds toward water");
                var cell = order[i];
                var downstream = Neighbor(cell, directions[cell], width, height);
                if (downstream < 0 || source[downstream] < sea) continue;
                var bed = RiverMap.ApplyCut(source[cell], cuts[cell]);
                if (bed < RiverMap.ApplyCut(source[downstream], cuts[downstream]))
                    cuts[downstream] = bed;
            }

            var result = RiverMap.Compose(world, cuts, directions, catchmentPercent, carveDepth, widths, widthScale);
            Report(progress, 1f, "Rivers complete");
            return result;
        }

        // Route an authored channel through ordered waypoints and grade the complete network downstream.
        // Existing tributaries remain connected. An abandoned tail is removed if nothing else feeds it.
        public static RiverMap RebuildAuthored(HeightmapResult world, RiverMap previous, IReadOnlyList<int> waypoints,
            byte channelWidth, float channelDepth)
        {
            if (world == null || waypoints == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(waypoints));
            var width = world.Recipe.Width;
            var height = world.Recipe.Height;
            var elevations = world.Elevations;
            var sea = world.Recipe.SeaLevel;
            var length = elevations.Length;
            if (previous != null && (previous.Base.Recipe.Width != width || previous.Base.Recipe.Height != height))
                throw new ArgumentException("The river network and edited world must share dimensions.");
            if (channelWidth < 1 || channelWidth > 24 || float.IsNaN(channelDepth)
                || channelDepth < MinimumCarveDepth || channelDepth > MaximumCarveDepth)
                throw new ArgumentException("The authored river width or depth is outside its supported range.");
            if (waypoints.Count == 1)
                throw new ArgumentException("Draw a river with at least a start and a water outlet.");
            if (previous == null && waypoints.Count == 0) return null;

            var directions = previous == null ? new byte[length] : (byte[])previous.Directions.Clone();
            var widths = new byte[length];
            var repair = previous == null ? null : new bool[length];
            for (var i = 0; i < length; i++)
            {
                if (directions[i] == 0) continue;
                widths[i] = previous?.Widths?[i] ?? 1;
                if (elevations[i] < sea)
                {
                    MarkOldBanks(i, widths[i]);
                    directions[i] = widths[i] = 0;
                }
            }

            var path = new List<int>();
            var authored = new HashSet<int>();
            var abandoned = new List<int>();
            for (var i = 0; i < waypoints.Count; i++)
            {
                var to = waypoints[i];
                if ((uint)to >= (uint)length) throw new ArgumentException("A river waypoint is outside the map.");
                if (i == 0) { path.Add(to); authored.Add(to); continue; }
                var from = waypoints[i - 1];
                var x = from % width;
                var y = from / width;
                var dx = to % width - x;
                var dy = to / width - y;
                var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
                for (var step = 1; step <= steps; step++)
                {
                    var next = (y + (int)Math.Round((double)dy * step / steps)) * width
                        + x + (int)Math.Round((double)dx * step / steps);
                    if (path[path.Count - 1] == next) continue;
                    if (!authored.Add(next)) throw new ArgumentException("The drawn river crosses itself.");
                    path.Add(next);
                }
            }

            if (path.Count > 0)
            {
                if (path.Count < 2 || elevations[path[path.Count - 1]] >= sea)
                    throw new ArgumentException("End the river at an ocean or lake cell.");
                for (var i = 0; i < path.Count - 1; i++)
                {
                    var cell = path[i];
                    if (elevations[cell] < sea) throw new ArgumentException("The river must stay on land until its outlet.");
                    var next = path[i + 1];
                    var old = Neighbor(cell, directions[cell], width, height);
                    if (old >= 0 && old != next) abandoned.Add(old);
                    if (old >= 0) MarkOldBanks(cell, widths[cell]);
                    byte direction = 1;
                    while (direction <= 8 && Neighbor(cell, direction, width, height) != next) direction++;
                    if (direction > 8) throw new InvalidOperationException("The river path has a gap.");
                    directions[cell] = direction;
                    widths[cell] = channelWidth;
                }
            }

            var incoming = new byte[length];
            for (var cell = 0; cell < length; cell++)
            {
                if (directions[cell] == 0) continue;
                var next = Neighbor(cell, directions[cell], width, height);
                if (next >= 0 && directions[next] != 0) incoming[next]++;
            }

            foreach (var start in abandoned)
                for (var cell = start; cell >= 0 && directions[cell] != 0 && incoming[cell] == 0 && !authored.Contains(cell);)
                {
                    var next = Neighbor(cell, directions[cell], width, height);
                    MarkOldBanks(cell, widths[cell]);
                    directions[cell] = widths[cell] = 0;
                    if (next >= 0 && directions[next] != 0) incoming[next]--;
                    cell = next;
                }

            var queue = new Queue<int>();
            var cuts = new float[length];
            if (previous != null)
                for (var cell = 0; cell < length; cell++)
                {
                    var oldCut = previous.CutElevations[cell];
                    if (repair[cell] || oldCut == 0f || elevations[cell] <= sea) continue;
                    var oldBase = previous.Base.Elevations[cell];
                    var moved = elevations[cell] == oldBase ? oldCut : Math.Max(sea, elevations[cell] - (oldBase - oldCut));
                    if (moved < elevations[cell]) cuts[cell] = moved;
                }
            var channels = 0;
            var carveDepth = previous?.CarveDepth ?? DefaultCarveDepth;
            for (var cell = 0; cell < length; cell++)
            {
                if (directions[cell] == 0) continue;
                var next = Neighbor(cell, directions[cell], width, height);
                if (next < 0 || (elevations[next] >= sea && directions[next] == 0))
                    throw new InvalidOperationException("A river lost its water outlet. Extend it to the new shoreline before applying.");
                channels++;
                if (incoming[cell] == 0) queue.Enqueue(cell);
                var depth = authored.Contains(cell) ? channelDepth : carveDepth * .7f;
                if (previous != null && !authored.Contains(cell) && previous.CutElevations[cell] != 0f)
                    depth = Math.Min(MaximumCarveDepth, previous.Base.Elevations[cell] - previous.CutElevations[cell]);
                CutChannel(cell, width, height, sea, .5 + widths[cell] * .8, depth, elevations, cuts,
                    authored.Contains(cell) ? null : repair);
            }

            var graded = 0;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                graded++;
                var next = Neighbor(cell, directions[cell], width, height);
                if (directions[next] == 0) continue;
                var bed = RiverMap.ApplyCut(elevations[cell], cuts[cell]);
                if (bed < RiverMap.ApplyCut(elevations[next], cuts[next])) cuts[next] = bed;
                if (--incoming[next] == 0) queue.Enqueue(next);
            }
            if (graded != channels) throw new InvalidOperationException("The river route creates a loop. Change the waypoints.");
            return RiverMap.Compose(world, cuts, directions, previous?.CatchmentPercent ?? DefaultCatchmentPercent,
                carveDepth, widths, previous?.WidthScale ?? DefaultWidthScale);

            void MarkOldBanks(int cell, byte diameter)
            {
                if (repair == null) return;
                var x = cell % width;
                var y = cell / width;
                var reach = (int)Math.Ceiling(.5 + diameter * .8);
                for (var row = Math.Max(0, y - reach); row <= Math.Min(height - 1, y + reach); row++)
                    for (var column = Math.Max(0, x - reach); column <= Math.Min(width - 1, x + reach); column++)
                        repair[row * width + column] = true;
            }
        }

        internal static void ValidateSettings(float catchmentPercent, float carveDepth, float widthScale)
        {
            if (float.IsNaN(catchmentPercent) || catchmentPercent < MinimumCatchmentPercent || catchmentPercent > MaximumCatchmentPercent)
                throw new ArgumentOutOfRangeException(nameof(catchmentPercent));
            if (float.IsNaN(carveDepth) || carveDepth < MinimumCarveDepth || carveDepth > MaximumCarveDepth)
                throw new ArgumentOutOfRangeException(nameof(carveDepth));
            if (float.IsNaN(widthScale) || widthScale < MinimumWidthScale || widthScale > MaximumWidthScale)
                throw new ArgumentOutOfRangeException(nameof(widthScale));
        }

        internal static int Neighbor(int cell, byte direction, int width, int height)
        {
            if (direction == 0 || direction > 8) return -1;
            var x = cell % width + DeltaX[direction];
            var y = cell / width + DeltaY[direction];
            return x >= 0 && x < width && y >= 0 && y < height ? y * width + x : -1;
        }

        private static int Priority(float elevation) => (int)(elevation * (PriorityLevels - 1));

        private static void Enqueue(int cell, int priority, int[] heads, int[] tails, int[] links)
        {
            links[cell] = -1;
            if (heads[priority] < 0) heads[priority] = cell;
            else links[tails[priority]] = cell;
            tails[priority] = cell;
        }

        // Lower terrain near a selected channel while blending the cut toward its banks.
        private static void CutChannel(int cell, int width, int height, float sea, double radius,
            float depth, float[] source, float[] cuts, bool[] repair = null)
        {
            var x = cell % width;
            var y = cell / width;
            var reach = (int)Math.Ceiling(radius);
            var radiusSquared = radius * radius;
            for (var row = Math.Max(0, y - reach); row <= Math.Min(height - 1, y + reach); row++)
                for (var column = Math.Max(0, x - reach); column <= Math.Min(width - 1, x + reach); column++)
                {
                    var dx = column - x;
                    var dy = row - y;
                    var distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared >= radiusSquared) continue;
                    var index = row * width + column;
                    if (source[index] < sea || (repair != null && !repair[index])) continue;
                    var depthAtSample = depth * (float)(1d - distanceSquared / radiusSquared);
                    var target = Math.Max(sea, source[index] - depthAtSample);
                    if (target < RiverMap.ApplyCut(source[index], cuts[index]))
                        cuts[index] = target;
                }
        }

        private static void ReportRow(Func<float, string, bool> progress, int index, int length,
            float start, float span, string stage)
        {
            if (progress != null && (index & 8191) == 0) Report(progress, start + span * index / length, stage);
        }

        private static void Report(Func<float, string, bool> progress, float fraction, string stage)
        {
            if (progress != null && !progress(fraction, stage)) throw new OperationCanceledException();
        }
    }
}
