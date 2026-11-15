using System;
using System.IO;
using System.Linq;
using MartianMap.Edits;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MartianMap.Terrain.Tests
{
    /// <summary>
    /// The consumer side of P2's C3 contracts: edits (edit.schema.json, merge golden) and the API
    /// bodies (fixtures/api/), plus the local edit log and the hazard merge.
    /// </summary>
    public sealed class EditsAndHabitatTests
    {
        private static string Fixture(string relative) =>
            File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "contracts", "fixtures", relative)));

        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "martianmap-edits-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void EveryGoldenEditRoundTripsByteForByteInMeaning()
        {
            var log = JObject.Parse(Fixture("merge/log_in.json"));
            var edits = (JArray)log["edits"];
            Assert.AreEqual(9, edits.Count);
            foreach (JObject original in edits)
            {
                Assert.IsTrue(EditJson.TryRead(original, fromHabitat: false, out var edit), (string)original["uuid"]);
                var written = EditJson.Write(edit);
                Assert.IsTrue(JToken.DeepEquals(original, written), $"{original["uuid"]}\nexpected {original}\nwritten {written}");
            }
        }

        [Test]
        public void UnknownOpIsDiscardedNotThrown()
        {
            var json = JObject.Parse(Fixture("merge/log_in.json"))["edits"][0].DeepClone() as JObject;
            json["op"] = "height.set";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown op=height.set"));
            Assert.IsFalse(EditJson.TryRead(json, false, out _));
        }

        [Test]
        public void ApiFixturesParse()
        {
            var zones = HabitatApi.ParseZones(Fixture("api/zones.json"));
            Assert.AreEqual(1, zones.NextSince);
            Assert.AreEqual("synth", zones.Zones.Single().ZoneId);
            Assert.AreEqual(1, zones.Zones.Single().ZoneVersion);

            var changes = HabitatApi.ParseChanges(Fixture("api/changes.json"), "synth");
            Assert.AreEqual(3, changes.NextSince);
            Assert.IsFalse(changes.HasMore);
            Assert.AreEqual(EditOp.NoteSet, changes.Edits.Single().Op);
            Assert.IsTrue(changes.Edits.Single().FromHabitat, "winners from changes are from the habitat");
            Assert.IsEmpty(changes.Tiles);
            Assert.IsNull(changes.Conditions);

            var acks = HabitatApi.ParseAcks(Fixture("api/edits_ack.json"));
            CollectionAssert.AreEqual(new[] { "accepted", "duplicate", "rejected" }, acks.Select(a => a.Status));
            Assert.AreEqual("confirmation_required", acks[2].Code);
            Assert.IsNull(acks[0].Code);
        }

        [Test]
        public void PostBodyCarriesTheWireEdits()
        {
            var edit = NewNote("ev1", 1, "11111111-1111-4111-8111-111111111111");
            var body = JObject.Parse(HabitatApi.EditsBody(new[] { edit }));
            var sent = (JObject)body["edits"][0];
            Assert.AreEqual("note.set", (string)sent["op"]);
            Assert.IsNull(sent["fromHabitat"] ?? sent["_from_habitat"], "local fields never travel");
        }

        [Test]
        public void EditLogPersistsAcksAndRejections()
        {
            var log = EditLog.Open(_folder, "synth");
            var a = NewNote("ev1", log.NextLamport, "aaaaaaaa-0000-4000-8000-000000000001");
            log.Append(a);
            var b = NewNote("ev1", log.NextLamport, "aaaaaaaa-0000-4000-8000-000000000002");
            log.Append(b);
            Assert.AreEqual(2, b.Lamport, "Lamport grows with each own edit");
            Assert.AreEqual(2, log.Pending.Count);

            log.ApplyAck(a.Uuid, "accepted");
            var rejected = log.ApplyAck(b.Uuid, "rejected");
            Assert.AreEqual(b.Uuid, rejected.Uuid);

            var reopened = EditLog.Open(_folder, "synth");
            Assert.AreEqual(1, reopened.Count, "a rejected edit is not kept");
            Assert.IsTrue(reopened.Edits[0].FromHabitat, "an accepted edit counts as from the habitat");
            Assert.IsEmpty(reopened.Pending);
            Assert.AreEqual(2, reopened.NextLamport);
        }

        [Test]
        public void WinnersMergeOnceAndKeepTheCreator()
        {
            var log = EditLog.Open(_folder, "synth");
            var winners = HabitatApi.ParseChanges(Fixture("api/changes.json"), "synth").Edits;
            Assert.IsTrue(log.MergeWinners(winners));
            Assert.IsFalse(log.MergeWinners(winners), "the same uuid is not added twice");
            Assert.AreEqual("ev1", log.CreatorOf("cccccccc-cccc-4ccc-8ccc-cccccccccccc"));
            Assert.AreEqual(3, log.NextLamport, "the clock moves past every seen lamport");
        }

        [Test]
        public void HazardIsTheMaxOfBaseShapesAndEdits()
        {
            var tile = SyntheticZone.CreateFixtureLike().Tiles[0];
            var shapes = new byte[TileData.Size * TileData.Size];
            var edits = new byte[TileData.Size * TileData.Size];
            const int i = 10 * TileData.Size + 10;
            var baseValue = tile.BaseHazard[i];
            shapes[i] = 90;
            edits[i] = 200;

            tile.SetHazardOverlay(shapes);
            Assert.IsTrue(tile.SetEditHazard(edits));
            Assert.AreEqual(Math.Max(baseValue, (byte)200), tile.Hazard[i]);

            Assert.IsTrue(tile.SetEditHazard(null));
            Assert.AreEqual(Math.Max(baseValue, (byte)90), tile.Hazard[i]);
            Assert.IsFalse(tile.SetEditHazard(null), "no change, no reload");
        }

        [Test]
        public void ConditionsMapToRoutingWithAFallback()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no usable conditions"));
            Assert.AreEqual(RouteOrchestrator.FallbackDoseRateUGyH, RouteOrchestrator.ToRoutingConditions(null).ValueUGyH);

            var info = new ConditionsInfo { Value = 6.3 };
            info.Modifiers.Add(new ConditionsModifier { Type = "SEP", Factor = 2.0, ValidFrom = "2026-11-14T00:00:00Z", ValidTo = "2026-11-15T00:00:00Z" });
            var conditions = RouteOrchestrator.ToRoutingConditions(info);
            Assert.AreEqual(6.3, conditions.ValueUGyH, 1e-9);
            Assert.AreEqual(2.0, conditions.Modifiers.Single().Factor, 1e-9);
            Assert.AreEqual(DateTimeKind.Utc, conditions.Modifiers.Single().ValidFrom.Kind);
        }

        [Test]
        public void FixedPointRoundsToTheNearest256th()
        {
            var p = EditJson.ToFixed(new Vector2(-100f, 20.5f));
            Assert.AreEqual(-25600, p.E);
            Assert.AreEqual(5248, p.N);
            Assert.AreEqual(new Vector2(-100f, 20.5f), EditJson.ToEnu(p));
        }

        private static Edit NewNote(string author, long lamport, string uuid) =>
            new Edit(uuid, lamport, author, author, "synth", "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
                EditOp.NoteSet, EditPayload.Note(new FixedPoint(0, 0), "test"), false, false);
    }
}
