using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="Router.PlanRoute"/>: A* 16-connected, then smoothed. The expected values are
    /// literals from the oracle (~/scratch/c4-verify/oracle/cases.py and cases_smooth.py t2, not
    /// committed): an independent Dijkstra over the segment rule, in exact fractions for the
    /// geometry. The v1 literals (wall gap, maze, amber bands) are unchanged by smoothing: their
    /// shortcuts are collinear with the A* edges or rejected (oracle).
    /// </summary>
    public class PlanRouteTests
    {
        private const double Tol = 1e-9;

        // A* keys are float: costs agree with the oracle's double Dijkstra to ~1e-7 relative.
        private const double Rel = 1e-4;

        private static CostParams P => Fixtures.CostParams;

        [Test]
        public void AStraightRouteOnFlatGroundFollowsTheRow()
        {
            // 16 m at 0.70 · 0.6 m/s.
            CostGrid g = Grids.Flat(9, 1);
            Route r = Router.PlanRoute(g, new EnuPoint(0, 0), new EnuPoint(16, 0), P);
            Assert.That(r.Found, Is.True);
            Assert.That(r.Cells, Is.EqualTo(Enumerable.Range(0, 9)));
            Assert.That(r.EtaS.P50, Is.EqualTo(38.095238095238095).Within(Tol));
            Assert.That(r.LengthM, Is.EqualTo(16).Within(Tol));
        }

        [Test]
        public void AWallWithAGapIsCrossedThroughTheGap()
        {
            // Oracle: the unique optimum is (0,3) (2,2) (4,1) (6,2) (8,3), four knight moves.
            CostGrid g = Grids.FromMap(Grids.WallGap9x7);
            Route r = Router.PlanRoute(g, new EnuPoint(0, 6), new EnuPoint(16, 6), P);
            Assert.That(r.Found, Is.True);
            Assert.That(r.Cells, Does.Contain(g.Index(4, 1)));
            Assert.That(r.EtaS.P50, Is.EqualTo(42.591770999996).Within(Tol));
        }

        [Test]
        public void AWallWithoutAGapGivesNoRoute()
        {
            CostGrid g = Grids.FromMap(Grids.Wall9x7);
            Assert.That(Router.PlanRoute(g, new EnuPoint(0, 6), new EnuPoint(16, 6), P), Is.SameAs(Route.NotFound));
        }

        [Test]
        public void ABlockedStartOrGoalGivesNoRoute()
        {
            CostGrid g = Grids.FromMap(Grids.Nodata8x6);
            Assert.That(Router.PlanRoute(g, new EnuPoint(8, 6), new EnuPoint(14, 0), P), Is.SameAs(Route.NotFound));
            Assert.That(Router.PlanRoute(g, new EnuPoint(0, 0), new EnuPoint(8, 6), P), Is.SameAs(Route.NotFound));
        }

        [Test]
        public void TheMazeRouteHasTheOptimalCost()
        {
            // Oracle: corner (1, 9) to corner (9, 1), heights 0.1·i + 0.05·j; a unique optimum of 21 nodes.
            CostGrid g = Grids.FromMap(Grids.Maze11x11, Grids.MazeHeight);
            int[] nodes = Search.AStar(g, g.Index(1, 9), g.Index(9, 1), P.V0Mps.P50, P, 1.0, out double cost);
            Assert.That(nodes, Is.Not.Null);
            Assert.That(nodes.First(), Is.EqualTo(g.Index(1, 9)));
            Assert.That(nodes.Last(), Is.EqualTo(g.Index(9, 1)));
            Assert.That(cost, Is.EqualTo(105.9282678070628).Within(Rel).Percent);
            Route r = Router.PlanRoute(g, new EnuPoint(2, 18), new EnuPoint(18, 2), P);
            Assert.That(r.EtaS.P50, Is.EqualTo(105.9282678070628).Within(Rel).Percent);
        }

        [Test]
        public void AmberIsAvoidedWhenTheDetourIsCheaper()
        {
            // Oracle: through the pass two nodes north, 42.591770999996 s, against the straight row's
            // 38.095238095238095 s plus 2 m of amber · 2.4 s/m = 42.89523809523809 s.
            CostGrid near = Grids.FromMap(Grids.AmberBand9x5);
            Route r = Router.PlanRoute(near, new EnuPoint(0, 2), new EnuPoint(16, 2), P);
            Assert.That(r.AmberM, Is.EqualTo(0));
            Assert.That(r.EtaS.P50, Is.EqualTo(42.591770999996).Within(Tol));

            // Oracle: with the pass ten nodes north the detour costs more; the straight row wins.
            CostGrid far = Grids.FromMap(Grids.AmberBandFar9x21);
            r = Router.PlanRoute(far, new EnuPoint(0, 2), new EnuPoint(16, 2), P);
            Assert.That(r.AmberM, Is.EqualTo(2.0).Within(Tol));
            Assert.That(r.EtaS.P50, Is.EqualTo(38.095238095238095).Within(Tol));
        }

        [TestCase(0, 0, 11, 8, 101.05200666714978)]
        [TestCase(1, 1, 10, 4, double.PositiveInfinity)]
        [TestCase(3, 7, 9, 0, 69.31721979250177)]
        [TestCase(0, 4, 11, 4, 127.43452609529103)]
        [TestCase(2, 2, 6, 6, 41.23040355109812)]
        public void AStarMatchesTheOracleOnARandomGrid(int si, int sj, int gi, int gj, double expected)
        {
            // Oracle: Random12x9 with heights 0.2 · ((7i + 3j) mod 5); (10, 4) is blocked.
            CostGrid g = Grids.FromMap(Grids.Random12x9, Grids.RandomHeight);
            int[] nodes = Search.AStar(g, g.Index(si, sj), g.Index(gi, gj), P.V0Mps.P50, P, 1.0, out double cost);
            if (double.IsPositiveInfinity(expected))
            {
                Assert.That(nodes, Is.Null);
                Assert.That(cost, Is.EqualTo(double.PositiveInfinity));
            }
            else
            {
                Assert.That(cost, Is.EqualTo(expected).Within(Rel).Percent);
            }
        }

        [Test]
        public void ARouteAtAnAngleOnFlatGroundIsTheStraightSegment()
        {
            // Oracle (cases_smooth.py t2): the single segment (0,0)→(29,11), 147.69583256448405 s,
            // against 150.46070358332233 s along an optimal 16-connected path.
            CostGrid g = Grids.Flat(30, 12);
            Route r = Router.PlanRoute(g, new EnuPoint(0, 0), new EnuPoint(58, 22), P);
            Assert.That(r.Cells, Is.EqualTo(new[]
            {
                0, 1, 31, 32, 33, 34, 64, 65, 66, 67, 97, 98, 99, 129, 130, 131, 132, 162, 163, 164,
                195, 196, 197, 227, 228, 229, 230, 260, 261, 262, 292, 293, 294, 295, 325, 326, 327, 328, 358, 359,
            }));
            Assert.That(r.EtaS.P50, Is.EqualTo(147.69583256448405).Within(Tol));
            Assert.That(r.EtaS.P90, Is.EqualTo(184.61979070560486).Within(Tol));
        }

        [TestCase(0, 0, 11, 8, 101.05200666714978)]
        [TestCase(3, 7, 9, 0, 62.08150933561341)]
        [TestCase(0, 4, 11, 4, 123.81667056845235)]
        [TestCase(2, 2, 6, 6, 37.34127089449969)]
        public void TheSmoothedRouteIsNeverWorseThanAStar(int si, int sj, int gi, int gj, double expectedEtaP50)
        {
            // Oracle: Random12x9, each pair with a unique optimum (optimal_paths = 1), smoothed with
            // S2-S3; no pair crosses amber, so the cost is eta_s.p50.
            CostGrid g = Grids.FromMap(Grids.Random12x9, Grids.RandomHeight);
            Route r = Router.PlanRoute(g, new EnuPoint(2 * si, 2 * sj), new EnuPoint(2 * gi, 2 * gj), P);
            Search.AStar(g, g.Index(si, sj), g.Index(gi, gj), P.V0Mps.P50, P, 1.0, out double aStarCost);
            Assert.That(r.Found, Is.True);
            Assert.That(r.EtaS.P50 + (r.AmberM * P.AmberPenaltySPerM), Is.LessThanOrEqualTo(aStarCost * (1 + 1e-6)));
            Assert.That(r.EtaS.P50, Is.EqualTo(expectedEtaP50).Within(Tol));
        }

        [Test]
        public void TheSameNodeIsAFoundRouteThatStaysThere()
        {
            CostGrid g = Grids.FromMap(Grids.WallGap9x7);
            Route r = Router.PlanRoute(g, new EnuPoint(4.2, 6), new EnuPoint(3.9, 5.5), P);
            Assert.That(r.Found, Is.True);
            Assert.That(r.Cells, Is.EqualTo(new[] { g.Index(2, 3) }));
            Assert.That(r.PolylineEnuM.Count, Is.EqualTo(1));
            Assert.That((r.PolylineEnuM[0].E, r.PolylineEnuM[0].N), Is.EqualTo((4.0, 6.0)));
            Assert.That((r.LengthM, r.EtaS.P50), Is.EqualTo((0.0, 0.0)));

            int[] nodes = Search.AStar(g, g.Index(2, 3), g.Index(2, 3), P.V0Mps.P50, P, 1.0, out double cost);
            Assert.That(nodes, Is.EqualTo(new[] { g.Index(2, 3) }));
            Assert.That(cost, Is.EqualTo(0));
        }

        [Test]
        public void APointMidwayGoesToTheEastOrNorthNode()
        {
            CostGrid g = Grids.Flat(9, 7);
            Assert.That(Router.NearestNode(g, new EnuPoint(1, 3)), Is.EqualTo(g.Index(1, 2)));
            Assert.That(Router.NearestNode(g, new EnuPoint(0.999, 2.999)), Is.EqualTo(g.Index(0, 1)));
        }

        [Test]
        public void APointOnTheFarEdgeMapsToTheLastNode()
        {
            CostGrid g = Grids.Flat(9, 7);
            Assert.That(Router.NearestNode(g, new EnuPoint(16, 12)), Is.EqualTo(g.Index(8, 6)));
            var shifted = new CostGrid(9, 7, 2f, new EnuPoint(-100.5, 3.25), new float[63], new byte[63], new byte[63], new byte[63]);
            Assert.That(Router.NearestNode(shifted, new EnuPoint(-100.5 + 16, 3.25 + 12)), Is.EqualTo(shifted.Index(8, 6)));
        }

        [Test]
        public void ARouteOnAOneNodeWideGrid()
        {
            Route column = Router.PlanRoute(Grids.Flat(1, 5), new EnuPoint(0, 0), new EnuPoint(0, 8), P);
            Assert.That(column.Found, Is.True);
            Assert.That(column.Cells, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Route row = Router.PlanRoute(Grids.Flat(5, 1), new EnuPoint(8, 0), new EnuPoint(0, 0), P);
            Assert.That(row.Found, Is.True);
            Assert.That(row.Cells, Is.EqualTo(new[] { 4, 3, 2, 1, 0 }));
            Route one = Router.PlanRoute(Grids.Flat(1, 1), new EnuPoint(0, 0), new EnuPoint(0, 0), P);
            Assert.That(one.Found, Is.True);
            Assert.That(one.Cells, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void NullParamsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => Router.PlanRoute(Grids.Flat(3, 3), new EnuPoint(0, 0), new EnuPoint(4, 4), null));
        }

        [Test]
        public void ASmallGridAfterALargeOneGivesTheSameRoute()
        {
            CostGrid small = Grids.FromMap(Grids.WallGap9x7);
            Route fresh = null;
            var thread = new Thread(() => fresh = Router.PlanRoute(small, new EnuPoint(0, 6), new EnuPoint(16, 6), P));
            thread.Start();
            thread.Join();

            Router.PlanRoute(Grids.Flat(300, 300), new EnuPoint(0, 0), new EnuPoint(598, 598), P);
            Route after = Router.PlanRoute(small, new EnuPoint(0, 6), new EnuPoint(16, 6), P);
            Assert.That(after.Cells, Is.EqualTo(fresh.Cells));
            Assert.That(after.EtaS.P50, Is.EqualTo(fresh.EtaS.P50));
            Assert.That(after.EtaS.P90, Is.EqualTo(fresh.EtaS.P90));
        }

        [Test]
        public void ARouteQueryAllocatesNoLargeArraysAfterWarmUp()
        {
            CostGrid g = Grids.Flat(400, 400);
            for (int k = 0; k < 2; k++)
            {
                Router.PlanRoute(g, new EnuPoint(100 + k, 400), new EnuPoint(350 + k, 400), P);
            }

            // A collection now, so the earlier tests' garbage does not count; then only the route's
            // own small lists (~20 KB a query) are allocated, never an array per node (640 KB here).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int before = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 10; k++)
            {
                double a = k * Math.PI / 5;
                Route r = Router.PlanRoute(g, new EnuPoint(400, 400), new EnuPoint(400 + 250 * Math.Cos(a), 400 + 250 * Math.Sin(a)), P);
                Assert.That(r.Found, Is.True);
            }

            Assert.That((GC.GetAllocatedBytesForCurrentThread() - bytes) / 10, Is.LessThan(200_000));
            Assert.That(GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2), Is.EqualTo(before));
        }
    }
}
