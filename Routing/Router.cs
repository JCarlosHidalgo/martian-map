using System;
using System.Collections.Generic;

namespace MartianMap.Routing
{
    /// <summary>
    /// The Routing API (C4). Every call is thread-safe and reentrant. Routing reads no files,
    /// network or JSON, and writes no logs.
    /// </summary>
    public static class Router
    {
        /// <summary>
        /// Builds the 2 m planning grid from <c>z = 0</c> tiles, with the edits already applied.
        /// Node <c>(i, j)</c> is at <c>origin_enu_m + (2i, 2j)</c>; in the tile that holds it, it is the
        /// sample of column <c>c = 2i</c> and row <c>r = 256 − 2j</c>, with <c>i</c> and <c>j</c>
        /// counted from that tile's south-west corner (row 0 is the north edge). <c>height_m</c>
        /// takes that sample minus <paramref name="originHeightM"/>; <c>slope_deg</c>,
        /// <c>roughness_cm</c> and <c>hazard</c> take the maximum of the 3 × 3 samples centred on
        /// it. A node on a <c>nodata</c> sample has <c>height_m</c> = 0 and <c>hazard</c> = 255.
        /// <para>Edge rules (docs/guide/chapters/tablet.md, "API de Routing"):</para>
        /// <list type="number">
        /// <item>The grid covers the bounding box of the tiles: its origin is the box's south-west
        /// corner, and it has <c>128·nₑ + 1</c> × <c>128·nₙ + 1</c> nodes. It throws if a tile is
        /// repeated or its origin is not on the 256 m lattice of the others.</item>
        /// <item>A node that falls in no tile (an L-shaped zone, a tile not loaded) is nodata.</item>
        /// <item>Each sample of the 3 × 3 window is read from whichever tile holds it; neighbouring
        /// tiles share their edge samples and their edge nodes. A sample that falls in no tile is
        /// skipped: it is not treated as nodata.</item>
        /// <item>A shared sample with different values in two tiles: <c>slope</c>,
        /// <c>roughness</c> and <c>hazard</c> take the maximum; <c>height</c> is nodata if either
        /// is, and otherwise comes from the south-westmost tile (lowest <c>n</c>, then lowest
        /// <c>e</c>). The result does not depend on the order of <paramref name="tiles"/>.</item>
        /// </list>
        /// It throws <see cref="ArgumentNullException"/> for null <paramref name="tiles"/>, a null tile or a
        /// null <paramref name="p"/>, and <see cref="ArgumentException"/> for no tiles, a non-finite
        /// <paramref name="originHeightM"/> or tile origin, or a box with more nodes than an <c>int</c> counts.
        /// <paramref name="p"/> does not change the grid, which keeps the rasters without the cost.
        /// </summary>
        public static CostGrid BuildGrid(TileRasters[] tiles, double originHeightM, CostParams p)
        {
            if (tiles == null)
            {
                throw new ArgumentNullException(nameof(tiles));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (tiles.Length == 0)
            {
                throw new ArgumentException("BuildGrid needs at least one tile.", nameof(tiles));
            }

            foreach (TileRasters t in tiles)
            {
                if (t == null)
                {
                    throw new ArgumentNullException(nameof(tiles), "A tile is null.");
                }
            }

            if (!double.IsFinite(originHeightM))
            {
                throw new ArgumentException("origin_height_m must be finite.", nameof(originHeightM));
            }

            return GridBuilder.Build(tiles, originHeightM);
        }

        /// <summary>
        /// The fastest route from <paramref name="start"/> to <paramref name="goal"/>: A*
        /// 16-connected over the segment rule, choosing with the amber penalty, at v₀ P50, then
        /// smoothed by <see cref="Smoothing.Smooth"/> (a shortcut only where it costs no more than
        /// the A* edges it replaces). <c>cells</c>, <c>polyline_enu_m</c> and every other field come
        /// from <see cref="Cost.Trace"/> over the smoothed waypoints. Each point
        /// goes to its nearest node (a point midway goes to the east or north one);
        /// <see cref="Route.NotFound"/> if either node is blocked or there is no path. With both points
        /// on the same free node, the route is found and stays there: one cell, one point, all 0.
        /// A goal that cannot be reached is only known after exploring the whole connected part of
        /// the grid around <paramref name="start"/>, which on a large grid takes as long as a field:
        /// call it off the main thread.
        /// </summary>
        public static Route PlanRoute(CostGrid grid, EnuPoint start, EnuPoint goal, CostParams p)
        {
            RequirePointIn(grid, start, nameof(start));
            RequirePointIn(grid, goal, nameof(goal));
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            return Leg(grid, NearestNode(grid, start), NearestNode(grid, goal), p, Weights.Fastest, out _);
        }

        /// <summary>
        /// One leg between two nodes with the weights <paramref name="w"/>: A* at v₀ P50, then
        /// <see cref="Smoothing.Smooth"/> with the same weights, then <see cref="Cost.Trace"/>.
        /// <paramref name="costS"/> is the cost the leg was chosen by: the sum, in <c>double</c> and
        /// left to right, of <see cref="Cost.SegmentCostS"/> with <paramref name="w"/> over the
        /// smoothed waypoints; 0 if <paramref name="s"/> = <paramref name="t"/> and +∞ with no route.
        /// </summary>
        internal static Route Leg(CostGrid g, int s, int t, CostParams p, Weights w, out double costS)
        {
            double v0 = p.V0Mps.P50;
            int[] nodes = Search.AStar(g, s, t, v0, p, w, out costS);
            if (nodes == null)
            {
                return Route.NotFound;
            }

            int[] waypoints = Smoothing.Smooth(g, nodes, v0, p, w);
            costS = 0;
            for (int k = 1; k < waypoints.Length; k++)
            {
                costS += Cost.SegmentCostS(g, waypoints[k - 1], waypoints[k], v0, p, w, out _);
            }

            return Cost.Trace(g, waypoints, p);
        }

        /// <summary>
        /// The node nearest to <paramref name="point"/>, already checked to be in the grid:
        /// <c>i = floor((e − origin_e) / cell_m + 0.5)</c>, and the same for <c>j</c>. A point midway
        /// between two nodes goes to the one with the higher index (east or north).
        /// </summary>
        internal static int NearestNode(CostGrid g, EnuPoint point)
        {
            int i = (int)Math.Floor(((point.E - g.OriginEnuM.E) / g.CellM) + 0.5);
            int j = (int)Math.Floor(((point.N - g.OriginEnuM.N) / g.CellM) + 0.5);
            i = Math.Min(Math.Max(i, 0), g.Width - 1);
            j = Math.Min(Math.Max(j, 0), g.Height - 1);
            return (j * g.Width) + i;
        }

        /// <summary>
        /// The reverse Dijkstra field to <paramref name="home"/>, at v₀ P90: each node's way back is
        /// chosen with the amber penalty, and its value is that way's time without it. It is
        /// computed from the node nearest to <paramref name="home"/> (the same rounding as
        /// <see cref="PlanRoute"/>), and <see cref="Field.Home"/> keeps the point as given. With that
        /// node blocked every value is +∞: there is no return, which is a result, not an error. It
        /// visits the whole grid: call it off the main thread.
        /// </summary>
        public static Field ReturnField(CostGrid grid, EnuPoint home, CostParams p)
        {
            RequirePointIn(grid, home, nameof(home));
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            var timeS = new float[grid.Width * grid.Height];
            Search.Field(grid, NearestNode(grid, home), p.V0Mps.P90, p, timeS);
            return Field.Adopt(grid, home, timeS);
        }

        /// <summary>
        /// One route per value of <see cref="Alternative"/>, in that order: always all of them, even
        /// when two coincide (docs/guide/chapters/tablet.md, "Las tres alternativas"). Each is
        /// <see cref="PlanRoute"/>'s A* and smoothing with its own weights, from the same nodes:
        /// fastest is the formula as it is (and exactly <see cref="PlanRoute"/>), safest multiplies
        /// the amber penalty by <c>alt_safest_amber_mult</c>, and gentlest adds
        /// <c>alt_gentlest_s_per_m</c> seconds per metre of height change, climbed plus descended.
        /// The smoothing takes a shortcut only where it does not worsen the alternative's own cost.
        /// Every field comes from <see cref="Cost.Trace"/>, so <c>eta_s</c> has no penalty. With
        /// no route, the three are <see cref="Route.NotFound"/>.
        /// </summary>
        public static IReadOnlyList<Route> Alternatives(CostGrid grid, EnuPoint start, EnuPoint goal, CostParams p)
        {
            RequirePointIn(grid, start, nameof(start));
            RequirePointIn(grid, goal, nameof(goal));
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            int s = NearestNode(grid, start);
            int t = NearestNode(grid, goal);
            var routes = new Route[3];
            for (int a = 0; a < routes.Length; a++)
            {
                routes[a] = Leg(grid, s, t, p, Weights.For((Alternative)a, p), out _);
            }

            return Array.AsReadOnly(routes);
        }

        /// <summary>
        /// The radiation duration limit in seconds: the first <c>t</c> at which the dose absorbed
        /// from <paramref name="tUtc"/>, the start of the EVA, reaches <paramref name="doseUGy"/>.
        /// The rate is piecewise constant: at each instant, <c>value</c> times the product of the
        /// factors valid then, in <c>[valid_from, valid_to)</c>, with a factor below 1 counted as 1
        /// (the Forbush rule). With a constant rate it is <c>dose_uGy / rate · 3600</c>.
        /// </summary>
        public static double DurationLimit(Conditions conditions, DateTime tUtc, double doseUGy)
        {
            if (!(doseUGy >= 0 && double.IsFinite(doseUGy)))
            {
                throw new ArgumentOutOfRangeException(nameof(doseUGy));
            }

            double[] cuts = Cuts(conditions, tUtc);
            double startS = 0;
            double remaining = doseUGy;
            foreach (double endS in cuts)
            {
                double rate = DoseRateUGyH(conditions, tUtc.AddTicks((long)Math.Round(startS * TimeSpan.TicksPerSecond)));
                double pieceDose = rate * (endS - startS) / 3600.0;
                if (remaining <= pieceDose)
                {
                    return startS + (remaining / rate * 3600.0);
                }

                remaining -= pieceDose;
                startS = endS;
            }

            // After the last cut no modifier is valid.
            return startS + (remaining / conditions.ValueUGyH * 3600.0);
        }

        /// <summary>
        /// The dose absorbed in <c>[tUtc, tUtc + durationS)</c>, in uGy: the integral of the same
        /// piecewise-constant rate as <see cref="DurationLimit"/>. It is <c>Plan.dose_uGy</c>, with
        /// <c>total_s.p50</c>.
        /// </summary>
        internal static double DoseUGy(Conditions conditions, DateTime tUtc, double durationS)
        {
            if (!(durationS >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(durationS));
            }

            double startS = 0;
            double dose = 0;
            foreach (double cut in Cuts(conditions, tUtc))
            {
                double endS = Math.Min(cut, durationS);
                if (endS <= startS)
                {
                    break;
                }

                dose += DoseRateUGyH(conditions, tUtc.AddTicks((long)Math.Round(startS * TimeSpan.TicksPerSecond))) * (endS - startS) / 3600.0;
                startS = endS;
            }

            if (startS < durationS)
            {
                dose += DoseRateUGyH(conditions, tUtc.AddTicks((long)Math.Round(startS * TimeSpan.TicksPerSecond))) * (durationS - startS) / 3600.0;
            }

            return dose;
        }

        /// <summary>
        /// The instants after <paramref name="tUtc"/> where the rate can change (every
        /// <c>valid_from</c> and <c>valid_to</c>), in seconds from it, sorted and without repeats.
        /// </summary>
        private static double[] Cuts(Conditions conditions, DateTime tUtc)
        {
            if (conditions == null)
            {
                throw new ArgumentNullException(nameof(conditions));
            }

            Conditions.RequireUtc(tUtc, nameof(tUtc));
            var cuts = new SortedSet<long>();
            foreach (ConditionModifier m in conditions.Modifiers)
            {
                if (m.ValidFrom > tUtc)
                {
                    cuts.Add((m.ValidFrom - tUtc).Ticks);
                }

                if (m.ValidTo > tUtc)
                {
                    cuts.Add((m.ValidTo - tUtc).Ticks);
                }
            }

            var seconds = new double[cuts.Count];
            int k = 0;
            foreach (long ticks in cuts)
            {
                seconds[k++] = (double)ticks / TimeSpan.TicksPerSecond;
            }

            return seconds;
        }

        /// <summary>
        /// Orders the stops and plans every leg, from <paramref name="start"/> to
        /// <c>homeField.Home</c>, against <c>min(budget.t_s, limit_s)</c>, with
        /// <c>limit_s = DurationLimit(conditions, tUtc, budget.dose_uGy)</c>. <c>Plan.dose_uGy</c> is
        /// the dose absorbed from <paramref name="tUtc"/> to <c>tUtc + total_s.p50</c>, the integral of
        /// the same rate as <see cref="DurationLimit"/>. One plan per
        /// <see cref="Alternative"/>, in that order, all with the order of the fastest.
        /// <paramref name="homeField"/> is the <see cref="ReturnField"/> already computed for the
        /// heatmap, used again for <c>return_ok</c>; it throws if it was computed on another grid.
        /// <para>TSP (docs/guide/chapters/tablet.md, "API de Routing"): the order minimises the sum of
        /// the fastest legs' costs (time P50 plus the amber penalty, what A* minimises), the return
        /// to home included; the legs are <see cref="PlanRoute"/>'s, both ways. Held-Karp gives the
        /// optimum exactly, and of the orders within 1e-9 of it the lexicographically smallest by
        /// index in <paramref name="stops"/> wins, so a tie keeps the input order. A stop that cannot
        /// be reached from <paramref name="start"/> goes last, in input order, and its legs are not
        /// found; if home cannot be reached, the open path is ordered. With a leg not found,
        /// <c>total_s</c> and <c>dose_uGy</c> are +∞ and <c>return_ok</c> is false. Otherwise
        /// <c>return_ok</c> says whether, on leaving each stop, the walk at P90 plus every
        /// <c>dwell_s</c> so far plus the field's time back fits (<see cref="ReturnFits"/>); with no
        /// stops there is nothing to leave, so it only says whether the leg home was found, without
        /// the budget. Each alternative has its own legs, totals, dose and <c>return_ok</c>.
        /// With more than <see cref="StopOrder.MaxExact"/> stops reachable from
        /// <paramref name="start"/>, the order is nearest neighbour from the start and then 2-opt over
        /// the same costs, both ways, which is deterministic but not always the optimum.</para>
        /// <para>OP: of the stops reachable from <paramref name="start"/>, the subset with the most
        /// <c>science_value</c> whose fastest plan fits the <c>return_ok</c> rule at every stop, so the
        /// fastest plan's <c>return_ok</c> is true whenever it has a stop; the rest, the unreachable
        /// ones too, go to <c>dropped</c> in input order, and a stop of value 0 is never taken. Each
        /// subset is ordered as in TSP and the rule is asked of that order. Up to
        /// <see cref="StopOrder.MaxExact"/> reachable stops it is exact; on a tie of value, the lower
        /// cost (legs plus <c>dwell_s</c>, within 1e-9), then the subset that holds the stop of lowest
        /// input index where the two differ. Beyond, greedy insertion by value per cost and 2-opt
        /// (<see cref="StopOrder.Choose"/>). <c>order</c> and <c>dropped</c> are the same in the three
        /// plans.</para>
        /// It throws <see cref="ArgumentException"/> for two stops with the same id and
        /// <see cref="ArgumentOutOfRangeException"/> for a <paramref name="mode"/> that is not defined.
        /// It runs one A* per ordered pair of reachable stops, and one per stop from the start: the
        /// time grows with the square of the stops, and it is dominated by the long legs and by the
        /// unreachable stops, each of which costs a search of the whole connected part of the grid
        /// around <paramref name="start"/> (in TSP, again in each other alternative). Call it off the
        /// main thread.
        /// </summary>
        public static IReadOnlyList<Plan> OrderStops(
            CostGrid grid,
            EnuPoint start,
            Stop[] stops,
            Field homeField,
            Budget budget,
            Conditions conditions,
            DateTime tUtc,
            StopMode mode,
            CostParams p)
        {
            RequireFieldOf(grid, homeField);
            RequirePointIn(grid, start, nameof(start));
            if (stops == null)
            {
                throw new ArgumentNullException(nameof(stops));
            }

            foreach (Stop stop in stops)
            {
                if (stop == null)
                {
                    throw new ArgumentException("No stop may be null.", nameof(stops));
                }

                RequirePointIn(grid, stop.EnuM, nameof(stops));
            }

            if (conditions == null)
            {
                throw new ArgumentNullException(nameof(conditions));
            }

            Conditions.RequireUtc(tUtc, nameof(tUtc));
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Stop stop in stops)
            {
                if (!ids.Add(stop.Id))
                {
                    throw new ArgumentException($"Two stops have the id '{stop.Id}'.", nameof(stops));
                }
            }

            if (mode != StopMode.Tsp && mode != StopMode.Op)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            int k = stops.Length;
            int startNode = NearestNode(grid, start);
            int homeNode = NearestNode(grid, homeField.Home);
            var node = new int[k];
            for (int i = 0; i < k; i++)
            {
                node[i] = NearestNode(grid, stops[i].EnuM);
            }

            // The fastest legs, kept by (from, to) with the start as k and home as k + 1.
            var fastest = new Route[k + 2, k + 2];
            var reachable = new List<int>();
            var fromStart = new double[k];
            for (int i = 0; i < k; i++)
            {
                fastest[k, i] = Leg(grid, startNode, node[i], p, Weights.Fastest, out fromStart[i]);
                if (fastest[k, i].Found)
                {
                    reachable.Add(i);
                }
            }

            int m = reachable.Count;
            var between = new double[m, m];
            var toHome = new double[m];
            bool homeReached = true;
            for (int a = 0; a < m; a++)
            {
                int ia = reachable[a];
                fastest[ia, k + 1] = Leg(grid, node[ia], homeNode, p, Weights.Fastest, out toHome[a]);
                homeReached &= fastest[ia, k + 1].Found;
                for (int b = 0; b < m; b++)
                {
                    if (a != b)
                    {
                        fastest[ia, reachable[b]] = Leg(grid, node[ia], node[reachable[b]], p, Weights.Fastest, out between[a, b]);
                    }
                }
            }

            var reachedFromStart = new double[m];
            for (int a = 0; a < m; a++)
            {
                reachedFromStart[a] = fromStart[reachable[a]];
            }

            double limitS = DurationLimit(conditions, tUtc, budget.DoseUGy);
            double availableS = Math.Min(budget.TS, limitS);
            double[] back = homeReached ? toHome : null;
            int[] best;
            if (mode == StopMode.Tsp)
            {
                best = m <= StopOrder.MaxExact
                    ? StopOrder.Order(reachedFromStart, between, back)
                    : StopOrder.Heuristic(reachedFromStart, between, back);
            }
            else
            {
                var value = new int[m];
                var dwell = new double[m];
                for (int a = 0; a < m; a++)
                {
                    value[a] = stops[reachable[a]].ScienceValue;
                    dwell[a] = stops[reachable[a]].DwellS;
                }

                // Only the greedy insertion needs the leg with no stops; the plan reuses it.
                double direct = 0;
                if (m > StopOrder.MaxExact && homeReached)
                {
                    fastest[k, k + 1] = Leg(grid, startNode, homeNode, p, Weights.Fastest, out direct);
                }

                // The return_ok rule of the fastest plan, over the fastest legs of the matrix.
                bool Fits(int[] chosen)
                {
                    double t90 = 0;
                    int fromKey = k;
                    foreach (int a in chosen)
                    {
                        int i = reachable[a];
                        Route r = fastest[fromKey, i];
                        if (r == null || !r.Found)
                        {
                            return false;
                        }

                        t90 += r.EtaS.P90 + stops[i].DwellS;
                        if (!ReturnFits(t90, homeField.TimeSArray[node[i]], availableS))
                        {
                            return false;
                        }

                        fromKey = i;
                    }

                    // The leg home must be found too, or the plan has no return.
                    return chosen.Length == 0 || (fastest[fromKey, k + 1] != null && fastest[fromKey, k + 1].Found);
                }

                best = StopOrder.Choose(reachedFromStart, between, back, direct, value, dwell, Fits);
            }

            // In TSP every stop is in the order, the unreachable ones last; in OP the rest is dropped.
            var inOrder = new bool[k];
            var order = new int[mode == StopMode.Tsp ? k : best.Length];
            for (int a = 0; a < best.Length; a++)
            {
                order[a] = reachable[best[a]];
                inOrder[order[a]] = true;
            }

            int at = best.Length;
            var dropped = new List<string>();
            for (int i = 0; i < k; i++)
            {
                if (inOrder[i])
                {
                    continue;
                }

                if (mode == StopMode.Tsp)
                {
                    order[at++] = i;
                }
                else
                {
                    dropped.Add(stops[i].Id);
                }
            }

            var ordered = new string[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                ordered[i] = stops[order[i]].Id;
            }

            string[] droppedIds = dropped.ToArray();
            var plans = new Plan[3];
            for (int alt = 0; alt < plans.Length; alt++)
            {
                plans[alt] = PlanOf(grid, stops, order, ordered, droppedIds, node, startNode, homeNode, homeField, fastest, Weights.For((Alternative)alt, p), alt == (int)Alternative.Fastest, availableS, limitS, conditions, tUtc, p);
            }

            return Array.AsReadOnly(plans);
        }

        /// <summary>
        /// The plan of one alternative over a fixed <paramref name="order"/>: its legs (reusing the
        /// fastest legs of the matrix for the fastest alternative), totals, dose and
        /// <c>return_ok</c>, as <see cref="OrderStops"/> says.
        /// </summary>
        private static Plan PlanOf(
            CostGrid grid,
            Stop[] stops,
            int[] order,
            string[] ordered,
            string[] dropped,
            int[] node,
            int startNode,
            int homeNode,
            Field homeField,
            Route[,] fastest,
            Weights w,
            bool isFastest,
            double availableS,
            double limitS,
            Conditions conditions,
            DateTime tUtc,
            CostParams p)
        {
            // The matrix keys every stop by its index, with the start as n and home as n + 1.
            int k = order.Length, n = stops.Length;
            var legs = new Route[k + 1];
            double t10 = 0, t50 = 0, t90 = 0;
            bool found = true, fits = true;
            int from = startNode, fromKey = n;
            for (int leg = 0; leg <= k; leg++)
            {
                bool toHome = leg == k;
                int to = toHome ? homeNode : node[order[leg]];
                int toKey = toHome ? n + 1 : order[leg];
                Route r = isFastest ? fastest[fromKey, toKey] : null;
                legs[leg] = r ?? Leg(grid, from, to, p, w, out _);
                found &= legs[leg].Found;
                double dwell = toHome ? 0 : stops[order[leg]].DwellS;
                t10 += legs[leg].EtaS.P10 + dwell;
                t50 += legs[leg].EtaS.P50 + dwell;
                t90 += legs[leg].EtaS.P90 + dwell;
                if (!toHome)
                {
                    fits &= ReturnFits(t90, homeField.TimeSArray[to], availableS);
                }

                from = to;
                fromKey = toKey;
            }

            if (!found)
            {
                return new Plan(ordered, dropped, legs, new Percentiles(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), double.PositiveInfinity, limitS, false);
            }

            return new Plan(ordered, dropped, legs, new Percentiles(t10, t50, t90), DoseUGy(conditions, tUtc, t50), limitS, fits);
        }

        /// <summary>
        /// The <c>return_ok</c> rule at one stop: on leaving it, the time elapsed at P90 (walking
        /// at P90 plus every <c>dwell_s</c> so far) plus the return to <c>home</c> at P90 fits in
        /// <paramref name="availableS"/> = <c>min(budget.t_s, limit_s)</c>. Equality fits.
        /// </summary>
        internal static bool ReturnFits(double elapsedP90S, double backP90S, double availableS) =>
            elapsedP90S + backP90S <= availableS;

        /// <summary>
        /// Throws <see cref="ArgumentException"/> if <paramref name="point"/> is not finite, and
        /// <see cref="ArgumentOutOfRangeException"/> if it is outside the box of the grid's nodes,
        /// <c>[origin, origin + cell_m · (size − 1)]</c> on each axis, edges included.
        /// </summary>
        private static void RequirePointIn(CostGrid grid, EnuPoint point, string name)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (!double.IsFinite(point.E) || !double.IsFinite(point.N))
            {
                throw new ArgumentException("A point must be finite.", name);
            }

            double e = point.E - grid.OriginEnuM.E;
            double n = point.N - grid.OriginEnuM.N;
            if (!(e >= 0 && e <= (double)grid.CellM * (grid.Width - 1) && n >= 0 && n <= (double)grid.CellM * (grid.Height - 1)))
            {
                throw new ArgumentOutOfRangeException(name, "The point is outside the grid.");
            }
        }

        /// <summary>Throws unless <paramref name="field"/> was computed on <paramref name="grid"/>.</summary>
        private static void RequireFieldOf(CostGrid grid, Field field)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (field == null)
            {
                throw new ArgumentNullException("homeField");
            }

            if (!ReferenceEquals(field.Grid, grid))
            {
                throw new ArgumentException("The field was computed on another grid.", "homeField");
            }
        }

        /// <summary>The dose rate valid at <paramref name="tUtc"/>, in uGy/h, with the Forbush rule.</summary>
        internal static double DoseRateUGyH(Conditions conditions, DateTime tUtc)
        {
            if (conditions == null)
            {
                throw new ArgumentNullException(nameof(conditions));
            }

            Conditions.RequireUtc(tUtc, nameof(tUtc));
            double rate = conditions.ValueUGyH;
            foreach (ConditionModifier m in conditions.Modifiers)
            {
                if (m.ValidFrom <= tUtc && tUtc < m.ValidTo)
                {
                    rate *= Math.Max(1.0, m.Factor);
                }
            }

            return rate;
        }
    }
}
