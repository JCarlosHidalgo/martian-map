using MartianMap.Routing;

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

        private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
}
