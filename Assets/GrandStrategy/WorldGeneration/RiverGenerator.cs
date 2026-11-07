using System;

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
        public int ChannelCells { get; }
        public float CatchmentPercent { get; }
        public float CarveDepth { get; }

        private RiverMap(HeightmapResult world, HeightmapResult terrain, float[] cuts, byte[] directions,
            int channelCells, float catchmentPercent, float carveDepth)
        {
            Base = world;
            Terrain = terrain;
            CutElevations = cuts;
            Directions = directions;
            ChannelCells = channelCells;
            CatchmentPercent = catchmentPercent;
            CarveDepth = carveDepth;
        }

        public int DownstreamIndex(int index) => RiverGenerator.Neighbor(index, Directions[index],
            Base.Recipe.Width, Base.Recipe.Height);

        internal static float ApplyCut(float original, float cut) => cut == 0f ? original : cut;

        internal static RiverMap Compose(HeightmapResult world, float[] cuts, byte[] directions,
            float catchmentPercent, float carveDepth)
        {
            RiverGenerator.ValidateSettings(catchmentPercent, carveDepth);
            var width = world.Recipe.Width;
            var height = world.Recipe.Height;
            var sea = world.Recipe.SeaLevel;
            var source = world.Elevations;
            if (cuts == null || directions == null || cuts.Length != source.Length || directions.Length != source.Length)
                throw new InvalidOperationException("River layers do not match the heightmap dimensions.");

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

                if (directions[i] == 0) continue;
                channels++;
                var next = RiverGenerator.Neighbor(i, directions[i], width, height);
                if (source[i] < sea || next < 0 || ApplyCut(source[next], cuts[next]) > ApplyCut(source[i], cut)
                    || (source[next] >= sea && directions[next] == 0))
                    throw new InvalidOperationException("The river network contains an invalid flow direction.");
            }

            var terrain = final == null ? world : new HeightmapResult(world.Recipe.Copy(), final);
            return new RiverMap(world, terrain, cuts, directions, channels, catchmentPercent, carveDepth);
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

        private const int PriorityLevels = 65536;
        private static readonly int[] DeltaX = { 0, 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] DeltaY = { 0, 0, 1, 1, 1, 0, -1, -1, -1 };

        public static RiverMap Generate(HeightmapResult world, float catchmentPercent = DefaultCatchmentPercent,
            float carveDepth = DefaultCarveDepth, Func<float, string, bool> progress = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            ValidateSettings(catchmentPercent, carveDepth);
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
                return RiverMap.Compose(world, new float[length], directions, catchmentPercent, carveDepth);
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
                var size = Math.Min(4d, Math.Sqrt((double)flow[cell] / threshold));
                var radius = Math.Max(1d, size * resolutionScale);
                CutChannel(cell, width, height, sea, radius, carveDepth * (float)(.4 + .15 * size), source, cuts);
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

            var result = RiverMap.Compose(world, cuts, directions, catchmentPercent, carveDepth);
            Report(progress, 1f, "Rivers complete");
            return result;
        }

        internal static void ValidateSettings(float catchmentPercent, float carveDepth)
        {
            if (float.IsNaN(catchmentPercent) || catchmentPercent < MinimumCatchmentPercent || catchmentPercent > MaximumCatchmentPercent)
                throw new ArgumentOutOfRangeException(nameof(catchmentPercent));
            if (float.IsNaN(carveDepth) || carveDepth < MinimumCarveDepth || carveDepth > MaximumCarveDepth)
                throw new ArgumentOutOfRangeException(nameof(carveDepth));
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

        private static void CutChannel(int cell, int width, int height, float sea, double radius,
            float depth, float[] source, float[] cuts)
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
                    if (source[index] < sea) continue;
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
