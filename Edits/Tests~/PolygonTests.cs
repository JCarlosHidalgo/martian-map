using System;
using System.Numerics;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// <see cref="Exact.CompareProducts"/> and <see cref="Polygon.Contains"/>: inside or on the edge,
    /// with the even-odd rule, in exact integers (docs/guide/chapters/tablet.md, "apply() (D5)").
    /// The literals come from ~/scratch/c4-verify/oracle/cases_apply.py t4 (Python ints, not committed).
    /// </summary>
    public class PolygonTests
    {
        private const long M = FixedPoint.UnitsPerMetre;

        [Test]
        public void CompareProductsIsExact()
        {
            // Oracle: -1, 1, 0, 0, -1, 1.
            Assert.That(Exact.CompareProducts(1L << 62, 4, long.MaxValue, 8), Is.EqualTo(-1));
            Assert.That(Exact.CompareProducts(long.MinValue, long.MinValue, long.MaxValue, long.MaxValue), Is.EqualTo(1));
            Assert.That(Exact.CompareProducts(3, 5, 5, 3), Is.EqualTo(0));
            Assert.That(Exact.CompareProducts(long.MinValue, long.MaxValue, long.MinValue, long.MaxValue), Is.EqualTo(0));
            Assert.That(Exact.CompareProducts(long.MinValue, 1, long.MaxValue, -1), Is.EqualTo(-1));
            Assert.That(Exact.CompareProducts(0, 5, -1, 1), Is.EqualTo(1));

            var rng = new Random(20261114);
            long[] edges = { 0, 1, -1, long.MaxValue, long.MinValue, int.MaxValue, int.MinValue, 1L << 32, -(1L << 32) };
            for (int k = 0; k < 10000; k++)
            {
                long a = Pick(rng, edges), b = Pick(rng, edges), c = Pick(rng, edges), d = Pick(rng, edges);
                int expected = (new BigInteger(a) * b - new BigInteger(c) * d).Sign;
                Assert.That(Exact.CompareProducts(a, b, c, d), Is.EqualTo(expected), $"{a}·{b} − {c}·{d}");
            }
        }

        [Test]
        public void TheAlignedRectangleCountsItsBorder()
        {
            // [0, 0]–[10 m, 5 m]: 11 × 6 = 66 whole-metre points, the border included.
            FixedPoint[] rect = Ring(0, 0, 2560, 0, 2560, 1280, 0, 1280);
            int count = 0;
            for (long i = -64; i <= 64; i++)
            {
                for (long j = -64; j <= 64; j++)
                {
                    count += Polygon.Contains(rect, i * M, j * M) ? 1 : 0;
                }
            }

            Assert.That(count, Is.EqualTo(66));
        }

        [Test]
        public void SamplesOnVerticesAndHorizontalEdgesAreInside()
        {
            FixedPoint[] trapezoid = Ring(0, 0, 1536, 0, 1024, 768, 512, 768);

            // The 4 vertices, both horizontal edges, both oblique edges and the middle.
            long[,] inside = { { 0, 0 }, { 1536, 0 }, { 1024, 768 }, { 512, 768 }, { 768, 0 }, { 768, 768 }, { 256, 384 }, { 1280, 384 }, { 768, 384 } };

            // 1/256 m outside each of those edges and vertices.
            long[,] outside = { { -1, 0 }, { 1537, 0 }, { 768, -1 }, { 768, 769 }, { 255, 384 }, { 1281, 384 }, { 511, 768 }, { 1025, 768 } };
            for (int k = 0; k < inside.GetLength(0); k++)
            {
                Assert.That(Polygon.Contains(trapezoid, inside[k, 0], inside[k, 1]), Is.True, $"({inside[k, 0]}, {inside[k, 1]})");
            }

            for (int k = 0; k < outside.GetLength(0); k++)
            {
                Assert.That(Polygon.Contains(trapezoid, outside[k, 0], outside[k, 1]), Is.False, $"({outside[k, 0]}, {outside[k, 1]})");
            }
        }

        [Test]
        public void TheEvenOddRuleLeavesTheCrossingOut()
        {
            // A bowtie: the crossing (256, 256) is on the edge; the two lobes are inside, the gaps above and below are not.
            FixedPoint[] bowtie = Ring(0, 0, 512, 512, 512, 0, 0, 512);
            Assert.That(Polygon.Contains(bowtie, 256, 256), Is.True);
            Assert.That(Polygon.Contains(bowtie, 100, 256), Is.True);
            Assert.That(Polygon.Contains(bowtie, 412, 256), Is.True);
            Assert.That(Polygon.Contains(bowtie, 256, 100), Is.False);
            Assert.That(Polygon.Contains(bowtie, 256, 412), Is.False);

            // A pentagram: the central pentagon is crossed twice, so it is outside; a tip is inside,
            // and (0, 309) and (300, 309) lie on the horizontal stroke.
            FixedPoint[] star = Ring(0, 1000, 588, -809, -951, 309, 951, 309, -588, -809);
            Assert.That(Polygon.Contains(star, 0, 0), Is.False);
            Assert.That(Polygon.Contains(star, 0, 900), Is.True);
            Assert.That(Polygon.Contains(star, 0, 309), Is.True);
            Assert.That(Polygon.Contains(star, 300, 309), Is.True);
            Assert.That(Polygon.Contains(star, 2000, 0), Is.False);
        }

        [Test]
        public void ACollinearRingMarksOnlyItsSegment()
        {
            // Oracle: the whole-metre points (0, 0) … (4, 4) and nothing else in [-4, 8]².
            FixedPoint[] ring = Ring(0, 0, 512, 512, 1024, 1024);
            for (long i = -4; i <= 8; i++)
            {
                for (long j = -4; j <= 8; j++)
                {
                    bool expected = i == j && i >= 0 && i <= 4;
                    Assert.That(Polygon.Contains(ring, i * M, j * M), Is.EqualTo(expected), $"({i}, {j}) m");
                }
            }
        }

        [Test]
        public void ExtremeCoordinatesDoNotOverflow()
        {
            const long Mn = int.MinValue, Mx = int.MaxValue;
            FixedPoint[] tri = { new FixedPoint(int.MinValue, int.MinValue), new FixedPoint(int.MaxValue, int.MinValue), new FixedPoint(0, int.MaxValue) };

            // Oracle: in, in, in, in, in, then out × 6.
            long[,] points = { { 0, 0 }, { Mn, Mn }, { Mx, Mn }, { 0, Mx }, { 5, Mn }, { Mx + 1, Mn }, { 1L << 40, 0 }, { -(1L << 40), -(1L << 40) }, { 0, Mx + 1 }, { Mn, Mn + 1 }, { -1000, Mx - 1 } };
            bool[] expected = { true, true, true, true, true, false, false, false, false, false, false };
            for (int k = 0; k < expected.Length; k++)
            {
                Assert.That(Polygon.Contains(tri, points[k, 0], points[k, 1]), Is.EqualTo(expected[k]), $"({points[k, 0]}, {points[k, 1]})");
            }
        }

        private static long Pick(Random rng, long[] edges)
        {
            if (rng.Next(4) == 0)
            {
                return edges[rng.Next(edges.Length)];
            }

            var bytes = new byte[8];
            rng.NextBytes(bytes);
            return BitConverter.ToInt64(bytes, 0) >> rng.Next(64);
        }

        private static FixedPoint[] Ring(params int[] en)
        {
            var ring = new FixedPoint[en.Length / 2];
            for (int k = 0; k < ring.Length; k++)
            {
                ring[k] = new FixedPoint(en[2 * k], en[2 * k + 1]);
            }

            return ring;
        }
    }
}
