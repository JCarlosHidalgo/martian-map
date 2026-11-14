using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The planning grid. Node <c>(i, j)</c> is at index <c>j · Width + i</c>, at
    /// <c>OriginEnuM + (CellM·i, CellM·j)</c>; <c>i</c> grows east and <c>j</c> north. It is
    /// immutable: the constructor copies the arrays, and they are read as <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    public sealed class CostGrid
    {
        public CostGrid(
            int width,
            int height,
            float cellM,
            EnuPoint originEnuM,
            float[] heightM,
            byte[] slopeDeg,
            byte[] roughnessCm,
            byte[] hazard)
        {
            if (width < 1 || height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The grid needs at least one node.");
            }

            if (!(cellM > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(cellM));
            }

            int count = checked(width * height);
            Width = width;
            Height = height;
            CellM = cellM;
            OriginEnuM = originEnuM;
            HeightMArray = ReadOnly.Array(heightM, count, nameof(heightM));
            SlopeDegArray = ReadOnly.Array(slopeDeg, count, nameof(slopeDeg));
            RoughnessCmArray = ReadOnly.Array(roughnessCm, count, nameof(roughnessCm));
            HazardArray = ReadOnly.Array(hazard, count, nameof(hazard));
        }

        public int Width { get; }

        public int Height { get; }

        public float CellM { get; }

        /// <summary>The position of node <c>(0, 0)</c>, the south-west corner.</summary>
        public EnuPoint OriginEnuM { get; }

        /// <summary>Height over the areoid minus the zone's <c>origin_height_m</c>.</summary>
        public ReadOnlySpan<float> HeightM => HeightMArray;

        public ReadOnlySpan<byte> SlopeDeg => SlopeDegArray;

        public ReadOnlySpan<byte> RoughnessCm => RoughnessCmArray;

        public ReadOnlySpan<byte> Hazard => HazardArray;

        internal float[] HeightMArray { get; }

        internal byte[] SlopeDegArray { get; }

        internal byte[] RoughnessCmArray { get; }

        internal byte[] HazardArray { get; }

        public int Index(int i, int j)
        {
            if ((uint)i >= (uint)Width || (uint)j >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(nameof(i), "Node outside the grid.");
            }

            return j * Width + i;
        }
    }
}
