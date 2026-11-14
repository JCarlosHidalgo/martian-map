using NUnit.Framework;
using UnityEngine;

namespace MartianMap.Terrain.Tests
{
    public sealed class HazardShapesTests
    {
        [Test]
        public void SquareIsFilledInsideOnlyAndRowsAreNorthUp()
        {
            var tile = SyntheticZone.CreateFixtureLike().Tiles[0];
            // A square in the south-west quarter of the 256 m tile: e 10..50, n 10..50.
            var square = new[] { new Vector2(10, 10), new Vector2(10, 50), new Vector2(50, 50), new Vector2(50, 10) };
            var overlay = HazardShapes.Rasterize(tile, new[] { new HazardPolygon(new[] { square }, 200) });

            Assert.NotNull(overlay);
            Assert.AreEqual(200, overlay[Index(n: 30, e: 30)], "inside");
            Assert.AreEqual(0, overlay[Index(n: 100, e: 30)], "north of it");
            Assert.AreEqual(0, overlay[Index(n: 30, e: 100)], "east of it");
        }

        [Test]
        public void HoleStaysEmptyAndMaxWins()
        {
            var tile = SyntheticZone.CreateFixtureLike().Tiles[0];
            var outer = new[] { new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0) };
            var hole = new[] { new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60) };
            var polygons = new[]
            {
                new HazardPolygon(new[] { outer, hole }, 128),
                new HazardPolygon(new[] { new[] { new Vector2(5, 5), new Vector2(5, 20), new Vector2(20, 20), new Vector2(20, 5) } }, 255),
            };

            var overlay = HazardShapes.Rasterize(tile, polygons);

            Assert.AreEqual(0, overlay[Index(n: 50, e: 50)], "hole");
            Assert.AreEqual(128, overlay[Index(n: 80, e: 80)], "ring");
            Assert.AreEqual(255, overlay[Index(n: 10, e: 10)], "max of both");
        }

        [Test]
        public void OverlayMergesWithMaxAndCanBeRemoved()
        {
            var tile = SyntheticZone.CreateFixtureLike().Tiles[0];
            var overlay = new byte[TileData.Size * TileData.Size];
            overlay[0] = 77;
            var baseValue = tile.BaseHazard[0];

            tile.SetHazardOverlay(overlay);
            Assert.AreEqual(System.Math.Max(baseValue, (byte)77), tile.Hazard[0]);

            tile.SetHazardOverlay(null);
            Assert.AreEqual(baseValue, tile.Hazard[0]);
        }

        // Sample index of a point given in metres from the tile's SW corner (1 m/px, row 0 = north).
        private static int Index(int n, int e) => (TileData.Cells - n) * TileData.Size + e;
    }
}
