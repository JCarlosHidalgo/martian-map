using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// The segment rule (docs/guide/chapters/tablet.md, "Costo"). The expected values are
    /// literals, computed outside Routing by an independent script with exact fractions; no test
    /// recomputes the formula.
    /// </summary>
    public class SegmentTests
    {
        private const double Tol = 1e-9;

        private static CostParams P => Fixtures.CostParams;

        [Test]
        public void AKnightMoveOnFlatGroundTakesItsWholeLength()
        {
            CostGrid g = Grid(3, 2);
            Assert.That(Time(g, (0, 0), (2, 1)), Is.EqualTo(10.647942749999).Within(Tol));
        }

        [TestCase(1, 0)]
        [TestCase(1, 1)]
        public void AKnightMoveIsBlockedByEitherNodeItCrosses(int i, int j)
        {
            CostGrid g = Grid(3, 2, hazard: Set(3, 2, (i, j, 255)));
            Assert.That(Time(g, (0, 0), (2, 1)), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void AKnightMoveGivesAQuarterToEachOfItsFourNodes()
        {
            CostGrid g = Grid(3, 2, hazard: Set(3, 2, (1, 0, 128)));
            Assert.That(Cost.SegmentAmberM(g, g.Index(0, 0), g.Index(2, 1), P), Is.EqualTo(1.118033988749895).Within(Tol));
            Assert.That(Time(g, (0, 0), (2, 1), withAmberPenalty: true), Is.EqualTo(13.33122432299875).Within(Tol));
            Assert.That(Time(g, (0, 0), (2, 1)), Is.EqualTo(10.647942749999).Within(Tol));
        }

        [TestCase(1, 0)]
        [TestCase(0, 1)]
        public void ADiagonalDoesNotCutABlockedCorner(int i, int j)
        {
            CostGrid g = Grid(2, 2, hazard: Set(2, 2, (i, j, 255)));
            Assert.That(Time(g, (0, 0), (1, 1)), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void ADiagonalWalksNoMetresInTheSideCells()
        {
            CostGrid g = Grid(2, 2, hazard: Set(2, 2, (1, 0, 128)));
            Assert.That(Cost.SegmentAmberM(g, g.Index(0, 0), g.Index(1, 1), P), Is.EqualTo(0));
        }

        [Test]
        public void AOneNodeWallStopsKnightMovesAndShortcuts()
        {
            CostGrid g = Grid(5, 3, hazard: Set(5, 3, (2, 0, 255), (2, 1, 255), (2, 2, 255)));
            Assert.That(Time(g, (1, 1), (3, 2)), Is.EqualTo(double.PositiveInfinity));
            Assert.That(Time(g, (0, 0), (4, 2)), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void AShortcutOverAHillSeesItsClimb()
        {
            CostGrid g = Grid(5, 1, heightM: new[] { 0f, 1f, 2f, 1f, 0f });
            double edges = 0;
            for (int k = 0; k < 4; k++)
            {
                edges += Time(g, (k, 0), (k + 1, 0));
            }

            Assert.That(Time(g, (0, 0), (4, 0)), Is.EqualTo(95.44153728851191).Within(Tol));
            Assert.That(edges, Is.EqualTo(95.44153728851192).Within(Tol));
        }

        [Test]
        public void ArbitrarySegmentsUseBilinearHeightAndEachCellsRelief()
        {
            float[] height =
            {
                0f, 0.5f, 1.25f, 0.75f, 0f,
                0.25f, 1f, 2f, 1.5f, 0.5f,
                0.5f, 1.25f, 2.5f, 2f, 1f,
            };
            byte[] relief =
            {
                0, 40, 0, 50, 0,
                10, 0, 45, 0, 0,
                0, 0, 0, 38, 0,
            };
            CostGrid g = Grid(5, 3, heightM: height, relief: relief);
            Assert.That(Time(g, (0, 0), (4, 2)), Is.EqualTo(76.88314506177288).Within(Tol));
            Assert.That(Time(g, (1, 0), (3, 1)), Is.EqualTo(35.15191279744196).Within(Tol));
            Assert.That(Time(g, (3, 2), (2, 0)), Is.EqualTo(19.48920801029821).Within(Tol));
            Assert.That(Time(g, (0, 0), (4, 2), v0: P.V0Mps.P90), Is.EqualTo(96.1039313272161).Within(Tol));
        }

        [Test]
        public void ASegmentNeedsTwoDifferentNodesInsideTheGrid()
        {
            CostGrid g = Grid(2, 2);
            Assert.Throws<ArgumentException>(() => Cost.SegmentTimeS(g, 1, 1, P.V0Mps.P50, P, withAmberPenalty: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => Cost.SegmentTimeS(g, 0, 4, P.V0Mps.P50, P, withAmberPenalty: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => Cost.SegmentAmberM(g, -1, 0, P));
        }

        private static double Time(CostGrid g, (int I, int J) a, (int I, int J) b, bool withAmberPenalty = false, double? v0 = null) =>
            Cost.SegmentTimeS(g, g.Index(a.I, a.J), g.Index(b.I, b.J), v0 ?? P.V0Mps.P50, P, withAmberPenalty);

        private static byte[] Set(int width, int height, params (int I, int J, byte Value)[] cells)
        {
            var values = new byte[width * height];
            foreach ((int i, int j, byte v) in cells)
            {
                values[j * width + i] = v;
            }

            return values;
        }

        private static CostGrid Grid(int width, int height, float[] heightM = null, byte[] hazard = null, byte[] relief = null)
        {
            int n = width * height;
            return new CostGrid(width, height, 2f, new EnuPoint(0, 0), heightM ?? new float[n], new byte[n], relief ?? new byte[n], hazard ?? new byte[n]);
        }
    }
}
