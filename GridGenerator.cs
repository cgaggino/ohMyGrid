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

        /// <summary>
        /// Annular sector ("wedge"): the points of the matching <see cref="Donut"/> whose direction
        /// from the center falls in [heading - sweep/2, heading + sweep/2). Angles are compass
        /// degrees (0 = +Z/north, clockwise). Because every wedge is cut from the same donut
        /// lattice with a half-open interval, wedges placed side by side around one center tile
        /// the donut exactly: no shared edge points, no gaps. The center point (inner radius 0)
        /// goes only to the wedge that contains north, so it is planted once per circle.
        /// </summary>
        public static IEnumerable<GridPoint> Wedge(
            float centerX, float centerZ,
            float innerRadius, float outerRadius,
            float spacing,
            float headingDeg, float sweepDeg)
        {
            if (sweepDeg <= 0f) throw new ArgumentOutOfRangeException(nameof(sweepDeg));

            const double angleTolerance = 1e-3;
            const float tolerance = 1e-4f;
            var fullCircle = sweepDeg >= 360f - tolerance;
            var start = headingDeg - sweepDeg / 2.0;

            foreach (var p in Donut(centerX, centerZ, innerRadius, outerRadius, spacing))
            {
                if (fullCircle)
                {
                    yield return p;
                    continue;
                }

                var dx = p.X - centerX;
                var dz = p.Z - centerZ;
                var compass = dx * dx + dz * dz <= tolerance * tolerance
                    ? 0.0
                    : Math.Atan2(dx, dz) * 180.0 / Math.PI;
                var rel = ((compass - start) % 360.0 + 360.0) % 360.0;
                // Snap float noise right below the start edge back onto it, so a lattice
                // point sitting exactly on a boundary belongs to exactly one wedge.
                if (rel > 360.0 - angleTolerance) rel = 0.0;
                if (rel < sweepDeg - angleTolerance) yield return p;
            }
        }
    }
}
