using System;

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
        /// </summary>
        public static CostGrid BuildGrid(TileRasters[] tiles, double originHeightM, CostParams p) =>
            throw new NotImplementedException();

        public static Route PlanRoute(CostGrid grid, EnuPoint start, EnuPoint goal, CostParams p)
        {
            RequirePointIn(grid, start, nameof(start));
            RequirePointIn(grid, goal, nameof(goal));
            throw new NotImplementedException();
        }

        /// <summary>The reverse Dijkstra field to <paramref name="home"/>, at v₀ P90.</summary>
        public static Field ReturnField(CostGrid grid, EnuPoint home, CostParams p)
        {
            RequirePointIn(grid, home, nameof(home));
            throw new NotImplementedException();
        }

        /// <summary>
        /// One route per value of <see cref="Alternative"/>, in that order: always all of them, even
        /// when two coincide.
        /// </summary>
        public static Route[] Alternatives(CostGrid grid, EnuPoint start, EnuPoint goal, CostParams p)
        {
            RequirePointIn(grid, start, nameof(start));
            RequirePointIn(grid, goal, nameof(goal));
            throw new NotImplementedException();
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
            if (!(doseUGy >= 0))
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
            var cuts = new System.Collections.Generic.SortedSet<long>();
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
        /// </summary>
        public static Plan[] OrderStops(
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

            throw new NotImplementedException();
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
