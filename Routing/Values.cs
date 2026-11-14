using System;

namespace MartianMap.Routing
{
    /// <summary>A point in the zone's ENU frame, in metres: east and north.</summary>
    public readonly struct EnuPoint
    {
        public EnuPoint(double e, double n)
        {
            E = e;
            N = n;
        }

        public double E { get; }

        public double N { get; }
    }

    /// <summary>
    /// A polyline vertex in the zone's ENU frame, in metres. <see cref="U"/> is the height over the
    /// areoid minus the zone's <c>origin_height_m</c>: the scene's Y.
    /// </summary>
    public readonly struct EnuPoint3
    {
        public EnuPoint3(double e, double n, double u)
        {
            E = e;
            N = n;
            U = u;
        }

        public double E { get; }

        public double N { get; }

        public double U { get; }
    }

    /// <summary>One quantity at the P10, P50 and P90 walking speeds.</summary>
    public readonly struct Percentiles
    {
        public Percentiles(double p10, double p50, double p90)
        {
            P10 = p10;
            P50 = p50;
            P90 = p90;
        }

        public double P10 { get; }

        public double P50 { get; }

        public double P90 { get; }
    }

    /// <summary>A parameter with its central value and its sensitivity range.</summary>
    public readonly struct SensitivityRange
    {
        public SensitivityRange(double p50, double low, double high)
        {
            if (!(low <= p50 && p50 <= high))
            {
                throw new ArgumentException("Expected low <= p50 <= high.");
            }

            P50 = p50;
            Low = low;
            High = high;
        }

        public double P50 { get; }

        public double Low { get; }

        public double High { get; }
    }

    /// <summary>The three route alternatives. Every array of alternatives holds all three, in this order.</summary>
    public enum Alternative
    {
        Fastest = 0,
        Safest = 1,
        Gentlest = 2,
    }

    /// <summary>How <see cref="Router.OrderStops"/> treats the stops.</summary>
    public enum StopMode
    {
        /// <summary>Travelling salesman with depot: visits every stop.</summary>
        Tsp = 0,

        /// <summary>Orienteering: maximises the sum of science value and drops the rest.</summary>
        Op = 1,
    }
}
