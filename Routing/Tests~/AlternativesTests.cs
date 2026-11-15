using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="Router.Alternatives"/>: one route per <see cref="Alternative"/>, each A* and
    /// smoothing with its own weights (docs/guide/chapters/tablet.md, "Las tres alternativas").
    /// The literals come from the oracle (~/scratch/c4-verify/oracle/cases_alt.py, not committed):
    /// a Dijkstra and a smoothing with the same weights, over the segment rule; every case has a
    /// unique optimum for its weights.
    /// </summary>
    public class AlternativesTests
    {
        private const double Tol = 1e-9;

        private static CostParams P => Fixtures.CostParams;

        private static EnuPoint At(int i, int j) => new EnuPoint(2 * i, 2 * j);

        [Test]
        public void ThereAreAlwaysThreeInOrder()
        {
            // On flat free ground the three coincide, and there are still three.
            CostGrid g = Grids.Flat(20, 9);
            IReadOnlyList<Route> alts = Router.Alternatives(g, At(0, 0), At(19, 8), P);
            Route planned = Router.PlanRoute(g, At(0, 0), At(19, 8), P);
            Assert.That(alts.Count, Is.EqualTo(3));
            foreach (Route r in alts)
            {
                Assert.That(r.Cells, Is.EqualTo(planned.Cells));
                Assert.That(r.EtaS.P50, Is.EqualTo(planned.EtaS.P50));
                Assert.That(r.AscentM, Is.EqualTo(planned.AscentM));
            }
        }

        [Test]
        public void TheFastestIsPlanRoute()
        {
            CostGrid g = Grids.FromMap(Grids.Random12x9, Grids.RandomHeight);
            Route fastest = Router.Alternatives(g, At(0, 0), At(11, 8), P)[(int)Alternative.Fastest];
            Route planned = Router.PlanRoute(g, At(0, 0), At(11, 8), P);
            Assert.That(fastest.Cells, Is.EqualTo(planned.Cells));
            Assert.That(fastest.PolylineEnuM, Is.EqualTo(planned.PolylineEnuM));
            Assert.That(fastest.EtaS, Is.EqualTo(planned.EtaS));
            Assert.That((fastest.LengthM, fastest.AscentM, fastest.AmberM, fastest.MaxHazard), Is.EqualTo((planned.LengthM, planned.AscentM, planned.AmberM, planned.MaxHazard)));
        }

        [Test]
        public void TheSafestGoesAroundAmberThatTheFastestCrosses()
        {
            // Oracle: fastest (0,4)→(14,4) straight, 2.000016249931121 m of amber, 68.08869326822332 s;
            // safest via (4,0), no amber, 79.81610591948353 s.
            CostGrid g = Grids.FromMap(Grids.AmberPatch15x9, Grids.TiltHeight);
            IReadOnlyList<Route> alts = Router.Alternatives(g, At(0, 4), At(14, 4), P);
            Route fastest = alts[(int)Alternative.Fastest], safest = alts[(int)Alternative.Safest];
            Assert.That(fastest.AmberM, Is.EqualTo(2.000016249931121).Within(Tol));
            Assert.That(fastest.EtaS.P50, Is.EqualTo(68.08869326822332).Within(Tol));
            Assert.That(safest.AmberM, Is.EqualTo(0));
            Assert.That(safest.EtaS.P50, Is.EqualTo(79.81610591948353).Within(Tol));
        }

        [Test]
        public void TheGentlestClimbsLessThanTheFastest()
        {
            // Oracle: fastest (0,0)→(20,0) over the ridge, 2.399985283613205 m up and down;
            // gentlest around its end, 0.3311587267865739 m, 123.25285484120923 s.
            CostGrid g = Grids.FromMap(Grids.Flat21x9, Grids.RidgeHeight);
            IReadOnlyList<Route> alts = Router.Alternatives(g, At(0, 0), At(20, 0), P);
            Route fastest = alts[(int)Alternative.Fastest], gentlest = alts[(int)Alternative.Gentlest];
            Assert.That(fastest.AscentM, Is.EqualTo(2.399985283613205).Within(Tol));
            Assert.That(fastest.EtaS.P50, Is.EqualTo(118.93860022502068).Within(Tol));
            Assert.That(gentlest.AscentM, Is.EqualTo(0.3311587267865739).Within(Tol));
            Assert.That(gentlest.EtaS.P50, Is.EqualTo(123.25285484120923).Within(Tol));
        }

        [Test]
        public void TheSafestRouteIsSmoothedWithItsOwnWeights()
        {
            // Oracle: the safest A* path, smoothed with the fastest's weights, takes (0,4)→(6,1)
            // across 2.2360751066303575 m of amber; with its own weights it keeps (4,0) and none.
            CostGrid g = Grids.FromMap(Grids.AmberPatch15x9, Grids.TiltHeight);
            var safest = Weights.For(Alternative.Safest, P);
            int[] path = Search.AStar(g, g.Index(0, 4), g.Index(14, 4), P.V0Mps.P50, P, safest, out _);
            Route withFastest = Cost.Trace(g, Smoothing.Smooth(g, path, P.V0Mps.P50, P, Weights.Fastest), P);
            Assert.That(withFastest.AmberM, Is.EqualTo(2.2360751066303575).Within(Tol));
            Assert.That(Router.Alternatives(g, At(0, 4), At(14, 4), P)[(int)Alternative.Safest].AmberM, Is.EqualTo(0));
        }

        [Test]
        public void NoRouteGivesThreeNotFound()
        {
            CostGrid wall = Grids.FromMap(Grids.Wall9x7);
            CostGrid nodata = Grids.FromMap(Grids.Nodata8x6);
            foreach (IReadOnlyList<Route> alts in new[]
            {
                Router.Alternatives(wall, At(0, 3), At(8, 3), P),
                Router.Alternatives(nodata, At(4, 3), At(0, 0), P),
            })
            {
                Assert.That(alts.Count, Is.EqualTo(3));
                foreach (Route r in alts)
                {
                    Assert.That(r.Found, Is.False);
                }
            }
        }

        [Test]
        public void NullParamsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => Router.Alternatives(Grids.Flat(3, 3), At(0, 0), At(2, 2), null));
        }
    }
}
