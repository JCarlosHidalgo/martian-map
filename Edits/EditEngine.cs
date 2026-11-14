using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MartianMap.Edits
{
    /// <summary>
    /// <c>apply()</c> (D5): a pure function with integer arithmetic and a single implementation.
    /// Edits are walked in <c>(lamport, author, uuid)</c> order, with the same confirmation rule
    /// as the habitat's merge.
    /// </summary>
    public static class EditEngine
    {
        /// <summary>Samples per side of a <c>z = 0</c> tile.</summary>
        internal const int TileSizePx = 257;

        /// <summary>
        /// Returns the <c>hazard</c> of a <c>z = 0</c> tile with the edits applied:
        /// <c>max(base, live entities)</c>. <paramref name="originEnuM"/> is the tile's south-west
        /// corner in whole metres. A sample is inside a polygon if it falls inside or on its edge,
        /// with the even-odd rule; sample <c>(r, c)</c> is at <c>(256·(origin.E + c), 256·(origin.N + 256 − r))</c>
        /// in fixed point. The winners are those of <see cref="LiveEntities"/>. The input arrays are not
        /// modified. Throws <see cref="ArgumentNullException"/> for a null <paramref name="baseHazard"/>,
        /// <paramref name="edits"/> or edit, and <see cref="ArgumentException"/> for a hazard that is not
        /// 257 × 257 or two edits with the same <c>uuid</c>; an edit the rule ignores does not throw.
        /// </summary>
        public static byte[] Apply(byte[] baseHazard, EnuMetres originEnuM, Edit[] edits)
        {
            if (baseHazard == null)
            {
                throw new ArgumentNullException(nameof(baseHazard));
            }

            if (baseHazard.Length != TileSizePx * TileSizePx)
            {
                throw new ArgumentException($"A z = 0 hazard has {TileSizePx} × {TileSizePx} samples.", nameof(baseHazard));
            }

            Edit[] winners = EditMerge.Winners(edits);
            var tile = (byte[])baseHazard.Clone();
            foreach (Edit w in winners)
            {
                if (!w.Tombstone && w.Payload.Polygon != null)
                {
                    Paint(tile, originEnuM, w.Payload.Polygon, EditMerge.LevelOf(w).Value);
                }
            }

            return tile;
        }

        /// <summary>
        /// The live entities of a log, one per <c>entity_id</c>, with the same rules and the same
        /// walk as <see cref="Apply"/>. A tombstoned entity is not live. An edit replaces its entity's
        /// winner unless it has another <c>op</c>, or it lowers a live entity (a tombstone or a lower
        /// level) and its author is not its creator, it is not <c>confirmed</c> and not
        /// <see cref="Edit.FromHabitat"/>. The list is in the winners' <c>(lamport, author, uuid)</c>
        /// order, that of <c>log_merged.json</c>. Throws <see cref="ArgumentNullException"/> for a null
        /// array or edit and <see cref="ArgumentException"/> for a repeated <c>uuid</c>.
        /// </summary>
        public static IReadOnlyList<LiveEntity> LiveEntities(Edit[] edits)
        {
            Edit[] winners = EditMerge.Winners(edits);
            var live = new List<LiveEntity>(winners.Length);
            foreach (Edit w in winners)
            {
                if (!w.Tombstone)
                {
                    live.Add(new LiveEntity(w.EntityId, w.Op, w.Creator, EditMerge.LevelOf(w), w.Payload));
                }
            }

            return new ReadOnlyCollection<LiveEntity>(live);
        }

        /// <summary>
        /// <c>max</c> of <paramref name="level"/> into the samples inside <paramref name="ring"/>. Sample
        /// <c>(r, c)</c> is at <c>(256·(origin.E + c), 256·(origin.N + 256 − r))</c>; only the samples
        /// of the ring's box, clipped to the tile, are tested.
        /// </summary>
        private static void Paint(byte[] tile, EnuMetres origin, IReadOnlyList<FixedPoint> ring, byte level)
        {
            long minX = long.MaxValue, maxX = long.MinValue, minY = long.MaxValue, maxY = long.MinValue;
            for (int k = 0; k < ring.Count; k++)
            {
                minX = Math.Min(minX, ring[k].E);
                maxX = Math.Max(maxX, ring[k].E);
                minY = Math.Min(minY, ring[k].N);
                maxY = Math.Max(maxY, ring[k].N);
            }

            const int Last = TileSizePx - 1;
            long c0 = Math.Max(0, CeilDiv(minX) - origin.E), c1 = Math.Min(Last, FloorDiv(maxX) - origin.E);
            long r0 = Math.Max(0, (long)origin.N + Last - FloorDiv(maxY)), r1 = Math.Min(Last, (long)origin.N + Last - CeilDiv(minY));
            for (long r = r0; r <= r1; r++)
            {
                long y = FixedPoint.UnitsPerMetre * ((long)origin.N + Last - r);
                for (long c = c0; c <= c1; c++)
                {
                    long x = FixedPoint.UnitsPerMetre * ((long)origin.E + c);
                    int k = (int)(r * TileSizePx + c);
                    if (tile[k] < level && Polygon.Contains(ring, x, y))
                    {
                        tile[k] = level;
                    }
                }
            }
        }

        /// <summary>⌊v / 256⌋, rounding toward −∞.</summary>
        private static long FloorDiv(long v) => v >= 0 ? v / FixedPoint.UnitsPerMetre : -((-v + FixedPoint.UnitsPerMetre - 1) / FixedPoint.UnitsPerMetre);

        /// <summary>⌈v / 256⌉, rounding toward +∞.</summary>
        private static long CeilDiv(long v) => -FloorDiv(-v);
    }
}
