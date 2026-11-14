using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The walking cost formula (D6). This is its only implementation: no other environment,
    /// script or test computes cost.
    /// <code>t = length_3D / [v₀ · exp(−b · |s + c|) · f_rug · f_terreno] + amber penalty</code>
    /// </summary>
    internal static class Cost
    {
        /// <summary>A node is blocked by slope, local relief or red hazard (nodata carries hazard 255).</summary>
        internal static bool IsBlocked(byte slopeDeg, byte reliefCm, byte hazard, CostParams p) =>
            slopeDeg > p.MaxSlopeDeg || reliefCm > p.ReliefBlockCm || hazard >= p.HazardRedMin;

        internal static bool IsAmber(byte hazard, CostParams p) =>
            hazard >= p.HazardAmberMin && hazard < p.HazardRedMin;

        /// <summary>1 up to <c>relief_penalty_from_cm</c>, then linear down to <c>f_rug_min</c> at <c>relief_block_cm</c>.</summary>
        internal static double FRug(byte reliefCm, CostParams p)
        {
            if (reliefCm <= p.ReliefPenaltyFromCm)
            {
                return 1.0;
            }

            if (reliefCm >= p.ReliefBlockCm)
            {
                return p.FRugMin;
            }

            double t = (reliefCm - p.ReliefPenaltyFromCm) / (p.ReliefBlockCm - p.ReliefPenaltyFromCm);
            return 1.0 - t * (1.0 - p.FRugMin);
        }

        /// <summary>
        /// Walking speed on grade <paramref name="grade"/> (rise over horizontal length, no unit),
        /// capped at <c>walk_ceiling_mps</c>. Uses <c>b</c> P50.
        /// </summary>
        internal static double SpeedMps(double grade, byte reliefCm, double v0Mps, CostParams p)
        {
            double v = v0Mps * Math.Exp(-p.B.P50 * Math.Abs(grade + p.C)) * FRug(reliefCm, p) * p.FTerreno;
            return Math.Min(v, p.WalkCeilingMps);
        }

        internal static double Length3DM(double horizontalM, double riseM) =>
            Math.Sqrt(horizontalM * horizontalM + riseM * riseM);

        /// <summary>
        /// Time to walk the edge from node <paramref name="a"/> to node <paramref name="b"/>, in
        /// seconds; <see cref="double.PositiveInfinity"/> if either end is blocked. Each node owns
        /// half the edge: its half takes that node's relief and, with
        /// <paramref name="withAmberPenalty"/>, the amber penalty if the node is amber. The grade is
        /// the height difference between the nodes over their horizontal distance.
        /// </summary>
        internal static double EdgeTimeS(Grid g, int a, int b, double v0Mps, CostParams p, bool withAmberPenalty)
        {
            if (IsBlockedNode(g, a, p) || IsBlockedNode(g, b, p))
            {
                return double.PositiveInfinity;
            }

            double horizontalM = HorizontalM(g, a, b);
            double riseM = g.HeightMArray[b] - g.HeightMArray[a];
            double grade = riseM / horizontalM;
            double halfM = Length3DM(horizontalM, riseM) / 2;

            double t = halfM / SpeedMps(grade, g.RoughnessCmArray[a], v0Mps, p)
                + halfM / SpeedMps(grade, g.RoughnessCmArray[b], v0Mps, p);
            if (withAmberPenalty)
            {
                t += AmberPenaltyS(halfM, g.HazardArray[a], p) + AmberPenaltyS(halfM, g.HazardArray[b], p);
            }

            return t;
        }

        /// <summary>Metres of the edge walked in amber cells, with the same half-edge rule as <see cref="EdgeTimeS"/>.</summary>
        internal static double EdgeAmberM(Grid g, int a, int b, CostParams p)
        {
            double horizontalM = HorizontalM(g, a, b);
            double halfM = Length3DM(horizontalM, g.HeightMArray[b] - g.HeightMArray[a]) / 2;
            return (IsAmber(g.HazardArray[a], p) ? halfM : 0) + (IsAmber(g.HazardArray[b], p) ? halfM : 0);
        }

        internal static bool IsBlockedNode(Grid g, int node, CostParams p) =>
            IsBlocked(g.SlopeDegArray[node], g.RoughnessCmArray[node], g.HazardArray[node], p);

        private static double AmberPenaltyS(double metres, byte hazard, CostParams p) =>
            IsAmber(hazard, p) ? metres * p.AmberPenaltySPerM : 0;

        private static double HorizontalM(Grid g, int a, int b)
        {
            int di = (a % g.Width) - (b % g.Width);
            int dj = (a / g.Width) - (b / g.Width);
            if (di == 0 && dj == 0)
            {
                throw new ArgumentException("An edge joins two different nodes.");
            }

            return g.CellM * Math.Sqrt(di * di + dj * dj);
        }
    }
}
