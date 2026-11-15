using System;
using System.Globalization;

namespace MartianMap.HUD
{
    public static class HudFormatting
    {
        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                return HudStrings.Unavailable;
            }

            double minutes = Math.Ceiling(seconds / 60);
            double hours = Math.Floor(minutes / 60);
            double remainder = minutes % 60;
            return hours == 0 ? Format(HudStrings.Minutes, minutes)
                : remainder == 0 ? Format(HudStrings.Hours, hours)
                : Format(HudStrings.HoursMinutes, hours, remainder);
        }

        public static string Dose(double microgray) => Format(
            microgray >= 1000 ? HudStrings.Milligray : HudStrings.Microgray,
            microgray >= 1000 ? microgray / 1000 : microgray);

        public static string Format(string template, params object[] values) =>
            string.Format(CultureInfo.InvariantCulture, template, values);
    }
}
