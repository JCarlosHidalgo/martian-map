using System;
using System.IO;
using MartianMap.Routing.FixtureGen;

namespace MartianMap.Routing.BenchmarkRunner
{
    /// <summary>
    /// Runs the quick laptop scenario of the benchmark (<see cref="BenchmarkConfig.Quick"/>, not
    /// the E5 protocol) and writes <c>Routing/BENCHMARK.md</c>. The tablet runs the public
    /// <see cref="Benchmark.Run(Action{string})"/>, the full E5. Run it in Release:
    /// <c>dotnet run -c Release --project "Routing/Tests~/BenchmarkRunner"</c>.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : RepoRoot.Find(Directory.GetCurrentDirectory());
            string text = Benchmark.Run(Console.WriteLine, BenchmarkConfig.Quick);
            text += "\n## Tablet (IL2CPP ARM64)\n\nNO VERIFICADO — la tablet corre el protocolo E5 completo (`Benchmark.Run(log)`, 5 terrenos, 3 + 20 corridas, 4000²) desde la escena de benchmark de P4 (`BenchmarkSandbox.unity`); la tabla de arriba es el escenario rápido del portátil.\n";
            string path = Path.Combine(root, "Routing", "BENCHMARK.md");
            File.WriteAllText(path, text);
            Console.WriteLine($"wrote {path}");
            return 0;
        }
    }
}
