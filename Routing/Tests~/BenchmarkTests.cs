using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>The seeded grids of E5 and <see cref="Benchmark.Run(Action{string})"/> (docs/guide/chapters/reparto.md, E5).</summary>
    public class BenchmarkTests
    {
        private static readonly Terrain[] AllTerrains = (Terrain[])Enum.GetValues(typeof(Terrain));

        [Test]
        public void TheGridsAreTheSameForTheSameSeed()
        {
            foreach (Terrain t in AllTerrains)
            {
                Assert.That(Bytes(BenchmarkGrids.Make(t, 64, 7)), Is.EqualTo(Bytes(BenchmarkGrids.Make(t, 64, 7))), t.ToString());
            }

            Assert.That(Bytes(BenchmarkGrids.Make(Terrain.Blocked30, 64, 8)), Is.Not.EqualTo(Bytes(BenchmarkGrids.Make(Terrain.Blocked30, 64, 7))));
        }

        [Test]
        public void TheGeneratorIsPinned()
        {
            // Blocked30: rebuilt outside the code (~/scratch/c4-verify/oracle/bench_pin.py: SplitMix64(1),
            // NextDouble() < 0.30 per node in index order; 32.0 % blocked).
            Assert.That(Sha256(BenchmarkGrids.Make(Terrain.Blocked30, 64, 1)), Is.EqualTo("a5788291f23ad2d111c667ca417234bcfbeec58a2e08b7cb5988b5a6b24c6558"));

            // Fractal: the generator's own output when it was written (commit "fill Benchmark.Run"),
            // pinned so that two BENCHMARK.md tables stay comparable. Not an oracle value.
            Assert.That(Sha256(BenchmarkGrids.Make(Terrain.Fractal, 64, 1)), Is.EqualTo("3741b1955885f4c9c4e3c049c91f4b9b94a1409aff727483a8b4a48f8be355b4"));
        }

        [Test]
        public void Blocked30BlocksAboutThirtyPercent()
        {
            CostGrid g = BenchmarkGrids.Make(Terrain.Blocked30, 200, 3);
            double blocked = g.Hazard.ToArray().Count(h => h == 255) / (double)(200 * 200);
            Assert.That(blocked, Is.InRange(0.28, 0.32));
        }

        [Test]
        public void TheFractalHasHillsAndSomeSteepGround()
        {
            CostGrid g = BenchmarkGrids.Make(Terrain.Fractal, 400, 5);
            float[] h = g.HeightM.ToArray();
            Assert.That(h.Max() - h.Min(), Is.InRange(10f, 80f));
            Assert.That(h.Max(), Is.LessThanOrEqualTo(40f));
            Assert.That(h.Min(), Is.GreaterThanOrEqualTo(-40f));
            byte[] slope = g.SlopeDeg.ToArray();
            byte[] hazard = g.Hazard.ToArray();
            for (int k = 0; k < hazard.Length; k++)
            {
                Assert.That(hazard[k], Is.EqualTo(slope[k] > 15 ? 255 : 0), $"node {k}");
            }

            Assert.That(g.RoughnessCm.ToArray().Max(), Is.InRange((byte)36, (byte)70));
        }

        [Test]
        public void TheWallHasOneGapAndTheMazeIsConnected()
        {
            CostParams p = Fixtures.CostParams;
            CostGrid wall = BenchmarkGrids.Make(Terrain.WallWithGap, 101, 1);
            Assert.That(Enumerable.Range(0, 101).Count(j => wall.Hazard[wall.Index(50, j)] == 0), Is.EqualTo(4));
            Assert.That(Router.PlanRoute(wall, new EnuPoint(0, 160), new EnuPoint(200, 160), p).Found, Is.True);

            CostGrid maze = BenchmarkGrids.Make(Terrain.Maze, 101, 1);
            Assert.That(maze.Hazard[maze.Index(0, 0)], Is.EqualTo(255));
            Assert.That(Router.PlanRoute(maze, new EnuPoint(2, 2), new EnuPoint(198, 198), p).Found, Is.True);
        }

        [Test]
        public void RunWritesOneRowPerCaseAndLogsEveryLine()
        {
            var lines = new List<string>();
            string text = Benchmark.Run(lines.Add, Small(stress: 0));
            Assert.That(string.Concat(lines.Select(l => l + "\n")), Is.EqualTo(text));
            string[] rows = lines.Where(l => l.StartsWith("| 64 |", StringComparison.Ordinal)).ToArray();
            Assert.That(rows.Count(r => r.Contains("| A* 40 m |")), Is.EqualTo(5));
            Assert.That(rows.Count(r => r.Contains("| field |")), Is.EqualTo(5));
            foreach (Terrain t in AllTerrains)
            {
                Assert.That(rows.Count(r => r.Contains($"| {t} |")), Is.EqualTo(2), t.ToString());
            }

            Assert.That(lines.Count(l => l.Contains("stress")), Is.EqualTo(0));
        }

        [Test]
        public void TheStressFieldIsOneRowOutsideTheCriterion()
        {
            var lines = new List<string>();
            Benchmark.Run(lines.Add, Small(stress: 32));
            string[] stress = lines.Where(l => l.StartsWith("| 32 |", StringComparison.Ordinal)).ToArray();
            Assert.That(stress.Length, Is.EqualTo(1));
            Assert.That(stress[0], Does.Contain("| Uniform | field |"));
            Assert.That(stress[0].TrimEnd().EndsWith("| stress |", StringComparison.Ordinal), Is.True, stress[0]);
        }

        [Test]
        public void RunRejectsANullLog()
        {
            Assert.Throws<ArgumentNullException>(() => Benchmark.Run(null));
            Assert.Throws<ArgumentNullException>(() => Benchmark.Run(null, Small(0)));
        }

        [Test]
        public void TheE5ParamsAreThoseOfCostParamsJson()
        {
            CostParams copy = BenchmarkConfig.E5.Params;
            CostParams json = Fixtures.CostParams;
            foreach (PropertyInfo prop in typeof(CostParams).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(prop.GetValue(copy), Is.EqualTo(prop.GetValue(json)), prop.Name);
            }
        }

        [Test]
        public void TheE5ConfigIsTheProtocol()
        {
            BenchmarkConfig c = BenchmarkConfig.E5;
            Assert.That(c.Sizes, Is.EqualTo(new[] { 1000, 2000, 4000 }));
            Assert.That((c.RouteWarmUp, c.RouteRuns, c.RouteM), Is.EqualTo((3, 20, 500.0)));
            Assert.That(c.FieldSizes, Is.EqualTo(new[] { 1000, 2000 }));
            Assert.That((c.FieldWarmUp, c.FieldRuns, c.StressFieldSize), Is.EqualTo((3, 20, 4000)));
        }

        [Test]
        public void TheQuickConfigIsTheLaptopScenario()
        {
            BenchmarkConfig q = BenchmarkConfig.Quick;
            Assert.That(q.Sizes, Is.EqualTo(new[] { 1000, 2000 }));
            Assert.That((q.RouteWarmUp, q.RouteRuns, q.RouteM), Is.EqualTo((1, 5, 500.0)));
            Assert.That(q.FieldSizes, Is.EqualTo(new[] { 1000, 2000 }));
            Assert.That((q.FieldWarmUp, q.FieldRuns, q.StressFieldSize), Is.EqualTo((1, 1, 0)));
            Assert.That(q.Seed, Is.EqualTo(BenchmarkConfig.E5.Seed));
            Assert.That(Same(q.Params, BenchmarkConfig.E5.Params), Is.True);
            Assert.That(q.Label, Does.Contain("not the E5 protocol"));
            Assert.That(BenchmarkConfig.E5.Label, Does.StartWith("E5"));
        }

        [Test]
        public void TheHeaderSaysWhichScenarioRan()
        {
            var lines = new List<string>();
            BenchmarkConfig c = Small(stress: 0);
            c.Label = "a tiny test scenario";
            Benchmark.Run(lines.Add, c);
            Assert.That(lines, Has.Some.EqualTo("- Scenario: a tiny test scenario"));
        }

        private static bool Same(CostParams a, CostParams b) =>
            typeof(CostParams).GetProperties(BindingFlags.Public | BindingFlags.Instance).All(prop => Equals(prop.GetValue(a), prop.GetValue(b)));

        private static BenchmarkConfig Small(int stress) => new BenchmarkConfig
        {
            Sizes = new[] { 64 },
            RouteRuns = 2,
            RouteWarmUp = 1,
            RouteM = 40,
            FieldSizes = new[] { 64 },
            FieldRuns = 2,
            FieldWarmUp = 1,
            StressFieldSize = stress,
            Seed = 1,
            Params = Fixtures.CostParams,
        };

        private static byte[] Bytes(CostGrid g)
        {
            float[] h = g.HeightM.ToArray();
            var bytes = new byte[h.Length * 4];
            Buffer.BlockCopy(h, 0, bytes, 0, bytes.Length);
            return bytes.Concat(g.SlopeDeg.ToArray()).Concat(g.RoughnessCm.ToArray()).Concat(g.Hazard.ToArray()).ToArray();
        }

        private static string Sha256(CostGrid g)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Bytes(g)).Select(b => b.ToString("x2")));
        }
    }
}
