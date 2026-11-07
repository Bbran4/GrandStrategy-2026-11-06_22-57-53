using System;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    public sealed class WorldHeightmap : ScriptableObject
    {
        public const float DefaultHeightScale = .35f;
        public const float MinimumHeightScale = .05f;
        public const float MaximumHeightScale = 1f;

        [SerializeField, HideInInspector] private HeightmapRecipe recipe;
        [SerializeField, HideInInspector] private float heightScale;
        [SerializeField, HideInInspector] private float[] elevations;
        [SerializeField, HideInInspector] private float[] carvedElevations;
        [SerializeField, HideInInspector] private byte[] riverDirections;
        [SerializeField, HideInInspector] private float riverCatchmentPercent;
        [SerializeField, HideInInspector] private float riverCarveDepth;

        public int Width => recipe == null ? 0 : recipe.Width;
        public int Height => recipe == null ? 0 : recipe.Height;
        public int Seed => recipe == null ? 0 : recipe.Seed;
        public float SeaLevel => recipe == null ? 0 : recipe.SeaLevel;
        public bool HasRivers => (riverDirections?.Length ?? 0) > 0 || (carvedElevations?.Length ?? 0) > 0;

        // Assets saved before height scale was introduced have no serialized value.
        public float HeightScale => heightScale == 0f ? DefaultHeightScale : heightScale;

        public void Initialize(HeightmapResult result, float heightScale = DefaultHeightScale, RiverMap rivers = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            ValidateHeightScale(heightScale);
            if (rivers != null && !ReferenceEquals(rivers.Terrain, result))
                throw new ArgumentException("The river network belongs to a different terrain.", nameof(rivers));
            recipe = result.Recipe.Copy();
            elevations = (float[])(rivers?.Base ?? result).Elevations.Clone();
            carvedElevations = rivers == null ? null : (float[])result.Elevations.Clone();
            riverDirections = rivers == null ? null : (byte[])rivers.Directions.Clone();
            riverCatchmentPercent = rivers?.CatchmentPercent ?? 0f;
            riverCarveDepth = rivers?.CarveDepth ?? 0f;
            this.heightScale = heightScale;
        }

        public HeightmapResult Read() => ReadSamples(HasRivers ? carvedElevations : elevations);

        public RiverMap ReadRivers()
        {
            if (!HasRivers) return null;
            RiverGenerator.ValidateSettings(riverCatchmentPercent, riverCarveDepth);
            var world = ReadSamples(elevations);
            var terrain = ReadSamples(carvedElevations);
            var length = world.Elevations.Length;
            if (riverDirections == null || riverDirections.Length != length)
                throw new InvalidOperationException("River directions do not match the heightmap dimensions.");

            var channels = 0;
            var sea = recipe.SeaLevel;
            for (var i = 0; i < length; i++)
            {
                var original = world.Elevations[i];
                var carved = terrain.Elevations[i];
                if (carved > original || (original < sea ? carved != original : carved < sea))
                    throw new InvalidOperationException("River carving must preserve existing land and water coverage.");
                if (riverDirections[i] == 0) continue;
                channels++;
                var next = RiverGenerator.Neighbor(i, riverDirections[i], Width, Height);
                if (original < sea || next < 0 || terrain.Elevations[next] > carved
                    || (world.Elevations[next] >= sea && riverDirections[next] == 0))
                    throw new InvalidOperationException("The saved river network contains an invalid flow direction.");
            }

            return new RiverMap(world, terrain, (byte[])riverDirections.Clone(), channels,
                riverCatchmentPercent, riverCarveDepth);
        }

        private HeightmapResult ReadSamples(float[] samples)
        {
            if (recipe == null) throw new InvalidOperationException("Heightmap has no recipe.");
            recipe.Validate();
            ValidateHeightScale(HeightScale);
            if (samples == null || samples.Length != recipe.Width * recipe.Height)
                throw new InvalidOperationException("Heightmap sample count does not match its dimensions.");
            foreach (var value in samples)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                    throw new InvalidOperationException("Heightmap contains invalid elevation data.");
            return new HeightmapResult(recipe.Copy(), (float[])samples.Clone());
        }

        private static void ValidateHeightScale(float scale)
        {
            if (float.IsNaN(scale) || scale < MinimumHeightScale || scale > MaximumHeightScale)
                throw new ArgumentOutOfRangeException(nameof(scale), "Terrain height scale is outside the supported range.");
        }
    }
}
