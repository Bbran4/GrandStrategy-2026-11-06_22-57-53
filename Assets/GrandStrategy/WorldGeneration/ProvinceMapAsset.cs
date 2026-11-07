using System;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    public sealed class ProvinceMapAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] private ProvinceMap map;
        [SerializeField, HideInInspector] private HeightmapRecipe recipe;
        [SerializeField, HideInInspector] private float[] elevations;
        [SerializeField, HideInInspector] private float heightScale;

        public ProvinceMap Map => map;
        public float HeightScale => heightScale;

        public void Initialize(ProvinceMap provinces, HeightmapResult terrain, float scale)
        {
            if (provinces == null || terrain == null || provinces.Width != terrain.Recipe.Width
                || provinces.Height != terrain.Recipe.Height)
                throw new ArgumentException("The province and terrain dimensions must match.");
            if (!(scale >= WorldHeightmap.MinimumHeightScale && scale <= WorldHeightmap.MaximumHeightScale))
                throw new ArgumentOutOfRangeException(nameof(scale));
            map = provinces;
            recipe = terrain.Recipe.Copy();
            elevations = (float[])terrain.Elevations.Clone();
            heightScale = scale;
        }

        public HeightmapResult ReadTerrain()
        {
            if (recipe == null || map == null || elevations == null)
                throw new InvalidOperationException("The imported map asset is incomplete.");
            recipe.Validate();
            if (map.Width != recipe.Width || map.Height != recipe.Height
                || elevations.Length != recipe.Width * recipe.Height || map.ProvinceAt?.Length != elevations.Length
                || map.Provinces == null || map.Provinces.Length == 0)
                throw new InvalidOperationException("The saved province map and terrain dimensions do not match.");
            return new HeightmapResult(recipe.Copy(), (float[])elevations.Clone());
        }
    }
}
