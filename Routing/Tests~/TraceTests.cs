using System;
using System.Linq;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// What a <see cref="Route"/> holds (docs/guide/chapters/tablet.md, "Contrato de rutas (C4)").
    /// The expected values are literals, computed outside Routing by an independent script with
    /// exact fractions.
    /// </summary>
    public class TraceTests
    {
        private const double Tol = 1e-9;

        private static readonly float[] HeightM =
        {
            0f, 0.5f, 1.25f, 0.75f, 0f,
            0.25f, 1f, 2f, 1.5f, 0.5f,
            0.5f, 1.25f, 2.5f, 2f, 1f,
        };

        private static readonly byte[] Relief =
        {
            0, 40, 0, 50, 0,
            10, 0, 45, 0, 0,
            0, 0, 0, 38, 0,
        };

        private static readonly byte[] Hazard =
        {
            0, 100, 0, 0, 0,
            0, 0, 150, 0, 0,
            0, 0, 0, 0, 120,
        };

        private static CostParams P => Fixtures.CostParams;

        private static CostGrid G { get; } =
            new CostGrid(5, 3, 2f, new EnuPoint(0, 0), HeightM, new byte[15], Relief, Hazard);

        [Test]
        public void AKnightMoveThenADiagonal()
        {
            Route r = Cost.Trace(G, new[] { G.Index(0, 0), G.Index(2, 1), G.Index(3, 2) }, P);
            Assert.That(r.Found, Is.True);
            Assert.That(r.Cells, Is.EqualTo(Nodes((0, 0), (1, 0), (1, 1), (2, 1), (3, 2))));
            AssertPolyline(r, (0, 0, 0), (2, 1, 0.75), (4, 2, 2), (6, 4, 2));
            Assert.That(r.LengthM, Is.EqualTo(7.749985324502928).Within(Tol));
            Assert.That(r.AscentM, Is.EqualTo(2.0).Within(Tol));
            Assert.That(r.AmberM, Is.EqualTo(3.900171988833459).Within(Tol));
            Assert.That(r.EtaS.P10, Is.EqualTo(45.05526068197703).Within(Tol));
            Assert.That(r.EtaS.P50, Is.EqualTo(60.50277863008345).Within(Tol));
            Assert.That(r.EtaS.P90, Is.EqualTo(75.6284732876043).Within(Tol));
            Assert.That(r.MaxHazard, Is.EqualTo(150));
        }

        [Test]
        public void AShortcutGetsAPointAtEveryLineOfNodesItCrosses()
        {
            Route r = Cost.Trace(G, new[] { G.Index(0, 0), G.Index(4, 2) }, P);
            Assert.That(r.Cells, Is.EqualTo(Nodes((0, 0), (1, 0), (1, 1), (2, 1), (3, 1), (3, 2), (4, 2))));
            AssertPolyline(r, (0, 0, 0), (2, 1, 0.75), (4, 2, 2), (6, 3, 1.75), (8, 4, 1));
            Assert.That(r.LengthM, Is.EqualTo(9.530053482770889).Within(Tol));
            Assert.That(r.AscentM, Is.EqualTo(3.0).Within(Tol));
            Assert.That(r.AmberM, Is.EqualTo(4.790206067967439).Within(Tol));
            Assert.That(r.EtaS.P50, Is.EqualTo(76.88314506177288).Within(Tol));
            Assert.That(r.MaxHazard, Is.EqualTo(150));
        }

        [Test]
        public void ABlockedSegmentGivesNoRoute()
        {
            var hazard = (byte[])Hazard.Clone();
            hazard[G.Index(1, 1)] = 255;
            var g = new CostGrid(5, 3, 2f, new EnuPoint(0, 0), HeightM, new byte[15], Relief, hazard);
            Assert.That(Cost.Trace(g, new[] { g.Index(0, 0), g.Index(4, 2) }, P), Is.SameAs(Route.NotFound));
        }

        [Test]
        public void ARouteNeedsTwoWaypoints()
        {
            Assert.Throws<ArgumentException>(() => Cost.Trace(G, new[] { 0 }, P));
            Assert.Throws<ArgumentNullException>(() => Cost.Trace(G, null, P));
            Assert.Throws<ArgumentOutOfRangeException>(() => Cost.Trace(G, new[] { 15, 0 }, P));
        }

        private static int[] Nodes(params (int I, int J)[] nodes) => nodes.Select(n => G.Index(n.I, n.J)).ToArray();

        private static void AssertPolyline(Route r, params (double E, double N, double U)[] expected)
        {
            Assert.That(r.PolylineEnuM.Count, Is.EqualTo(expected.Length));
            for (int k = 0; k < expected.Length; k++)
            {
                Assert.That(r.PolylineEnuM[k].E, Is.EqualTo(expected[k].E).Within(Tol), $"e of point {k}");
                Assert.That(r.PolylineEnuM[k].N, Is.EqualTo(expected[k].N).Within(Tol), $"n of point {k}");
                Assert.That(r.PolylineEnuM[k].U, Is.EqualTo(expected[k].U).Within(Tol), $"u of point {k}");
            }
        }
    }
}
