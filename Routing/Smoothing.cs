using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The smoothing of an A* path (docs/guide/chapters/tablet.md, "Rutas (D7)"): string-pulling
    /// that takes a shortcut only if it does not worsen the integrated cost, the one the segment
    /// rule of <see cref="Cost"/> gives the whole shortcut. It computes no cost of its own.
    /// </summary>
    internal static class Smoothing
    {
        /// <summary>
        /// How much a shortcut may exceed the edges it replaces and still count as no worse: a tie
        /// up to rounding. A shortcut along the same line as its edges is cut at the same points and
        /// costs the same, but the sums differ in the last bits (up to ~1e-14 s).
        /// </summary>
        private const double TieRel = 1e-9;

        /// <summary>
        /// The waypoints of <paramref name="path"/>: a subset of its nodes, in its order, with the
        /// first and the last always kept. From an anchor <c>a</c> it tries the shortcuts to
        /// <c>a + 2, a + 3, …</c> while each is accepted, and at the first rejection the last accepted
        /// node (or <c>a + 1</c>) becomes the next waypoint and anchor. A shortcut is accepted if its
        /// <see cref="Cost.SegmentCostS"/> (the time at <paramref name="v0Mps"/> plus the amber penalty
        /// times <paramref name="amberMult"/>) is at most the sum of the path's edges it replaces,
        /// within <see cref="TieRel"/>; a blocked one is +∞ and never accepted. So the smoothed path
        /// never costs more than <paramref name="path"/>. A path of one or two nodes is returned as a
        /// copy.
        /// </summary>
        internal static int[] Smooth(CostGrid g, int[] path, double v0Mps, CostParams p, double amberMult)
        {
            if (g == null)
            {
                throw new ArgumentNullException(nameof(g));
            }

            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (path.Length == 0)
            {
                throw new ArgumentException("A path has at least one node.", nameof(path));
            }

            int n = path.Length;
            if (n <= 2)
            {
                return (int[])path.Clone();
            }

            var waypoints = new int[n];
            int count = 0;
            waypoints[count++] = path[0];
            int a = 0;
            while (a < n - 1)
            {
                int best = a + 1;
                double run = Cost.SegmentCostS(g, path[a], path[a + 1], v0Mps, p, amberMult, out _);
                for (int k = a + 2; k < n; k++)
                {
                    run += Cost.SegmentCostS(g, path[k - 1], path[k], v0Mps, p, amberMult, out _);
                    double shortcut = Cost.SegmentCostS(g, path[a], path[k], v0Mps, p, amberMult, out _);
                    if (!(shortcut <= run * (1 + TieRel)))
                    {
                        break;
                    }

                    best = k;
                }

                waypoints[count++] = path[best];
                a = best;
            }

            Array.Resize(ref waypoints, count);
            return waypoints;
        }
    }
}
