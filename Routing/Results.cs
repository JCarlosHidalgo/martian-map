using System;
using System.Collections.Generic;

namespace MartianMap.Routing
{
    /// <summary>
    /// A route. When <see cref="Found"/> is false the other fields are empty: "no route" is a
    /// result, not an exception. It is immutable: the constructor copies the arrays.
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
            Cells = ReadOnly.List(cells, nameof(cells));
            PolylineEnuM = ReadOnly.List(polylineEnuM, nameof(polylineEnuM));
            LengthM = lengthM;
            AscentM = ascentM;
            AmberM = amberM;
            EtaS = etaS;
            MaxHazard = maxHazard;
        }

        public bool Found { get; }

        /// <summary>Node indices, from start to goal.</summary>
        public IReadOnlyList<int> Cells { get; }

        public IReadOnlyList<EnuPoint3> PolylineEnuM { get; }

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
    /// <see cref="float.PositiveInfinity"/> where there is no way back, indexed like
    /// <see cref="Grid"/>. Only <see cref="Router.ReturnField"/> makes one, and it carries the
    /// <see cref="Grid"/> and the <see cref="Home"/> it was computed for, so
    /// <see cref="Router.OrderStops"/> cannot be given a field of another grid or another base.
    /// It is immutable: the constructor copies the array.
    /// </summary>
    public sealed class Field
    {
        internal Field(CostGrid grid, EnuPoint home, float[] timeS)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Home = home;
            TimeSArray = ReadOnly.Array(timeS, grid.Width * grid.Height, nameof(timeS));
        }

        /// <summary>The grid the field was computed on.</summary>
        public CostGrid Grid { get; }

        /// <summary>The base the field leads back to.</summary>
        public EnuPoint Home { get; }

        public ReadOnlySpan<float> TimeS => TimeSArray;

        internal float[] TimeSArray { get; }
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

    /// <summary>
    /// The fields of <c>conditions.json</c> that Routing needs. Every date is UTC. The constructor
    /// copies the array, and throws on a <c>default(ConditionModifier)</c>, which skipped its checks.
    /// </summary>
    public sealed class Conditions
    {
        public Conditions(double valueUGyH, ConditionModifier[] modifiers)
        {
            if (!(valueUGyH > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(valueUGyH));
            }

            ValueUGyH = valueUGyH;
            Modifiers = ReadOnly.List(modifiers, nameof(modifiers));
            foreach (ConditionModifier m in Modifiers)
            {
                // Only default(ConditionModifier) has a factor that is not > 0: it skipped the constructor.
                if (!(m.Factor > 0))
                {
                    throw new ArgumentException("A modifier must be built with its constructor.", nameof(modifiers));
                }
            }
        }

        public double ValueUGyH { get; }

        public IReadOnlyList<ConditionModifier> Modifiers { get; }

        internal static void RequireUtc(DateTime t, string name)
        {
            if (t.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Expected a UTC DateTime.", name);
            }
        }
    }

    /// <summary>
    /// A plan: the stop order and one leg per stretch, including the return to <c>home</c>. It is
    /// immutable: the constructor copies the arrays.
    /// </summary>
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
            Order = ReadOnly.List(order, nameof(order));
            Dropped = ReadOnly.List(dropped, nameof(dropped));
            Legs = ReadOnly.List(legs, nameof(legs));
            TotalS = totalS;
            DoseUGy = doseUGy;
            LimitS = limitS;
            ReturnOk = returnOk;
        }

        /// <summary>Stop ids in visiting order.</summary>
        public IReadOnlyList<string> Order { get; }

        /// <summary>Stop ids that do not fit; only in <see cref="StopMode.Op"/>.</summary>
        public IReadOnlyList<string> Dropped { get; }

        public IReadOnlyList<Route> Legs { get; }

        /// <summary>Walking plus <c>dwell_s</c>.</summary>
        public Percentiles TotalS { get; }

        public double DoseUGy { get; }

        /// <summary>The radiation duration limit, from <see cref="Router.DurationLimit"/>.</summary>
        public double LimitS { get; }

        public bool ReturnOk { get; }
    }
}
