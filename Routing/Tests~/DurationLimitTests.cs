using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    public class DurationLimitTests
    {
        private static readonly DateTime T = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void IsDoseOverRateInSeconds()
        {
            Assert.That(Router.DurationLimit(new Conditions(6.0, Array.Empty<ConditionModifier>()), T, 50), Is.EqualTo(30000).Within(1e-9));
        }

        [Test]
        public void AValidFactorAboveOneShortensTheLimit()
        {
            var c = new Conditions(6.0, new[] { Modifier(2.0, -1, 1) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(15000).Within(1e-9));
        }

        [Test]
        public void AFactorBelowOneCountsAsOne()
        {
            var c = new Conditions(6.0, new[] { Modifier(0.95, -1, 1) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(30000).Within(1e-9));
        }

        [Test]
        public void AModifierOutsideItsWindowIsIgnored()
        {
            var c = new Conditions(6.0, new[] { Modifier(2.0, 1, 2), Modifier(2.0, -2, 0) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(30000).Within(1e-9));
        }

        [Test]
        public void ALocalTimeIsAnInvalidArgument()
        {
            var c = new Conditions(6.0, Array.Empty<ConditionModifier>());
            Assert.Throws<ArgumentException>(() => Router.DurationLimit(c, DateTime.SpecifyKind(T, DateTimeKind.Local), 50));
        }

        private static ConditionModifier Modifier(double factor, int fromHours, int toHours) =>
            new ConditionModifier(factor, T.AddHours(fromHours), T.AddHours(toHours));
    }
}
