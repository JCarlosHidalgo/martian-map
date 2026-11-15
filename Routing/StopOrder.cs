using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The exact stop order of <see cref="Router.OrderStops"/> in TSP mode: Held-Karp over the
    /// costs of the legs, up to <see cref="MaxExact"/> stops. It knows no grid: it receives the
    /// matrix of leg costs and returns the order.
    /// </summary>
    internal static class StopOrder
    {
        /// <summary>The most stops Held-Karp orders: its table has <c>2^m · m</c> entries.</summary>
        internal const int MaxExact = 12;

        /// <summary>
        /// How much an order may exceed the optimum and still tie with it: the same margin as a
        /// shortcut of <see cref="Smoothing"/>. Two orders that are equal but for rounding (a
        /// symmetric mission walked one way or the other) must not be told apart by the last bit.
        /// </summary>
        private const double TieRel = 1e-9;

        /// <summary>
        /// The order of the <c>m</c> stops (indices into <paramref name="fromStart"/>) that
        /// minimises <c>fromStart[π₀] + between[π₀, π₁] + … + toHome[π_last]</c>. Of the orders that
        /// cost no more than the optimum × (1 + 1e-9), it returns the lexicographically smallest,
        /// so a tie keeps the input order. A null <paramref name="toHome"/> leaves the return out:
        /// the open path. Every cost must be finite and at least 0; the caller leaves unreachable
        /// stops out.
        /// <para>The table is filled backwards: <c>g(S, j)</c> is the cheapest way to leave stop
        /// <c>j</c>, visit every stop of <c>S</c> and end at home. The order is rebuilt forwards: at
        /// each step, the stop of lowest index whose prefix plus <c>g</c> of the rest still fits.</para>
        /// </summary>
        internal static int[] Order(double[] fromStart, double[,] between, double[] toHome)
        {
            if (fromStart == null)
            {
                throw new ArgumentNullException(nameof(fromStart));
            }

            if (between == null)
            {
                throw new ArgumentNullException(nameof(between));
            }

            int m = fromStart.Length;
            if (between.GetLength(0) != m || between.GetLength(1) != m || (toHome != null && toHome.Length != m))
            {
                throw new ArgumentException("The matrix has one row and one column per stop.");
            }

            if (m > MaxExact)
            {
                throw new ArgumentOutOfRangeException(nameof(fromStart), $"Held-Karp orders at most {MaxExact} stops.");
            }

            for (int i = 0; i < m; i++)
            {
                RequireCost(fromStart[i], nameof(fromStart));
                if (toHome != null)
                {
                    RequireCost(toHome[i], nameof(toHome));
                }

                for (int j = 0; j < m; j++)
                {
                    RequireCost(between[i, j], nameof(between));
                }
            }

            if (m == 0)
            {
                return Array.Empty<int>();
            }

            int full = (1 << m) - 1;

            // g[S · m + j], for j not in S.
            var g = new double[(1 << m) * m];
            for (int j = 0; j < m; j++)
            {
                g[j] = toHome == null ? 0 : toHome[j];
            }

            for (int set = 1; set <= full; set++)
            {
                for (int j = 0; j < m; j++)
                {
                    if ((set >> j & 1) != 0)
                    {
                        continue;
                    }

                    double best = double.PositiveInfinity;
                    for (int l = 0; l < m; l++)
                    {
                        if ((set >> l & 1) != 0)
                        {
                            double v = between[j, l] + g[((set & ~(1 << l)) * m) + l];
                            if (v < best)
                            {
                                best = v;
                            }
                        }
                    }

                    g[(set * m) + j] = best;
                }
            }

            double optimum = double.PositiveInfinity;
            for (int j = 0; j < m; j++)
            {
                optimum = Math.Min(optimum, fromStart[j] + g[((full & ~(1 << j)) * m) + j]);
            }

            double limit = optimum * (1 + TieRel);
            var order = new int[m];
            int rest = full, last = -1;
            double prefix = 0;
            for (int k = 0; k < m; k++)
            {
                int pick = -1;
                for (int j = 0; j < m && pick < 0; j++)
                {
                    if ((rest >> j & 1) == 0)
                    {
                        continue;
                    }

                    double step = last < 0 ? fromStart[j] : between[last, j];
                    if (prefix + step + g[((rest & ~(1 << j)) * m) + j] <= limit)
                    {
                        pick = j;
                        prefix += step;
                    }
                }

                if (pick < 0)
                {
                    // Only if a stop taken earlier fitted the limit by less than the rounding of
                    // these sums: then the cheapest continuation, which is the optimum's.
                    double best = double.PositiveInfinity;
                    for (int j = 0; j < m; j++)
                    {
                        if ((rest >> j & 1) != 0)
                        {
                            double step = last < 0 ? fromStart[j] : between[last, j];
                            double v = step + g[((rest & ~(1 << j)) * m) + j];
                            if (v < best)
                            {
                                best = v;
                                pick = j;
                            }
                        }
                    }

                    prefix += last < 0 ? fromStart[pick] : between[last, pick];
                }

                order[k] = pick;
                rest &= ~(1 << pick);
                last = pick;
            }

            return order;
        }

        private static void RequireCost(double c, string name)
        {
            if (!(c >= 0 && double.IsFinite(c)))
            {
                throw new ArgumentException("A leg cost must be finite and at least 0.", name);
            }
        }
    }
}
