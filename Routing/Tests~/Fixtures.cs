using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        public void TheGoldenRouteIsThePlannedRoute()
        {
            // expected_route.json is PlanRoute (A* + smoothing) from the base to s2 on the synthetic
            // grid; the golden rounds to 1 mm and 1 ms.
            CostGrid g = SynthFixtures.BuildSynthGrid(Fixtures.CostParams);
            Route r = Router.PlanRoute(g, new EnuPoint(-100, -20), new EnuPoint(100, -20), Fixtures.CostParams);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Dir, "expected_route.json")));
            JsonElement golden = doc.RootElement;
            Assert.That(r.Found, Is.True);
            Assert.That(r.Cells, Is.EqualTo(golden.GetProperty("cells").EnumerateArray().Select(c => c.GetInt32())));
            Assert.That(r.PolylineEnuM.Count, Is.EqualTo(golden.GetProperty("polyline_enu_m").GetArrayLength()));
            Assert.That(r.LengthM, Is.EqualTo(golden.GetProperty("length_m").GetDouble()).Within(0.0005));
            Assert.That(r.EtaS.P50, Is.EqualTo(golden.GetProperty("eta_s").GetProperty("p50").GetDouble()).Within(0.0005));
            Assert.That(r.EtaS.P90, Is.EqualTo(golden.GetProperty("eta_s").GetProperty("p90").GetDouble()).Within(0.0005));
        }

        [Test]
        public void TheGoldenPlanIsOrderStops()
        {
            // expected_plan.json is OrderStops on the synthetic mission (convenciones.md): one plan
            // per alternative; the golden rounds to 1 mm and 1 ms.
            IReadOnlyList<Plan> plans = Router.OrderStops(
                OrderStopsTests.Synth, OrderStopsTests.Base, OrderStopsTests.SynthStops, OrderStopsTests.SynthField,
                new Budget(14400, 50), OrderStopsTests.SynthConditions, OrderStopsTests.T, StopMode.Tsp, Fixtures.CostParams);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Dir, "expected_plan.json")));
            JsonElement golden = doc.RootElement.GetProperty("plans");
            Assert.That(golden.GetArrayLength(), Is.EqualTo(plans.Count));
            for (int k = 0; k < plans.Count; k++)
            {
                JsonElement g = golden[k];
                Plan plan = plans[k];
                Assert.That(plan.Order, Is.EqualTo(g.GetProperty("order").EnumerateArray().Select(e => e.GetString())));
                Assert.That(plan.Dropped, Is.Empty);
                Assert.That(plan.Legs.Count, Is.EqualTo(g.GetProperty("legs").GetArrayLength()));
                for (int l = 0; l < plan.Legs.Count; l++)
                {
                    Assert.That(plan.Legs[l].Cells, Is.EqualTo(g.GetProperty("legs")[l].GetProperty("cells").EnumerateArray().Select(c => c.GetInt32())));
                }

                JsonElement total = g.GetProperty("total_s");
                Assert.That(plan.TotalS.P10, Is.EqualTo(total.GetProperty("p10").GetDouble()).Within(0.0005));
                Assert.That(plan.TotalS.P50, Is.EqualTo(total.GetProperty("p50").GetDouble()).Within(0.0005));
                Assert.That(plan.TotalS.P90, Is.EqualTo(total.GetProperty("p90").GetDouble()).Within(0.0005));
                Assert.That(plan.DoseUGy, Is.EqualTo(g.GetProperty("dose_uGy").GetDouble()).Within(0.0005));
                Assert.That(plan.LimitS, Is.EqualTo(g.GetProperty("limit_s").GetDouble()).Within(0.0005));
                Assert.That(plan.ReturnOk, Is.EqualTo(g.GetProperty("return_ok").GetBoolean()));
            }
        }

        [Test]
        public void CostgridSynthMatchesTheOracle()
        {
            // ~/scratch/c4-verify/oracle/buildgrid_oracle.py (not committed) builds the synthetic tile
            // from convenciones.md and reduces it with tablet.md's BuildGrid rule, without Routing.
            Assert.That(Tiles.Sha256(File.ReadAllBytes(Path.Combine(Fixtures.Dir, "costgrid_synth.bin"))),
                Is.EqualTo("2880dde353476f5f860d9fc5bc5c934d987a02ebb2e29301db3f284b0cd7303d"));
        }

        [Test]
        public void CostgridSynthHasTheSizeOfA129By129Grid()
        {
            // int32 width, int32 height, float32 cell_m, float64 origin_e, float64 origin_n,
            // then float32 height_m and three uint8 arrays.
            Assert.That(Generated.Costgrid.Length, Is.EqualTo(28 + 129 * 129 * (4 + 1 + 1 + 1)));
        }
    }

    /// <summary>
    /// The committed goldens against values computed outside Routing, by hand from the synthetic
    /// ramp: along the row n = −20 every 2 m edge rises 40/256 m, so s = 0.078125, one edge is
    /// sqrt(4 + (40/256)²) m and takes that over v₀ · e^(−3 · s) · 0.6. The up-to-date tests only
    /// say a golden matches the code; these say it is right. The goldens round to 1 ms and 1 mm.
    /// </summary>
    public class SynthGoldenValuesTests
    {
        private const double Rounding = 0.0005;

        [Test]
        public void TheRouteMatchesTheRampByHand()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Dir, "expected_route.json")));
            JsonElement r = doc.RootElement;
            Assert.That(r.GetProperty("length_m").GetDouble(), Is.EqualTo(200.60942307130043).Within(Rounding));
            Assert.That(r.GetProperty("ascent_m").GetDouble(), Is.EqualTo(15.625).Within(Rounding));
            Assert.That(r.GetProperty("eta_s").GetProperty("p10").GetDouble(), Is.EqualTo(449.6348803149127).Within(Rounding));
            Assert.That(r.GetProperty("eta_s").GetProperty("p50").GetDouble(), Is.EqualTo(603.795410708597).Within(Rounding));
            Assert.That(r.GetProperty("eta_s").GetProperty("p90").GetDouble(), Is.EqualTo(754.7442633857462).Within(Rounding));
            Assert.That(r.GetProperty("cells").GetArrayLength(), Is.EqualTo(101));

            // u = 20 · (e + 128) / 256 + 5 · (n + 128) / 256 − 12.5 at e = −100 and e = 100.
            JsonElement line = r.GetProperty("polyline_enu_m");
            Assert.That(line[0][2].GetDouble(), Is.EqualTo(-8.203125).Within(Rounding));
            Assert.That(line[line.GetArrayLength() - 1][2].GetDouble(), Is.EqualTo(7.421875).Within(Rounding));
        }

        [Test]
        public void ThePlanMatchesTheRampByHand()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Dir, "expected_plan.json")));
            JsonElement plan = doc.RootElement.GetProperty("plans")[0];

            // Twice the 200 m of the route (out in two legs, back in one) plus 2 × 600 s of dwell.
            Assert.That(plan.GetProperty("total_s").GetProperty("p10").GetDouble(), Is.EqualTo(2099.269760629825).Within(Rounding));
            Assert.That(plan.GetProperty("total_s").GetProperty("p50").GetDouble(), Is.EqualTo(2407.590821417194).Within(Rounding));
            Assert.That(plan.GetProperty("total_s").GetProperty("p90").GetDouble(), Is.EqualTo(2709.4885267714926).Within(Rounding));

            // 50 uGy at 6 uGy/h is 30000 s; the dose is 6 uGy/h over total_s.p50.
            Assert.That(plan.GetProperty("limit_s").GetDouble(), Is.EqualTo(30000).Within(Rounding));
            Assert.That(plan.GetProperty("dose_uGy").GetDouble(), Is.EqualTo(4.0126513690286565).Within(Rounding));
            Assert.That(plan.GetProperty("return_ok").GetBoolean(), Is.True);
        }
    }
}
