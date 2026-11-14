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
        /// sample of column <c>c = 2i</c> and row <c>r = 256 − 2j</c>. <c>height_m</c> takes that sample
        /// minus <paramref name="originHeightM"/>; <c>slope_deg</c>, <c>roughness_cm</c> and
        /// <c>hazard</c> take the maximum of the 3 × 3 samples centred on it. A node on a
        /// <c>nodata</c> sample has <c>height_m</c> = 0 and <c>hazard</c> = 255.
        /// <para>
        /// Edge rule: each sample of the 3 × 3 window is read from whichever input tile holds it;
        /// neighbouring tiles share their edge samples. A sample that falls in no input tile is
        /// skipped: it is not treated as nodata.
        /// </para>
        /// </summary>
        public static Grid BuildGrid(TileRasters[] tiles, double originHeightM, CostParams p) =>
            throw new NotImplementedException();

        public static Route PlanRoute(Grid grid, EnuPoint start, EnuPoint goal, CostParams p) =>
            throw new NotImplementedException();

        /// <summary>The reverse Dijkstra field to <paramref name="home"/>, at v₀ P90.</summary>
        public static Field ReturnField(Grid grid, EnuPoint home, CostParams p) =>
            throw new NotImplementedException();

        /// <summary>One route per <see cref="Alternative"/>, in that order.</summary>
        public static Route[] Alternatives(Grid grid, EnuPoint start, EnuPoint goal, CostParams p, int k = 3) =>
            throw new NotImplementedException();

        /// <summary>
        /// The radiation duration limit in seconds: <c>dose_uGy / rate · 3600</c>. The rate is
        /// <c>value</c> times the product of the factors valid at <paramref name="tUtc"/>; a factor
        /// below 1 counts as 1 (the Forbush rule).
        /// </summary>
        public static double DurationLimit(Conditions conditions, DateTime tUtc, double doseUGy)
        {
            if (!(doseUGy >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(doseUGy));
            }

            return doseUGy / DoseRateUGyH(conditions, tUtc) * 3600.0;
        }

        /// <summary>
        /// Orders the stops and plans every leg, from <paramref name="start"/> to
        /// <paramref name="home"/>, against <c>min(budget.t_s, limitS)</c>. One plan per
        /// <see cref="Alternative"/>, in that order, all with the order of the fastest.
        /// </summary>
        public static Plan[] OrderStops(
            Grid grid,
            EnuPoint start,
            Stop[] stops,
            EnuPoint home,
            Budget budget,
            double limitS,
            StopMode mode,
            CostParams p) =>
            throw new NotImplementedException();

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
