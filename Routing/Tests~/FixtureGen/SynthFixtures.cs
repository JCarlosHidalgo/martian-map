using System;
using System.IO;
using System.Linq;

namespace MartianMap.Routing.FixtureGen
{
    /// <summary>
    /// Generates the synthetic C4 fixtures from the shared synthetic coordinates
    /// (docs/guide/chapters/convenciones.md, "Cómo se fija un contrato"). It builds the tile
    /// rasters from those formulas and does not read P1's tile. Every time comes from
    /// <see cref="Cost"/>: this generator computes no cost of its own.
    /// </summary>
    public static class SynthFixtures
    {
        // Zone `synth`: one 256 m tile centred on the origin.
        private const int TileOriginE = -128;
        private const int TileOriginN = -128;
        private const double OriginHeightM = 12.5;
        private const int Samples = TileRasters.SizePx;

        // costgrid_synth.bin: 129 × 129 nodes of 2 m, node (0, 0) at [-128, -128].
        private const int Nodes = 129;
        private const float CellM = 2f;

        // mission.json: base [-100, -20]; s1 [0, -20] and s2 [100, -20], value 5 and 8, dwell 600 s.
        private static readonly EnuPoint Base = new EnuPoint(-100, -20);
        private static readonly Stop[] Stops =
        {
            new Stop("s1", new EnuPoint(0, -20), 5, 600),
            new Stop("s2", new EnuPoint(100, -20), 8, 600),
        };

        private static readonly Budget MissionBudget = new Budget(14400, 50);

        // conditions.json: value 6.0 uGy/h with a WSAEnlilSimulations modifier of factor 0.95.
        // The fixture's validity window is not fixed yet; a factor below 1 counts as 1, so the
        // window chosen here does not change any value.
        private static readonly DateTime PlanTimeUtc = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        private static readonly Conditions SynthConditions = new Conditions(6.0, new[]
        {
            new ConditionModifier(
                0.95,
                new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)),
        });

        public sealed class Output
        {
            public Output(byte[] costgrid, string expectedRoute, string expectedPlan)
            {
                Costgrid = costgrid;
                ExpectedRoute = expectedRoute;
                ExpectedPlan = expectedPlan;
            }

            public byte[] Costgrid { get; }

            public string ExpectedRoute { get; }

            public string ExpectedPlan { get; }
        }

        public static Output Generate(CostParams p)
        {
            Grid grid = BuildSynthGrid();

            // expected_route.json: the straight line from [-100, -20] to [100, -20].
            Route route = StraightRoute(grid, p, Base, Stops[1].EnuM);
            var routeJson = new JsonText.Obj { { "schema_version", 1 } };
            routeJson.AddRange(RouteJson(route));

            var plan = PlanJson(BuildPlan(grid, p));
            var planJson = new JsonText.Obj
            {
                { "schema_version", 1 },
                // On the ramp the three alternatives coincide.
                { "plans", new object[] { plan, plan, plan } },
            };

            return new Output(SerializeGrid(grid), JsonText.Write(routeJson), JsonText.Write(planJson));
        }

        /// <summary>The grid that BuildGrid gives for the synthetic tile, with BuildGrid's rule.</summary>
        internal static Grid BuildSynthGrid()
        {
            int n = Samples * Samples;
            var height = new ushort[n];
            var slope = new byte[n];
            var hazard = new byte[n];
            for (int r = 0; r < Samples; r++)
            {
                for (int c = 0; c < Samples; c++)
                {
                    int e = TileOriginE + c;
                    int no = TileOriginN + 256 - r;
                    int k = r * Samples + c;
                    if (In(e, 113, 128) && In(no, -128, -113))
                    {
                        height[k] = TileRasters.HeightNodata;
                        hazard[k] = 255;
                        continue;
                    }

                    // 20 · (e + 128) / 256 + 5 · (n + 128) / 256, at scale 1/1024: exact.
                    height[k] = checked((ushort)((80 * (e + 128)) + (20 * (no + 128))));
                    if (In(e, 40, 100) && In(no, 40, 100))
                    {
                        hazard[k] = 255;
                        slope[k] = 20;
                    }
                    else if (In(e, -100, -60) && In(no, -100, -60))
                    {
                        hazard[k] = 128;
                    }
                }
            }

            var tile = new TileRasters(
                new EnuPoint(TileOriginE, TileOriginN), height, 1.0 / 1024, 0, slope, new byte[n], hazard);

            int count = Nodes * Nodes;
            var heightM = new float[count];
            var slopeDeg = new byte[count];
            var roughnessCm = new byte[count];
            var nodeHazard = new byte[count];
            for (int j = 0; j < Nodes; j++)
            {
                for (int i = 0; i < Nodes; i++)
                {
                    int c = 2 * i;
                    int r = 256 - 2 * j;
                    int node = j * Nodes + i;
                    ushort h = tile.Height[r * Samples + c];
                    bool nodata = h == TileRasters.HeightNodata;
                    heightM[node] = nodata ? 0f : (float)(tile.Offset + tile.Scale * h - OriginHeightM);
                    slopeDeg[node] = Max3x3(tile.Slope, r, c);
                    roughnessCm[node] = Max3x3(tile.Roughness, r, c);
                    nodeHazard[node] = nodata ? (byte)255 : Max3x3(tile.Hazard, r, c);
                }
            }

            return new Grid(Nodes, Nodes, CellM, new EnuPoint(TileOriginE, TileOriginN), heightM, slopeDeg, roughnessCm, nodeHazard);
        }

        /// <summary>The 3 × 3 maximum; samples outside the tile are skipped (BuildGrid's edge rule).</summary>
        private static byte Max3x3(byte[] raster, int r, int c)
        {
            byte max = 0;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int rr = r + dr;
                    int cc = c + dc;
                    if (rr >= 0 && rr < Samples && cc >= 0 && cc < Samples)
                    {
                        max = Math.Max(max, raster[rr * Samples + cc]);
                    }
                }
            }

            return max;
        }

        private static byte[] SerializeGrid(Grid g)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                if (!BitConverter.IsLittleEndian)
                {
                    throw new PlatformNotSupportedException("costgrid_synth.bin is little-endian.");
                }

                w.Write(g.Width);
                w.Write(g.Height);
                w.Write(g.CellM);
                w.Write(g.OriginEnuM.E);
                w.Write(g.OriginEnuM.N);
                foreach (float h in g.HeightM)
                {
                    w.Write(h);
                }

                w.Write(g.SlopeDeg);
                w.Write(g.RoughnessCm);
                w.Write(g.Hazard);
            }

            return ms.ToArray();
        }

        /// <summary>A route along one grid row; both ends must sit exactly on nodes of that row.</summary>
        private static Route StraightRoute(Grid g, CostParams p, EnuPoint from, EnuPoint to)
        {
            (int i0, int j) = NodeAt(g, from);
            (int i1, int j1) = NodeAt(g, to);
            if (j != j1 || i0 == i1)
            {
                throw new ArgumentException("The synthetic route runs along one grid row.");
            }

            int step = Math.Sign(i1 - i0);
            int[] cells = Enumerable.Range(0, Math.Abs(i1 - i0) + 1).Select(k => g.Index(i0 + step * k, j)).ToArray();

            double length = 0, ascent = 0, amber = 0, t10 = 0, t50 = 0, t90 = 0;
            for (int k = 1; k < cells.Length; k++)
            {
                int a = cells[k - 1];
                int b = cells[k];
                double rise = g.HeightM[b] - g.HeightM[a];
                length += Cost.Length3DM(g.CellM, rise);
                ascent += Math.Abs(rise);
                amber += Cost.EdgeAmberM(g, a, b, p);
                t10 += Cost.EdgeTimeS(g, a, b, p.V0Mps.P10, p, withAmberPenalty: false);
                t50 += Cost.EdgeTimeS(g, a, b, p.V0Mps.P50, p, withAmberPenalty: false);
                t90 += Cost.EdgeTimeS(g, a, b, p.V0Mps.P90, p, withAmberPenalty: false);
            }

            if (double.IsInfinity(t50))
            {
                throw new InvalidOperationException("The synthetic route crosses a blocked node.");
            }

            var polyline = cells
                .Select(c => new EnuPoint3(
                    g.OriginEnuM.E + g.CellM * (c % g.Width),
                    g.OriginEnuM.N + g.CellM * (c / g.Width),
                    g.HeightM[c]))
                .ToArray();
            byte maxHazard = cells.Select(c => g.Hazard[c]).Max();
            return new Route(true, cells, polyline, length, ascent, amber, new Percentiles(t10, t50, t90), maxHazard);
        }

        /// <summary>The TSP plan of the synthetic mission, in the given stop order: base → s1 → s2 → base.</summary>
        private static Plan BuildPlan(Grid g, CostParams p)
        {
            double limitS = Router.DurationLimit(SynthConditions, PlanTimeUtc, MissionBudget.DoseUGy);
            double availableS = Math.Min(MissionBudget.TS, limitS);
            double rate = Router.DoseRateUGyH(SynthConditions, PlanTimeUtc);

            var legs = new Route[Stops.Length + 1];
            EnuPoint at = Base;
            double t10 = 0, t50 = 0, t90 = 0;
            bool returnOk = true;
            for (int k = 0; k < Stops.Length; k++)
            {
                legs[k] = StraightRoute(g, p, at, Stops[k].EnuM);
                t10 += legs[k].EtaS.P10 + Stops[k].DwellS;
                t50 += legs[k].EtaS.P50 + Stops[k].DwellS;
                t90 += legs[k].EtaS.P90 + Stops[k].DwellS;
                at = Stops[k].EnuM;

                // The return at P90 must fit in what is left after leaving this stop.
                double backP90 = StraightRoute(g, p, at, Base).EtaS.P90;
                returnOk &= t50 + backP90 <= availableS;
            }

            legs[Stops.Length] = StraightRoute(g, p, at, Base);
            t10 += legs[Stops.Length].EtaS.P10;
            t50 += legs[Stops.Length].EtaS.P50;
            t90 += legs[Stops.Length].EtaS.P90;

            return new Plan(
                Stops.Select(s => s.Id).ToArray(),
                Array.Empty<string>(),
                legs,
                new Percentiles(t10, t50, t90),
                rate * t50 / 3600.0,
                limitS,
                returnOk);
        }

        private static JsonText.Obj RouteJson(Route r) => new JsonText.Obj
        {
            { "found", r.Found },
            { "cells", r.Cells.Select(c => (object)c).ToArray() },
            { "polyline_enu_m", r.PolylineEnuM.Select(v => (object)new object[] { v.E, v.N, v.U }).ToArray() },
            { "length_m", r.LengthM },
            { "ascent_m", r.AscentM },
            { "amber_m", r.AmberM },
            { "eta_s", PercentilesJson(r.EtaS) },
            { "max_hazard", (int)r.MaxHazard },
        };

        private static JsonText.Obj PlanJson(Plan plan) => new JsonText.Obj
        {
            { "order", plan.Order.Select(s => (object)s).ToArray() },
            { "dropped", plan.Dropped.Select(s => (object)s).ToArray() },
            { "legs", plan.Legs.Select(l => (object)RouteJson(l)).ToArray() },
            { "total_s", PercentilesJson(plan.TotalS) },
            { "dose_uGy", plan.DoseUGy },
            { "limit_s", plan.LimitS },
            { "return_ok", plan.ReturnOk },
        };

        private static JsonText.Obj PercentilesJson(Percentiles x) => new JsonText.Obj
        {
            { "p10", x.P10 },
            { "p50", x.P50 },
            { "p90", x.P90 },
        };

        private static (int I, int J) NodeAt(Grid g, EnuPoint pt)
        {
            double fi = (pt.E - g.OriginEnuM.E) / g.CellM;
            double fj = (pt.N - g.OriginEnuM.N) / g.CellM;
            int i = (int)Math.Round(fi);
            int j = (int)Math.Round(fj);
            if (fi != i || fj != j)
            {
                throw new ArgumentException("The synthetic points sit exactly on nodes.");
            }

            g.Index(i, j);
            return (i, j);
        }

        private static bool In(int x, int lo, int hi) => x >= lo && x <= hi;
    }
}
