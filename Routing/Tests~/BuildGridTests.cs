using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="Router.BuildGrid"/> (docs/guide/chapters/tablet.md, "API de Routing", and P9).
    /// Literals from ~/scratch/c4-verify/oracle/buildgrid_oracle.py (not committed), which follows
    /// the guide's rule sample by sample, looking each sample up in every tile.
    /// </summary>
    public class BuildGridTests
    {
        private static CostParams P => Fixtures.CostParams;

        private static int Node(CostGrid g, int i, int j) => g.Index(i, j);

        // cases_buildgrid.py t1: tile at (0, 0), h = 200r + c, slope = (7r + 3c) mod 50,
        // roughness = (r + 2c) mod 40, hazard 200 at (127, 129) only; origin_height_m = 10.
        private static TileRasters T1 => Tiles.Make(
            0, 0,
            (r, c) => (ushort)(r * 200 + c),
            (r, c) => (byte)((r * 7 + c * 3) % 50),
            (r, c) => (byte)((r + 2 * c) % 40),
            (r, c) => (byte)(r == 127 && c == 129 ? 200 : 0));

        [TestCase(64, 64, 15.125f, 40, 27, 200)]
        [TestCase(0, 0, 40.0f, 45, 18, 0)]
        [TestCase(128, 128, -9.75f, 25, 33, 0)]
        [TestCase(0, 128, -10.0f, 10, 3, 0)]
        [TestCase(37, 91, 4.525390625f, 47, 25, 0)]
        public void ANodeTakesItsSampleAndTheMaxOfItsWindow(int i, int j, float heightM, int slope, int rough, int hazard)
        {
            CostGrid g = Router.BuildGrid(new[] { T1 }, 10.0, P);
            int n = Node(g, i, j);
            Assert.That(g.HeightM[n], Is.EqualTo(heightM));
            Assert.That(g.SlopeDeg[n], Is.EqualTo(slope));
            Assert.That(g.RoughnessCm[n], Is.EqualTo(rough));
            Assert.That(g.Hazard[n], Is.EqualTo(hazard));
        }

        [Test]
        public void TheWholeOneTileGridMatchesTheOracle()
        {
            CostGrid g = Router.BuildGrid(new[] { T1 }, 10.0, P);
            Assert.That(g.Width, Is.EqualTo(129));
            Assert.That(g.Height, Is.EqualTo(129));
            Assert.That(g.CellM, Is.EqualTo(2f));
            Assert.That(Tiles.Sha256(Tiles.Serialize(g)), Is.EqualTo("667a77ace1895f6e5313dbe3d0debb8605095ee4b498f3889b4ea16ee5a7a034"));
        }

        [Test]
        public void TheHeightIsScaleTimesSamplePlusOffsetMinusTheOrigin()
        {
            // By hand: 0.25 · 1000 − 3 − 1.5 = 245.5, at sample (0, 0), the north-west node (0, 128).
            TileRasters t = Tiles.Make(0, 0, (r, c) => (ushort)(r == 0 && c == 0 ? 1000 : 0), scale: 0.25, offset: -3);
            CostGrid g = Router.BuildGrid(new[] { t }, 1.5, P);
            Assert.That(g.HeightM[Node(g, 0, 128)], Is.EqualTo(245.5f));
        }

        [Test]
        public void ANodataNodeIsZeroAndBlocked()
        {
            // Sample (256, 0) is the south-west corner: node (0, 0).
            TileRasters t = Tiles.Make(0, 0, (r, c) => (ushort)(r == 256 && c == 0 ? TileRasters.HeightNodata : 4000));
            CostGrid g = Router.BuildGrid(new[] { t }, 0, P);
            Assert.That(g.HeightM[Node(g, 0, 0)], Is.EqualTo(0f));
            Assert.That(g.Hazard[Node(g, 0, 0)], Is.EqualTo(255));
            Assert.That(g.Hazard[Node(g, 1, 0)], Is.EqualTo(0));
        }

        [Test]
        public void ThePerimeterIsNotNodata()
        {
            // P9.1: window samples outside every tile are skipped, not counted as nodata.
            CostGrid g = Router.BuildGrid(new[] { Tiles.Make(0, 0, (r, c) => 0) }, 0, P);
            int blocked = 0;
            foreach (byte h in g.Hazard)
            {
                blocked += h == 0 ? 0 : 1;
            }

            Assert.That(blocked, Is.EqualTo(0));
        }

        private static TileRasters Flat(double e0, double n0, ushort h) => Tiles.Make(e0, n0, (r, c) => h);

        [Test]
        public void TheGridCoversTheBoxOfTheTiles()
        {
            CostGrid g = Router.BuildGrid(new[] { Flat(256, 256, 0), Flat(0, 0, 0) }, 0, P);
            Assert.That(g.Width, Is.EqualTo(257));
            Assert.That(g.Height, Is.EqualTo(257));
            Assert.That(g.CellM, Is.EqualTo(2f));
            Assert.That(g.OriginEnuM.E, Is.EqualTo(0));
            Assert.That(g.OriginEnuM.N, Is.EqualTo(0));
        }

        [Test]
        public void ANodeInNoTileIsNodata()
        {
            // cases_buildgrid.py t2, "L": tiles at (0, 0), (256, 0) and (0, 256), all samples 1024 (1 m).
            CostGrid g = Router.BuildGrid(new[] { Flat(0, 0, 1024), Flat(256, 0, 1024), Flat(0, 256, 1024) }, 0, P);
            foreach ((int i, int j) in new[] { (129, 129), (256, 256) })
            {
                Assert.That(g.HeightM[Node(g, i, j)], Is.EqualTo(0f), $"({i}, {j})");
                Assert.That(g.Hazard[Node(g, i, j)], Is.EqualTo(255), $"({i}, {j})");
            }

            // The edges of the present tiles are theirs: their window samples in the missing tile are skipped.
            foreach ((int i, int j) in new[] { (128, 129), (129, 128), (128, 128), (128, 256), (256, 128) })
            {
                Assert.That(g.HeightM[Node(g, i, j)], Is.EqualTo(1f), $"({i}, {j})");
                Assert.That(g.Hazard[Node(g, i, j)], Is.EqualTo(0), $"({i}, {j})");
            }
        }

        [Test]
        public void ASharedEdgeTakesTheMaximumAndAnyNodata()
        {
            // cases_buildgrid.py t2, "edge": the west tile's column c = 256 is the east tile's c = 0.
            // West: slope 30, hazard 10 on it, height 100 at r = 128. East: slope 5, hazard 90 on it,
            // height 200 at r = 128 and nodata at r = 64. origin_height_m = 2.
            TileRasters west = Tiles.Make(
                0, 0,
                (r, c) => (ushort)(c == 256 && r == 128 ? 100 : 0),
                (r, c) => (byte)(c == 256 ? 30 : 0),
                hazard: (r, c) => (byte)(c == 256 ? 10 : 0));
            TileRasters east = Tiles.Make(
                256, 0,
                (r, c) => c == 0 && r == 128 ? (ushort)200 : c == 0 && r == 64 ? TileRasters.HeightNodata : (ushort)0,
                (r, c) => (byte)(c == 0 ? 5 : 0),
                hazard: (r, c) => (byte)(c == 0 ? 90 : 0));
            CostGrid g = Router.BuildGrid(new[] { east, west }, 2, P);

            int n = Node(g, 128, 64);
            Assert.That(g.HeightM[n], Is.EqualTo(-1.90234375f), "the west tile's 100 / 1024 − 2");
            Assert.That(g.SlopeDeg[n], Is.EqualTo(30));
            Assert.That(g.Hazard[n], Is.EqualTo(90));

            int nodata = Node(g, 128, 96);
            Assert.That(g.HeightM[nodata], Is.EqualTo(0f));
            Assert.That(g.Hazard[nodata], Is.EqualTo(255));
            Assert.That(g.SlopeDeg[nodata], Is.EqualTo(30));

            Assert.That(g.SlopeDeg[Node(g, 127, 64)], Is.EqualTo(0), "its window stops at c = 255");
            Assert.That(g.Hazard[Node(g, 129, 64)], Is.EqualTo(0), "its window starts at the east tile's c = 1");
        }

        [TestCase(128, 128, 1f)]
        [TestCase(64, 128, 1f)]
        [TestCase(192, 128, 2f)]
        [TestCase(128, 64, 1f)]
        [TestCase(128, 192, 3f)]
        [TestCase(200, 200, 4f)]
        public void TheHeightComesFromTheSouthWestmostTile(int i, int j, float heightM)
        {
            // cases_buildgrid.py t2, "four": 2 × 2 tiles at 1, 2, 3 and 4 m (SW, SE, NW, NE).
            CostGrid g = Router.BuildGrid(new[] { Flat(256, 256, 4096), Flat(0, 256, 3072), Flat(256, 0, 2048), Flat(0, 0, 1024) }, 0, P);
            Assert.That(g.HeightM[Node(g, i, j)], Is.EqualTo(heightM));
        }

        [Test]
        public void TheGridDoesNotDependOnTheOrderOfTheTiles()
        {
            // cases_buildgrid.py t2, "three"; origin_height_m = 7.25.
            TileRasters[] three =
            {
                Tiles.Make(0, 0, (r, c) => (ushort)((3 * r + 5 * c) % 60000), (r, c) => (byte)(r * c % 37), (r, c) => (byte)((r ^ c) % 23), (r, c) => (byte)((7 * r + c) % 256)),
                Tiles.Make(256, 0, (r, c) => (ushort)((11 * r + c) % 60000), (r, c) => (byte)((r + c) % 41), (r, c) => (byte)((3 * r + c) % 29), (r, c) => (byte)((r * 13 + c * 5) % 256)),
                Tiles.Make(256, 256, (r, c) => (ushort)(r * c % 60000), (r, c) => (byte)((2 * r + 3 * c) % 47), (r, c) => (byte)((r * 5 + c) % 31), (r, c) => (byte)((r + c * 9) % 256)),
            };
            int[][] permutations = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            foreach (int[] perm in permutations)
            {
                CostGrid g = Router.BuildGrid(new[] { three[perm[0]], three[perm[1]], three[perm[2]] }, 7.25, P);
                Assert.That(Tiles.Sha256(Tiles.Serialize(g)), Is.EqualTo("25ab5d16137b70ad92763e18a6d6c3da650a414d3c98e127c1f476e3b13b1234"), string.Join(",", perm));
            }
        }

        [Test]
        public void ABoxTooLargeForAnIntThrows()
        {
            const double Far = 256.0 * (1 << 20);
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { Flat(0, 0, 0), Flat(Far, Far, 0) }, 0, P));
        }

        [Test]
        [Ignore("Pending P1: height.r16 of tile_synth_0_0_0 on main has no nodata yet (64 nodes differ).")]
        public void TheSynthTileOfP1GivesCostgridSynth()
        {
            // convenciones.md, "Cómo se fija un contrato": BuildGrid on P1's synthetic tile gives,
            // byte for byte, costgrid_synth.bin. Runs once P1's tile is in contracts/fixtures/.
            string dir = Path.Combine(Path.GetDirectoryName(Fixtures.Dir), "tile_synth_0_0_0");
            if (!Directory.Exists(dir))
            {
                Assert.Ignore("Pending P1: contracts/fixtures/tile_synth_0_0_0/ is not here yet.");
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tile.json")));
            JsonElement t = doc.RootElement;
            JsonElement h = t.GetProperty("height");
            byte[] raw = File.ReadAllBytes(Path.Combine(dir, h.GetProperty("file").GetString()));
            var height = new ushort[raw.Length / 2];
            Buffer.BlockCopy(raw, 0, height, 0, raw.Length);
            var tile = new TileRasters(
                new EnuPoint(t.GetProperty("origin_enu_m")[0].GetDouble(), t.GetProperty("origin_enu_m")[1].GetDouble()),
                height,
                h.GetProperty("scale").GetDouble(),
                h.GetProperty("offset").GetDouble(),
                File.ReadAllBytes(Path.Combine(dir, "slope.r8")),
                File.ReadAllBytes(Path.Combine(dir, "roughness.r8")),
                File.ReadAllBytes(Path.Combine(dir, "hazard.r8")));

            CostGrid g = Router.BuildGrid(new[] { tile }, 12.5, P);
            byte[] expected = File.ReadAllBytes(Path.Combine(Fixtures.Dir, "costgrid_synth.bin"));
            byte[] got = Tiles.Serialize(g);
            Assert.That(got.Length, Is.EqualTo(expected.Length));
            var differing = new System.Collections.Generic.List<string>();
            for (int k = 0; k < g.Width * g.Height; k++)
            {
                if (!SameNode(got, expected, k, g.Width * g.Height))
                {
                    differing.Add($"({k % g.Width}, {k / g.Width})");
                }
            }

            Assert.That(differing, Is.Empty, $"{differing.Count} nodes differ from costgrid_synth.bin");
            Assert.That(got, Is.EqualTo(expected));
        }

        private static bool SameNode(byte[] a, byte[] b, int k, int count)
        {
            const int Header = 28;
            for (int o = 0; o < 4; o++)
            {
                if (a[Header + 4 * k + o] != b[Header + 4 * k + o])
                {
                    return false;
                }
            }

            for (int array = 0; array < 3; array++)
            {
                int at = Header + 4 * count + array * count + k;
                if (a[at] != b[at])
                {
                    return false;
                }
            }

            return true;
        }

        [Test]
        public void ArgumentsAreChecked()
        {
            TileRasters a = Tiles.Make(-128, -128, (r, c) => 0);
            Assert.Throws<ArgumentNullException>(() => Router.BuildGrid(null, 0, P));
            Assert.Throws<ArgumentNullException>(() => Router.BuildGrid(new TileRasters[] { null }, 0, P));
            Assert.Throws<ArgumentNullException>(() => Router.BuildGrid(new[] { a }, 0, null));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new TileRasters[0], 0, P));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { a }, double.NaN, P));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { a }, double.PositiveInfinity, P));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { a, Tiles.Make(-128, -128, (r, c) => 1) }, 0, P));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { a, Tiles.Make(-128, -100, (r, c) => 0) }, 0, P));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { a, Tiles.Make(128.5, -128, (r, c) => 0) }, 0, P));
            Assert.Throws<ArgumentException>(() => Router.BuildGrid(new[] { a, Tiles.Make(double.NaN, -128, (r, c) => 0) }, 0, P));
        }
    }
}
