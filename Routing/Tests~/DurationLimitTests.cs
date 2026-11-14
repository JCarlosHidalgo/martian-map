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
            var c = new Conditions(6.0, new[] { Modifier(2.0, -1, 10) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(15000).Within(1e-9));
        }

        [Test]
        public void AFactorBelowOneCountsAsOne()
        {
            var c = new Conditions(6.0, new[] { Modifier(0.95, -1, 1) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(30000).Within(1e-9));
        }

        [Test]
        public void AModifierThatEndedAtTheStartIsIgnored()
        {
            // [valid_from, valid_to): it ends exactly at T.
            var c = new Conditions(6.0, new[] { Modifier(2.0, -2, 0) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(30000).Within(1e-9));
        }

        [Test]
        public void AModifierThatStartsAtTheStartCounts()
        {
            var c = new Conditions(6.0, new[] { Modifier(2.0, 0, 10) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(15000).Within(1e-9));
        }

        [Test]
        public void ASepThatStartsDuringTheEvaShortensTheLimit()
        {
            // 6 uGy in the first hour, 12 in the second, then 32 at 6 uGy/h: 7 h 20 min.
            var c = new Conditions(6.0, new[] { Modifier(2.0, 1, 2) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(26400).Within(1e-9));
        }

        [Test]
        public void AModifierThatEndsDuringTheEvaCountsUntilItEnds()
        {
            // 18 uGy in the first hour, then 32 at 6 uGy/h: 6 h 20 min.
            var c = new Conditions(6.0, new[] { Modifier(3.0, -1, 1) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(22800).Within(1e-9));
        }

        [Test]
        public void AModifierThatStartsAfterTheLimitIsIgnored()
        {
            var c = new Conditions(6.0, new[] { Modifier(2.0, 9, 10) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(30000).Within(1e-9));
        }

        [Test]
        public void OverlappingModifiersMultiply()
        {
            // 0–1 h at 12, 1–2 h at 18, 2–3 h at 9 (39 uGy), then 11 at 6 uGy/h: 4 h 50 min.
            var c = new Conditions(6.0, new[] { Modifier(2.0, 0, 2), Modifier(1.5, 1, 3) });
            Assert.That(Router.DurationLimit(c, T, 50), Is.EqualTo(17400).Within(1e-9));
        }

        [Test]
        public void TheDoseIsTheIntegralOfTheSameRate()
        {
            var c = new Conditions(6.0, new[] { Modifier(2.0, 1, 2) });
            Assert.That(Router.DoseUGy(c, T, 3 * 3600), Is.EqualTo(24).Within(1e-9));
            Assert.That(Router.DoseUGy(c, T, 1.5 * 3600), Is.EqualTo(12).Within(1e-9));
            Assert.That(Router.DoseUGy(c, T, 0), Is.EqualTo(0));
            Assert.That(Router.DoseUGy(c, T, 26400), Is.EqualTo(50).Within(1e-9));
        }

        [Test]
        public void ANegativeDurationIsAnInvalidArgument()
        {
            var c = new Conditions(6.0, Array.Empty<ConditionModifier>());
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.DoseUGy(c, T, -1));
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
