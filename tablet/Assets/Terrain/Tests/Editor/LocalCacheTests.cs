using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MartianMap.Terrain.Tests
{
    public sealed class LocalCacheTests
    {
        private string _root;
        private string _bundle;
        private LocalCache _cache;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "martianmap-tests-" + Guid.NewGuid().ToString("N"));
            _bundle = Path.Combine(_root, "bundle");
            _cache = new LocalCache(Path.Combine(_root, "cache", "zones"), new JsonCatalogStore(Path.Combine(_root, "cache", "catalog.json")));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [UnityTest]
        public IEnumerator ImportThenLoadGivesTheSameRasters()
        {
            var original = SyntheticZone.CreateFixtureLike();
            var height = (ushort[])original.Tiles[0].Height.Clone();
            var hazard = (byte[])original.Tiles[0].BaseHazard.Clone();
            ZoneBundleWriter.Write(original, _bundle, zoneVersion: 3);

            var import = PreloadedZones.ImportAsync(_cache, _bundle);
            yield return Wait(import);
            Assert.AreEqual(PreloadedZones.Result.Imported, import.Result);
            Assert.AreEqual(3, _cache.ZoneVersion("synth"));
            Assert.AreEqual(new[] { "synth" }, _cache.Zones.Select(z => z.ZoneId).ToArray());

            var load = _cache.LoadZoneAsync("synth");
            yield return Wait(load);
            var tile = load.Result.Tiles.Single();
            CollectionAssert.AreEqual(height, tile.Height);
            CollectionAssert.AreEqual(hazard, tile.BaseHazard);
            Assert.AreEqual(3, tile.Layers.Count);

            // A catalog reopened from disk sees the same zone.
            var reopened = new LocalCache(_cache.Root, new JsonCatalogStore(Path.Combine(_root, "cache", "catalog.json")));
            Assert.AreEqual(3, reopened.ZoneVersion("synth"));
        }

        [UnityTest]
        public IEnumerator SameVersionIsNotImportedTwice()
        {
            ZoneBundleWriter.Write(SyntheticZone.CreateFixtureLike(), _bundle, zoneVersion: 1);
            var first = PreloadedZones.ImportAsync(_cache, _bundle);
            yield return Wait(first);
            var second = PreloadedZones.ImportAsync(_cache, _bundle);
            yield return Wait(second);

            Assert.AreEqual(PreloadedZones.Result.Imported, first.Result);
            Assert.AreEqual(PreloadedZones.Result.AlreadyCached, second.Result);
        }

        [UnityTest]
        public IEnumerator TamperedTileIsRejectedAndTheZoneStaysOut()
        {
            ZoneBundleWriter.Write(SyntheticZone.CreateFixtureLike(), _bundle, zoneVersion: 1);
            var heightFile = Path.Combine(_bundle, "data", "tiles", "0", "0", "0", "height.r16");
            var bytes = File.ReadAllBytes(heightFile);
            bytes[1000] ^= 0xFF;
            File.WriteAllBytes(heightFile, bytes);

            var import = PreloadedZones.ImportAsync(_cache, _bundle);
            yield return Wait(import, expectFailure: true);

            Assert.IsTrue(import.IsFaulted);
            Assert.IsInstanceOf<CacheIntegrityException>(import.Exception!.GetBaseException());
            Assert.AreEqual(0, _cache.ZoneVersion("synth"));
            Assert.IsEmpty(_cache.Zones);
        }

        [UnityTest]
        public IEnumerator NeedsTileFollowsVersionAndHash()
        {
            ZoneBundleWriter.Write(SyntheticZone.CreateFixtureLike(), _bundle, zoneVersion: 2);
            var import = PreloadedZones.ImportAsync(_cache, _bundle);
            yield return Wait(import);

            var load = _cache.LoadZoneAsync("synth");
            yield return Wait(load);
            var reference = load.Result.Zone.Tiles.Single();

            Assert.IsFalse(_cache.NeedsTile("synth", reference));
            Assert.IsTrue(_cache.NeedsTile("synth", new ZoneTileRef { Z = 0, X = 0, Y = 0, Version = 3, Sha256 = reference.Sha256 }));
            Assert.IsTrue(_cache.NeedsTile("synth", new ZoneTileRef { Z = 0, X = 0, Y = 0, Version = 2, Sha256 = "00" }));
            Assert.IsTrue(_cache.NeedsTile("synth", new ZoneTileRef { Z = 0, X = 1, Y = 0, Version = 2, Sha256 = reference.Sha256 }));
        }

        [Test]
        public void CursorsPersist()
        {
            _cache.SetCursor("zones", 42);
            var reopened = new LocalCache(_cache.Root, new JsonCatalogStore(Path.Combine(_root, "cache", "catalog.json")));
            Assert.AreEqual(42, reopened.GetCursor("zones"));
            Assert.AreEqual(0, reopened.GetCursor("changes/gale"));
        }

        private static IEnumerator Wait(Task task, bool expectFailure = false)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("task did not finish in 30 s");
                }

                yield return null;
            }

            if (task.IsFaulted && !expectFailure)
            {
                throw task.Exception!.GetBaseException();
            }
        }
    }
}
