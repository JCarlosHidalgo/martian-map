using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The E5 benchmark of the routing core. Plain C#, so <c>dotnet</c> and the tablet's benchmark
    /// scene call it the same way.
    /// </summary>
    public static class Benchmark
    {
        /// <summary>
        /// Generates the seeded grids of E5, runs its cases and returns the results table, the
        /// text of <c>Routing/BENCHMARK.md</c>. <paramref name="log"/> receives each line as it comes.
        /// </summary>
        public static string Run(Action<string> log) => throw new NotImplementedException();
    }
}
