using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The body of <see cref="Router.BuildGrid"/>: the 2 m grid of the bounding box of the tiles,
    /// with the edge rules of docs/guide/chapters/tablet.md, "API de Routing".
    /// </summary>
    internal static class GridBuilder
    {
        private const int S = TileRasters.SizePx;

        /// <summary>The nodes along one side of a tile, less the shared edge.</summary>
        private const int NodesPerTile = 128;

        private const byte Unset = 0;
        private const byte HeightSet = 1;
        private const byte Nodata = 2;

        /// <summary>
        /// Builds the grid; the arguments are already checked to be non-null and finite. Throws
        /// <see cref="ArgumentException"/> for a repeated tile, a tile off the 256 m lattice of the
        /// south-westmost origin, or a box whose node count does not fit an <c>int</c>.
        /// </summary>
        internal static CostGrid Build(TileRasters[] tiles, double originHeightM)
        {
            double minE = double.PositiveInfinity, minN = double.PositiveInfinity;
            foreach (TileRasters t in tiles)
            {
                if (!double.IsFinite(t.OriginEnuM.E) || !double.IsFinite(t.OriginEnuM.N))
                {
                    throw new ArgumentException("A tile origin is not finite.", nameof(tiles));
                }

                minE = Math.Min(minE, t.OriginEnuM.E);
                minN = Math.Min(minN, t.OriginEnuM.N);
            }

            // Each tile's place in the box, in tiles, sorted south-west first: lowest n, then lowest e.
            var ke = new long[tiles.Length];
            var kn = new long[tiles.Length];
            var order = new int[tiles.Length];
            long maxKe = 0, maxKn = 0;
            for (int t = 0; t < tiles.Length; t++)
            {
                ke[t] = LatticeStep(tiles[t].OriginEnuM.E - minE);
                kn[t] = LatticeStep(tiles[t].OriginEnuM.N - minN);
                maxKe = Math.Max(maxKe, ke[t]);
                maxKn = Math.Max(maxKn, kn[t]);
                order[t] = t;
            }

            Array.Sort(order, (a, b) => kn[a] != kn[b] ? kn[a].CompareTo(kn[b]) : ke[a].CompareTo(ke[b]));
            for (int k = 1; k < order.Length; k++)
            {
                if (ke[order[k]] == ke[order[k - 1]] && kn[order[k]] == kn[order[k - 1]])
                {
                    throw new ArgumentException("A tile is repeated.", nameof(tiles));
                }
            }

            int width, height, count;
            try
            {
                width = checked((int)(NodesPerTile * (maxKe + 1) + 1));
                height = checked((int)(NodesPerTile * (maxKn + 1) + 1));
                count = checked(width * height);
            }
            catch (OverflowException e)
            {
                throw new ArgumentException("The box of the tiles has more nodes than an int can count.", nameof(tiles), e);
            }

            var heightM = new float[count];
            var slope = new byte[count];
            var roughness = new byte[count];
            var hazard = new byte[count];
            var state = new byte[count];
            var covered = new bool[count];

            // A node and the samples 1 m around it: node and tile edges fall on even metres, so every
            // window sample lies in a tile that also holds the node, and each tile only reads itself.
            foreach (int t in order)
            {
                TileRasters tile = tiles[t];
                int i0 = (int)(NodesPerTile * ke[t]);
                int j0 = (int)(NodesPerTile * kn[t]);
                for (int lj = 0; lj <= NodesPerTile; lj++)
                {
                    int r = 2 * (NodesPerTile - lj);
                    for (int li = 0; li <= NodesPerTile; li++)
                    {
                        int c = 2 * li;
                        int node = (j0 + lj) * width + i0 + li;
                        covered[node] = true;
                        ushort h = tile.HeightArray[r * S + c];
                        if (h == TileRasters.HeightNodata)
                        {
                            state[node] = Nodata;
                        }
                        else if (state[node] == Unset)
                        {
                            heightM[node] = (float)(tile.Offset + tile.Scale * h - originHeightM);
                            state[node] = HeightSet;
                        }

                        WindowMax(tile, r, c, node, slope, roughness, hazard);
                    }
                }
            }

            for (int node = 0; node < count; node++)
            {
                if (!covered[node] || state[node] == Nodata)
                {
                    heightM[node] = 0f;
                    hazard[node] = 255;
                }
            }

            return new CostGrid(width, height, 2f, new EnuPoint(minE, minN), heightM, slope, roughness, hazard);
        }

        /// <summary>The offset in whole tiles of a distance that must be a multiple of 256 m.</summary>
        private static long LatticeStep(double offsetM)
        {
            double k = offsetM / 256.0;
            if (offsetM % 256.0 != 0 || k > long.MaxValue / (2 * NodesPerTile))
            {
                throw new ArgumentException("A tile origin is off the 256 m lattice of the others.", "tiles");
            }

            return (long)k;
        }

        private static void WindowMax(TileRasters tile, int r, int c, int node, byte[] slope, byte[] roughness, byte[] hazard)
        {
            for (int rr = Math.Max(r - 1, 0); rr <= Math.Min(r + 1, S - 1); rr++)
            {
                for (int cc = Math.Max(c - 1, 0); cc <= Math.Min(c + 1, S - 1); cc++)
                {
                    int k = rr * S + cc;
                    slope[node] = Math.Max(slope[node], tile.SlopeArray[k]);
                    roughness[node] = Math.Max(roughness[node], tile.RoughnessArray[k]);
                    hazard[node] = Math.Max(hazard[node], tile.HazardArray[k]);
                }
            }
        }
    }
}
