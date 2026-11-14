using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// A route. When <see cref="Found"/> is false the other fields are empty: "no route" is a
    /// result, not an exception.
    /// </summary>
    public sealed class Route
    {
        public static readonly Route NotFound = new Route(
            false, Array.Empty<int>(), Array.Empty<EnuPoint3>(), 0, 0, 0, default, 0);

        public Route(
            bool found,
            int[] cells,
            EnuPoint3[] polylineEnuM,
            double lengthM,
            double ascentM,
            double amberM,
            Percentiles etaS,
            byte maxHazard)
        {
            Found = found;
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
            PolylineEnuM = polylineEnuM ?? throw new ArgumentNullException(nameof(polylineEnuM));
            LengthM = lengthM;
            AscentM = ascentM;
            AmberM = amberM;
            EtaS = etaS;
            MaxHazard = maxHazard;
        }

        public bool Found { get; }

        /// <summary>Node indices, from start to goal.</summary>
        public int[] Cells { get; }

        public EnuPoint3[] PolylineEnuM { get; }

        public double LengthM { get; }

        /// <summary>Accumulated height change, climbed plus descended.</summary>
        public double AscentM { get; }

        /// <summary>Metres walked in amber cells.</summary>
        public double AmberM { get; }

        /// <summary>Walking time. It never includes penalties.</summary>
        public Percentiles EtaS { get; }

        public byte MaxHazard { get; }
    }

    /// <summary>
    /// The return field: one value per node, the time back to <c>home</c> at v₀ P90;
    /// <see cref="float.PositiveInfinity"/> where there is no way back.
    /// </summary>
    public sealed class Field
    {
        public Field(float[] timeS)
        {
            TimeS = timeS ?? throw new ArgumentNullException(nameof(timeS));
        }

        public float[] TimeS { get; }
    }

    /// <summary>A mission stop. <see cref="ScienceValue"/> goes from 0 to 10.</summary>
    public sealed class Stop
    {
        public Stop(string id, EnuPoint enuM, int scienceValue, double dwellS)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A stop needs an id.", nameof(id));
            }

            if (scienceValue < 0 || scienceValue > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(scienceValue));
            }

            if (!(dwellS >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(dwellS));
            }

            Id = id;
            EnuM = enuM;
            ScienceValue = scienceValue;
            DwellS = dwellS;
        }

        public string Id { get; }

        public EnuPoint EnuM { get; }

        public int ScienceValue { get; }

        public double DwellS { get; }
    }

    /// <summary>The mission budget: time and absorbed dose.</summary>
    public readonly struct Budget
    {
        public Budget(double tS, double doseUGy)
        {
            if (!(tS >= 0) || !(doseUGy >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(tS), "The budget must be >= 0.");
            }

            TS = tS;
            DoseUGy = doseUGy;
        }

        public double TS { get; }

        public double DoseUGy { get; }
    }

    /// <summary>A dose-rate modifier, valid from <see cref="ValidFrom"/> (inclusive) to <see cref="ValidTo"/> (exclusive).</summary>
    public readonly struct ConditionModifier
    {
        public ConditionModifier(double factor, DateTime validFrom, DateTime validTo)
        {
            if (!(factor > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(factor));
            }

            Conditions.RequireUtc(validFrom, nameof(validFrom));
            Conditions.RequireUtc(validTo, nameof(validTo));
            if (!(validFrom < validTo))
            {
                throw new ArgumentException("Expected valid_from < valid_to.");
            }

            Factor = factor;
            ValidFrom = validFrom;
            ValidTo = validTo;
        }

        public double Factor { get; }

        public DateTime ValidFrom { get; }

        public DateTime ValidTo { get; }
    }

    /// <summary>The fields of <c>conditions.json</c> that Routing needs. Every date is UTC.</summary>
    public sealed class Conditions
    {
        public Conditions(double valueUGyH, ConditionModifier[] modifiers)
        {
            if (!(valueUGyH > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(valueUGyH));
            }

            ValueUGyH = valueUGyH;
            Modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
        }

        public double ValueUGyH { get; }

        public ConditionModifier[] Modifiers { get; }

        internal static void RequireUtc(DateTime t, string name)
        {
            if (t.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Expected a UTC DateTime.", name);
            }
        }
    }

    /// <summary>A plan: the stop order and one leg per stretch, including the return to <c>home</c>.</summary>
    public sealed class Plan
    {
        public Plan(
            string[] order,
            string[] dropped,
            Route[] legs,
            Percentiles totalS,
            double doseUGy,
            double limitS,
            bool returnOk)
        {
            Order = order ?? throw new ArgumentNullException(nameof(order));
            Dropped = dropped ?? throw new ArgumentNullException(nameof(dropped));
            Legs = legs ?? throw new ArgumentNullException(nameof(legs));
            TotalS = totalS;
            DoseUGy = doseUGy;
            LimitS = limitS;
            ReturnOk = returnOk;
        }

        /// <summary>Stop ids in visiting order.</summary>
        public string[] Order { get; }

        /// <summary>Stop ids that do not fit; only in <see cref="StopMode.Op"/>.</summary>
        public string[] Dropped { get; }

        public Route[] Legs { get; }

        /// <summary>Walking plus <c>dwell_s</c>.</summary>
        public Percentiles TotalS { get; }

        public double DoseUGy { get; }

        /// <summary>The radiation duration limit, from <see cref="Router.DurationLimit"/>.</summary>
        public double LimitS { get; }

        public bool ReturnOk { get; }
    }
}
