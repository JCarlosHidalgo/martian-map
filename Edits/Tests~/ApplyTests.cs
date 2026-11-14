using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using static MartianMap.Edits.Tests.Log;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// <see cref="EditEngine.Apply"/>: <c>max(base, live entities)</c> on the samples inside or on
    /// the edge of each polygon (docs/guide/chapters/tablet.md, "apply() (D5)"; plan A11). The
    /// counts and hashes come from ~/scratch/c4-verify/oracle/cases_apply.py t6, which tests every
    /// sample of the tile with Python ints (not committed).
    /// </summary>
    public class ApplyTests
    {
        private const int Size = 257;

        private static readonly EnuMetres Synth = new EnuMetres(-128, -128);

        [Test]
        public void WithoutEditsTheBaseComesBackAsACopy()
        {
            byte[] b = Pattern();
            byte[] before = (byte[])b.Clone();
            byte[] out0 = EditEngine.Apply(b, Synth, new Edit[0]);
            Assert.That(Sha(out0), Is.EqualTo(Sha(b)));
            Assert.That(out0, Is.Not.SameAs(b));

            EditEngine.Apply(b, Synth, new[] { E(1, "ev1", "1", Ob(ring: Metres(-10, -10, 10, 10))) });
            Assert.That(Sha(b), Is.EqualTo(Sha(before)));
        }

        [Test]
        public void TheMaxOfBaseAndLiveEntitiesWins()
        {
            // Base: 200 in rows 120…130; a 128 square of [-10, 10]² m crosses it.
            byte[] b = new byte[Size * Size];
            for (int k = 120 * Size; k < 131 * Size; k++)
            {
                b[k] = 200;
            }

            byte[] out1 = EditEngine.Apply(b, Synth, new[] { E(1, "ev1", "1", H(128, ring: Metres(-10, -10, 10, 10))) });

            // Oracle: 210 samples at 128, the 2827 of the stripe still 200.
            Assert.That(out1.Count(v => v == 128), Is.EqualTo(210));
            Assert.That(out1.Count(v => v == 200), Is.EqualTo(2827));
            Assert.That(Sha(out1), Is.EqualTo("802eea5c7d66eb3bf772456eb806a8a15275f63863d60b2d8f39d34f54564431"));
        }

        [Test]
        public void AnObstacleIsWorth255()
        {
            byte[] out1 = EditEngine.Apply(new byte[Size * Size], Synth, new[] { E(1, "ev1", "1", Ob(ring: Metres(-10, -10, 10, 10))) });

            // Oracle: 21 × 21 = 441 samples at 255.
            Assert.That(out1.Count(v => v == 255), Is.EqualTo(441));
            Assert.That(Sha(out1), Is.EqualTo("5a534a50a12eda65cf23a105e4a96653041c87688b28b981ca5ff1e786d18454"));
        }

        [Test]
        public void ANoteNeverTouchesTheTile()
        {
            byte[] b = Pattern();
            Assert.That(Sha(EditEngine.Apply(b, Synth, new[] { E(1, "ev1", "1", Note("here")) })), Is.EqualTo(Sha(b)));
        }

        [Test]
        public void TheOriginPlacesTheVertices()
        {
            Edit[] log = { E(1, "ev1", "1", H(9, ring: Metres(0, 0, 2, 2))) };

            // Oracle: origin (0, 0) → samples (254…256, 0…2), first 65278, last 65794;
            // origin (-128, -128) → (126…128, 128…130), first 32510, last 33026: 128 samples over and up.
            int[] at0 = Marked(EditEngine.Apply(new byte[Size * Size], new EnuMetres(0, 0), log));
            int[] atSynth = Marked(EditEngine.Apply(new byte[Size * Size], Synth, log));
            Assert.That(at0, Has.Length.EqualTo(9));
            Assert.That((at0.First(), at0.Last()), Is.EqualTo((65278, 65794)));
            Assert.That(atSynth, Has.Length.EqualTo(9));
            Assert.That((atSynth.First(), atSynth.Last()), Is.EqualTo((32510, 33026)));
        }

        [Test]
        public void APolygonPartlyOutsideTheTileOnlyTouchesTheInside()
        {
            byte[] zero = new byte[Size * Size];

            // [100, 200] × [-10, 10] m on a tile of [-128, 128]²: oracle 609 = 29 × 21 samples, (118, 228)…(138, 256).
            byte[] part = EditEngine.Apply(zero, Synth, new[] { E(1, "ev1", "1", H(50, ring: Metres(100, -10, 200, 10))) });
            int[] marked = Marked(part);
            Assert.That(marked, Has.Length.EqualTo(609));
            Assert.That((marked.First(), marked.Last()), Is.EqualTo((118 * Size + 228, 138 * Size + 256)));
            Assert.That(Sha(part), Is.EqualTo("0f47954ea359b98eca46a94fab7787299e27dab18feaefbd4ae82d62cf49496a"));

            // Wholly outside: nothing changes.
            Assert.That(Sha(EditEngine.Apply(zero, Synth, new[] { E(1, "ev1", "1", H(50, ring: Metres(200, -10, 300, 10))) })), Is.EqualTo(Sha(zero)));

            // The int extremes: a square covers every sample; the triangle's oracle hash.
            FixedPoint[] huge = { new FixedPoint(int.MinValue, int.MinValue), new FixedPoint(int.MaxValue, int.MinValue), new FixedPoint(int.MaxValue, int.MaxValue), new FixedPoint(int.MinValue, int.MaxValue) };
            FixedPoint[] tri = { new FixedPoint(int.MinValue, int.MinValue), new FixedPoint(int.MaxValue, int.MinValue), new FixedPoint(0, int.MaxValue) };
            Assert.That(EditEngine.Apply(zero, Synth, new[] { E(1, "ev1", "1", H(50, ring: huge)) }).All(v => v == 50), Is.True);
            byte[] t = EditEngine.Apply(zero, Synth, new[] { E(1, "ev1", "1", H(50, ring: tri)) });
            Assert.That(Sha(t), Is.EqualTo("0f32e82d733f5a1550c3c4194613b4d57fcba53feae787f1426541784ba2f9c4"));
        }

        [Test]
        public void ApplyAndLiveEntitiesAgree()
        {
            // The log of LiveEntitiesTests.RaisingOrMovingIsPlainLww plus a lowering that is ignored,
            // a tombstoned entity and a note: the changed samples are exactly those of the live polygons.
            Edit[] log =
            {
                E(1, "ev1", "1", H(100)), E(2, "ev2", "1", H(200, ring: Metres(-20, -20, -5, -5)), creator: "ev1"),
                E(1, "ev1", "2", H(100)), E(2, "ev2", "2", H(100, ring: Moved), creator: "ev1"),
                E(1, "ev1", "3", Ob(ring: Metres(30, 30, 40, 50))), E(3, "ev2", "3", Ob(ring: Metres(30, 30, 40, 50)), creator: "ev1", tombstone: true),
                E(1, "ev1", "4", H(60, ring: Metres(50, -60, 70, -40))), E(2, "ev1", "4", H(60, ring: Metres(50, -60, 70, -40)), tombstone: true),
                E(1, "ev1", "5", Note("x")),
            };

            byte[] out1 = EditEngine.Apply(new byte[Size * Size], Synth, log);
            var expected = new byte[Size * Size];
            foreach (LiveEntity live in EditEngine.LiveEntities(log).Where(l => l.Payload.Polygon != null))
            {
                for (int r = 0; r < Size; r++)
                {
                    for (int c = 0; c < Size; c++)
                    {
                        long x = 256L * (Synth.E + c), y = 256L * (Synth.N + Size - 1 - r);
                        if (Polygon.Contains(live.Payload.Polygon, x, y))
                        {
                            expected[r * Size + c] = Math.Max(expected[r * Size + c], live.Level.Value);
                        }
                    }
                }
            }

            Assert.That(Sha(out1), Is.EqualTo(Sha(expected)));
            Assert.That(Marked(out1), Is.Not.Empty);
        }

        [Test]
        public void ArgumentsAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => EditEngine.Apply(null, Synth, new Edit[0]));
            Assert.Throws<ArgumentException>(() => EditEngine.Apply(new byte[Size * Size - 1], Synth, new Edit[0]));
            Assert.Throws<ArgumentNullException>(() => EditEngine.Apply(new byte[Size * Size], Synth, null));
            Assert.Throws<ArgumentNullException>(() => EditEngine.Apply(new byte[Size * Size], Synth, new Edit[] { null }));
            Assert.Throws<ArgumentException>(() => EditEngine.Apply(new byte[Size * Size], Synth, new[] { E(1, "ev1", "1", H(1), uuid: "d"), E(2, "ev1", "2", H(1), uuid: "d") }));
        }

        internal static string Sha(byte[] data)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        /// <summary>The square [e0, e1] × [n0, n1] in whole metres, as a fixed-point ring.</summary>
        private static FixedPoint[] Metres(int e0, int n0, int e1, int n1) =>
            new[] { new FixedPoint(256 * e0, 256 * n0), new FixedPoint(256 * e1, 256 * n0), new FixedPoint(256 * e1, 256 * n1), new FixedPoint(256 * e0, 256 * n1) };

        private static int[] Marked(byte[] tile)
        {
            var marked = new List<int>();
            for (int k = 0; k < tile.Length; k++)
            {
                if (tile[k] != 0)
                {
                    marked.Add(k);
                }
            }

            return marked.ToArray();
        }

        private static byte[] Pattern()
        {
            var b = new byte[Size * Size];
            for (int k = 0; k < b.Length; k++)
            {
                b[k] = (byte)(k * 31 % 251);
            }

            return b;
        }
    }
}
