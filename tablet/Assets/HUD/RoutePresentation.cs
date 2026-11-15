using MartianMap.Routing;
using MartianMap.Terrain;

namespace MartianMap.HUD
{
    /// <summary>Formats C4 results and aggregates existing leg metrics without calculating routes.</summary>
    public static class RoutePresentation
    {
        public static string Name(Alternative alternative) => alternative == Alternative.Fastest ? HudStrings.Fastest
            : alternative == Alternative.Safest ? HudStrings.Safest : HudStrings.Gentlest;

        public static string Details(Plan plan)
        {
            if (plan == null || plan.Legs.Count == 0 || !Valid(plan.TotalS.P10) ||
                !Valid(plan.TotalS.P50) || !Valid(plan.TotalS.P90) || !Valid(plan.DoseUGy))
            {
                return HudStrings.RouteUnavailable;
            }

            double amber = 0;
            double heightChange = 0;
            foreach (Route leg in plan.Legs)
            {
                if (leg == null || !leg.Found || !Valid(leg.AmberM) || !Valid(leg.AscentM))
                {
                    return HudStrings.RouteUnavailable;
                }

                amber += leg.AmberM;
                heightChange += leg.AscentM;
            }

            if (!Valid(amber) || !Valid(heightChange))
            {
                return HudStrings.RouteUnavailable;
            }

            return HudFormatting.Format(HudStrings.RouteDetails,
                HudFormatting.Duration(plan.TotalS.P50), HudFormatting.Duration(plan.TotalS.P10),
                HudFormatting.Duration(plan.TotalS.P90), HudFormatting.Distance(amber),
                HudFormatting.Distance(heightChange), HudFormatting.Dose(plan.DoseUGy),
                plan.ReturnOk ? HudStrings.Available : HudStrings.Unavailable);
        }

        // Same colours as P5's RouteLine: the selected plan green, the others cyan then purple.
        private const string SelectedColor = "#33FF59";
        private static readonly string[] AlternativeColors = { "#40D9FF", "#B859FF" };

        /// <summary>
        /// One line per alternative with its colour on the map, total distance (sum of the legs) and
        /// walking time (P50), or "unreachable"; preceded by a header. Null for an invalid message.
        /// </summary>
        public static string Options(RouteSelected message)
        {
            if (message.Plans == null || message.Plans.Count != 3 || (int)message.Selected < 0 || (int)message.Selected > 2)
            {
                return null;
            }

            var text = new System.Text.StringBuilder(HudStrings.RouteOptionsHeader);
            var other = 0;
            var anyFound = false;
            for (var index = 0; index < 3; index++)
            {
                var alternative = (Alternative)index;
                var selected = alternative == message.Selected;
                var color = selected ? SelectedColor : AlternativeColors[other++];
                var suffix = selected ? HudStrings.RouteOptionSelectedMark : string.Empty;
                var plan = message.Plans[index];
                var distance = Distance(plan);
                text.Append('\n');
                if (distance < 0)
                {
                    text.Append(HudFormatting.Format(HudStrings.RouteOptionUnreachable, color, Name(alternative), suffix));
                    continue;
                }

                anyFound = true;
                text.Append(HudFormatting.Format(HudStrings.RouteOptionLine, color, Name(alternative),
                    HudFormatting.Distance(distance), HudFormatting.Duration(plan.TotalS.P50), suffix));
            }

            if (!anyFound)
            {
                text.Append('\n').Append(HudStrings.RouteAllUnreachable);
            }

            return text.ToString();
        }

        /// <summary>Total length of a plan's legs in metres, or -1 if any leg was not found.</summary>
        private static double Distance(Plan plan)
        {
            if (plan == null || plan.Legs.Count == 0)
            {
                return -1;
            }

            double total = 0;
            foreach (Route leg in plan.Legs)
            {
                if (leg == null || !leg.Found || !Valid(leg.LengthM))
                {
                    return -1;
                }

                total += leg.LengthM;
            }

            return total;
        }

        private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
}
