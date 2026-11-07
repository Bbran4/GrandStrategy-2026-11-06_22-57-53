using System;

namespace GrandStrategy.WorldGeneration
{
    [Serializable]
    public sealed class WorldRecipe
    {
        public const string FormatId = "GrandStrategy.HeightmapRecipe";
        public const int CurrentVersion = 2;
        public const float DefaultHeightScale = .35f;
        public const float MinimumHeightScale = .05f;
        public const float MaximumHeightScale = 1f;

        public string Format;
        public int Version;
        public HeightmapRecipe Recipe;
        public float HeightScale;
        public bool BuildRivers;
        public float RiverCatchment;
        public float RiverDepth;
        public float RiverWidthScale;
        public bool BuildClimate;
        public ClimateSettings Climate;
        public bool BuildBiomes;
        public float MaximumNavalRise;
        public bool ExportNavalMask;

        public bool IsLegacy => Version == 0 || Version == 1;

        public void Validate()
        {
            if (Format != FormatId || Recipe == null || (!IsLegacy && Version != CurrentVersion))
                throw new ArgumentException("Unsupported GrandStrategy recipe format or version.");
            Recipe.Validate();
            if (IsLegacy) return;

            if (!(HeightScale >= MinimumHeightScale && HeightScale <= MaximumHeightScale))
                throw new ArgumentException("Invalid terrain height scale in the recipe.");
            RiverGenerator.ValidateSettings(RiverCatchment, RiverDepth, RiverWidthScale);
            if (Climate == null) throw new ArgumentException("The climate settings are missing.");
            Climate.Validate();
            if (BuildBiomes && !BuildClimate) throw new ArgumentException("Biomes require climate in the recipe.");
            if (!(MaximumNavalRise >= 0f && MaximumNavalRise <= RiverNavigation.MaximumRiseLimit))
                throw new ArgumentException("Invalid naval rise limit in the recipe.");
        }

        public RebuiltWorld Rebuild(Func<float, string, bool> progress = null)
        {
            Validate();
            var terrain = HeightmapGenerator.Generate(Recipe, Stage(progress, 0f, .55f));
            RiverMap rivers = null;
            if (!IsLegacy && BuildRivers)
            {
                var candidate = RiverGenerator.Generate(terrain, RiverCatchment, RiverDepth,
                    Stage(progress, .55f, .25f), RiverWidthScale);
                if (candidate.ChannelCells > 0)
                {
                    rivers = candidate;
                    terrain = rivers.Terrain;
                }
            }

            var water = WaterMap.Classify(terrain);
            ClimateMap climate = null;
            BiomeMap biomes = null;
            if (!IsLegacy && BuildClimate)
            {
                climate = ClimateGenerator.Generate(terrain, water, Climate, Stage(progress, .8f, .14f));
                if (BuildBiomes)
                    biomes = BiomeGenerator.Generate(climate, water, rivers, Stage(progress, .94f, .06f));
            }

            if (progress != null && !progress(1f, "World ready")) throw new OperationCanceledException();
            return new RebuiltWorld(terrain, water, rivers, climate, biomes);
        }

        private static Func<float, string, bool> Stage(Func<float, string, bool> progress, float start, float span)
        {
            if (progress == null) return null;
            return (fraction, label) => progress(Math.Min(1f, start + span * fraction), label);
        }
    }

    public sealed class RebuiltWorld
    {
        public HeightmapResult Terrain { get; }
        public WaterMap Water { get; }
        public RiverMap Rivers { get; }
        public ClimateMap Climate { get; }
        public BiomeMap Biomes { get; }

        internal RebuiltWorld(HeightmapResult terrain, WaterMap water, RiverMap rivers, ClimateMap climate, BiomeMap biomes)
        {
            Terrain = terrain;
            Water = water;
            Rivers = rivers;
            Climate = climate;
            Biomes = biomes;
        }
    }
}
