using System;
using System.Collections.Generic;
using System.Globalization;
using MartianMap.Terrain;

namespace MartianMap.HUD
{
    /// <summary>Immutable presentation of a C6 source rate and active modifiers; does not compute routing limits.</summary>
    public sealed class RadiationReading
    {
        private RadiationReading() { }

        public string RateText { get; private set; }
        public string BasisText { get; private set; }
        public string SourceText { get; private set; }
        public string ModifiersText { get; private set; }
        public string DerivationText { get; private set; }
        public bool IsModelled { get; private set; }
        public bool IsScenario { get; private set; }
        public bool IsPending { get; private set; }
        public bool HasForbush { get; private set; }

        public static RadiationReading Create(ConditionsInfo source, DateTime nowUtc)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (nowUtc.Kind != DateTimeKind.Utc || source.SchemaVersion != 1 || source.Quantity != "dose_rate" ||
                source.Unit != "uGy/h" || string.IsNullOrWhiteSpace(source.SourceId) ||
                (source.Basis != "measured" && source.Basis != "modelled") ||
                !Finite(source.Value) || source.Value <= 0 || !Finite(source.Uncertainty * 100) || source.Uncertainty < 0 ||
                (source.Sol.HasValue && source.Sol.Value < 0) || source.Modifiers == null)
            {
                throw new ArgumentException("Radiation conditions have invalid units, basis, source, or numeric values.");
            }

            DateTime observed = Utc(source.TUtc);
            DateTime applies = Utc(source.TValidUtc);
            var reading = new RadiationReading
            {
                RateText = HudFormatting.Format(HudStrings.RadiationValue, source.Value, source.Uncertainty * 100),
                IsModelled = source.Basis == "modelled",
                IsPending = nowUtc < applies,
                BasisText = source.Basis == "modelled" ? HudStrings.Modelled : HudStrings.Measured,
                SourceText = HudFormatting.Format(HudStrings.RadiationSource, source.SourceId,
                    observed.ToString(HudStrings.Timestamp, CultureInfo.InvariantCulture),
                    source.Sol.HasValue ? HudFormatting.Format(HudStrings.Sol, source.Sol.Value) : string.Empty,
                    applies.ToString(HudStrings.Timestamp, CultureInfo.InvariantCulture)),
                DerivationText = string.IsNullOrWhiteSpace(source.DerivedFrom) ? HudStrings.DerivationMissing
                    : HudFormatting.Format(HudStrings.DerivedFrom, source.DerivedFrom),
            };
            var active = new List<string>();
            foreach (ConditionsModifier modifier in source.Modifiers)
            {
                if (modifier == null || !Finite(modifier.Factor) || modifier.Factor <= 0 || string.IsNullOrWhiteSpace(modifier.Type))
                {
                    throw new ArgumentException("Radiation modifier must supply a type and a finite positive factor.");
                }

                DateTime from = Utc(modifier.ValidFrom);
                DateTime to = Utc(modifier.ValidTo);
                if (from >= to)
                {
                    throw new ArgumentException("Radiation modifier requires valid_from before valid_to.");
                }

                if (!reading.IsPending && nowUtc >= from && nowUtc < to)
                {
                    active.Add(HudFormatting.Format(HudStrings.Modifier, modifier.Type, modifier.Factor));
                    reading.IsScenario |= modifier.Type == "SEP";
                    reading.HasForbush |= modifier.Factor < 1;
                }
            }

            reading.ModifiersText = active.Count == 0 ? HudStrings.NoModifiers
                : HudFormatting.Format(HudStrings.ActiveModifiers, string.Join("\n", active));
            return reading;
        }

        private static DateTime Utc(string text)
        {
            string[] formats = { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" };
            if (text == null || (!text.EndsWith("Z", StringComparison.Ordinal) && !text.EndsWith("+00:00", StringComparison.Ordinal)) ||
                !DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime value))
            {
                throw new ArgumentException("Radiation timestamps must be ISO-8601 UTC.");
            }

            return value;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
