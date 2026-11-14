using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    public class CostParamsTests
    {
        [Test]
        public void V0P10AtTheWalkingCeilingIsValid()
        {
            Assert.DoesNotThrow(() => With(v0P10: 1.3, ceiling: 1.3));
        }

        [Test]
        public void V0P10AboveTheWalkingCeilingThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => With(v0P10: 1.31, ceiling: 1.3));
        }

        [Test]
        public void AZeroCeilingThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => With(v0P10: 0.94, ceiling: 0));
        }

        private static CostParams With(double v0P10, double ceiling) => new CostParams(
            1, new Percentiles(v0P10, 0.70, 0.56), new SensitivityRange(3.0, 2.5, 3.5), 0, ceiling,
            15, 61, 36, 192, 96, 0.6, 0.5, 2.4, 4, 10);
    }
}
