using System;
using MartianMap.Routing.FixtureGen;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    public class CostTests
    {
        private static CostParams P => Fixtures.CostParams;

        [Test]
        public void FlatGroundAtP50Is042MetresPerSecond()
        {
            // 0.70 × 0.6 (docs/guide/chapters/convenciones.md, "Valores fijados", Costo).
            Assert.That(Cost.SpeedMps(0, 0, P.V0Mps.P50, P), Is.EqualTo(0.42).Within(1e-12));
        }

        [Test]
        public void SymmetricToblerGivesTheSameSpeedUpAndDown()
        {
            Assert.That(Cost.SpeedMps(0.1, 0, P.V0Mps.P50, P), Is.EqualTo(Cost.SpeedMps(-0.1, 0, P.V0Mps.P50, P)));
            Assert.That(Cost.SpeedMps(0.1, 0, P.V0Mps.P50, P), Is.EqualTo(0.42 * Math.Exp(-0.3)).Within(1e-12));
        }

        [TestCase((byte)0, 1.0)]
        [TestCase((byte)36, 1.0)]
        [TestCase((byte)61, 0.5)]
        public void FRugIsOneUpTo36CentimetresAndHalfAt61(byte reliefCm, double expected)
        {
            Assert.That(Cost.FRug(reliefCm, P), Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public void FRugIsLinearBetween36And61Centimetres()
        {
            Assert.That(Cost.FRug(46, P), Is.EqualTo(1.0 - 0.5 * 10 / 25).Within(1e-12));
        }

        [TestCase((byte)15, (byte)0, (byte)0, false)]
        [TestCase((byte)16, (byte)0, (byte)0, true)]
        [TestCase((byte)0, (byte)61, (byte)0, false)]
        [TestCase((byte)0, (byte)62, (byte)0, true)]
        [TestCase((byte)0, (byte)0, (byte)191, false)]
        [TestCase((byte)0, (byte)0, (byte)192, true)]
        public void BlocksAboveThresholdsAndOnRed(byte slopeDeg, byte reliefCm, byte hazard, bool blocked)
        {
            Assert.That(Cost.IsBlocked(slopeDeg, reliefCm, hazard, P), Is.EqualTo(blocked));
        }

        [TestCase((byte)95, false)]
        [TestCase((byte)96, true)]
        [TestCase((byte)191, true)]
        [TestCase((byte)192, false)]
        public void AmberIsFrom96UpToRed(byte hazard, bool amber)
        {
            Assert.That(Cost.IsAmber(hazard, P), Is.EqualTo(amber));
        }

        [Test]
        public void AmberPenaltyDoublesTheFlatGroundTime()
        {
            // 2.4 s/m in amber doubles the 1/0.42 s/m of flat ground (Valores fijados).
            Grid g = FlatPair(hazard: 128);
            double plain = Cost.EdgeTimeS(g, 0, 1, P.V0Mps.P50, P, withAmberPenalty: false);
            double penalised = Cost.EdgeTimeS(g, 0, 1, P.V0Mps.P50, P, withAmberPenalty: true);
            Assert.That(plain, Is.EqualTo(2 / 0.42).Within(1e-9));
            Assert.That(penalised, Is.EqualTo(plain + 2 * 2.4).Within(1e-9));
            Assert.That(Cost.EdgeAmberM(g, 0, 1, P), Is.EqualTo(2).Within(1e-12));
        }

        [Test]
        public void AnEdgeToABlockedNodeIsInfinite()
        {
            Grid g = FlatPair(hazard: 255);
            Assert.That(Cost.EdgeTimeS(g, 0, 1, P.V0Mps.P50, P, withAmberPenalty: false), Is.EqualTo(double.PositiveInfinity));
        }

        private static Grid FlatPair(byte hazard) =>
            new Grid(2, 1, 2f, new EnuPoint(0, 0), new float[2], new byte[2], new byte[2], new[] { hazard, hazard });
    }
}
