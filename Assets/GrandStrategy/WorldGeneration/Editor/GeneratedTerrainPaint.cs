// Stores the generated paint separately from the TerrainData used as the editable composite.
using System;
using UnityEngine;

namespace GrandStrategy.WorldGeneration.Editor
{
    public sealed class GeneratedTerrainPaint : ScriptableObject
    {
        private const int LayerCount = 4;
        // Alphamaps are stored in 8-bit textures. Allow for quantization when detecting native brush edits.
        private const float PaintTolerance = 2f / 255f;
        [SerializeField] private int resolution;
        [SerializeField] private TerrainLayer[] layers;
        [SerializeField] private byte[] generated;
        [SerializeField] private byte[] manual;

        public bool Matches(TerrainData terrain)
        {
            var current = terrain.terrainLayers;
            if (resolution != terrain.alphamapResolution || current.Length != LayerCount
                || layers?.Length != LayerCount || manual?.Length != resolution * resolution
                || generated?.Length != resolution * resolution * LayerCount)
                return false;
            for (var layer = 0; layer < LayerCount; layer++)
                if (layers[layer] != current[layer]) return false;
            return true;
        }

        public void Initialize(TerrainData terrain, float[,,] weights)
        {
            resolution = terrain.alphamapResolution;
            layers = (TerrainLayer[])terrain.terrainLayers.Clone();
            generated = Encode(weights);
            manual = new byte[resolution * resolution];
        }

        public void Merge(TerrainData terrain, float[,,] next)
        {
            var nextGenerated = Encode(next);
            var nextManual = (byte[])manual.Clone();
            var current = terrain.GetAlphamaps(0, 0, resolution, resolution);
            for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var cell = y * resolution + x;
                    if (nextManual[cell] == 0)
                        for (var layer = 0; layer < LayerCount; layer++)
                            if (Mathf.Abs(current[y, x, layer] - Weight(cell, layer)) > PaintTolerance)
                            {
                                nextManual[cell] = 1;
                                break;
                            }
                    if (nextManual[cell] != 0)
                        for (var layer = 0; layer < LayerCount; layer++)
                            next[y, x, layer] = current[y, x, layer];
                }
            terrain.SetAlphamaps(0, 0, next);
            generated = nextGenerated;
            manual = nextManual;
        }

        public void RestoreCell(float[,,] weights, RectInt area, int x, int y)
        {
            var cell = (area.y + y) * resolution + area.x + x;
            manual[cell] = 0;
            for (var layer = 0; layer < LayerCount; layer++)
                weights[y, x, layer] = Weight(cell, layer);
        }

        public void MarkManual(RectInt area, int x, int y, float change)
        {
            if (change > PaintTolerance)
                manual[(area.y + y) * resolution + area.x + x] = 1;
        }

        private float Weight(int cell, int layer)
        {
            var index = cell * LayerCount;
            var sum = generated[index] + generated[index + 1] + generated[index + 2] + generated[index + 3];
            return sum > 0 ? (float)generated[index + layer] / sum : layer == 0 ? 1f : 0f;
        }

        private static byte[] Encode(float[,,] weights)
        {
            var width = weights.GetLength(1);
            var height = weights.GetLength(0);
            var bytes = new byte[width * height * LayerCount];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    for (var layer = 0; layer < LayerCount; layer++)
                        bytes[(y * width + x) * LayerCount + layer] =
                            (byte)Mathf.RoundToInt(Mathf.Clamp01(weights[y, x, layer]) * 255f);
            return bytes;
        }
    }
}
