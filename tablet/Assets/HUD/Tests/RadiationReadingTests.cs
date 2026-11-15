using System;
using System.Collections.Generic;
using MartianMap.Terrain;
using NUnit.Framework;

namespace MartianMap.HUD
{
    public sealed class RadiationReadingTests
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        internal static ConditionsInfo Conditions() => new ConditionsInfo
        {
            SchemaVersion = 1,
            SourceId = "synthetic-radiation-test",
            TUtc = "2026-11-15T10:00:00Z",
            TValidUtc = "2026-11-15T10:00:00Z",
            Quantity = "dose_rate",
            Value = 6,
            Unit = "uGy/h",
            Uncertainty = 0.2,
            Basis = "modelled",
            DerivedFrom = "Synthetic test values.",
            Modifiers = new List<ConditionsModifier>(),
        };

        [Test]
        public void SourceRateHasUnitsUncertaintyAndExplicitBasis()
        {
            RadiationReading reading = RadiationReading.Create(Conditions(), Noon);
            Assert.That(reading.RateText, Is.EqualTo("6.0 µGy/h ± 20 %"));
            Assert.That(reading.BasisText, Is.EqualTo("MODELLED"));
            Assert.That(reading.IsModelled, Is.True);
            Assert.That(reading.DerivationText, Does.Contain("Synthetic test values."));
            var source = Conditions();
            source.Basis = "measured";
            source.Sol = 4710;
            reading = RadiationReading.Create(source, Noon);
            Assert.That(reading.BasisText, Is.EqualTo("MEASURED"));
            Assert.That(reading.IsModelled, Is.False);
            Assert.That(reading.SourceText, Does.Contain("2026-11-15 10:00 UTC · Sol 4710"));
        }

        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(3599, true)]
        [TestCase(3600, false)]
        public void ScenarioUsesTheHalfOpenUtcWindow(int seconds, bool expected)
        {
            var source = Conditions();
            source.Modifiers.Add(new ConditionsModifier
            {
                Type = "SEP", Factor = 2, ValidFrom = "2026-11-15T12:00:00Z", ValidTo = "2026-11-15T13:00:00Z",
            });
            Assert.That(RadiationReading.Create(source, Noon.AddSeconds(seconds)).IsScenario, Is.EqualTo(expected));
        }

        [Test]
        public void ForbushIsDisplayedWithoutChangingSourceOrMissionBudgets()
        {
            var source = Conditions();
            source.Modifiers.Add(new ConditionsModifier
            {
                Type = "WSAEnlilSimulations", Factor = 0.95,
                ValidFrom = "2026-11-15T10:00:00Z", ValidTo = "2026-11-15T13:00:00Z",
            });
            var reading = RadiationReading.Create(source, Noon);
            Assert.That(reading.HasForbush, Is.True);
            Assert.That(reading.ModifiersText, Does.Contain("×0.95"));
            Assert.That(reading.RateText, Does.StartWith("6.0 µGy/h"));
            Assert.That(source.Value, Is.EqualTo(6));
        }

        [Test]
        public void PendingConditionsDoNotActivateScenarioModifiers()
        {
            var source = Conditions();
            source.TValidUtc = "2026-11-15T13:00:00Z";
            source.Modifiers.Add(new ConditionsModifier
            {
                Type = "SEP", Factor = 2, ValidFrom = "2026-11-15T12:00:00Z", ValidTo = "2026-11-15T14:00:00Z",
            });
            var reading = RadiationReading.Create(source, Noon);
            Assert.That(reading.IsPending, Is.True);
            Assert.That(reading.IsScenario, Is.False);
        }

        [TestCase("2026-11-15T10:00:00.123Z")]
        [TestCase("2026-11-15T10:00:00+00:00")]
        public void UtcFractionalSecondsAndZeroOffsetsAreAccepted(string timestamp)
        {
            var source = Conditions();
            source.TUtc = timestamp;
            Assert.That(RadiationReading.Create(source, Noon).SourceText, Does.Contain("10:00 UTC"));
        }

        [TestCase("basis")]
        [TestCase("unit")]
        [TestCase("quantity")]
        [TestCase("source")]
        [TestCase("value")]
        [TestCase("uncertainty")]
        [TestCase("timestamp")]
        [TestCase("factor")]
        [TestCase("window")]
        public void InvalidConditionsAreRejected(string field)
        {
            var source = Conditions();
            switch (field)
            {
                case "basis": source.Basis = "unknown"; break;
                case "unit": source.Unit = "mSv/h"; break;
                case "quantity": source.Quantity = "temperature"; break;
                case "source": source.SourceId = null; break;
                case "value": source.Value = double.NaN; break;
                case "uncertainty": source.Uncertainty = double.PositiveInfinity; break;
                case "timestamp": source.TUtc = "2026-11-15T10:00:00+01:00"; break;
                default:
                    source.Modifiers.Add(new ConditionsModifier
                    {
                        Type = "SEP", Factor = field == "factor" ? 0 : 2,
                        ValidFrom = "2026-11-15T13:00:00Z", ValidTo = "2026-11-15T12:00:00Z",
                    });
                    break;
            }

            Assert.Throws<ArgumentException>(() => RadiationReading.Create(source, Noon));
        }

        [TestCase(2700, "45 min")]
        [TestCase(5100, "1 h 25 min")]
        [TestCase(14400, "4 h")]
        public void DurationsFollowThePresentationContract(double seconds, string expected)
        {
            Assert.That(HudFormatting.Duration(seconds), Is.EqualTo(expected));
            Assert.That(HudFormatting.Dose(1000), Is.EqualTo("1 mGy"));
        }
    }
}
