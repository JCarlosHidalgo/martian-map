using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MartianMap.Terrain.Tests
{
    /// <summary>
    /// The consumer side of C1: P1's <c>tile_synth_0_0_0</c> read by Terrain, checked against the
    /// shared synthetic coordinates of the guide (Convenciones, "Cómo se fija un contrato"). Zone
    /// <c>synth</c>: one 256 m tile with its SW corner at ENU (-128, -128); local (x, y) = enu + 128.
    /// </summary>
    public sealed class ContractTileTests
    {
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "contracts", "fixtures"));

        private LoadedZone _zone;
        private TileData _tile;

        [UnitySetUp]
        public IEnumerator Load()
        {
            var task = FixtureLoader.LoadSynthZoneAsync(Root);
            yield return Wait(task);
            _zone = task.Result;
            _tile = _zone.Tiles[0];
        }

        [Test]
        public void HeightIsTheGuideRamp()
        {
            // 20 · (e + 128) / 256 + 5 · (n + 128) / 256: 0 at the SW corner, 25 at the NE corner.
            AssertHeight(-128, -128, 0.0f);
            AssertHeight(128, 128, 25.0f);
            AssertHeight(0, 0, 12.5f);
            AssertHeight(-100, -20, 20f * 28 / 256 + 5f * 108 / 256);
            AssertHeight(100, -20, 20f * 228 / 256 + 5f * 108 / 256);
            Assert.AreEqual(0.0009765625, _tile.Info.Height.Scale, 1e-12, "height.scale = 1/1024");
        }

        [Test]
        public void HazardRegionsAreWhereTheGuideSays()
        {
            Assert.AreEqual(255, HazardAt(70, 70), "red: e 40…100, n 40…100");
            Assert.AreEqual(128, HazardAt(-80, -80), "amber: e −100…−60, n −100…−60");
            Assert.AreEqual(0, HazardAt(0, 0), "clear in the middle");
            Assert.AreEqual(0, HazardAt(70, -80), "clear in the SE quadrant away from nodata");
            Assert.AreEqual(255, HazardAt(120, -120), "hazard is 255 in the nodata square");
        }

        [Test]
        public void LayersFollowTheGuide()
        {
            var ortho = _tile.Layers[LayerIds.Ortho];
            var thermal = _tile.Layers[LayerIds.Thermal];
            var thermalIr = _tile.Layers[LayerIds.ThermalIr];

            for (var r = 0; r < 200; r += 37)
            {
                for (var c = 0; c < 200; c += 41)
                {
                    var i = r * TileData.Size + c;
                    Assert.AreEqual(Mathf.Min(c, 255), ortho[i], $"ortho = min(c, 255) at r={r} c={c}");
                    Assert.AreEqual(Mathf.Min(256 - r, 255), thermal[i], $"thermal = min(256 − r, 255) at r={r} c={c}");
                    var square = (r / 32 + c / 32) % 2;
                    Assert.Contains((int)thermalIr[i], new[] { 64, 192 }, "thermal_ir is a 64/192 checkerboard");
                    Assert.AreEqual(thermalIr[r * TileData.Size + (c / 32) * 32], thermalIr[i], "one value per 32-sample square");
                    Assert.AreNotEqual(thermalIr[i], thermalIr[r * TileData.Size + ((c / 32 + 1) % 8) * 32], $"neighbouring squares differ (square parity {square})");
                }
            }
        }

        [Test]
        public void EveryFileMatchesItsSha256()
        {
            var folder = Path.Combine(Root, FixtureLoader.SynthTileFolder);
            foreach (var expected in _tile.Info.Sha256)
            {
                var bytes = File.ReadAllBytes(Path.Combine(folder, expected.Key));
                Assert.AreEqual(expected.Value, Sha256.Hex(bytes), expected.Key);
            }

            Assert.AreEqual(7, _tile.Info.Sha256.Count, "height, slope, roughness, hazard and three layers");
        }

        [Test]
        public void ZoneIsSynthCentredOnTheOrigin()
        {
            var zone = _zone.Zone;
            Assert.AreEqual("synth", zone.ZoneId);
            CollectionAssert.AreEqual(new[] { -128.0, -128.0, 128.0, 128.0 }, zone.ExtentEnuM);
            CollectionAssert.AreEqual(new[] { -100.0, -20.0 }, zone.HabitatEnuM);
            Assert.AreEqual(12.5, zone.OriginHeightM, 1e-9);
            Assert.AreEqual(new Vector2(-128f, -128f), _tile.OriginEnuM, "tile SW corner");
        }

        [Test]
        [Ignore("P1: tile_synth_0_0_0/height.r16 has no nodata yet; the 16 × 16 SE square must be 65535 (Convenciones, \"Formatos\"). Enable when the fixture is fixed.")]
        public void HeightHasTheNodataSquareInTheSouthEast()
        {
            for (var r = TileData.Size - 16; r < TileData.Size; r++)
            {
                for (var c = TileData.Size - 16; c < TileData.Size; c++)
                {
                    Assert.IsFalse(_tile.TryHeightAtSample(r, c, out _), $"sample r={r} c={c} should be nodata");
                }
            }

            Assert.IsTrue(_tile.TryHeightAtSample(TileData.Size - 17, TileData.Size - 1, out _), "just north of the square has data");
        }

        private void AssertHeight(float e, float n, float expected)
        {
            Assert.IsTrue(_tile.TryHeightAtLocal(e + 128f, n + 128f, out var metres), $"no height at e={e} n={n}");
            Assert.AreEqual(expected, metres, 0.002f, $"height at e={e} n={n}");
        }

        private byte HazardAt(int e, int n)
        {
            var col = e + 128;
            var row = TileData.Cells - (n + 128); // row 0 is the north edge
            return _tile.Hazard[row * TileData.Size + col];
        }

        private static IEnumerator Wait(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("loading the contract tile took more than 30 s");
                }

                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception!.GetBaseException();
            }
        }
    }
}
