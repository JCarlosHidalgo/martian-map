using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The fields of <c>cost_params.json</c>, one to one. It is the <c>params</c> of every call.
    /// <see cref="FTerreno"/> keeps the Spanish name of its JSON field, <c>f_terreno</c>.
    /// </summary>
    public sealed class CostParams
    {
        public CostParams(
            int schemaVersion,
            Percentiles v0Mps,
            SensitivityRange b,
            double c,
            double walkCeilingMps,
            double maxSlopeDeg,
            double reliefBlockCm,
            double reliefPenaltyFromCm,
            byte hazardRedMin,
            byte hazardAmberMin,
            double fTerreno,
            double fRugMin,
            double amberPenaltySPerM,
            double altSafestAmberMult,
            double altGentlestSPerM)
        {
            if (schemaVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (!(v0Mps.P90 > 0 && v0Mps.P90 <= v0Mps.P50 && v0Mps.P50 <= v0Mps.P10))
            {
                throw new ArgumentException("Expected 0 < v0 p90 <= p50 <= p10.", nameof(v0Mps));
            }

            if (!(walkCeilingMps > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(walkCeilingMps));
            }

            if (!(reliefPenaltyFromCm < reliefBlockCm))
            {
                throw new ArgumentException("Expected relief_penalty_from_cm < relief_block_cm.");
            }

            if (!(hazardAmberMin < hazardRedMin))
            {
                throw new ArgumentException("Expected hazard_amber_min < hazard_red_min.");
            }

            if (!(fTerreno > 0 && fTerreno <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(fTerreno));
            }

            if (!(fRugMin > 0 && fRugMin <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(fRugMin));
            }

            if (!(amberPenaltySPerM >= 0 && altSafestAmberMult >= 0 && altGentlestSPerM >= 0))
            {
                throw new ArgumentException("Penalties and alternative weights must be >= 0.");
            }

            SchemaVersion = schemaVersion;
            V0Mps = v0Mps;
            B = b;
            C = c;
            WalkCeilingMps = walkCeilingMps;
            MaxSlopeDeg = maxSlopeDeg;
            ReliefBlockCm = reliefBlockCm;
            ReliefPenaltyFromCm = reliefPenaltyFromCm;
            HazardRedMin = hazardRedMin;
            HazardAmberMin = hazardAmberMin;
            FTerreno = fTerreno;
            FRugMin = fRugMin;
            AmberPenaltySPerM = amberPenaltySPerM;
            AltSafestAmberMult = altSafestAmberMult;
            AltGentlestSPerM = altGentlestSPerM;
        }

        public int SchemaVersion { get; }

        public Percentiles V0Mps { get; }

        public SensitivityRange B { get; }

        public double C { get; }

        public double WalkCeilingMps { get; }

        public double MaxSlopeDeg { get; }

        public double ReliefBlockCm { get; }

        public double ReliefPenaltyFromCm { get; }

        public byte HazardRedMin { get; }

        public byte HazardAmberMin { get; }

        public double FTerreno { get; }

        public double FRugMin { get; }

        public double AmberPenaltySPerM { get; }

        public double AltSafestAmberMult { get; }

        public double AltGentlestSPerM { get; }
    }
}
