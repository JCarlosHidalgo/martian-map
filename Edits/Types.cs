using System;

namespace MartianMap.Edits
{
    /// <summary>The edit operations (docs/guide/chapters/convenciones.md, "Catálogo de op").</summary>
    public enum EditOp
    {
        /// <summary><c>hazard.set</c>: a hazard zone, <c>{polygon, level, confirmed}</c>.</summary>
        HazardSet = 0,

        /// <summary><c>obstacle.set</c>: an obstacle, <c>{polygon, confirmed}</c>; worth level 255.</summary>
        ObstacleSet = 1,

        /// <summary><c>note.set</c>: a note, <c>{point, text}</c>. It never touches the tile.</summary>
        NoteSet = 2,
    }

    /// <summary>
    /// A vertex or point <c>[e, n]</c> in fixed point: integer 1/256 of a 1 m cell, relative to the
    /// zone's ENU origin. No float enters <see cref="EditEngine.Apply"/>.
    /// </summary>
    public readonly struct FixedPoint
    {
        public const int UnitsPerMetre = 256;

        public FixedPoint(int e, int n)
        {
            E = e;
            N = n;
        }

        public int E { get; }

        public int N { get; }
    }

    /// <summary>A position in whole metres of the zone's ENU frame, such as a tile's south-west corner.</summary>
    public readonly struct EnuMetres
    {
        public EnuMetres(int e, int n)
        {
            E = e;
            N = n;
        }

        public int E { get; }

        public int N { get; }
    }

    /// <summary>
    /// The fields of the three payloads. Each <see cref="EditOp"/> uses its own and leaves the
    /// others null. A polygon is a simple ring of 3 or more vertices; the first is not repeated.
    /// </summary>
    public sealed class EditPayload
    {
        public EditPayload(FixedPoint[] polygon, FixedPoint? point, byte? level, bool? confirmed, string text)
        {
            if (polygon != null && polygon.Length < 3)
            {
                throw new ArgumentException("A polygon has at least 3 vertices.", nameof(polygon));
            }

            Polygon = polygon;
            Point = point;
            Level = level;
            Confirmed = confirmed;
            Text = text;
        }

        public FixedPoint[] Polygon { get; }

        public FixedPoint? Point { get; }

        public byte? Level { get; }

        /// <summary>Optional. Set by the HUD after the confirmation dialog.</summary>
        public bool? Confirmed { get; }

        public string Text { get; }
    }

    /// <summary>
    /// One edit of the log: the fields of the <c>Edit</c> schema, plus <see cref="FromHabitat"/>,
    /// which does not travel.
    /// </summary>
    public sealed class Edit
    {
        public Edit(
            string uuid,
            long lamport,
            string author,
            string creator,
            string zoneId,
            string entityId,
            EditOp op,
            EditPayload payload,
            bool tombstone,
            bool fromHabitat)
        {
            Uuid = Require(uuid, nameof(uuid));
            Lamport = lamport;
            Author = Require(author, nameof(author));
            Creator = Require(creator, nameof(creator));
            ZoneId = Require(zoneId, nameof(zoneId));
            EntityId = Require(entityId, nameof(entityId));
            Op = op;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Tombstone = tombstone;
            FromHabitat = fromHabitat;
        }

        public string Uuid { get; }

        public long Lamport { get; }

        /// <summary>The device that wrote this edit; compared as text, byte by byte, to break ties.</summary>
        public string Author { get; }

        /// <summary>The <c>author</c> of the entity's first edition.</summary>
        public string Creator { get; }

        public string ZoneId { get; }

        public string EntityId { get; }

        public EditOp Op { get; }

        public EditPayload Payload { get; }

        public bool Tombstone { get; }

        /// <summary>
        /// True for a winner received through <c>changes</c>, or a local edit whose ack was
        /// <c>accepted</c>: its <c>confirmed</c> is not evaluated again.
        /// </summary>
        public bool FromHabitat { get; }

        private static string Require(string value, string name) =>
            string.IsNullOrEmpty(value) ? throw new ArgumentException("Required.", name) : value;
    }

    /// <summary>
    /// A live entity: the winner of one <c>entity_id</c>. <see cref="Level"/> is the
    /// <c>hazard.set</c> level, 255 for <c>obstacle.set</c> and null for a note.
    /// </summary>
    public sealed class LiveEntity
    {
        public LiveEntity(string entityId, EditOp op, string creator, byte? level, EditPayload payload)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            Op = op;
            Creator = creator ?? throw new ArgumentNullException(nameof(creator));
            Level = level;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public string EntityId { get; }

        public EditOp Op { get; }

        public string Creator { get; }

        public byte? Level { get; }

        public EditPayload Payload { get; }
    }
}
