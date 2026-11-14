using System;
using System.Collections.Generic;

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
        /// Walking speed on grade <paramref name="grade"/> (rise over horizontal length, no unit).
        /// Uses <c>b</c> P50. Every factor is at most 1, so it never exceeds <paramref name="v0Mps"/>;
        /// <c>walk_ceiling_mps</c> bounds v0 in <see cref="CostParams"/>, it is not a cap here.
        /// </summary>
        internal static double SpeedMps(double grade, byte reliefCm, double v0Mps, CostParams p) =>
            v0Mps * Math.Exp(-p.B.P50 * Math.Abs(grade + p.C)) * FRug(reliefCm, p) * p.FTerreno;

        internal static double Length3DM(double horizontalM, double riseM) =>
            Math.Sqrt(horizontalM * horizontalM + riseM * riseM);

        /// <summary>
        /// Time to walk the straight segment from node <paramref name="a"/> to node
        /// <paramref name="b"/>, in seconds; <see cref="double.PositiveInfinity"/> if it touches a
        /// blocked node. The same rule serves an A* edge of any of the 16 moves and a shortcut of
        /// the smoothing: see <see cref="Walk"/>.
        /// </summary>
        internal static double SegmentTimeS(CostGrid g, int a, int b, double v0Mps, CostParams p, bool withAmberPenalty) =>
            Walk(g, a, b, v0Mps, p, withAmberPenalty, null).TimeS;

        /// <summary>
        /// Metres of the segment walked in amber nodes' cells, with the same rule as
        /// <see cref="SegmentTimeS"/>; <see cref="double.PositiveInfinity"/> if it is blocked.
        /// </summary>
        internal static double SegmentAmberM(CostGrid g, int a, int b, CostParams p) =>
            Walk(g, a, b, p.V0Mps.P50, p, withAmberPenalty: false, null).AmberM;

        internal static bool IsBlockedNode(CostGrid g, int node, CostParams p) =>
            IsBlocked(g.SlopeDegArray[node], g.RoughnessCmArray[node], g.HazardArray[node], p);

        /// <summary>
        /// The <see cref="Route"/> through <paramref name="waypoints"/>, node indices joined by
        /// straight segments with the rule of <see cref="Walk"/>; <see cref="Route.NotFound"/> if a
        /// segment is blocked. <c>cells</c> are the nodes whose cell the route crosses, in order and
        /// without consecutive repeats; <c>polyline_enu_m</c> has the start, every crossing of a line
        /// of nodes, every waypoint and the goal, with the bilinear height; <c>max_hazard</c> is the
        /// maximum over <c>cells</c>. <c>eta_s</c> never includes penalties.
        /// </summary>
        internal static Route Trace(CostGrid g, int[] waypoints, CostParams p)
        {
            if (waypoints == null)
            {
                throw new ArgumentNullException(nameof(waypoints));
            }

            if (waypoints.Length < 2)
            {
                throw new ArgumentException("A route has at least two waypoints.", nameof(waypoints));
            }

            var tracer = new Tracer();
            int w0 = waypoints[0];
            if ((uint)w0 >= (uint)(g.Width * g.Height))
            {
                throw new ArgumentOutOfRangeException(nameof(waypoints), "Node outside the grid.");
            }

            tracer.Points.Add(NodePoint(g, w0));
            double length = 0, ascent = 0, amber = 0, t10 = 0, t50 = 0, t90 = 0;
            for (int k = 1; k < waypoints.Length; k++)
            {
                int a = waypoints[k - 1];
                int b = waypoints[k];
                Segment m50 = Walk(g, a, b, p.V0Mps.P50, p, withAmberPenalty: false, tracer);
                if (double.IsPositiveInfinity(m50.TimeS))
                {
                    return Route.NotFound;
                }

                t10 += Walk(g, a, b, p.V0Mps.P10, p, withAmberPenalty: false, null).TimeS;
                t50 += m50.TimeS;
                t90 += Walk(g, a, b, p.V0Mps.P90, p, withAmberPenalty: false, null).TimeS;
                length += m50.LengthM;
                ascent += m50.AscentM;
                amber += m50.AmberM;
            }

            return new Route(
                true,
                tracer.Cells.ToArray(),
                tracer.Points.ToArray(),
                length,
                ascent,
                amber,
                new Percentiles(t10, t50, t90),
                tracer.MaxHazard);
        }

        /// <summary>
        /// Walks the segment from the centre of node <paramref name="a"/> to the centre of node
        /// <paramref name="b"/> (docs/guide/chapters/tablet.md, "Costo de caminata (D6)"):
        /// <list type="bullet">
        /// <item>Each node owns its cell, the closed square of side <c>cell_m</c> centred on it. The
        /// segment is cut exactly where it crosses a cell edge, and each piece takes the relief and
        /// the hazard of its cell's node.</item>
        /// <item>The segment is blocked if it touches a blocked cell: passing exactly through a
        /// cell corner touches the four cells of that corner.</item>
        /// <item>The height between nodes is the bilinear interpolation of <c>height_m</c>. It is
        /// also cut where it crosses a line of nodes, where the interpolation bends, and each piece
        /// takes its own grade: its rise over its horizontal length.</item>
        /// </list>
        /// The cuts are found in exact integer arithmetic. Along one row or column this is the
        /// half-edge rule: half the edge per node, with the grade of the whole edge. With a
        /// <paramref name="tracer"/>, it also records the cells and the points of
        /// <see cref="Trace"/>; the segment's first point is not recorded, it is the previous end.
        /// </summary>
        private static Segment Walk(CostGrid g, int a, int b, double v0Mps, CostParams p, bool withAmberPenalty, Tracer tracer)
        {
            int count = g.Width * g.Height;
            if ((uint)a >= (uint)count || (uint)b >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(a), "Node outside the grid.");
            }

            if (a == b)
            {
                throw new ArgumentException("A segment joins two different nodes.");
            }

            int ia = a % g.Width, ja = a / g.Width;
            int dx = (b % g.Width) - ia, dy = (b / g.Width) - ja;
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            long nx = 2L * Math.Abs(dx), ny = 2L * Math.Abs(dy);
            double horizontalM = g.CellM * Math.Sqrt((double)dx * dx + (double)dy * dy);
            float ha = g.HeightMArray[a];

            // Events along x are at t = kx / nx and along y at t = ky / ny, for 0 < t < 1. An odd k
            // crosses a cell edge; an even k crosses a line of nodes.
            long kx = 1, ky = 1;
            int ci = ia, cj = ja;
            double tPrev = 0, hPrev = 0;
            var m = new Segment();
            while (true)
            {
                bool hasX = kx < nx, hasY = ky < ny;
                double tNext;
                bool stepX = false, stepY = false;
                if (!hasX && !hasY)
                {
                    tNext = 1;
                }
                else
                {
                    // Compare kx / nx with ky / ny without division.
                    long cmp = !hasY ? -1 : !hasX ? 1 : (kx * ny) - (ky * nx);
                    stepX = cmp <= 0;
                    stepY = cmp >= 0;
                    tNext = stepX ? (double)kx / nx : (double)ky / ny;
                }

                double hNext = tNext == 1 ? (double)(g.HeightMArray[b] - ha) : RelativeHeight(g, ha, ia + tNext * dx, ja + tNext * dy);
                int cell = cj * g.Width + ci;
                if (IsBlockedNode(g, cell, p))
                {
                    return Segment.Blocked;
                }

                double pieceH = (tNext - tPrev) * horizontalM;
                double rise = hNext - hPrev;
                double pieceM = Length3DM(pieceH, rise);
                bool amber = IsAmber(g.HazardArray[cell], p);
                m.TimeS += pieceM / SpeedMps(rise / pieceH, g.RoughnessCmArray[cell], v0Mps, p);
                if (withAmberPenalty && amber)
                {
                    m.TimeS += pieceM * p.AmberPenaltySPerM;
                }

                m.LengthM += pieceM;
                m.AscentM += Math.Abs(rise);
                m.AmberM += amber ? pieceM : 0;
                tracer?.Cell(cell, g.HazardArray[cell]);

                if (tNext == 1)
                {
                    tracer?.Points.Add(NodePoint(g, b));
                    return m;
                }

                bool edgeX = stepX && (kx & 1) == 1;
                bool edgeY = stepY && (ky & 1) == 1;
                if (edgeX && edgeY)
                {
                    // Exactly through a cell corner: the two side cells are touched too.
                    if (IsBlockedNode(g, cj * g.Width + ci + sx, p) || IsBlockedNode(g, (cj + sy) * g.Width + ci, p))
                    {
                        return Segment.Blocked;
                    }
                }

                if (tracer != null && ((stepX && (kx & 1) == 0) || (stepY && (ky & 1) == 0)))
                {
                    // A crossing of a line of nodes: a point of the polyline.
                    double x = ia + tNext * dx, y = ja + tNext * dy;
                    tracer.Points.Add(new EnuPoint3(
                        g.OriginEnuM.E + g.CellM * x,
                        g.OriginEnuM.N + g.CellM * y,
                        Height(g, x, y)));
                }

                ci += edgeX ? sx : 0;
                cj += edgeY ? sy : 0;
                kx += stepX ? 1 : 0;
                ky += stepY ? 1 : 0;
                tPrev = tNext;
                hPrev = hNext;
            }
        }

        private static EnuPoint3 NodePoint(CostGrid g, int node) => new EnuPoint3(
            g.OriginEnuM.E + g.CellM * (node % g.Width),
            g.OriginEnuM.N + g.CellM * (node / g.Width),
            g.HeightMArray[node]);

        /// <summary>The bilinear height at <c>(x, y)</c>, in node units: exactly <c>height_m</c> on a node.</summary>
        private static double Height(CostGrid g, double x, double y)
        {
            int i0 = Math.Min((int)Math.Floor(x), Math.Max(g.Width - 2, 0));
            int j0 = Math.Min((int)Math.Floor(y), Math.Max(g.Height - 2, 0));
            double fx = x - i0, fy = y - j0;
            double h = 0;
            h += Corner(g, 0f, i0, j0, (1 - fx) * (1 - fy));
            h += Corner(g, 0f, i0 + 1, j0, fx * (1 - fy));
            h += Corner(g, 0f, i0, j0 + 1, (1 - fx) * fy);
            h += Corner(g, 0f, i0 + 1, j0 + 1, fx * fy);
            return h;
        }

        /// <summary>
        /// The bilinear height at <c>(x, y)</c>, in node units, minus <paramref name="ha"/>. The
        /// differences are taken in <c>float</c>, like the heights, so that along a row it is
        /// exactly the half-edge rule.
        /// </summary>
        private static double RelativeHeight(CostGrid g, float ha, double x, double y)
        {
            int i0 = Math.Min((int)Math.Floor(x), Math.Max(g.Width - 2, 0));
            int j0 = Math.Min((int)Math.Floor(y), Math.Max(g.Height - 2, 0));
            double fx = x - i0, fy = y - j0;
            double h = 0;
            h += Corner(g, ha, i0, j0, (1 - fx) * (1 - fy));
            h += Corner(g, ha, i0 + 1, j0, fx * (1 - fy));
            h += Corner(g, ha, i0, j0 + 1, (1 - fx) * fy);
            h += Corner(g, ha, i0 + 1, j0 + 1, fx * fy);
            return h;
        }

        private static double Corner(CostGrid g, float ha, int i, int j, double weight) =>
            weight == 0 ? 0 : weight * (g.HeightMArray[j * g.Width + i] - ha);

        /// <summary>What <see cref="Walk"/> measures on one segment; every field is +∞ if it is blocked.</summary>
        private struct Segment
        {
            public static readonly Segment Blocked = new Segment
            {
                TimeS = double.PositiveInfinity,
                LengthM = double.PositiveInfinity,
                AscentM = double.PositiveInfinity,
                AmberM = double.PositiveInfinity,
            };

            public double TimeS;
            public double LengthM;
            public double AscentM;
            public double AmberM;
        }

        /// <summary>What <see cref="Trace"/> collects along the route.</summary>
        private sealed class Tracer
        {
            public readonly List<int> Cells = new List<int>();
            public readonly List<EnuPoint3> Points = new List<EnuPoint3>();
            public byte MaxHazard;

            public void Cell(int cell, byte hazard)
            {
                if (Cells.Count == 0 || Cells[Cells.Count - 1] != cell)
                {
                    Cells.Add(cell);
                }

                MaxHazard = Math.Max(MaxHazard, hazard);
            }
        }
    }
}
