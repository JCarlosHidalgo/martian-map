using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// The core of the reverse Dijkstra field (<see cref="Search.Field"/>): the P90 time back to
    /// <c>home</c> without penalty, along the return chosen with the amber penalty. The expected
    /// values are literals from the oracle (~/scratch/c4-verify/oracle/cases.py, dijkstra with
    /// reverse=True, not committed).
    /// </summary>
    public class FieldCoreTests
    {
        // The field is float32.
        private const double Rel = 1e-4;

        private static CostParams P => Fixtures.CostParams;

        [Test]
        public void TheFieldOnFlatGroundIsTheStraightTime()
        {
            double[] expected =
            {
                16.835875742536846, 13.309928437498748, 11.904761904761903, 13.309928437498748, 16.835875742536846,
                13.309928437498748, 8.417937871268423, 5.952380952380952, 8.417937871268423, 13.309928437498748,
                11.904761904761903, 5.952380952380952, 0.0, 5.952380952380952, 11.904761904761903,
                13.309928437498748, 8.417937871268423, 5.952380952380952, 8.417937871268423, 13.309928437498748,
                16.835875742536846, 13.309928437498748, 11.904761904761903, 13.309928437498748, 16.835875742536846,
            };
            CostGrid g = Grids.Flat(5, 5);
            float[] t = Field(g, g.Index(2, 2));
            for (int k = 0; k < expected.Length; k++)
            {
                Assert.That(t[k], Is.EqualTo(expected[k]).Within(Rel).Percent, $"node {k}");
            }
        }

        [Test]
        public void TheFieldGoesThroughTheGap()
        {
            CostGrid gap = Grids.FromMap(Grids.WallGap9x7);
            Assert.That(Field(gap, gap.Index(0, 3))[gap.Index(8, 3)], Is.EqualTo(53.23971374999499).Within(Rel).Percent);

            CostGrid wall = Grids.FromMap(Grids.Wall9x7);
            float[] t = Field(wall, wall.Index(0, 3));
            Assert.That(t[wall.Index(8, 3)], Is.EqualTo(float.PositiveInfinity));
            Assert.That(t[wall.Index(4, 3)], Is.EqualTo(float.PositiveInfinity));
            Assert.That(t[wall.Index(3, 3)], Is.EqualTo(17.857142857142854).Within(Rel).Percent);
        }

        [Test]
        public void TheFieldMeasuresTheWalkBackNotTheWalkOut()
        {
            // Oracle: a ramp h = 0.5·i with c = 0.05; going back from node 6 walks down
            // (|−0.25 + 0.05| = 0.2), going out from home would walk up (90.54645914640997 s).
            var p = new CostParams(1, new Percentiles(0.94, 0.70, 0.56), new SensitivityRange(3.0, 2.5, 3.5), 0.05, 1.3, 15, 61, 36, 192, 96, 0.6, 0.5, 2.4, 4, 10);
            var g = new CostGrid(7, 1, 2f, new EnuPoint(0, 0), Enumerable.Range(0, 7).Select(i => 0.5f * i).ToArray(), new byte[7], new byte[7], new byte[7]);
            var t = new float[7];
            Search.Field(g, 0, p.V0Mps.P90, p, t);
            Assert.That(t[6], Is.EqualTo(67.07846675387331).Within(Rel).Percent);
            Assert.That(t[3], Is.EqualTo(33.53923337693666).Within(Rel).Percent);
        }

        [Test]
        public void TheFieldChoosesWithThePenaltyAndStoresTheTime()
        {
            // Oracle: from (8, 2) to home (0, 1) at P90, the fastest return crosses 2 m of amber
            // (49.02421415178445 s, + 4.8 s of penalty); the field takes the pass at (4, 3) and
            // stores that detour's time, without penalty.
            CostGrid g = Grids.FromMap(Grids.AmberBand9x5);
            Assert.That(Field(g, g.Index(0, 1))[g.Index(8, 2)], Is.EqualTo(51.83454721725814).Within(Rel).Percent);

            // Oracle: with the pass ten nodes north, the return from (8, 1) crosses the band; the
            // field stores its time, 16 m at 0.56 · 0.6 m/s, not its cost with the 4.8 s of penalty.
            CostGrid far = Grids.FromMap(Grids.AmberBandFar9x21);
            Assert.That(Field(far, far.Index(0, 1))[far.Index(8, 1)], Is.EqualTo(47.619047619047606).Within(Rel).Percent);
        }

        [Test]
        public void AnAllBlockedGridHasNoReturnButHome()
        {
            string[] rows = { "####", "####", "#.##", "####" };
            CostGrid g = Grids.FromMap(rows);
            int home = g.Index(1, 1);
            float[] t = Field(g, home);
            for (int k = 0; k < t.Length; k++)
            {
                Assert.That(t[k], Is.EqualTo(k == home ? 0f : float.PositiveInfinity), $"node {k}");
            }

            t = Field(g, g.Index(0, 0));
            Assert.That(t, Has.All.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void TheFieldNeedsAnArrayOfTheGridsSize()
        {
            CostGrid g = Grids.Flat(3, 3);
            Assert.Throws<ArgumentException>(() => Search.Field(g, 0, P.V0Mps.P90, P, new float[8]));
            Assert.Throws<ArgumentNullException>(() => Search.Field(g, 0, P.V0Mps.P90, P, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Search.Field(g, 9, P.V0Mps.P90, P, new float[9]));
        }

        [Test]
        public void FieldAndRoutesInParallelMatchTheSerialRun()
        {
            CostGrid g = Grids.Pattern200();
            int home = g.Index(0, 1);
            var routes = new (EnuPoint A, EnuPoint B)[20];
            for (int k = 0; k < routes.Length; k++)
            {
                routes[k] = (new EnuPoint(2 * ((k * 37) % 200), 2 * ((k * 11) % 200)), new EnuPoint(2 * ((k * 53 + 90) % 200), 2 * ((k * 29 + 50) % 200)));
            }

            float[] serialField = Field(g, home);
            Route[] serialRoutes = routes.Select(r => Router.PlanRoute(g, r.A, r.B, P)).ToArray();

            var parallelField = new float[g.Width * g.Height];
            Task field = Task.Run(() => Search.Field(g, home, P.V0Mps.P90, P, parallelField));
            Task<Route[]> plans = Task.Run(() => routes.Select(r => Router.PlanRoute(g, r.A, r.B, P)).ToArray());
            Task.WaitAll(field, plans);

            Assert.That(parallelField, Is.EqualTo(serialField));
            Assert.That(serialField.Count(float.IsFinite), Is.GreaterThan(g.Width * g.Height / 2));
            Assert.That(serialRoutes.Count(r => r.Found), Is.GreaterThanOrEqualTo(6));
            for (int k = 0; k < routes.Length; k++)
            {
                Assert.That(plans.Result[k].Cells, Is.EqualTo(serialRoutes[k].Cells), $"route {k}");
                Assert.That(plans.Result[k].EtaS.P50, Is.EqualTo(serialRoutes[k].EtaS.P50), $"route {k}");
            }
        }

        [Test]
        public void TheFieldReusesTheWorkspaceWithoutGc()
        {
            // 200²: an array per node would be 160 KB, above the bound.
            CostGrid g = Grids.Flat(200, 200);
            var t = new float[g.Width * g.Height];
            Search.Field(g, g.Index(100, 100), P.V0Mps.P90, P, t);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int before = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            Search.Field(g, g.Index(0, 199), P.V0Mps.P90, P, t);

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.LessThan(100_000));
            Assert.That(GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2), Is.EqualTo(before));
        }

        private static float[] Field(CostGrid g, int home)
        {
            var t = new float[g.Width * g.Height];
            Search.Field(g, home, P.V0Mps.P90, P, t);
            return t;
        }
    }
}
