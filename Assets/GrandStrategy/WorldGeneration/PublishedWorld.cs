// A campaign-ready snapshot produced by the World Editor.
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    public sealed class PublishedWorld : ScriptableObject
    {
        [SerializeField] private int version;
        [SerializeField] private WorldHeightmap world;
        [SerializeField] private ProvinceMapAsset provinces;
        [SerializeField] private TerrainData terrain;
        [SerializeField] private int meshDetail;

        public int Version => version;
        public WorldHeightmap World => world;
        public ProvinceMapAsset Provinces => provinces;
        public TerrainData Terrain => terrain;
        public int MeshDetail => meshDetail;

#if UNITY_EDITOR
        public void Initialize(int nextVersion, WorldHeightmap sourceWorld, ProvinceMapAsset sourceProvinces,
            TerrainData sculptedTerrain, int detail)
        {
            version = nextVersion;
            world = sourceWorld;
            provinces = sourceProvinces;
            terrain = sculptedTerrain;
            meshDetail = Mathf.Clamp(detail, 512, 2048);
        }
#endif
    }
}
