using System.Collections.Generic;

namespace MartianMap.Edits
{
    /// <summary>Exact integer comparisons that a <c>long</c> product would overflow.</summary>
    internal static class Exact
    {
        /// <summary>
        /// The sign of <c>a·b − c·d</c>, exact for any <c>long</c>: the magnitudes are multiplied
        /// into 128 bits from 32-bit halves.
        /// </summary>
        internal static int CompareProducts(long a, long b, long c, long d)
        {
            int s1 = Sign(a) * Sign(b);
            int s2 = Sign(c) * Sign(d);
            if (s1 != s2)
            {
                return s1 > s2 ? 1 : -1;
            }

            if (s1 == 0)
            {
                return 0;
            }

            MultiplyMagnitudes(a, b, out ulong hi1, out ulong lo1);
            MultiplyMagnitudes(c, d, out ulong hi2, out ulong lo2);
            int m = hi1 != hi2 ? (hi1 > hi2 ? 1 : -1) : lo1 != lo2 ? (lo1 > lo2 ? 1 : -1) : 0;
            return s1 * m;
        }

        private static int Sign(long v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        /// <summary>|v| as an unsigned value; |long.MinValue| = 2⁶³ fits.</summary>
        private static ulong Magnitude(long v) => v < 0 ? (ulong)(-(v + 1)) + 1 : (ulong)v;

        /// <summary>|a|·|b| as a 128-bit unsigned number <c>hi·2⁶⁴ + lo</c>.</summary>
        private static void MultiplyMagnitudes(long a, long b, out ulong hi, out ulong lo)
        {
            ulong x = Magnitude(a), y = Magnitude(b);
            ulong x0 = x & 0xFFFFFFFFUL, x1 = x >> 32;
            ulong y0 = y & 0xFFFFFFFFUL, y1 = y >> 32;
            ulong p00 = x0 * y0, p01 = x0 * y1, p10 = x1 * y0, p11 = x1 * y1;
            ulong middle = (p00 >> 32) + (p01 & 0xFFFFFFFFUL) + (p10 & 0xFFFFFFFFUL);
            lo = (middle << 32) | (p00 & 0xFFFFFFFFUL);
            hi = p11 + (p01 >> 32) + (p10 >> 32) + (middle >> 32);
        }
    }

    /// <summary>Whether a point is inside a polygon, in fixed point and exact integers.</summary>
    internal static class Polygon
    {
        /// <summary>
        /// True if <c>(x, y)</c> lies on the ring's closed edges (vertices included) or strictly inside
        /// it by the even-odd rule. The ring's vertices are any <c>int</c>; <c>|x|</c> and <c>|y|</c>
        /// stay below 2⁶², so every difference fits in a <c>long</c> (a tile sample is below 2⁴⁰).
        /// </summary>
        internal static bool Contains(IReadOnlyList<FixedPoint> ring, long x, long y)
        {
            int n = ring.Count;
            for (int k = 0; k < n; k++)
            {
                FixedPoint p = ring[k], q = ring[(k + 1) % n];
                if (OnSegment(p.E, p.N, q.E, q.N, x, y))
                {
                    return true;
                }
            }

            bool inside = false;
            for (int k = 0; k < n; k++)
            {
                FixedPoint p = ring[k], q = ring[(k + 1) % n];
                long x1 = p.E, y1 = p.N, x2 = q.E, y2 = q.N;
                if ((y1 > y) != (y2 > y))
                {
                    // The edge crosses the line at height y to the right of x:
                    // (x − x1)(y2 − y1) < (y − y1)(x2 − x1) when y2 > y1, reversed when y2 < y1.
                    int cmp = Exact.CompareProducts(x - x1, y2 - y1, y - y1, x2 - x1);
                    if (y2 > y1 ? cmp < 0 : cmp > 0)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private static bool OnSegment(long x1, long y1, long x2, long y2, long x, long y)
        {
            if ((x < x1 && x < x2) || (x > x1 && x > x2) || (y < y1 && y < y2) || (y > y1 && y > y2))
            {
                return false;
            }

            return Exact.CompareProducts(x2 - x1, y - y1, y2 - y1, x - x1) == 0;
        }
    }
}
