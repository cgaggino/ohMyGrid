using System;
using System.Collections.Generic;

namespace OhMyGrid
{
    public readonly struct GridPoint
    {
        public readonly float X;
        public readonly float Z;
        public GridPoint(float x, float z) { X = x; Z = z; }
        public override string ToString() => $"({X:0.##}, {Z:0.##})";
    }

    public static class GridGenerator
    {
        /// <summary>Concentric rings between innerRadius and outerRadius, ~spacing apart.</summary>
        public static IEnumerable<GridPoint> Donut(
            float centerX, float centerZ,
            float innerRadius, float outerRadius,
            float spacing)
        {
            if (spacing <= 0f) throw new ArgumentOutOfRangeException(nameof(spacing));
            if (innerRadius < 0f) throw new ArgumentOutOfRangeException(nameof(innerRadius));
            if (outerRadius < innerRadius) throw new ArgumentOutOfRangeException(nameof(outerRadius));

            const float tolerance = 1e-4f;
            for (float r = innerRadius; r <= outerRadius + tolerance; r += spacing)
            {
                if (r <= tolerance)
                {
                    yield return new GridPoint(centerX, centerZ);
                    continue;
                }

                int n = Math.Max(1, (int)Math.Round(2.0 * Math.PI * r / spacing));
                for (int i = 0; i < n; i++)
                {
                    double theta = 2.0 * Math.PI * i / n;
                    float x = centerX + (float)(r * Math.Cos(theta));
                    float z = centerZ + (float)(r * Math.Sin(theta));
                    yield return new GridPoint(x, z);
                }
            }
        }
    }
}
