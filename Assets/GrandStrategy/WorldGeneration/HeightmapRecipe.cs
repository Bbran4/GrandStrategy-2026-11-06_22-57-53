using System;
using System.Collections.Generic;

namespace GrandStrategy.WorldGeneration
{
    public enum HeightmapOperation { Hill, Range, Trough, Pit, Strait, MultiplyLand, Detail, Smooth, Erode }

    [Serializable]
    public sealed class HeightmapStep
    {
        public bool Enabled = true;
        public HeightmapOperation Operation;
        public int CountMin = 1;
        public int CountMax = 1;
        public float StrengthMin = 0.2f;
        public float StrengthMax = 0.3f;
        public float XMin = 0.15f;
        public float XMax = 0.85f;
        public float YMin = 0.2f;
        public float YMax = 0.8f;
        public float Radius = 0.12f;
        public float Length = 0.35f;
        public bool Horizontal;

        public HeightmapStep Copy() => (HeightmapStep)MemberwiseClone();
    }

    [Serializable]
    public sealed class HeightmapRecipe
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public int Width = 1024;
        public int Height = 512;
        public int Seed = 271828;
        public float SeaLevel = 0.32f;
        public float CoastWarp = 0.065f;
        public float EdgeFalloff = 0.06f;
        public List<HeightmapStep> Steps = new List<HeightmapStep>();

        public static HeightmapRecipe OldWorld()
        {
            // Original raster recipe inspired by the ordered operations in Azgaar's editor.
            // Not a port of its Voronoi algorithm or a copy of its template script.
            return new HeightmapRecipe
            {
                Steps = new List<HeightmapStep>
                {
                    Place(Step(HeightmapOperation.Hill, 3, 4, .40f, .58f, .26f), .25f, .42f, .3f, .7f),
                    Place(Step(HeightmapOperation.Hill, 3, 4, .40f, .58f, .27f), .60f, .77f, .3f, .7f),
                    Step(HeightmapOperation.Hill, 5, 8, .12f, .22f, .13f),
                    Step(HeightmapOperation.MultiplyLand, 1, 1, .38f, .38f),
                    Place(Step(HeightmapOperation.Range, 2, 3, .18f, .32f, .035f, .32f), .23f, .43f, .3f, .7f),
                    Place(Step(HeightmapOperation.Range, 2, 3, .18f, .32f, .035f, .32f), .60f, .8f, .3f, .7f),
                    Place(Step(HeightmapOperation.Trough, 3, 5, .12f, .23f, .055f, .28f), .3f, .7f, .35f, .65f),
                    Place(Step(HeightmapOperation.Strait, 1, 1, 1f, 1f, .018f), .47f, .53f, .4f, .6f),
                    Step(HeightmapOperation.Pit, 3, 5, .12f, .24f, .085f),
                    Step(HeightmapOperation.Detail, 1, 1, .035f, .035f),
                    Step(HeightmapOperation.Smooth, 2, 2, .45f, .45f),
                    Step(HeightmapOperation.Erode, 8, 8, .35f, .35f)
                }
            };
        }

        private static HeightmapStep Place(HeightmapStep step, float west, float east, float south, float north)
        { step.XMin = west; step.XMax = east; step.YMin = south; step.YMax = north; return step; }

        public static HeightmapStep Step(HeightmapOperation operation, int min = 1, int max = 1,
            float strengthMin = .2f, float strengthMax = .3f, float radius = .12f, float length = .35f)
        {
            return new HeightmapStep { Operation = operation, CountMin = min, CountMax = max,
                StrengthMin = strengthMin, StrengthMax = strengthMax, Radius = radius, Length = length };
        }

        public HeightmapRecipe Copy()
        {
            var copy = (HeightmapRecipe)MemberwiseClone();
            copy.Steps = new List<HeightmapStep>(Steps.Count);
            foreach (var step in Steps) copy.Steps.Add(step.Copy());
            return copy;
        }

        public void Validate()
        {
            if (Version != CurrentVersion) throw new ArgumentException("Unsupported heightmap recipe version.");
            if (Width < 32 || Height < 32 || Width > 3072 || Height > 2048 || (long)Width * Height > 6291456)
                throw new ArgumentException("Map dimensions must be 32–3072 by 32–2048, at most 6,291,456 samples.");
            Check(SeaLevel, .05f, .85f, "Sea level");
            Check(CoastWarp, 0, .15f, "Coast warp");
            Check(EdgeFalloff, .01f, .2f, "Edge falloff");
            if (Steps == null || Steps.Count == 0 || Steps.Count > 48)
                throw new ArgumentException("A recipe needs 1–48 steps.");
            foreach (var step in Steps)
            {
                if (step == null || !Enum.IsDefined(typeof(HeightmapOperation), step.Operation))
                    throw new ArgumentException("Invalid recipe operation.");
                if (step.CountMin < 1 || step.CountMax < step.CountMin || step.CountMax > 32)
                    throw new ArgumentException("Step counts must be ordered and between 1 and 32.");
                Check(step.StrengthMin, 0, 1, "Minimum strength");
                Check(step.StrengthMax, step.StrengthMin, 1, "Maximum strength");
                Check(step.XMin, 0, 1, "Minimum X"); Check(step.XMax, step.XMin, 1, "Maximum X");
                Check(step.YMin, 0, 1, "Minimum Y"); Check(step.YMax, step.YMin, 1, "Maximum Y");
                Check(step.Radius, .005f, .5f, "Radius"); Check(step.Length, .02f, 1, "Length");
            }
        }

        private static void Check(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentException($"{name} must be between {min} and {max}.");
        }
    }
}
