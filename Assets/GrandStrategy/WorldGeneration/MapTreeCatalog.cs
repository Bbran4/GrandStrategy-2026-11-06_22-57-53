using System;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    [CreateAssetMenu(menuName = "GrandStrategy/Tree Model Catalog")]
    public sealed class MapTreeCatalog : ScriptableObject
    {
        [Serializable]
        internal sealed class Entry
        {
            public Mesh crown;
            public Mesh trunk;
            public bool singleMesh;
            public Color32 crownTint = new Color32(108, 123, 74, 255);
            public Color32 trunkTint = new Color32(91, 76, 56, 255);
        }

        [SerializeField] private Entry oak = new Entry();
        [SerializeField] private Entry birch = new Entry();
        [SerializeField] private Entry spruce = new Entry();
        [SerializeField] private Entry stonePine = new Entry();
        [SerializeField] private Entry olive = new Entry();
        [SerializeField] private Entry acacia = new Entry();
        [SerializeField] private Entry palm = new Entry();

        internal Entry Get(TreeSpecies species) => species switch
        {
            TreeSpecies.Oak => oak,
            TreeSpecies.Birch => birch,
            TreeSpecies.Spruce => spruce,
            TreeSpecies.StonePine => stonePine,
            TreeSpecies.Olive => olive,
            TreeSpecies.Acacia => acacia,
            TreeSpecies.Palm => palm,
            _ => throw new ArgumentOutOfRangeException(nameof(species))
        };
    }

    // Cache the imported asset data once. The nature renderer only places and batches copies.
    internal sealed class MapTreeModel
    {
        internal sealed class Part
        {
            public readonly Vector3[] Vertices;
            public readonly Vector3[] Normals;
            public readonly int[] Triangles;

            public Part(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Normals = mesh.normals;
                Triangles = mesh.triangles;
            }
        }

        public readonly Part Crown;
        public readonly Part Trunk;
        public readonly Color32 CrownTint;
        public readonly Color32 TrunkTint;
        public readonly float Base;
        public readonly float Height;

        public MapTreeModel(TreeSpecies species, MapTreeCatalog catalog)
        {
            var entry = catalog?.Get(species);
            var custom = entry?.crown != null;
            var crown = Readable(custom ? entry.crown : null, species, "Crown");
            var trunk = custom && entry.singleMesh ? null : Readable(custom ? entry.trunk : null, species, "Trunk");
            Crown = crown == null ? null : new Part(crown);
            Trunk = trunk == null ? null : new Part(trunk);
            Base = trunk != null ? Mathf.Min(trunk.bounds.min.y, crown != null ? crown.bounds.min.y : trunk.bounds.min.y)
                : crown != null ? crown.bounds.min.y : 0f;
            var top = crown != null ? crown.bounds.max.y : Base;
            if (trunk != null) top = Mathf.Max(top, trunk.bounds.max.y);
            Height = Mathf.Max(.001f, top - Base);
            CrownTint = custom ? entry.crownTint : species switch
            {
                TreeSpecies.Birch => new Color32(133, 148, 88, 255),
                TreeSpecies.Spruce => new Color32(66, 87, 65, 255),
                TreeSpecies.StonePine => new Color32(88, 110, 68, 255),
                TreeSpecies.Olive => new Color32(127, 139, 94, 255),
                TreeSpecies.Acacia => new Color32(123, 134, 66, 255),
                TreeSpecies.Palm => new Color32(105, 127, 65, 255),
                _ => new Color32(106, 123, 67, 255)
            };
            TrunkTint = custom ? entry.trunkTint : species == TreeSpecies.Birch
                ? new Color32(179, 176, 151, 255) : new Color32(102, 85, 61, 255);
        }

        private static Mesh Readable(Mesh custom, TreeSpecies species, string part)
        {
            if (custom != null && custom.isReadable) return custom;
            if (custom != null)
                Debug.LogWarning($"The {species} {part} needs Read/Write Enabled. Using the starter model.");
            var path = "MapTrees/" + species + part;
            var asset = Resources.Load<GameObject>(path);
            var mesh = asset == null ? null : asset.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                Debug.LogWarning($"Missing readable starter tree mesh at Resources/{path}.");
                return null;
            }
            return mesh;
        }
    }
}
