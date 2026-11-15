using System;
using System.Collections.Generic;

namespace MartianMap.Edits
{
    /// <summary>
    /// The only merge walk in C# (docs/guide/chapters/convenciones.md, "Bajar un peligro"):
    /// <see cref="EditEngine.Apply"/> and <see cref="EditEngine.LiveEntities"/> both read its winners.
    /// </summary>
    internal static class EditMerge
    {
        /// <summary>
        /// The winner of each <c>entity_id</c>, tombstones included, sorted by the winner's
        /// <c>(lamport, author, uuid)</c>. The edits are walked in that order; an edit replaces the
        /// current winner of its entity (LWW) unless the rule ignores it entirely: it has another
        /// <c>op</c> than the winner; or the winner is live, the edit lowers it (a tombstone or a
        /// lower level of <c>hazard.set</c>/<c>obstacle.set</c>), its <c>author</c> is not its
        /// <c>creator</c>, its <c>confirmed</c> is not true and it is not <see cref="Edit.FromHabitat"/>.
        /// A dead winner has no level, so the next edit of the same <c>op</c> replaces it.
        /// Throws <see cref="ArgumentNullException"/> for a null array or element and
        /// <see cref="ArgumentException"/> for a repeated <c>uuid</c>.
        /// </summary>
        internal static Edit[] Winners(Edit[] edits)
        {
            if (edits == null)
            {
                throw new ArgumentNullException(nameof(edits));
            }

            var uuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Edit e in edits)
            {
                if (e == null)
                {
                    throw new ArgumentNullException(nameof(edits), "An edit is null.");
                }

                if (!uuids.Add(e.Uuid))
                {
                    throw new ArgumentException($"The uuid {e.Uuid} is repeated.", nameof(edits));
                }
            }

            var sorted = (Edit[])edits.Clone();
            Array.Sort(sorted, WalkOrder.Instance);

            var current = new Dictionary<string, int>(StringComparer.Ordinal);
            var winners = new List<Edit>();
            foreach (Edit e in sorted)
            {
                if (!current.TryGetValue(e.EntityId, out int slot))
                {
                    current.Add(e.EntityId, winners.Count);
                    winners.Add(e);
                }
                else if (!Ignored(winners[slot], e))
                {
                    winners[slot] = e;
                }
            }

            Edit[] result = winners.ToArray();
            Array.Sort(result, WalkOrder.Instance);
            return result;
        }

        /// <summary>The level of an edit: the <c>hazard.set</c> level, 255 for an obstacle, null for a note.</summary>
        internal static byte? LevelOf(Edit e) =>
            e.Op == EditOp.HazardSet ? e.Payload.Level : e.Op == EditOp.ObstacleSet ? (byte?)255 : null;

        private static bool Ignored(Edit winner, Edit e)
        {
            if (e.Op != winner.Op)
            {
                return true;
            }

            if (winner.Tombstone || e.Op == EditOp.NoteSet)
            {
                return false;
            }

            bool lowers = e.Tombstone || LevelOf(e) < LevelOf(winner);
            return lowers
                && !string.Equals(e.Author, e.Creator, StringComparison.Ordinal)
                && e.Payload.Confirmed != true
                && !e.FromHabitat;
        }

        /// <summary><c>(lamport, author, uuid)</c>, the strings compared ordinally (byte by byte: they are ASCII).</summary>
        private sealed class WalkOrder : IComparer<Edit>
        {
            internal static readonly WalkOrder Instance = new WalkOrder();

            public int Compare(Edit a, Edit b)
            {
                int c = a.Lamport.CompareTo(b.Lamport);
                if (c != 0)
                {
                    return c;
                }

                c = string.CompareOrdinal(a.Author, b.Author);
                return c != 0 ? c : string.CompareOrdinal(a.Uuid, b.Uuid);
            }
        }
    }
}
