using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="StopOrder.Order"/>: Held-Karp over a cost matrix, ties to the lexicographically
    /// smallest order within 1e-9. The expected orders come from the oracle
    /// (~/scratch/c4-verify/oracle/cases_tsp.py, not committed), which tries every permutation
    /// (and, for 12 stops, its own Held-Karp, checked against the permutations up to 8 stops).
    /// </summary>
    public class StopOrderTests
    {
        private static readonly (int X, int Y)[] Points =
        {
            (3, 9), (14, 2), (8, 15), (1, 1), (12, 11), (6, 4), (15, 13),
        };

        private static readonly (int X, int Y)[] Points12 =
        {
            (3, 9), (14, 2), (8, 15), (1, 1), (12, 11), (6, 4), (15, 13), (2, 17), (19, 5), (10, 0), (17, 18), (5, 12),
        };

        [TestCase(0, false, new int[0])]
        [TestCase(1, false, new[] { 0 })]
        [TestCase(2, true, new[] { 1, 0 })]
        [TestCase(3, false, new[] { 0, 2, 1 })]
        [TestCase(5, true, new[] { 0, 2, 4, 1, 3 })]
        [TestCase(7, false, new[] { 0, 2, 6, 4, 1, 5, 3 })]
        public void ItMatchesBruteForceOnSmallMatrices(int m, bool asym, int[] expected)
        {
            Matrix(Points.AsSpan(0, m).ToArray(), (0, 0), (0, 0), asym, out double[] c0, out double[,] c, out double[] ch);
            Assert.That(StopOrder.Order(c0, c, ch), Is.EqualTo(expected));
        }

        [Test]
        public void TiesGoToTheLexicographicallySmallestOrder()
        {
            // Four stops on a line out of a base at the origin: there and back is 80 either way.
            Matrix(new[] { (10, 0), (20, 0), (30, 0), (40, 0) }, (0, 0), (0, 0), false, out double[] c0, out double[,] c, out double[] ch);
            Assert.That(StopOrder.Order(c0, c, ch), Is.EqualTo(new[] { 0, 1, 2, 3 }));

            // The reverse order 1e-15 cheaper is still a tie; 1e-4 s cheaper wins.
            c0[3] = 40 * (1 - 1e-15);
            Assert.That(StopOrder.Order(c0, c, ch), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            c0[3] = 40 - 1e-4;
            Assert.That(StopOrder.Order(c0, c, ch), Is.EqualTo(new[] { 3, 2, 1, 0 }));
        }

        [Test]
        public void WithoutAReturnItOrdersTheOpenPath()
        {
            Matrix(new[] { (10, 0), (12, 9), (-3, 8) }, (0, 0), (-20, 0), false, out double[] c0, out double[,] c, out double[] ch);
            Assert.That(StopOrder.Order(c0, c, ch), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(StopOrder.Order(c0, c, null), Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void TwelveStopsMatchTheOracle()
        {
            Matrix(Points12, (9, 9), (9, 9), true, out double[] c0, out double[,] c, out double[] ch);
            Assert.That(StopOrder.Order(c0, c, ch), Is.EqualTo(new[] { 2, 7, 11, 0, 3, 5, 9, 1, 8, 10, 6, 4 }));
        }

        [Test]
        public void BadMatricesThrow()
        {
            Matrix(Points.AsSpan(0, 3).ToArray(), (0, 0), (0, 0), false, out double[] c0, out double[,] c, out double[] ch);
            Assert.Throws<ArgumentNullException>(() => StopOrder.Order(null, c, ch));
            Assert.Throws<ArgumentNullException>(() => StopOrder.Order(c0, null, ch));
            Assert.Throws<ArgumentException>(() => StopOrder.Order(new double[2], c, ch));
            Assert.Throws<ArgumentException>(() => StopOrder.Order(c0, new double[3, 2], ch));
            Assert.Throws<ArgumentException>(() => StopOrder.Order(c0, c, new double[2]));

            double[,] negative = (double[,])c.Clone();
            negative[0, 1] = -1;
            Assert.Throws<ArgumentException>(() => StopOrder.Order(c0, negative, ch));
            double[] nan = (double[])c0.Clone();
            nan[1] = double.NaN;
            Assert.Throws<ArgumentException>(() => StopOrder.Order(nan, c, ch));
            double[] inf = (double[])ch.Clone();
            inf[2] = double.PositiveInfinity;
            Assert.Throws<ArgumentException>(() => StopOrder.Order(c0, c, inf));

            Assert.Throws<ArgumentOutOfRangeException>(() => StopOrder.Order(new double[13], new double[13, 13], new double[13]));
        }

        /// <summary>
        /// The test's input, built the same way in the oracle: Euclidean distances between integer
        /// points; with <paramref name="asym"/>, the cost from <c>i</c> to <c>j</c> is scaled by
        /// <c>1 + 0.1 · ((7i + 3j) mod 5)</c>, with the start as <c>i = m</c> and home as <c>j = m</c>.
        /// </summary>
        private static void Matrix((int X, int Y)[] stops, (int X, int Y) start, (int X, int Y) home, bool asym, out double[] c0, out double[,] c, out double[] ch)
        {
            int m = stops.Length;
            double F(int i, int j) => asym ? 1 + (0.1 * (((7 * i) + (3 * j)) % 5)) : 1.0;
            double D((int X, int Y) p, (int X, int Y) q) => Math.Sqrt(((double)(p.X - q.X) * (p.X - q.X)) + ((double)(p.Y - q.Y) * (p.Y - q.Y)));
            c = new double[m, m];
            c0 = new double[m];
            ch = new double[m];
            for (int i = 0; i < m; i++)
            {
                c0[i] = D(start, stops[i]) * F(m, i);
                ch[i] = D(stops[i], home) * F(i, m);
                for (int j = 0; j < m; j++)
                {
                    c[i, j] = i == j ? 0 : D(stops[i], stops[j]) * F(i, j);
                }
            }
        }
    }
}
