using System.IO;
using MartianMap.Routing.FixtureGen;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>The committed C4 fixtures, in contracts/fixtures/routing/.</summary>
    internal static class Fixtures
    {
        internal static string Dir { get; } =
            Path.Combine(RepoRoot.Find(TestContext.CurrentContext.TestDirectory), "contracts", "fixtures", "routing");

        internal static CostParams CostParams { get; } = CostParamsJson.Read(Path.Combine(Dir, "cost_params.json"));
    }

    public class SynthFixturesTests
    {
        private static readonly SynthFixtures.Output Generated = SynthFixtures.Generate(Fixtures.CostParams);

        [Test]
        public void CostgridSynthIsUpToDate()
        {
            Assert.That(File.ReadAllBytes(Path.Combine(Fixtures.Dir, "costgrid_synth.bin")), Is.EqualTo(Generated.Costgrid),
                "Regenerate with: dotnet run --project Routing/Tests~/FixtureGen");
        }

        [Test]
        public void ExpectedRouteIsUpToDate()
        {
            Assert.That(File.ReadAllText(Path.Combine(Fixtures.Dir, "expected_route.json")), Is.EqualTo(Generated.ExpectedRoute),
                "Regenerate with: dotnet run --project Routing/Tests~/FixtureGen");
        }

        [Test]
        public void ExpectedPlanIsUpToDate()
        {
            Assert.That(File.ReadAllText(Path.Combine(Fixtures.Dir, "expected_plan.json")), Is.EqualTo(Generated.ExpectedPlan),
                "Regenerate with: dotnet run --project Routing/Tests~/FixtureGen");
        }

        [Test]
        public void CostgridSynthHasTheSizeOfA129By129Grid()
        {
            // int32 width, int32 height, float32 cell_m, float64 origin_e, float64 origin_n,
            // then float32 height_m and three uint8 arrays.
            Assert.That(Generated.Costgrid.Length, Is.EqualTo(28 + 129 * 129 * (4 + 1 + 1 + 1)));
        }
    }
}
