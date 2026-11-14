using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// Every number the Routing types take is finite, and CostParams keeps the ranges of
    /// cost_params.schema.json: what comes out of Routing can always be written as JSON
    /// (docs/guide/chapters/convenciones.md, "Formatos": never NaN or Infinity).
    /// </summary>
    public class FiniteTests
    {
        private const int N = TileRasters.SizePx * TileRasters.SizePx;

        private static readonly DateTime T = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void TileRastersRejectANonFiniteScaleOrOffset()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Tile(double.PositiveInfinity, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tile(double.NaN, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tile(1, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tile(1, double.NegativeInfinity));
            Assert.DoesNotThrow(() => Tile(0.0009765625, -12.5));
        }

        [Test]
        public void CostGridRejectsANonFiniteCellOriginOrHeight()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Grid(float.PositiveInfinity, new EnuPoint(0, 0), 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Grid(2f, new EnuPoint(double.NaN, 0), 0f));
            Assert.Throws<ArgumentException>(() => Grid(2f, new EnuPoint(0, 0), float.NaN));
            Assert.Throws<ArgumentException>(() => Grid(2f, new EnuPoint(0, 0), float.NegativeInfinity));
            Assert.DoesNotThrow(() => Grid(2f, new EnuPoint(-128, -128), -12.5f));
        }

        [Test]
        public void ConditionsRejectANonFiniteRateOrFactor()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Conditions(double.PositiveInfinity, Array.Empty<ConditionModifier>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConditionModifier(double.PositiveInfinity, T, T.AddHours(1)));
        }

        [Test]
        public void BudgetRejectsInfinityAndNamesTheBadArgument()
        {
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new Budget(double.PositiveInfinity, 50)).ParamName, Is.EqualTo("tS"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new Budget(14400, double.PositiveInfinity)).ParamName, Is.EqualTo("doseUGy"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new Budget(14400, -1)).ParamName, Is.EqualTo("doseUGy"));
        }

        [Test]
        public void DurationLimitRejectsAnInfiniteDose()
        {
            var c = new Conditions(6, Array.Empty<ConditionModifier>());
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.DurationLimit(c, T, double.PositiveInfinity));
        }

        [Test]
        public void AStopRejectsAnInfiniteDwell()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Stop("s1", new EnuPoint(0, 0), 5, double.PositiveInfinity));
        }

        [Test]
        public void CostParamsKeepTheRangesOfTheSchema()
        {
            Assert.DoesNotThrow(() => Params());
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(b: new SensitivityRange(-3, -3.5, -2.5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(b: new SensitivityRange(3, 0, 3.5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(b: new SensitivityRange(3, 2.5, double.PositiveInfinity)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(c: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(ceiling: double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(maxSlope: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(maxSlope: 90.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(maxSlope: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(reliefPenalty: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Params(reliefBlock: double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => Params(amberPenalty: double.PositiveInfinity));
        }

        private static TileRasters Tile(double scale, double offset) =>
            new TileRasters(new EnuPoint(0, 0), new ushort[N], scale, offset, new byte[N], new byte[N], new byte[N]);

        private static CostGrid Grid(float cellM, EnuPoint origin, float height) =>
            new CostGrid(2, 1, cellM, origin, new[] { 0f, height }, new byte[2], new byte[2], new byte[2]);

        // The values of contracts/fixtures/routing/cost_params.json, one of them changed.
        private static CostParams Params(
            SensitivityRange? b = null,
            double c = 0,
            double ceiling = 1.3,
            double maxSlope = 15,
            double reliefBlock = 61,
            double reliefPenalty = 36,
            double amberPenalty = 2.4) =>
            new CostParams(
                1,
                new Percentiles(0.94, 0.70, 0.56),
                b ?? new SensitivityRange(3.0, 2.5, 3.5),
                c,
                ceiling,
                maxSlope,
                reliefBlock,
                reliefPenalty,
                192,
                96,
                0.6,
                0.5,
                amberPenalty,
                4,
                10);
    }
}
