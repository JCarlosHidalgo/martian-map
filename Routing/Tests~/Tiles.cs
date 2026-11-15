using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// Tiles for the BuildGrid tests, from formulas of the local sample <c>(r, c)</c> (row 0 is the
    /// north edge), the same formulas as the oracle (~/scratch/c4-verify/oracle/cases_buildgrid.py,
    /// not committed).
    /// </summary>
    internal static class Tiles
    {
        internal const int S = TileRasters.SizePx;

        internal static TileRasters Make(
            double e0,
            double n0,
            Func<int, int, ushort> height,
            Func<int, int, byte> slope = null,
            Func<int, int, byte> roughness = null,
            Func<int, int, byte> hazard = null,
            double scale = 1.0 / 1024,
            double offset = 0)
        {
            return new TileRasters(
                new EnuPoint(e0, n0), Raster(height), scale, offset, Raster(slope), Raster(roughness), Raster(hazard));
        }

        private static T[] Raster<T>(Func<int, int, T> f)
        {
            var a = new T[S * S];
            if (f != null)
            {
                for (int r = 0; r < S; r++)
                {
                    for (int c = 0; c < S; c++)
                    {
                        a[r * S + c] = f(r, c);
                    }
                }
            }

            return a;
        }

        /// <summary>The grid in the layout of costgrid_synth.bin (tablet.md, "Entregables y goldens").</summary>
        internal static byte[] Serialize(CostGrid g)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(g.Width);
                w.Write(g.Height);
                w.Write(g.CellM);
                w.Write(g.OriginEnuM.E);
                w.Write(g.OriginEnuM.N);
                foreach (float h in g.HeightM)
                {
                    w.Write(h);
                }

                w.Write(g.SlopeDeg);
                w.Write(g.RoughnessCm);
                w.Write(g.Hazard);
            }

            return ms.ToArray();
        }

        internal static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }
    }
}
