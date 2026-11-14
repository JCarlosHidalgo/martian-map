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
        /// <summary>
        /// Returns the <c>hazard</c> of a <c>z = 0</c> tile with the edits applied:
        /// <c>max(base, live entities)</c>. <paramref name="originEnuM"/> is the tile's south-west
        /// corner in whole metres. A sample is inside a polygon if it falls inside or on its edge,
        /// with the even-odd rule. The input arrays are not modified.
        /// </summary>
        public static byte[] Apply(byte[] baseHazard, EnuMetres originEnuM, Edit[] edits) =>
            throw new NotImplementedException();

        /// <summary>
        /// The live entities of a log, one per <c>entity_id</c>, with the same rules and the same
        /// walk as <see cref="Apply"/>. A tombstoned entity is not live.
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
    }
}
