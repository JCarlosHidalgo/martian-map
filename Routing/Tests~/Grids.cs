using System;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// The pathological grids of the routing tests, in the text format of the oracle
    /// (~/scratch/c4-verify/oracle/maps.py, not committed): row 0 is the north row
    /// (<c>j = height − 1</c>); <c>.</c> free, <c>#</c> hazard 255, <c>a</c> hazard 128 (amber),
    /// <c>r</c> relief 70 cm (blocks), <c>q</c> relief 50 cm (penalised). <c>cell_m</c> is 2 and the
    /// origin is <c>(0, 0)</c>.
    /// </summary>
    internal static class Grids
    {
        /// <summary>A wall of one node at <c>i = 4</c>, from south to north.</summary>
        internal static readonly string[] Wall9x7 =
        {
            "....#....",
            "....#....",
            "....#....",
            "....#....",
            "....#....",
            "....#....",
            "....#....",
        };

        /// <summary>The same wall with a gap at <c>(4, 1)</c>.</summary>
        internal static readonly string[] WallGap9x7 =
        {
            "....#....",
            "....#....",
            "....#....",
            "....#....",
            "....#....",
            ".........",
            "....#....",
        };

        /// <summary>A perfect maze with corridors of one node (maps.maze, DFS with seed 7).</summary>
        internal static readonly string[] Maze11x11 =
        {
            "###########",
            "#.#.......#",
            "#.###.#####",
            "#...#.....#",
            "###.#.###.#",
            "#.#.#.#...#",
            "#.#.###.#.#",
            "#.#.....#.#",
            "#.#######.#",
            "#.........#",
            "###########",
        };

        /// <summary>A 3 × 3 hole of the DTM (nodata is hazard 255) at <c>i = 3…5</c>, <c>j = 2…4</c>.</summary>
        internal static readonly string[] Nodata8x6 =
        {
            "........",
            "...###..",
            "...###..",
            "...###..",
            "........",
            "........",
        };

        /// <summary>An amber band at <c>i = 4</c> with a free pass at <c>j = 3</c>, two nodes north of the row <c>j = 1</c>.</summary>
        internal static readonly string[] AmberBand9x5 =
        {
            "....a....",
            ".........",
            "....a....",
            "....a....",
            "....a....",
        };

        /// <summary>The same band with the pass at <c>j = 11</c>, ten nodes north of the row <c>j = 1</c>.</summary>
        internal static readonly string[] AmberBandFar9x21 = Band(21, 11);

        /// <summary>30 % of hazard 255 (maps.random_map, Python's random.Random(2026)).</summary>
        internal static readonly string[] Random12x9 =
        {
            "#...##......",
            "..#.#.....##",
            ".....#..#...",
            ".#......#...",
            "..#....####.",
            ".#...#......",
            ".........###",
            "......#.....",
            "....#.......",
        };

        /// <summary>The heights of <see cref="Maze11x11"/>: <c>0.1·i + 0.05·j</c>.</summary>
        internal static float MazeHeight(int i, int j) => (float)((0.1 * i) + (0.05 * j));

        /// <summary>The heights of <see cref="Random12x9"/>: <c>0.2 · ((7i + 3j) mod 5)</c>.</summary>
        internal static float RandomHeight(int i, int j) => (float)(0.2 * (((7 * i) + (3 * j)) % 5));

        /// <summary>A free 15 × 9 grid, for <see cref="HillHeight"/> and <see cref="BumpHeight"/>.</summary>
        internal static readonly string[] Flat15x9 = Rows(15, 9);

        /// <summary>A 3 × 3 block of amber in the middle of 15 × 9 (<c>i</c> 6–8, <c>j</c> 3–5).</summary>
        internal static readonly string[] AmberBlob15x9 =
        {
            "...............",
            "...............",
            "...............",
            "......aaa......",
            "......aaa......",
            "......aaa......",
            "...............",
            "...............",
            "...............",
        };

        /// <summary>One blocked node at <c>(2, 1)</c>: its corner <c>(1.5, 1.5)</c> is on the diagonal from <c>(0, 0)</c>.</summary>
        internal static readonly string[] Corner4x4 =
        {
            "....",
            "....",
            "..#.",
            "....",
        };

        /// <summary>A random 8 × 6 map (oracle cases_smooth.py, scan) where a shortcut is rejected and the next one would pass.</summary>
        internal static readonly string[] ScanStop8x6 =
        {
            "...#....",
            "........",
            "....##..",
            "#.......",
            "......#.",
            "...#....",
        };

        /// <summary>A hill of 3 m centred on <c>(7, 4)</c>: <c>3 · exp(−((i − 7)² + (j − 4)²) / 4)</c>.</summary>
        internal static float HillHeight(int i, int j) => (float)(3.0 * Math.Exp(-(((i - 7) * (i - 7)) + ((j - 4) * (j - 4))) / 4.0));

        /// <summary>The same shape as <see cref="HillHeight"/>, 0.3 m high.</summary>
        internal static float BumpHeight(int i, int j) => (float)(0.3 * Math.Exp(-(((i - 7) * (i - 7)) + ((j - 4) * (j - 4))) / 4.0));

        internal static CostGrid FromMap(string[] rows, Func<int, int, float> heightM = null)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            int n = width * height;
            var h = new float[n];
            var relief = new byte[n];
            var hazard = new byte[n];
            for (int r = 0; r < height; r++)
            {
                if (rows[r].Length != width)
                {
                    throw new ArgumentException("Every row has the same width.");
                }

                int j = height - 1 - r;
                for (int i = 0; i < width; i++)
                {
                    int k = (j * width) + i;
                    switch (rows[r][i])
                    {
                        case '.': break;
                        case '#': hazard[k] = 255; break;
                        case 'a': hazard[k] = 128; break;
                        case 'r': relief[k] = 70; break;
                        case 'q': relief[k] = 50; break;
                        default: throw new ArgumentException($"Unknown cell '{rows[r][i]}'.");
                    }

                    h[k] = heightM == null ? 0f : heightM(i, j);
                }
            }

            return new CostGrid(width, height, 2f, new EnuPoint(0, 0), h, new byte[n], relief, hazard);
        }

        /// <summary>
        /// A 200 × 200 flat grid with hazard 255 on about 30 % of the nodes, where an integer hash
        /// of <c>(i, j)</c> is below 3 mod 10: <c>((i·2654435761) ^ (j·2246822519))·3266489917 &gt;&gt; 16</c>,
        /// in <c>uint</c>. About 68 % of the nodes reach <c>(0, 1)</c>.
        /// </summary>
        internal static CostGrid Pattern200()
        {
            const int size = 200;
            var hazard = new byte[size * size];
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    uint hash = ((((uint)i * 2654435761u) ^ ((uint)j * 2246822519u)) * 3266489917u) >> 16;
                    hazard[(j * size) + i] = (byte)(hash % 10 < 3 ? 255 : 0);
                }
            }

            int n = size * size;
            return new CostGrid(size, size, 2f, new EnuPoint(0, 0), new float[n], new byte[n], new byte[n], hazard);
        }

        /// <summary>A flat, free grid.</summary>
        internal static CostGrid Flat(int width, int height)
        {
            int n = width * height;
            return new CostGrid(width, height, 2f, new EnuPoint(0, 0), new float[n], new byte[n], new byte[n], new byte[n]);
        }

        private static string[] Rows(int width, int height)
        {
            var rows = new string[height];
            for (int r = 0; r < height; r++)
            {
                rows[r] = new string('.', width);
            }

            return rows;
        }

        private static string[] Band(int height, int passJ)
        {
            var rows = new string[height];
            for (int r = 0; r < height; r++)
            {
                rows[r] = height - 1 - r == passJ ? "........." : "....a....";
            }

            return rows;
        }
    }
}
