using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MartianMap.Routing
{
    /// <summary>
    /// The E5 benchmark of the routing core. Plain C#, run with <c>dotnet</c> on the laptop
    /// (<c>Routing/Tests~/BenchmarkRunner</c>); the tablet is not tested.
    /// </summary>
    public static class Benchmark
    {
        private const double RouteP95Ms = 100;
        private const double FieldP95Ms = 2000;
        private const double MaxManagedMb = 90;

        /// <summary>The verdict of the rows E5 does not measure (it measures the core).</summary>
        private const string OutsideE5 = "outside E5";

        private static readonly DateTime OrderStopsTimeUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Generates the seeded grids of E5, runs its cases and returns the results table, the
        /// text of <c>Routing/BENCHMARK.md</c>. <paramref name="log"/> receives each line as it comes.
        /// Per size, ascending (1000² and 2000²): an A* row of 500 m routes for each terrain (1 warm-up
        /// + 5 runs), then a field row (1 + 1); after every terrain's E5 rows, the rows outside E5's
        /// criterion: an
        /// <see cref="Router.Alternatives"/> row on the A* pairs and, on 1000², an
        /// <see cref="Router.OrderStops"/> row of 12 stops (1 run). It runs on the laptop; the tablet is
        /// not tested. The memory is the managed heap with the grid and the search's workspace alive.
        /// </summary>
        public static string Run(Action<string> log) => Run(log, BenchmarkConfig.E5);

        internal static string Run(Action<string> log, BenchmarkConfig c)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            if (c == null)
            {
                throw new ArgumentNullException(nameof(c));
            }

            var text = new StringBuilder();
            void Line(string line)
            {
                text.Append(line).Append('\n');
                log(line);
            }

            Line("# Routing benchmark (E5)");
            Line(string.Empty);
            Line($"- Scenario: {c.Label}");
            Line($"- Machine: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}, {Environment.ProcessorCount} logical CPUs");
            Line($"- Config: seed 0x{c.Seed:X}; A* routes of {G(c.RouteM)} m on {Join(c.Sizes)}², {c.RouteWarmUp} warm-up + {c.RouteRuns} runs; "
                + $"ReturnField (v0 P90) on {Join(c.FieldSizes)}², {c.FieldWarmUp} + {c.FieldRuns}; "
                + "the A* rows time PlanRoute (A* + smoothing + trace), and 'smooth ms' is the p50 of the smoothing alone on the same pairs; "
                + "outside E5, Alternatives on the same pairs, and OrderStops (TSP) on "
                + $"{Join(c.OrderStopsSizes)}² with {c.OrderStopsK} free stops within {G(c.OrderStopsRadiusM)} m of the field row's home, which is also the start, "
                + "and the field of that home (untimed), 1 run, where 'order ms' is Held-Karp alone on the straight-line distances of the same points"
                + (c.StressFieldSize > 0 ? $"; stress field on Uniform {c.StressFieldSize}², 1 + 1, outside the criterion" : string.Empty));
            Line($"- E5 (docs/guide/chapters/reparto.md): A* p95 <= {F(RouteP95Ms)} ms, field p95 <= {F(FieldP95Ms)} ms, 0 GC during the measured runs, managed heap <= {F(MaxManagedMb)} MB. "
                + "Measured on the laptop only: the tablet is not tested.");
            Line(string.Empty);
            Line("| size | terrain | query | runs | found | p50 ms | p95 ms | max ms | smooth ms | order ms | GC | managed MB | E5 |");
            Line("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            Terrain[] terrains = (Terrain[])Enum.GetValues(typeof(Terrain));
            int maxSize = 0;
            foreach (int s in c.Sizes)
            {
                maxSize = Math.Max(maxSize, s);
            }

            foreach (int s in c.FieldSizes)
            {
                maxSize = Math.Max(maxSize, s);
            }

            foreach (int s in c.OrderStopsSizes)
            {
                maxSize = Math.Max(maxSize, s);
            }

            maxSize = Math.Max(maxSize, c.StressFieldSize);
            for (int size = 1; size <= maxSize; size++)
            {
                bool route = Array.IndexOf(c.Sizes, size) >= 0;
                bool field = Array.IndexOf(c.FieldSizes, size) >= 0;
                bool stress = size == c.StressFieldSize;
                bool order = Array.IndexOf(c.OrderStopsSizes, size) >= 0;
                if (!route && !field && !stress && !order)
                {
                    continue;
                }

                // The E5 rows of every terrain first: the rows outside E5 search further and grow the
                // per-thread workspace, which stays, so they must not run before an E5 row.
                foreach (Terrain t in terrains)
                {
                    if (!route && !field && !(stress && t == Terrain.Uniform))
                    {
                        continue;
                    }

                    CostGrid g = BenchmarkGrids.Make(t, size, GridSeed(c.Seed, size, t));
                    if (route)
                    {
                        Line(RouteRow(g, t, c));
                    }

                    if (field || (stress && t == Terrain.Uniform))
                    {
                        Line(FieldRow(g, t, c, stress && !field));
                    }

                    GC.KeepAlive(g);
                }

                foreach (Terrain t in terrains)
                {
                    if (!route && !order)
                    {
                        continue;
                    }

                    CostGrid g = BenchmarkGrids.Make(t, size, GridSeed(c.Seed, size, t));
                    if (route)
                    {
                        Line(AlternativesRow(g, t, c));
                    }

                    if (order)
                    {
                        Line(OrderStopsRow(g, t, c));
                    }

                    GC.KeepAlive(g);
                }
            }

            return text.ToString();
        }

        private static ulong GridSeed(ulong seed, int size, Terrain t) =>
            new SplitMix64(seed ^ ((ulong)size << 8) ^ (ulong)t).Next();

        /// <summary>The A* rows' pairs (warm-up first), the same for the A* and the Alternatives rows; false if a pair cannot be drawn.</summary>
        private static bool Pairs(CostGrid g, Terrain t, BenchmarkConfig c, out EnuPoint[] starts, out EnuPoint[] goals)
        {
            int total = c.RouteWarmUp + c.RouteRuns;
            starts = new EnuPoint[total];
            goals = new EnuPoint[total];
            var rng = new SplitMix64(GridSeed(c.Seed, g.Width, t) ^ 0x5041495253UL);
            for (int k = 0; k < total; k++)
            {
                if (!Pair(g, c, rng, out starts[k], out goals[k]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string RouteRow(CostGrid g, Terrain t, BenchmarkConfig c)
        {
            if (!Pairs(g, t, c, out EnuPoint[] starts, out EnuPoint[] goals))
            {
                return $"| {g.Width} | {t} | A* {G(c.RouteM)} m | 0 | 0 | | | | | | | | no pair |";
            }

            for (int k = 0; k < c.RouteWarmUp; k++)
            {
                Router.PlanRoute(g, starts[k], goals[k], c.Params);
            }

            var ms = new double[c.RouteRuns];
            int found = 0;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = Collections();
            for (int k = 0; k < c.RouteRuns; k++)
            {
                long t0 = Stopwatch.GetTimestamp();
                Route r = Router.PlanRoute(g, starts[c.RouteWarmUp + k], goals[c.RouteWarmUp + k], c.Params);
                ms[k] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                found += r.Found ? 1 : 0;
            }

            int gcs = Collections() - gc0;
            double mb = GC.GetTotalMemory(true) / (double)(1 << 20);
            GC.KeepAlive(g);
            string smooth = F(SmoothP50Ms(g, starts, goals, c));
            return Row(g.Width, t, $"A* {G(c.RouteM)} m", ms, smooth, "-", found, gcs, mb, RouteP95Ms, null);
        }

        /// <summary>
        /// <see cref="Router.Alternatives"/> on the A* row's pairs, with the same warm-up and runs:
        /// three routes per call. Found counts the calls whose fastest route was found.
        /// </summary>
        private static string AlternativesRow(CostGrid g, Terrain t, BenchmarkConfig c)
        {
            if (!Pairs(g, t, c, out EnuPoint[] starts, out EnuPoint[] goals))
            {
                return $"| {g.Width} | {t} | Alternatives | 0 | 0 | | | | | | | | no pair |";
            }

            for (int k = 0; k < c.RouteWarmUp; k++)
            {
                Router.Alternatives(g, starts[k], goals[k], c.Params);
            }

            var ms = new double[c.RouteRuns];
            int found = 0;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = Collections();
            for (int k = 0; k < c.RouteRuns; k++)
            {
                long t0 = Stopwatch.GetTimestamp();
                IReadOnlyList<Route> alts = Router.Alternatives(g, starts[c.RouteWarmUp + k], goals[c.RouteWarmUp + k], c.Params);
                ms[k] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                found += alts[(int)Alternative.Fastest].Found ? 1 : 0;
            }

            int gcs = Collections() - gc0;
            double mb = GC.GetTotalMemory(true) / (double)(1 << 20);
            GC.KeepAlive(g);
            return Row(g.Width, t, "Alternatives", ms, "-", "-", found, gcs, mb, double.PositiveInfinity, OutsideE5);
        }

        private static string FieldRow(CostGrid g, Terrain t, BenchmarkConfig c, bool stress)
        {
            int home = HomeNode(g, c.Params);
            int warmUp = stress ? 1 : c.FieldWarmUp;
            int runs = stress ? 1 : c.FieldRuns;
            Field field = null;
            if (home < 0)
            {
                return $"| {g.Width} | {t} | ReturnField | 0 | 0 | | | | | | | | no home |";
            }

            EnuPoint homePoint = NodePoint(g, home);
            for (int k = 0; k < warmUp; k++)
            {
                field = Router.ReturnField(g, homePoint, c.Params);
            }

            var ms = new double[runs];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = Collections();
            for (int k = 0; k < runs; k++)
            {
                long t0 = Stopwatch.GetTimestamp();
                field = Router.ReturnField(g, homePoint, c.Params);
                ms[k] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }

            int gcs = Collections() - gc0;
            double mb = GC.GetTotalMemory(true) / (double)(1 << 20);
            GC.KeepAlive(field);
            GC.KeepAlive(g);
            return Row(g.Width, t, "ReturnField", ms, "-", "-", runs, gcs, mb, FieldP95Ms, stress ? "stress" : null);
        }

        /// <summary>
        /// One <see cref="Router.OrderStops"/> call in TSP mode, after the A* rows warmed the code
        /// up: <c>OrderStopsK</c> free stops drawn uniformly in a disc of <c>OrderStopsRadiusM</c>
        /// around the field row's home, which is also the start, with the field of that home
        /// (computed here, untimed). Found is 1 if every leg of the fastest plan was found. 'order ms'
        /// times <see cref="StopOrder.Order"/> alone, on the straight-line distances between the same
        /// points: its work, <c>2^k · k²</c>, does not depend on the costs.
        /// </summary>
        private static string OrderStopsRow(CostGrid g, Terrain t, BenchmarkConfig c)
        {
            string query = $"OrderStops k={c.OrderStopsK}";
            int home = HomeNode(g, c.Params);
            if (home < 0)
            {
                return $"| {g.Width} | {t} | {query} | 0 | 0 | | | | | | | | no home |";
            }

            EnuPoint homePoint = NodePoint(g, home);
            Field field = Router.ReturnField(g, homePoint, c.Params);
            var rng = new SplitMix64(GridSeed(c.Seed, g.Width, t) ^ 0x53544F5053UL);
            var stops = new Stop[c.OrderStopsK];
            for (int k = 0; k < stops.Length; k++)
            {
                if (!StopNear(g, c, rng, home, out EnuPoint at))
                {
                    return $"| {g.Width} | {t} | {query} | 0 | 0 | | | | | | | | no stops |";
                }

                stops[k] = new Stop("s" + k.ToString(CultureInfo.InvariantCulture), at, 1, 0);
            }

            var budget = new Budget(14400, 50);
            var conditions = new Conditions(6.0, Array.Empty<ConditionModifier>());
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = Collections();
            long t0 = Stopwatch.GetTimestamp();
            IReadOnlyList<Plan> plans = Router.OrderStops(g, homePoint, stops, field, budget, conditions, OrderStopsTimeUtc, StopMode.Tsp, c.Params);
            var ms = new[] { (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency };
            int gcs = Collections() - gc0;
            double mb = GC.GetTotalMemory(true) / (double)(1 << 20);
            int found = 1;
            foreach (Route leg in plans[(int)Alternative.Fastest].Legs)
            {
                found = leg.Found ? found : 0;
            }

            string order = F(HeldKarpMs(homePoint, stops));
            GC.KeepAlive(field);
            GC.KeepAlive(g);
            return Row(g.Width, t, query, ms, "-", order, found, gcs, mb, double.PositiveInfinity, OutsideE5);
        }

        /// <summary>
        /// The time of <see cref="StopOrder.Order"/> alone, on the straight-line distances between
        /// <paramref name="home"/> (start and end) and <paramref name="stops"/>.
        /// </summary>
        private static double HeldKarpMs(EnuPoint home, Stop[] stops)
        {
            int m = stops.Length;
            double D(EnuPoint a, EnuPoint b) => Math.Sqrt(((a.E - b.E) * (a.E - b.E)) + ((a.N - b.N) * (a.N - b.N)));
            var fromStart = new double[m];
            var between = new double[m, m];
            var toHome = new double[m];
            for (int a = 0; a < m; a++)
            {
                fromStart[a] = D(home, stops[a].EnuM);
                toHome[a] = fromStart[a];
                for (int b = 0; b < m; b++)
                {
                    between[a, b] = D(stops[a].EnuM, stops[b].EnuM);
                }
            }

            long t0 = Stopwatch.GetTimestamp();
            StopOrder.Order(fromStart, between, toHome);
            return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>A free node drawn uniformly in the disc of <c>OrderStopsRadiusM</c> around <paramref name="home"/>; up to 1000 tries.</summary>
        private static bool StopNear(CostGrid g, BenchmarkConfig c, SplitMix64 rng, int home, out EnuPoint at)
        {
            double reach = c.OrderStopsRadiusM / g.CellM;
            int hi = home % g.Width, hj = home / g.Width;
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                double radius = reach * Math.Sqrt(rng.NextDouble());
                double angle = rng.NextDouble() * 2 * Math.PI;
                int i = (int)Math.Round(hi + (radius * Math.Cos(angle)));
                int j = (int)Math.Round(hj + (radius * Math.Sin(angle)));
                if ((uint)i >= (uint)g.Width || (uint)j >= (uint)g.Height || Cost.IsBlockedNode(g, (j * g.Width) + i, c.Params))
                {
                    continue;
                }

                at = NodePoint(g, (j * g.Width) + i);
                return true;
            }

            at = default;
            return false;
        }

        private static EnuPoint NodePoint(CostGrid g, int node) =>
            new EnuPoint(g.OriginEnuM.E + (g.CellM * (node % g.Width)), g.OriginEnuM.N + (g.CellM * (node / g.Width)));

        /// <summary>
        /// The p50 time of <see cref="Smoothing.Smooth"/> alone on the measured pairs: each pair's
        /// A* path is found again without the clock, and only the smoothing is timed. Pairs with no
        /// path are skipped; NaN if none has one.
        /// </summary>
        private static double SmoothP50Ms(CostGrid g, EnuPoint[] starts, EnuPoint[] goals, BenchmarkConfig c)
        {
            double v0 = c.Params.V0Mps.P50;
            var ms = new double[c.RouteRuns];
            int n = 0;
            for (int k = 0; k < c.RouteRuns; k++)
            {
                int[] path = Search.AStar(g, Router.NearestNode(g, starts[c.RouteWarmUp + k]), Router.NearestNode(g, goals[c.RouteWarmUp + k]), v0, c.Params, Weights.Fastest, out _);
                if (path == null)
                {
                    continue;
                }

                long t0 = Stopwatch.GetTimestamp();
                Smoothing.Smooth(g, path, v0, c.Params, Weights.Fastest);
                ms[n++] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }

            Array.Resize(ref ms, n);
            Array.Sort(ms);
            return Rank(ms, 0.50);
        }

        /// <summary>
        /// A start on a random free node and a goal <c>RouteM</c> away at a random angle, rounded to
        /// a free node inside the grid; up to 1000 tries.
        /// </summary>
        private static bool Pair(CostGrid g, BenchmarkConfig c, SplitMix64 rng, out EnuPoint start, out EnuPoint goal)
        {
            double reach = c.RouteM / g.CellM;
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                int s = rng.NextInt(g.Width * g.Height);
                double angle = rng.NextDouble() * 2 * Math.PI;
                int si = s % g.Width, sj = s / g.Width;
                int gi = (int)Math.Round(si + (reach * Math.Cos(angle)));
                int gj = (int)Math.Round(sj + (reach * Math.Sin(angle)));
                if ((uint)gi >= (uint)g.Width || (uint)gj >= (uint)g.Height)
                {
                    continue;
                }

                int t = (gj * g.Width) + gi;
                if (Cost.IsBlockedNode(g, s, c.Params) || Cost.IsBlockedNode(g, t, c.Params))
                {
                    continue;
                }

                start = new EnuPoint(g.OriginEnuM.E + (g.CellM * si), g.OriginEnuM.N + (g.CellM * sj));
                goal = new EnuPoint(g.OriginEnuM.E + (g.CellM * gi), g.OriginEnuM.N + (g.CellM * gj));
                return true;
            }

            start = default;
            goal = default;
            return false;
        }

        /// <summary>The free node nearest to the centre (the lowest index on a tie), or −1.</summary>
        private static int HomeNode(CostGrid g, CostParams p)
        {
            double ci = (g.Width - 1) / 2.0, cj = (g.Height - 1) / 2.0;
            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int k = 0; k < g.Width * g.Height; k++)
            {
                double di = (k % g.Width) - ci, dj = (k / g.Width) - cj;
                double d = (di * di) + (dj * dj);
                if (d < bestD && !Cost.IsBlockedNode(g, k, p))
                {
                    best = k;
                    bestD = d;
                }
            }

            return best;
        }

        /// <summary>One table row; <paramref name="verdict"/> replaces the E5 verdict when not null.</summary>
        private static string Row(int size, Terrain t, string query, double[] ms, string smoothMs, string orderMs, int found, int gcs, double mb, double p95Limit, string verdict)
        {
            double[] sorted = (double[])ms.Clone();
            Array.Sort(sorted);
            double p50 = Rank(sorted, 0.50), p95 = Rank(sorted, 0.95), max = sorted.Length == 0 ? double.NaN : sorted[sorted.Length - 1];
            if (verdict == null)
            {
                var fails = new StringBuilder();
                if (!(p95 <= p95Limit))
                {
                    fails.Append(" p95");
                }

                if (gcs != 0)
                {
                    fails.Append(" GC");
                }

                if (!(mb <= MaxManagedMb))
                {
                    fails.Append(" MB");
                }

                verdict = fails.Length == 0 ? "ok" : "FAIL:" + fails;
            }

            return $"| {size} | {t} | {query} | {ms.Length} | {found} | {F(p50)} | {F(p95)} | {F(max)} | {smoothMs} | {orderMs} | {gcs} | {F(mb)} | {verdict} |";
        }

        /// <summary>The nearest-rank percentile: the ceil(q·n)-th smallest.</summary>
        private static double Rank(double[] sorted, double q)
        {
            if (sorted.Length == 0)
            {
                return double.NaN;
            }

            int rank = (int)Math.Ceiling(q * sorted.Length);
            return sorted[Math.Max(rank, 1) - 1];
        }

        private static int Collections() => GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        private static string G(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Join(int[] sizes)
        {
            var parts = new string[sizes.Length];
            for (int k = 0; k < sizes.Length; k++)
            {
                parts[k] = sizes[k].ToString(CultureInfo.InvariantCulture);
            }

            return string.Join("/", parts);
        }
    }

    /// <summary>What <see cref="Benchmark.Run(Action{string}, BenchmarkConfig)"/> runs. <see cref="E5"/> is the protocol of E5.</summary>
    internal sealed class BenchmarkConfig
    {
        internal int[] Sizes { get; set; } = Array.Empty<int>();

        internal int RouteRuns { get; set; }

        internal int RouteWarmUp { get; set; }

        internal double RouteM { get; set; }

        internal int[] FieldSizes { get; set; } = Array.Empty<int>();

        internal int FieldRuns { get; set; }

        internal int FieldWarmUp { get; set; }

        /// <summary>The sizes with an <see cref="Router.OrderStops"/> row per terrain, outside the criterion.</summary>
        internal int[] OrderStopsSizes { get; set; } = Array.Empty<int>();

        /// <summary>How many stops each <see cref="Router.OrderStops"/> row orders.</summary>
        internal int OrderStopsK { get; set; }

        /// <summary>The radius of the disc, around the field's home, where the stops are drawn.</summary>
        internal double OrderStopsRadiusM { get; set; }

        /// <summary>One field on the uniform grid of this size, 1 + 1, outside the criterion; 0 for none.</summary>
        internal int StressFieldSize { get; set; }

        internal ulong Seed { get; set; }

        /// <summary>What the scenario is, for the header of the table.</summary>
        internal string Label { get; set; } = string.Empty;

        /// <summary>The cost parameters: Routing reads no JSON, so E5 carries a copy of <c>cost_params.json</c>.</summary>
        internal CostParams Params { get; set; }

        /// <summary>
        /// E5 as it is run (the user's decision, 2026-11-14: only on the laptop, nothing on the
        /// tablet): the five terrains on 1000² and 2000², 500 m routes with 1 warm-up + 5 runs and
        /// fields with 1 + 1; no 4000² and no stress field. Outside E5: Alternatives on the A*
        /// pairs, and OrderStops with 12 stops within 100 m on 1000² (the user's decision,
        /// 2026-11-15: within 250 m the maze alone took 28 s and the runner 150 s; on 2000² the wall
        /// with a gap took 27 s). <see cref="Params"/> is
        /// <c>contracts/fixtures/routing/cost_params.json</c>, schema 1 (a test compares them).
        /// </summary>
        internal static BenchmarkConfig E5 => new BenchmarkConfig
        {
            Label = "laptop only (the tablet is not tested): 5 terrains on 1000²/2000², A* 1 + 5 runs, field 1 + 1",
            Sizes = new[] { 1000, 2000 },
            RouteRuns = 5,
            RouteWarmUp = 1,
            RouteM = 500,
            FieldSizes = new[] { 1000, 2000 },
            FieldRuns = 1,
            FieldWarmUp = 1,
            OrderStopsSizes = new[] { 1000 },
            OrderStopsK = 12,
            OrderStopsRadiusM = 100,
            StressFieldSize = 0,
            Seed = 0x4D41525321UL,
            Params = new CostParams(
                schemaVersion: 1,
                v0Mps: new Percentiles(0.94, 0.70, 0.56),
                b: new SensitivityRange(3.0, 2.5, 3.5),
                c: 0,
                walkCeilingMps: 1.3,
                maxSlopeDeg: 15,
                reliefBlockCm: 61,
                reliefPenaltyFromCm: 36,
                hazardRedMin: 192,
                hazardAmberMin: 96,
                fTerreno: 0.6,
                fRugMin: 0.5,
                amberPenaltySPerM: 2.4,
                altSafestAmberMult: 4,
                altGentlestSPerM: 10),
        };
    }
}
