using NUnit.Framework;

namespace MartianMap.Terrain.Tests
{
    /// <summary>
    /// The layers reach routing as soft-ground hazard. The fixture-like tile has thermal =
    /// north·255 (TI = 100 + 2·sample, so TI &lt; 150 in the southern ~10 %) and a 230/40
    /// thermal IR checkerboard (half dark, so the darkest 10 % is every dark square).
    /// </summary>
    public sealed class LayerHazardTests
    {
        private static int Index(int row, int col) => row * TileData.Size + col;

        [Test]
        public void LowThermalInertiaBecomesAmber()
        {
            var zone = SyntheticZone.CreateFixtureLike();
            var tile = zone.Tiles[0];
            Assert.AreEqual(0, tile.Hazard[Index(250, 10)], "south-west starts clear");

            var changed = LayerHazard.Apply(zone);

            Assert.AreEqual(1, changed.Count);
            Assert.AreEqual(LayerHazard.SoftGround, tile.Hazard[Index(250, 10)], "TI ≈ 106: soft ground");
            Assert.AreEqual(0, tile.Hazard[Index(10, 10)], "TI ≈ 600 in the north: firm ground");
            Assert.AreEqual(0, tile.BaseHazard[Index(250, 10)], "Earth's hazard is untouched");
        }

        [Test]
        public void QualitativeThermalInertiaMarksTheLowestTenPercent()
        {
            var zone = SyntheticZone.CreateFixtureLike();
            var layer = zone.Zone.Layers.Find(l => l.Id == LayerIds.Thermal);
            layer.Qualitative = true;
            layer.Scale = null;
            layer.Offset = null;
            var tile = zone.Tiles[0];

            LayerHazard.Apply(zone);

            // thermal = north·255: the southern rows are the lowest relative inertia.
            Assert.AreEqual(LayerHazard.SoftGround, tile.Hazard[Index(240, 10)], "lowest relative TI: soft ground");
            Assert.AreEqual(0, tile.Hazard[Index(128, 10)], "median TI: firm ground");
            Assert.AreEqual(0, tile.Hazard[Index(10, 10)], "highest TI: firm ground");
        }

        [Test]
        public void NightThermalIrIsTheFallbackWithoutThermalInertia()
        {
            var zone = SyntheticZone.CreateFixtureLike();
            zone.Zone.Layers.RemoveAll(l => l.Id == LayerIds.Thermal);
            var tile = zone.Tiles[0];

            LayerHazard.Apply(zone);

            // Checkerboard of 32 samples: (0, 0) is warm (230), (0, 32) is cold (40).
            Assert.AreEqual(LayerHazard.SoftGround, tile.Hazard[Index(0, 32)], "coldest night IR: soft ground");
            Assert.AreEqual(0, tile.Hazard[Index(0, 0)], "warm night IR: firm ground");
        }

        [Test]
        public void WithoutThermalLayersNothingIsAdded()
        {
            var zone = SyntheticZone.CreateFixtureLike();
            zone.Zone.Layers.RemoveAll(l => l.Id == LayerIds.Thermal || l.Id == LayerIds.ThermalIr);
            var tile = zone.Tiles[0];
            var before = (byte[])tile.Hazard.Clone();

            LayerHazard.Apply(zone);

            CollectionAssert.AreEqual(before, tile.Hazard);
        }
    }
}
