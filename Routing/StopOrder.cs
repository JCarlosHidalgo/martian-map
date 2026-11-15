using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The stop order of <see cref="Router.OrderStops"/>: Held-Karp over the costs of the legs, up to
    /// <see cref="MaxExact"/> stops, and nearest neighbour + 2-opt beyond; in orienteering, the
    /// subset of stops too. It knows no grid: it receives the matrix of leg costs and returns the
    /// order. A cost of +∞ is a leg with no route.
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
        /// The most passes of 2-opt, and the most rounds of insertion and 2-opt in orienteering. It
        /// is not reached: each pass lowers the cost by more than <see cref="TieRel"/>, and each round
        /// adds science value.
        /// </summary>
        private const int MaxPasses = 1000;

        /// <summary>
        /// The order of the <c>m</c> stops (indices into <paramref name="fromStart"/>) that
        /// minimises <c>fromStart[π₀] + between[π₀, π₁] + … + toHome[π_last]</c>. Of the orders that
        /// cost no more than the optimum × (1 + 1e-9), it returns the lexicographically smallest,
        /// so a tie keeps the input order. A null <paramref name="toHome"/> leaves the return out:
        /// the open path. Every cost must be at least 0, and may be +∞; if every order is +∞, the
        /// input order.
        /// <para>The table is filled backwards: <c>g(S, j)</c> is the cheapest way to leave stop
        /// <c>j</c>, visit every stop of <c>S</c> and end at home. The order is rebuilt forwards: at
        /// each step, the stop of lowest index whose prefix plus <c>g</c> of the rest still fits.</para>
        /// </summary>
        internal static int[] Order(double[] fromStart, double[,] between, double[] toHome)
        {
            int m = Check(fromStart, between, toHome);
            if (m > MaxExact)
            {
                throw new ArgumentOutOfRangeException(nameof(fromStart), $"Held-Karp orders at most {MaxExact} stops.");
            }

            if (m == 0)
            {
                return Array.Empty<int>();
            }

            double[] g = Table(between, toHome, m);
            return Rebuild(g, fromStart, between, m, (1 << m) - 1, out _);
        }

        /// <summary>
        /// The order of the <c>m</c> stops for any <c>m</c>, without the guarantee of the optimum:
        /// nearest neighbour from the start (by the cost of the leg; a tie goes to the lower index),
        /// then <see cref="TwoOpt"/>. The same arguments as <see cref="Order"/>.
        /// </summary>
        internal static int[] Heuristic(double[] fromStart, double[,] between, double[] toHome)
        {
            int m = Check(fromStart, between, toHome);
            var order = new int[m];
            var used = new bool[m];
            int last = -1;
            for (int k = 0; k < m; k++)
            {
                int pick = -1;
                double best = 0;
                for (int j = 0; j < m; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    double c = last < 0 ? fromStart[j] : between[last, j];
                    if (pick < 0 || c < best)
                    {
                        pick = j;
                        best = c;
                    }
                }

                order[k] = pick;
                used[pick] = true;
                last = pick;
            }

            TwoOpt(order, fromStart, between, toHome, 0, null);
            return order;
        }

        /// <summary>
        /// Orienteering: the subset of the <c>m</c> stops, in visiting order, with the most
        /// <paramref name="value"/> whose order <paramref name="fits"/>; the stops left out are
        /// dropped. A stop of value 0 is never taken. <paramref name="direct"/> is the cost from the
        /// start to home with no stops (0 with a null <paramref name="toHome"/>).
        /// <para>Up to <see cref="MaxExact"/> stops it is exact over the subsets: one Held-Karp table
        /// gives every subset's optimal cost, and its order is rebuilt as <see cref="Order"/> does.
        /// <paramref name="fits"/> is asked of that order. Of the subsets that fit, the most value wins;
        /// on a tie, the lowest cost (legs plus <paramref name="dwellS"/>; within 1e-9 of the lowest);
        /// on a tie, the one that holds the stop of lowest index where the two differ.</para>
        /// <para>Beyond, greedy insertion: from the empty route, it inserts the stop and position of
        /// highest <c>value / (Δcost + dwell_s)</c> whose route fits (a tie goes to the lower Δcost,
        /// then the lower index, then the earlier position); when none fits, <see cref="TwoOpt"/>
        /// keeping only routes that fit, and if that improved, it tries to insert again.</para>
        /// </summary>
        internal static int[] Choose(
            double[] fromStart,
            double[,] between,
            double[] toHome,
            double direct,
            int[] value,
            double[] dwellS,
            Func<int[], bool> fits)
        {
            int m = Check(fromStart, between, toHome);
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (dwellS == null)
            {
                throw new ArgumentNullException(nameof(dwellS));
            }

            if (fits == null)
            {
                throw new ArgumentNullException(nameof(fits));
            }

            if (value.Length != m || dwellS.Length != m)
            {
                throw new ArgumentException("One value and one dwell per stop.");
            }

            RequireCost(direct, nameof(direct));
            return m <= MaxExact
                ? ChooseExact(fromStart, between, toHome, value, dwellS, fits, m)
                : ChooseGreedy(fromStart, between, toHome, direct, value, dwellS, fits, m);
        }

        private static int[] ChooseExact(double[] fromStart, double[,] between, double[] toHome, int[] value, double[] dwellS, Func<int[], bool> fits, int m)
        {
            if (m == 0)
            {
                return Array.Empty<int>();
            }

            double[] g = Table(between, toHome, m);
            int full = (1 << m) - 1;

            // The value of each subset that fits (0 if it does not) and its cost.
            var setValue = new int[full + 1];
            var setCost = new double[full + 1];
            int bestValue = 0;
            for (int set = 1; set <= full; set++)
            {
                int v = 0;
                double dwell = 0;
                for (int j = 0; j < m; j++)
                {
                    if ((set >> j & 1) != 0)
                    {
                        v += value[j];
                        dwell += dwellS[j];
                    }
                }

                if (v == 0 || v < bestValue)
                {
                    continue;
                }

                int[] order = Rebuild(g, fromStart, between, m, set, out double legs);
                if (fits(order))
                {
                    setValue[set] = v;
                    setCost[set] = legs + dwell;
                    bestValue = v;
                }
            }

            if (bestValue == 0)
            {
                return Array.Empty<int>();
            }

            double lowest = double.PositiveInfinity;
            for (int set = 1; set <= full; set++)
            {
                if (setValue[set] == bestValue && setCost[set] < lowest)
                {
                    lowest = setCost[set];
                }
            }

            double limit = lowest * (1 + TieRel);
            int chosen = 0;
            for (int set = 1; set <= full; set++)
            {
                if (setValue[set] == bestValue && setCost[set] <= limit && (chosen == 0 || HoldsFirstDifference(set, chosen)))
                {
                    chosen = set;
                }
            }

            return Rebuild(g, fromStart, between, m, chosen, out _);
        }

        /// <summary>Whether <paramref name="a"/> holds the stop of lowest index where it and <paramref name="b"/> differ.</summary>
        private static bool HoldsFirstDifference(int a, int b)
        {
            int diff = a ^ b;
            return (a & diff & -diff) != 0;
        }

        private static int[] ChooseGreedy(double[] fromStart, double[,] between, double[] toHome, double direct, int[] value, double[] dwellS, Func<int[], bool> fits, int m)
        {
            int[] route = Array.Empty<int>();
            var used = new bool[m];
            for (int round = 0; round < MaxPasses; round++)
            {
                while (true)
                {
                    double current = Cost(route, fromStart, between, toHome, direct);
                    int[] best = null;
                    int bestJ = -1;
                    double bestDelta = 0, bestDen = 0;
                    for (int j = 0; j < m; j++)
                    {
                        if (used[j] || value[j] == 0)
                        {
                            continue;
                        }

                        for (int pos = 0; pos <= route.Length; pos++)
                        {
                            int[] trial = Insert(route, pos, j);
                            double delta = Cost(trial, fromStart, between, toHome, direct) - current;
                            if (!double.IsFinite(delta))
                            {
                                continue;
                            }

                            double den = Math.Max(delta + dwellS[j], 0);
                            bool better = best == null;
                            if (!better)
                            {
                                // value[j] / den against value[bestJ] / bestDen, without dividing by 0.
                                double mine = value[j] * bestDen, theirs = value[bestJ] * den;
                                better = mine > theirs || (mine == theirs && delta < bestDelta);
                            }

                            if (better && fits(trial))
                            {
                                best = trial;
                                bestJ = j;
                                bestDelta = delta;
                                bestDen = den;
                            }
                        }
                    }

                    if (best == null)
                    {
                        break;
                    }

                    route = best;
                    used[bestJ] = true;
                }

                if (!TwoOpt(route, fromStart, between, toHome, direct, fits))
                {
                    break;
                }
            }

            return route;
        }

        /// <summary><paramref name="route"/> with <paramref name="stop"/> inserted before position <paramref name="pos"/>.</summary>
        private static int[] Insert(int[] route, int pos, int stop)
        {
            var trial = new int[route.Length + 1];
            Array.Copy(route, 0, trial, 0, pos);
            trial[pos] = stop;
            Array.Copy(route, pos, trial, pos + 1, route.Length - pos);
            return trial;
        }

        /// <summary>
        /// 2-opt of best improvement over the asymmetric matrix, in place: each pass reverses the
        /// stretch <c>[i, j]</c> of <paramref name="route"/> that lowers the cost the most, if it
        /// lowers it by more than 1e-9 relative, and if <paramref name="keep"/> (when given) accepts
        /// it. Reversing a stretch walks its inner legs the other way, so each candidate is costed
        /// whole (a sum, never a difference, so +∞ gives no NaN). A tie goes to the lower
        /// <c>(i, j)</c>. Returns whether it changed the route.
        /// </summary>
        private static bool TwoOpt(int[] route, double[] fromStart, double[,] between, double[] toHome, double direct, Func<int[], bool> keep)
        {
            int n = route.Length;
            bool changed = false;
            double current = Cost(route, fromStart, between, toHome, direct);
            var trial = new int[n];
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                int bi = -1, bj = -1;
                double best = current;
                for (int i = 0; i < n - 1; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        Array.Copy(route, trial, n);
                        Array.Reverse(trial, i, j - i + 1);
                        double c = Cost(trial, fromStart, between, toHome, direct);
                        if (c * (1 + TieRel) < current && c < best && (keep == null || keep(trial)))
                        {
                            best = c;
                            bi = i;
                            bj = j;
                        }
                    }
                }

                if (bi < 0)
                {
                    break;
                }

                Array.Reverse(route, bi, bj - bi + 1);
                current = best;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// The cost of visiting <paramref name="route"/> in order, from the start to home (no return
        /// with a null <paramref name="toHome"/>); <paramref name="direct"/> with no stops.
        /// </summary>
        private static double Cost(int[] route, double[] fromStart, double[,] between, double[] toHome, double direct)
        {
            if (route.Length == 0)
            {
                return direct;
            }

            double c = fromStart[route[0]];
            for (int k = 1; k < route.Length; k++)
            {
                c += between[route[k - 1], route[k]];
            }

            return toHome == null ? c : c + toHome[route[route.Length - 1]];
        }

        /// <summary>Checks the matrix and returns its number of stops.</summary>
        private static int Check(double[] fromStart, double[,] between, double[] toHome)
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

            return m;
        }

        /// <summary>
        /// The Held-Karp table for <c>m</c> ≤ <see cref="MaxExact"/> stops: <c>g[S · m + j]</c>, for
        /// <c>j</c> not in <c>S</c>, is the cheapest way to leave <c>j</c>, visit every stop of
        /// <c>S</c> and end at home (or anywhere, with a null <paramref name="toHome"/>).
        /// </summary>
        private static double[] Table(double[,] between, double[] toHome, int m)
        {
            int full = (1 << m) - 1;
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

            return g;
        }

        /// <summary>
        /// The order of the stops of <paramref name="set"/> (not empty) from the table
        /// <paramref name="g"/>: of the orders within the optimum × (1 + 1e-9), the lexicographically
        /// smallest. <paramref name="costS"/> is the optimum.
        /// </summary>
        private static int[] Rebuild(double[] g, double[] fromStart, double[,] between, int m, int set, out double costS)
        {
            int count = 0;
            double optimum = double.PositiveInfinity;
            for (int j = 0; j < m; j++)
            {
                if ((set >> j & 1) != 0)
                {
                    count++;
                    optimum = Math.Min(optimum, fromStart[j] + g[((set & ~(1 << j)) * m) + j]);
                }
            }

            costS = optimum;
            double limit = optimum * (1 + TieRel);
            var order = new int[count];
            int rest = set, last = -1;
            double prefix = 0;
            for (int k = 0; k < count; k++)
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
                            if (pick < 0 || v < best)
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

        /// <summary>A leg cost is at least 0; +∞ is a leg with no route. NaN throws.</summary>
        private static void RequireCost(double c, string name)
        {
            if (!(c >= 0))
            {
                throw new ArgumentException("A leg cost must be at least 0 (+∞ for no route).", name);
            }
        }
    }
}
