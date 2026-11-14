using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// One <c>z = 0</c> tile as C1 delivers it: 257 × 257 samples, row 0 at the north edge.
    /// </summary>
    public sealed class TileRasters
    {
        public const int SizePx = 257;

        public const ushort HeightNodata = 65535;

        public TileRasters(
            EnuPoint originEnuM,
            ushort[] height,
            double scale,
            double offset,
            byte[] slope,
            byte[] roughness,
            byte[] hazard)
        {
            RequireSamples(height?.Length, nameof(height));
            RequireSamples(slope?.Length, nameof(slope));
            RequireSamples(roughness?.Length, nameof(roughness));
            RequireSamples(hazard?.Length, nameof(hazard));
            if (!(scale > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }

            OriginEnuM = originEnuM;
            Height = height;
            Scale = scale;
            Offset = offset;
            Slope = slope;
            Roughness = roughness;
            Hazard = hazard;
        }

        /// <summary>The tile's south-west corner, from <c>tile.json</c>.</summary>
        public EnuPoint OriginEnuM { get; }

        public ushort[] Height { get; }

        public double Scale { get; }

        public double Offset { get; }

        public byte[] Slope { get; }

        public byte[] Roughness { get; }

        public byte[] Hazard { get; }

        private static void RequireSamples(int? length, string name)
        {
            if (length != SizePx * SizePx)
            {
                throw new ArgumentException($"Expected {SizePx * SizePx} samples.", name);
            }
        }
    }
}
