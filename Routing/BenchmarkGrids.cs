using System;

namespace MartianMap.Routing
{
    /// <summary>The five terrains of E5 (docs/guide/chapters/reparto.md, E5).</summary>
    internal enum Terrain
    {
        Uniform,
        Fractal,
        WallWithGap,
        Maze,
        Blocked30,
    }

    /// <summary>
    /// The seeded grids of the E5 benchmark: square, <c>cell_m</c> 2, origin <c>(0, 0)</c>. They
    /// use their own <see cref="SplitMix64"/>, so a seed gives the same grid on x64 and ARM64.
    /// Heights are 0 except on <see cref="Terrain.Fractal"/>.
    /// </summary>
    internal static class BenchmarkGrids
    {
        private const float CellM = 2f;

        /// <summary>
        /// <list type="bullet">
        /// <item><see cref="Terrain.Uniform"/>: flat and free.</item>
        /// <item><see cref="Terrain.Fractal"/>: 6 octaves of bilinear value noise (lattice of 256 nodes,
        /// halving; gain 0.5), normalised to [−1, 1] and times 40 m; <c>slope_deg</c> = atan(|∇h|) by
        /// centred differences, rounded; relief from a separate octave (lattice of 16 nodes) in
        /// 0–70 cm; hazard 255 where <c>slope_deg</c> &gt; 15, as Tierra marks it.</item>
        /// <item><see cref="Terrain.WallWithGap"/>: a column of hazard 255 at <c>i = size / 2</c>, with
        /// a gap of 4 nodes from <c>j = size / 3</c>.</item>
        /// <item><see cref="Terrain.Maze"/>: a perfect maze by iterative DFS over the odd nodes
        /// (corridors and walls of one node).</item>
        /// <item><see cref="Terrain.Blocked30"/>: each node, in index order, hazard 255 if
        /// <see cref="SplitMix64.NextDouble"/> &lt; 0.30.</item>
        /// </list>
        /// </summary>
        internal static CostGrid Make(Terrain terrain, int size, ulong seed)
        {
            if (size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            int n = checked(size * size);
            var height = new float[n];
            var slope = new byte[n];
            var relief = new byte[n];
            var hazard = new byte[n];
            var rng = new SplitMix64(seed);
            switch (terrain)
            {
                case Terrain.Uniform:
                    break;
                case Terrain.Fractal:
                    Fractal(size, rng, height, slope, relief, hazard);
                    break;
                case Terrain.WallWithGap:
                    for (int j = 0; j < size; j++)
                    {
                        bool gap = j >= size / 3 && j < (size / 3) + 4;
                        hazard[(j * size) + (size / 2)] = gap ? (byte)0 : (byte)255;
                    }

                    break;
                case Terrain.Maze:
                    Maze(size, rng, hazard);
                    break;
                case Terrain.Blocked30:
                    for (int k = 0; k < n; k++)
                    {
                        hazard[k] = rng.NextDouble() < 0.30 ? (byte)255 : (byte)0;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(terrain));
            }

            return new CostGrid(size, size, CellM, new EnuPoint(0, 0), height, slope, relief, hazard);
        }

        private static void Fractal(int size, SplitMix64 rng, float[] height, byte[] slope, byte[] relief, byte[] hazard)
        {
            var noise = new double[size * size];
            double gainSum = 0;
            double gain = 1;
            for (int octave = 0, spacing = 256; octave < 6; octave++, spacing /= 2, gain *= 0.5)
            {
                AddValueNoise(size, spacing, gain, rng, noise, -1, 1);
                gainSum += gain;
            }

            for (int k = 0; k < noise.Length; k++)
            {
                height[k] = (float)(40.0 * noise[k] / gainSum);
            }

            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    double gx = Diff(height, size, i, j, 1, 0);
                    double gy = Diff(height, size, i, j, 0, 1);
                    double deg = Math.Atan(Math.Sqrt((gx * gx) + (gy * gy))) * 180.0 / Math.PI;
                    int k = (j * size) + i;
                    slope[k] = (byte)Math.Round(deg);
                    hazard[k] = slope[k] > 15 ? (byte)255 : (byte)0;
                }
            }

            var rough = new double[size * size];
            AddValueNoise(size, 16, 1, rng, rough, 0, 1);
            for (int k = 0; k < rough.Length; k++)
            {
                relief[k] = (byte)Math.Round(70.0 * rough[k]);
            }
        }

        /// <summary>Adds <paramref name="gain"/> times bilinear value noise on a lattice of <paramref name="spacing"/> nodes, with values in [lo, hi].</summary>
        private static void AddValueNoise(int size, int spacing, double gain, SplitMix64 rng, double[] into, double lo, double hi)
        {
            int cells = (size / spacing) + 2;
            var lattice = new double[cells * cells];
            for (int k = 0; k < lattice.Length; k++)
            {
                lattice[k] = lo + ((hi - lo) * rng.NextDouble());
            }

            for (int j = 0; j < size; j++)
            {
                int lj = j / spacing;
                double fy = (double)(j % spacing) / spacing;
                for (int i = 0; i < size; i++)
                {
                    int li = i / spacing;
                    double fx = (double)(i % spacing) / spacing;
                    double v00 = lattice[(lj * cells) + li], v10 = lattice[(lj * cells) + li + 1];
                    double v01 = lattice[((lj + 1) * cells) + li], v11 = lattice[((lj + 1) * cells) + li + 1];
                    double v = ((1 - fx) * (1 - fy) * v00) + (fx * (1 - fy) * v10) + ((1 - fx) * fy * v01) + (fx * fy * v11);
                    into[(j * size) + i] += gain * v;
                }
            }
        }

        /// <summary>The centred difference of the height along (di, dj), one-sided on the edge, over metres.</summary>
        private static double Diff(float[] h, int size, int i, int j, int di, int dj)
        {
            int i0 = Math.Max(i - di, 0), j0 = Math.Max(j - dj, 0);
            int i1 = Math.Min(i + di, size - 1), j1 = Math.Min(j + dj, size - 1);
            int steps = (i1 - i0) + (j1 - j0);
            return steps == 0 ? 0 : (h[(j1 * size) + i1] - h[(j0 * size) + i0]) / (steps * (double)CellM);
        }

        private static void Maze(int size, SplitMix64 rng, byte[] hazard)
        {
            for (int k = 0; k < hazard.Length; k++)
            {
                hazard[k] = 255;
            }

            if (size < 2)
            {
                return;
            }

            // Rooms are the nodes with odd i and j; a corridor opens the node between two rooms.
            var visited = new bool[size * size];
            var stack = new int[((size / 2) + 1) * ((size / 2) + 1)];
            int top = 0;
            int start = size + 1;
            visited[start] = true;
            hazard[start] = 0;
            stack[top++] = start;
            var options = new int[4];
            while (top > 0)
            {
                int c = stack[top - 1];
                int ci = c % size, cj = c / size;
                int count = 0;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 2 : d == 1 ? -2 : 0);
                    int nj = cj + (d == 2 ? 2 : d == 3 ? -2 : 0);
                    if (ni >= 1 && ni < size && nj >= 1 && nj < size && !visited[(nj * size) + ni])
                    {
                        options[count++] = d;
                    }
                }

                if (count == 0)
                {
                    top--;
                    continue;
                }

                int dir = options[rng.NextInt(count)];
                int di = dir == 0 ? 2 : dir == 1 ? -2 : 0;
                int dj = dir == 2 ? 2 : dir == 3 ? -2 : 0;
                int next = ((cj + dj) * size) + ci + di;
                hazard[((cj + (dj / 2)) * size) + ci + (di / 2)] = 0;
                hazard[next] = 0;
                visited[next] = true;
                stack[top++] = next;
            }
        }
    }

    /// <summary>SplitMix64 (Steele, Lea and Flood 2014): the same sequence on every runtime.</summary>
    internal sealed class SplitMix64
    {
        private ulong state;

        internal SplitMix64(ulong seed) => state = seed;

        internal ulong Next()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0, 1), from the top 53 bits.</summary>
        internal double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Uniform in [0, <paramref name="n"/>).</summary>
        internal int NextInt(int n) => (int)(NextDouble() * n);
    }
}
