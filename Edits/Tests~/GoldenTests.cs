using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// E9 (docs/guide/chapters/reparto.md): <c>SHA-256(Apply(base_tile, edits_NN))</c> =
    /// <c>expected_NN.sha256</c>. The logs and hashes were written by
    /// ~/scratch/c4-verify/oracle/cases_apply.py t7 (Python ints, not committed); <c>base_tile/</c> is
    /// the synthetic tile of P1 at <c>origin/p1/ingest</c> 3fd3ae0. They live here until P1's tile and
    /// P2's <c>edit_log</c> are in <c>main</c>; then they move to <c>contracts/fixtures/edits/</c> and only
    /// <see cref="Dir"/> changes (plan A13).
    /// </summary>
    public class GoldenTests
    {
        private const int Cases = 6;

        private static readonly string Root = FindRoot(TestContext.CurrentContext.TestDirectory);

        /// <summary>Where the E9 fixtures live: <c>base_tile/</c>, <c>edits_NN.json</c>, <c>expected_NN.sha256</c>.</summary>
        internal static string Dir { get; } = Path.Combine(Root, "Edits", "Tests~", "fixtures", "edits");

        private static string MergeDir => Path.Combine(Root, "contracts", "fixtures", "merge");

        [Test]
        public void TheBaseTileIsTheSynthHazard()
        {
            // The hash in P1's tile.json, and the one of the guide's synthetic hazard.
            string hash = ApplyTests.Sha(BaseHazard());
            Assert.That(hash, Is.EqualTo("966a39a8da05a55d113ba6e1fd8c82509b51fd0381083bceffc3b4e3e1f11b10"));
            Assert.That(File.ReadAllText(Path.Combine(Dir, "base_tile", "tile.json")), Does.Contain(hash));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void EachEditLogGivesItsExpectedHash(int n)
        {
            Edit[] edits = EditLogJson.Read(Path.Combine(Dir, $"edits_{n:00}.json"), false);
            Assert.That(ApplyTests.Sha(EditEngine.Apply(BaseHazard(), Origin(), edits)), Is.EqualTo(Expected(n)));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void TheHashDoesNotDependOnTheOrderOfArrival(int n)
        {
            Edit[] edits = EditLogJson.Read(Path.Combine(Dir, $"edits_{n:00}.json"), false);
            byte[] b = BaseHazard();
            var rng = new Random(n);
            for (int k = 0; k < 20; k++)
            {
                Edit[] shuffled = edits.OrderBy(_ => rng.Next()).ToArray();
                Assert.That(ApplyTests.Sha(EditEngine.Apply(b, Origin(), shuffled)), Is.EqualTo(Expected(n)), $"shuffle {k}");
            }
        }

        [Test]
        public void EveryExpectedFileIsALowercaseHash()
        {
            // validate.py's rule: 64 lowercase hex (we always write the final \n), each with its edits_NN.json.
            string[] files = Directory.GetFiles(Dir, "expected_*.sha256");
            Assert.That(files, Has.Length.EqualTo(Cases));
            foreach (string f in files)
            {
                Match m = Regex.Match(Path.GetFileName(f), @"^expected_([0-9]{2})\.sha256$");
                Assert.That(m.Success, Is.True, f);
                Assert.That(File.ReadAllText(f), Does.Match(@"^[0-9a-f]{64}\n$"), f);
                Assert.That(File.Exists(Path.Combine(Dir, $"edits_{m.Groups[1].Value}.json")), Is.True, f);
            }
        }

        [Test]
        public void TheMergeGoldenGivesTheSameTile()
        {
            (Edit[] logIn, Edit[] merged) = MergeGolden();
            Assert.That(ApplyTests.Sha(EditEngine.Apply(BaseHazard(), Origin(), logIn)), Is.EqualTo(ApplyTests.Sha(EditEngine.Apply(BaseHazard(), Origin(), merged))));
        }

        [Test]
        public void TheMergeGoldenWinnersAreTheLiveEntities()
        {
            (Edit[] logIn, Edit[] merged) = MergeGolden();
            Assert.That(
                EditEngine.LiveEntities(logIn).Select(Describe),
                Is.EqualTo(merged.Where(e => !e.Tombstone).Select(e => Describe(new LiveEntity(e.EntityId, e.Op, e.Creator, Level(e), e.Payload)))));
        }

        private static (Edit[] LogIn, Edit[] Merged) MergeGolden()
        {
            string logIn = Path.Combine(MergeDir, "log_in.json"), merged = Path.Combine(MergeDir, "log_merged.json");
            if (!File.Exists(logIn) || !File.Exists(merged))
            {
                Assert.Ignore("pendiente de P2: contracts/fixtures/merge/log_in.json y log_merged.json");
            }

            // log_merged.json holds the habitat's winners: they are not evaluated again.
            return (EditLogJson.Read(logIn, false), EditLogJson.Read(merged, true));
        }

        private static byte? Level(Edit e) => e.Op == EditOp.HazardSet ? e.Payload.Level : e.Op == EditOp.ObstacleSet ? (byte?)255 : null;

        private static string Describe(LiveEntity l)
        {
            string shape = l.Payload.Polygon != null
                ? string.Join(" ", l.Payload.Polygon.Select(v => $"{v.E},{v.N}"))
                : $"{l.Payload.Point.Value.E},{l.Payload.Point.Value.N} {l.Payload.Text}";
            return $"{l.EntityId} {l.Op} {l.Creator} {l.Level} {l.Payload.Confirmed} [{shape}]";
        }

        private static byte[] BaseHazard() => File.ReadAllBytes(Path.Combine(Dir, "base_tile", "hazard.r8"));

        private static EnuMetres Origin() => EditLogJson.TileOrigin(Path.Combine(Dir, "base_tile", "tile.json"));

        private static string Expected(int n) => File.ReadAllText(Path.Combine(Dir, $"expected_{n:00}.sha256")).TrimEnd('\n');

        /// <summary>The nearest ancestor that holds Edits/MartianMap.Edits.csproj.</summary>
        private static string FindRoot(string start)
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "Edits", "MartianMap.Edits.csproj")))
                {
                    return d.FullName;
                }
            }

            throw new DirectoryNotFoundException($"No Edits/MartianMap.Edits.csproj above {start}.");
        }
    }
}
