using System;
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

        /// <summary>
        /// Generates the seeded grids of E5, runs its cases and returns the results table, the
        /// text of <c>Routing/BENCHMARK.md</c>. <paramref name="log"/> receives each line as it comes.
        /// Per size, ascending (1000² and 2000²): an A* row of 500 m routes for each terrain (1 warm-up
        /// + 5 runs), then a field row for each terrain (1 + 1). It runs on the laptop; the tablet is
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
                + $"field at v0 P90 on {Join(c.FieldSizes)}², {c.FieldWarmUp} + {c.FieldRuns}"
                + (c.StressFieldSize > 0 ? $"; stress field on Uniform {c.StressFieldSize}², 1 + 1, outside the criterion" : string.Empty));
            Line($"- E5 (docs/guide/chapters/reparto.md): A* p95 <= {F(RouteP95Ms)} ms, field p95 <= {F(FieldP95Ms)} ms, 0 GC during the measured runs, managed heap <= {F(MaxManagedMb)} MB. "
                + "Measured on the laptop only: the tablet is not tested.");
            Line(string.Empty);
            Line("| size | terrain | query | runs | found | p50 ms | p95 ms | max ms | GC | managed MB | E5 |");
            Line("|---|---|---|---|---|---|---|---|---|---|---|");

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

            maxSize = Math.Max(maxSize, c.StressFieldSize);
            for (int size = 1; size <= maxSize; size++)
            {
                bool route = Array.IndexOf(c.Sizes, size) >= 0;
                bool field = Array.IndexOf(c.FieldSizes, size) >= 0;
                bool stress = size == c.StressFieldSize;
                if (!route && !field && !stress)
                {
                    continue;
                }

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
            }

            return text.ToString();
        }

        private static ulong GridSeed(ulong seed, int size, Terrain t) =>
            new SplitMix64(seed ^ ((ulong)size << 8) ^ (ulong)t).Next();

        private static string RouteRow(CostGrid g, Terrain t, BenchmarkConfig c)
        {
            int total = c.RouteWarmUp + c.RouteRuns;
            var starts = new EnuPoint[total];
            var goals = new EnuPoint[total];
            var rng = new SplitMix64(GridSeed(c.Seed, g.Width, t) ^ 0x5041495253UL);
            for (int k = 0; k < total; k++)
            {
                if (!Pair(g, c, rng, out starts[k], out goals[k]))
                {
                    return $"| {g.Width} | {t} | A* {G(c.RouteM)} m | 0 | 0 | | | | | | no pair |";
                }
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
            return Row(g.Width, t, $"A* {G(c.RouteM)} m", ms, found, gcs, mb, RouteP95Ms, stress: false);
        }

        private static string FieldRow(CostGrid g, Terrain t, BenchmarkConfig c, bool stress)
        {
            int home = HomeNode(g, c.Params);
            int warmUp = stress ? 1 : c.FieldWarmUp;
            int runs = stress ? 1 : c.FieldRuns;
            var timeS = new float[g.Width * g.Height];
            if (home < 0)
            {
                return $"| {g.Width} | {t} | field | 0 | 0 | | | | | | no home |";
            }

            double v0 = c.Params.V0Mps.P90;
            for (int k = 0; k < warmUp; k++)
            {
                Search.Field(g, home, v0, c.Params, timeS);
            }

            var ms = new double[runs];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = Collections();
            for (int k = 0; k < runs; k++)
            {
                long t0 = Stopwatch.GetTimestamp();
                Search.Field(g, home, v0, c.Params, timeS);
                ms[k] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }

            int gcs = Collections() - gc0;
            double mb = GC.GetTotalMemory(true) / (double)(1 << 20);
            GC.KeepAlive(timeS);
            GC.KeepAlive(g);
            return Row(g.Width, t, "field", ms, runs, gcs, mb, FieldP95Ms, stress);
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

        private static string Row(int size, Terrain t, string query, double[] ms, int found, int gcs, double mb, double p95Limit, bool stress)
        {
            double[] sorted = (double[])ms.Clone();
            Array.Sort(sorted);
            double p50 = Rank(sorted, 0.50), p95 = Rank(sorted, 0.95), max = sorted.Length == 0 ? double.NaN : sorted[sorted.Length - 1];
            string verdict;
            if (stress)
            {
                verdict = "stress";
            }
            else
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

            return $"| {size} | {t} | {query} | {ms.Length} | {found} | {F(p50)} | {F(p95)} | {F(max)} | {gcs} | {F(mb)} | {verdict} |";
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
        /// fields with 1 + 1; no 4000² and no stress field. <see cref="Params"/> is
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
