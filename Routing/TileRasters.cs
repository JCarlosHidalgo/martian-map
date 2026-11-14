using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// One <c>z = 0</c> tile as C1 delivers it: 257 × 257 samples, row 0 at the north edge. It is
    /// immutable: the constructor copies the arrays, and they are read as <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public sealed class TileRasters
    {
        public const int SizePx = 257;

        public const ushort HeightNodata = 65535;

        private const int Samples = SizePx * SizePx;

        public TileRasters(
            EnuPoint originEnuM,
            ushort[] height,
            double scale,
            double offset,
            byte[] slope,
            byte[] roughness,
            byte[] hazard)
        {
            if (!(scale > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }

            OriginEnuM = originEnuM;
            HeightArray = ReadOnly.Array(height, Samples, nameof(height));
            Scale = scale;
            Offset = offset;
            SlopeArray = ReadOnly.Array(slope, Samples, nameof(slope));
            RoughnessArray = ReadOnly.Array(roughness, Samples, nameof(roughness));
            HazardArray = ReadOnly.Array(hazard, Samples, nameof(hazard));
        }

        /// <summary>The tile's south-west corner, from <c>tile.json</c>.</summary>
        public EnuPoint OriginEnuM { get; }

        public ReadOnlySpan<ushort> Height => HeightArray;

        internal ushort[] HeightArray { get; }

        public double Scale { get; }

        public double Offset { get; }

        public ReadOnlySpan<byte> Slope => SlopeArray;

        internal byte[] SlopeArray { get; }

        public ReadOnlySpan<byte> Roughness => RoughnessArray;

        internal byte[] RoughnessArray { get; }

        public ReadOnlySpan<byte> Hazard => HazardArray;

        internal byte[] HazardArray { get; }
    }
}
