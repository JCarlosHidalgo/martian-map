using System.Text;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// A factory of test edits with short names. <c>entity</c> "7" is the UUIDv4
    /// <c>e0000000-0000-4000-8000-000000000007</c>; an explicit <c>uuid</c> "c1" is
    /// <c>00000000-0000-4000-8000-0000000000c1</c> (both hexadecimal, up to 12 digits). Without
    /// <c>uuid</c>, it is <c>{lamport:x8}-{entity:4}-4000-8000-{author as hex bytes}</c>, so two edits of one
    /// entity with the same lamport and author need an explicit one. The oracle (cases_apply.py) expands them the same way.
    /// </summary>
    internal static class Log
    {
        internal static readonly FixedPoint[] Square = { new FixedPoint(0, 0), new FixedPoint(1024, 0), new FixedPoint(1024, 1024), new FixedPoint(0, 1024) };

        internal static readonly FixedPoint[] Moved = { new FixedPoint(256, 0), new FixedPoint(1280, 0), new FixedPoint(1280, 1024), new FixedPoint(256, 1024) };

        internal static Edit E(
            int lamport,
            string author,
            string entity,
            EditPayload payload,
            string creator = null,
            bool tombstone = false,
            bool fromHabitat = false,
            string uuid = null) =>
            new Edit(
                uuid != null ? Uuid(uuid) : DefaultUuid(lamport, entity, author),
                lamport,
                author,
                creator ?? author,
                "synth",
                Entity(entity),
                payload.Op,
                payload,
                tombstone,
                fromHabitat);

        internal static EditPayload H(byte level, bool? confirmed = null, FixedPoint[] ring = null) => EditPayload.Hazard(ring ?? Square, level, confirmed);

        internal static EditPayload Ob(bool? confirmed = null, FixedPoint[] ring = null) => EditPayload.Obstacle(ring ?? Square, confirmed);

        internal static EditPayload Note(string text) => EditPayload.Note(new FixedPoint(512, 512), text);

        internal static string Entity(string shortName) => "e0000000-0000-4000-8000-" + shortName.PadLeft(12, '0');

        internal static string Uuid(string shortName) => "00000000-0000-4000-8000-" + shortName.PadLeft(12, '0');

        private static string DefaultUuid(int lamport, string entity, string author)
        {
            var hex = new StringBuilder();
            foreach (byte b in Encoding.ASCII.GetBytes(author))
            {
                hex.Append(b.ToString("x2"));
            }

            return lamport.ToString("x8") + "-" + entity.PadLeft(4, '0') + "-4000-8000-" + hex.ToString().PadLeft(12, '0');
        }
    }
}
