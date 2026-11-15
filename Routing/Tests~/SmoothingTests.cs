using System;
using System.Linq;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="Smoothing.Smooth"/> on explicit paths: a shortcut is taken only if it costs no more
    /// than the edges it replaces, and the scan from an anchor stops at the first rejection. The
    /// expected values are literals from the oracle (~/scratch/c4-verify/oracle/cases_smooth.py t1,
    /// smooth_oracle.py, not committed).
    /// </summary>
    public class SmoothingTests
    {
        private const double Tol = 1e-9;

        private static CostParams P => Fixtures.CostParams;

        private static double V50 => P.V0Mps.P50;

        [Test]
        public void AZigzagOnFlatGroundBecomesOneSegment()
        {
            CostGrid g = Grids.Flat(10, 5);
            int[] path = Nodes(g, (0, 0), (2, 1), (3, 1), (5, 2), (7, 3), (8, 3), (9, 4));
            int[] w = Smoothing.Smooth(g, path, V50, P, 1.0);
            Assert.That(w, Is.EqualTo(Nodes(g, (0, 0), (9, 4))));

            // Oracle: 46.89932286569572 s straight against 48.20198807082126 s along the path.
            Assert.That(Cost.Trace(g, w, P).EtaS.P50, Is.EqualTo(46.89932286569572).Within(Tol));
            AssertNoWorse(g, w, path, 1.0);
        }

        [Test]
        public void AShortcutThatTouchesABlockedCornerIsRejected()
        {
            // Oracle: (0,0)→(1,2) and (0,0)→(2,3) pass; (0,0)→(3,3) goes through the corner of (2,1).
            CostGrid g = Grids.FromMap(Grids.Corner4x4);
            int[] path = Nodes(g, (0, 0), (1, 1), (1, 2), (2, 3), (3, 3));
            Assert.That(Cost.SegmentTimeS(g, g.Index(0, 0), g.Index(3, 3), V50, P, true), Is.EqualTo(double.PositiveInfinity));
            int[] w = Smoothing.Smooth(g, path, V50, P, 1.0);
            Assert.That(w, Is.EqualTo(Nodes(g, (0, 0), (2, 3), (3, 3))));
            Assert.That(Cost.Trace(g, w, P).Found, Is.True);
            AssertNoWorse(g, w, path, 1.0);
        }

        [Test]
        public void AShortcutOverAHillIsRejectedWhenItCostsMore()
        {
            // Oracle: over the 3 m hill 175.1889316493167 s against 81.09648189126746 s around it;
            // over the 0.3 m bump 71.28800682893835 s against 77.18977086840181 s.
            CostGrid hill = Grids.FromMap(Grids.Flat15x9, Grids.HillHeight);
            int[] path = Nodes(hill, (0, 4), (7, 8), (14, 4));
            Assert.That(Smoothing.Smooth(hill, path, V50, P, 1.0), Is.EqualTo(path));

            CostGrid bump = Grids.FromMap(Grids.Flat15x9, Grids.BumpHeight);
            int[] w = Smoothing.Smooth(bump, path, V50, P, 1.0);
            Assert.That(w, Is.EqualTo(Nodes(bump, (0, 4), (14, 4))));
            AssertNoWorse(bump, w, path, 1.0);
        }

        [Test]
        public void AShortcutThroughAmberIsRejectedWhenItCostsMore()
        {
            // Oracle: through the 3 × 3 amber block 81.06666666666663 s with the penalty against
            // 76.78340712665285 s around it; without the penalty, 66.66666666666664 s through it.
            CostGrid g = Grids.FromMap(Grids.AmberBlob15x9);
            int[] path = Nodes(g, (0, 4), (7, 8), (14, 4));
            int[] w = Smoothing.Smooth(g, path, V50, P, 1.0);
            Assert.That(w, Is.EqualTo(path));
            Assert.That(Cost.Trace(g, w, P).AmberM, Is.EqualTo(0));

            w = Smoothing.Smooth(g, path, V50, P, 0.0);
            Assert.That(w, Is.EqualTo(Nodes(g, (0, 4), (14, 4))));
            Assert.That(Cost.Trace(g, w, P).AmberM, Is.EqualTo(6.0).Within(Tol));
            AssertNoWorse(g, w, path, 0.0);
        }

        [Test]
        public void TheScanStopsAtTheFirstRejection()
        {
            // Oracle: from the anchor (2,3), the shortcut to (5,4) is blocked and stops the scan,
            // although (2,3)→(7,5) would pass (25.64364193873573 s ≤ 26.057790261902763 s).
            CostGrid g = Grids.FromMap(Grids.ScanStop8x6);
            int[] path = Nodes(g, (0, 0), (1, 1), (2, 3), (4, 4), (5, 4), (7, 5));
            int[] w = Smoothing.Smooth(g, path, V50, P, 1.0);
            Assert.That(w, Is.EqualTo(Nodes(g, (0, 0), (2, 3), (4, 4), (7, 5))));
            AssertNoWorse(g, w, path, 1.0);
        }

        [Test]
        public void ACollinearRunCountsAsNoWorse()
        {
            // Oracle: along one line the shortcut and its edges differ only by rounding (up to
            // ~1e-14 s), and a rejection there would anchor the route off its final line.
            CostGrid g = Grids.Flat(10, 5);
            int[] path = Nodes(g, (0, 0), (2, 1), (4, 2), (6, 3), (8, 4), (9, 4));
            Assert.That(Smoothing.Smooth(g, path, V50, P, 1.0), Is.EqualTo(Nodes(g, (0, 0), (9, 4))));
        }

        [Test]
        public void ArgumentsAndShortPaths()
        {
            CostGrid g = Grids.Flat(3, 3);
            Assert.Throws<ArgumentNullException>(() => Smoothing.Smooth(null, new[] { 0 }, V50, P, 1.0));
            Assert.Throws<ArgumentNullException>(() => Smoothing.Smooth(g, null, V50, P, 1.0));
            Assert.Throws<ArgumentNullException>(() => Smoothing.Smooth(g, new[] { 0 }, V50, null, 1.0));
            Assert.Throws<ArgumentException>(() => Smoothing.Smooth(g, new int[0], V50, P, 1.0));

            int[] one = { 4 };
            Assert.That(Smoothing.Smooth(g, one, V50, P, 1.0), Is.EqualTo(one).And.Not.SameAs(one));
            int[] two = { 0, 4 };
            Assert.That(Smoothing.Smooth(g, two, V50, P, 1.0), Is.EqualTo(two).And.Not.SameAs(two));
        }

        private static int[] Nodes(CostGrid g, params (int I, int J)[] cells) => cells.Select(c => g.Index(c.I, c.J)).ToArray();

        /// <summary>S3: the smoothed route never costs more than the path it came from.</summary>
        private static void AssertNoWorse(CostGrid g, int[] waypoints, int[] path, double amberMult)
        {
            Assert.That(Sum(g, waypoints, amberMult), Is.LessThanOrEqualTo(Sum(g, path, amberMult) * (1 + 1e-9)));
        }

        private static double Sum(CostGrid g, int[] nodes, double amberMult)
        {
            double s = 0;
            for (int k = 1; k < nodes.Length; k++)
            {
                s += Cost.SegmentCostS(g, nodes[k - 1], nodes[k], V50, P, amberMult, out _);
            }

            return s;
        }
    }
}
