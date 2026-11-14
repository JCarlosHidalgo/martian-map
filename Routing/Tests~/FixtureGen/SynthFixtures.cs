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

        // costgrid_synth.bin: 129 × 129 nodes of 2 m, node (0, 0) at [-128, -128]: BuildGrid's grid of the one tile.

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
            CostGrid grid = BuildSynthGrid(p);

            // expected_route.json: PlanRoute from [-100, -20] to [100, -20]; on the ramp it is the straight row.
            Route route = PlannedRoute(grid, p, Base, Stops[1].EnuM);
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

        /// <summary>The synthetic tile's rasters, from the formulas, reduced by <see cref="Router.BuildGrid"/>.</summary>
        public static CostGrid BuildSynthGrid(CostParams p)
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

            return Router.BuildGrid(new[] { tile }, OriginHeightM, p);
        }

        private static byte[] SerializeGrid(CostGrid g)
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

        /// <summary>
        /// The route of <see cref="Router.PlanRoute"/> (A* + smoothing), which must be found: the
        /// synthetic points are free nodes of the ramp.
        /// </summary>
        private static Route PlannedRoute(CostGrid g, CostParams p, EnuPoint from, EnuPoint to)
        {
            Route route = Router.PlanRoute(g, from, to, p);
            if (!route.Found)
            {
                throw new InvalidOperationException("The synthetic route was not found.");
            }

            return route;
        }

        /// <summary>The TSP plan of the synthetic mission, in the given stop order: base → s1 → s2 → base, every leg from PlanRoute. Until OrderStops exists the order, totals and return_ok are computed here.</summary>
        private static Plan BuildPlan(CostGrid g, CostParams p)
        {
            double limitS = Router.DurationLimit(SynthConditions, PlanTimeUtc, MissionBudget.DoseUGy);
            double availableS = Math.Min(MissionBudget.TS, limitS);

            var legs = new Route[Stops.Length + 1];
            EnuPoint at = Base;
            double t10 = 0, t50 = 0, t90 = 0;
            bool returnOk = true;
            for (int k = 0; k < Stops.Length; k++)
            {
                legs[k] = PlannedRoute(g, p, at, Stops[k].EnuM);
                t10 += legs[k].EtaS.P10 + Stops[k].DwellS;
                t50 += legs[k].EtaS.P50 + Stops[k].DwellS;
                t90 += legs[k].EtaS.P90 + Stops[k].DwellS;
                at = Stops[k].EnuM;

                // On leaving this stop, the elapsed time at P90 plus the return at P90 must fit.
                double backP90 = PlannedRoute(g, p, at, Base).EtaS.P90;
                returnOk &= Router.ReturnFits(t90, backP90, availableS);
            }

            legs[Stops.Length] = PlannedRoute(g, p, at, Base);
            t10 += legs[Stops.Length].EtaS.P10;
            t50 += legs[Stops.Length].EtaS.P50;
            t90 += legs[Stops.Length].EtaS.P90;

            return new Plan(
                Stops.Select(s => s.Id).ToArray(),
                Array.Empty<string>(),
                legs,
                new Percentiles(t10, t50, t90),
                Router.DoseUGy(SynthConditions, PlanTimeUtc, t50),
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

        private static bool In(int x, int lo, int hi) => x >= lo && x <= hi;
    }
}
