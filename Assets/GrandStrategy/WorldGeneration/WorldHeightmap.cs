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

        public int Width => recipe == null ? 0 : recipe.Width;
        public int Height => recipe == null ? 0 : recipe.Height;
        public int Seed => recipe == null ? 0 : recipe.Seed;
        public float SeaLevel => recipe == null ? 0 : recipe.SeaLevel;

        // Assets saved before height scale was introduced have no serialized value.
        public float HeightScale => heightScale == 0f ? DefaultHeightScale : heightScale;

        public void Initialize(HeightmapResult result, float heightScale = DefaultHeightScale)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            ValidateHeightScale(heightScale);
            recipe = result.Recipe.Copy();
            elevations = (float[])result.Elevations.Clone();
            this.heightScale = heightScale;
        }

        public HeightmapResult Read()
        {
            if (recipe == null) throw new InvalidOperationException("Heightmap has no recipe.");
            recipe.Validate();
            ValidateHeightScale(HeightScale);
            if (elevations == null || elevations.Length != recipe.Width * recipe.Height)
                throw new InvalidOperationException("Heightmap sample count does not match its dimensions.");
            foreach (var value in elevations)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                    throw new InvalidOperationException("Heightmap contains invalid elevation data.");
            return new HeightmapResult(recipe.Copy(), (float[])elevations.Clone());
        }

        private static void ValidateHeightScale(float scale)
        {
            if (float.IsNaN(scale) || scale < MinimumHeightScale || scale > MaximumHeightScale)
                throw new ArgumentOutOfRangeException(nameof(scale), "Terrain height scale is outside the supported range.");
        }
    }
}
