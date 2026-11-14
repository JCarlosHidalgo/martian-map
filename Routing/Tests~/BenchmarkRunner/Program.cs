using System;
using System.IO;
using MartianMap.Routing.FixtureGen;

namespace MartianMap.Routing.BenchmarkRunner
{
    /// <summary>
    /// Runs <see cref="Benchmark.Run(Action{string})"/> on this machine and writes
    /// <c>Routing/BENCHMARK.md</c>. Run it in Release:
    /// <c>dotnet run -c Release --project "Routing/Tests~/BenchmarkRunner"</c>.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : RepoRoot.Find(Directory.GetCurrentDirectory());
            string text = Benchmark.Run(Console.WriteLine);
            text += "\n## Tablet (IL2CPP ARM64)\n\nNO VERIFICADO — la mide la escena de benchmark de P4 (`BenchmarkSandbox.unity`), con el mismo `Benchmark.Run`.\n";
            string path = Path.Combine(root, "Routing", "BENCHMARK.md");
            File.WriteAllText(path, text);
            Console.WriteLine($"wrote {path}");
            return 0;
        }
    }
}
