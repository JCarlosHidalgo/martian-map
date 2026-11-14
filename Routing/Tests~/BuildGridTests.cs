using System;
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
