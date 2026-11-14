using System;
using System.IO;

namespace MartianMap.Routing.FixtureGen
{
    /// <summary>
    /// Writes costgrid_synth.bin, expected_route.json and expected_plan.json into
    /// contracts/fixtures/routing/, from the cost_params.json found there.
    /// Usage: dotnet run --project Routing/Tests~/FixtureGen [-- REPO_ROOT]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : RepoRoot.Find(Directory.GetCurrentDirectory());
            string dir = Path.Combine(root, "contracts", "fixtures", "routing");
            CostParams p = CostParamsJson.Read(Path.Combine(dir, "cost_params.json"));
            SynthFixtures.Output output = SynthFixtures.Generate(p);
            File.WriteAllBytes(Path.Combine(dir, "costgrid_synth.bin"), output.Costgrid);
            File.WriteAllText(Path.Combine(dir, "expected_route.json"), output.ExpectedRoute);
            File.WriteAllText(Path.Combine(dir, "expected_plan.json"), output.ExpectedPlan);
            Console.WriteLine($"wrote {dir}: costgrid_synth.bin ({output.Costgrid.Length} bytes), expected_route.json, expected_plan.json");
            return 0;
        }
    }

    public static class RepoRoot
    {
        /// <summary>The nearest ancestor of <paramref name="start"/> that holds contracts/fixtures/routing/.</summary>
        public static string Find(string start)
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "contracts", "fixtures", "routing")))
                {
                    return d.FullName;
                }
            }

            throw new DirectoryNotFoundException($"No contracts/fixtures/routing/ above {start}.");
        }
    }
}
