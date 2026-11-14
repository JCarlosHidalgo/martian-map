using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MartianMap.Edits
{
    /// <summary>
    /// The edit operations (docs/guide/chapters/convenciones.md, "Catálogo de op"). 0 is
    /// <see cref="None"/>, so a forgotten or failed value never reads as <c>hazard.set</c>.
    /// </summary>
    public enum EditOp
    {
        /// <summary>No operation: what <see cref="EditOpText.TryParse"/> leaves when it fails. No edit, payload or entity carries it.</summary>
        None = 0,

        /// <summary><c>hazard.set</c>: a hazard zone, <c>{polygon, level, confirmed}</c>.</summary>
        HazardSet = 1,

        /// <summary><c>obstacle.set</c>: an obstacle, <c>{polygon, confirmed}</c>; worth level 255.</summary>
        ObstacleSet = 2,

        /// <summary><c>note.set</c>: a note, <c>{point, text}</c>. It never touches the tile.</summary>
        NoteSet = 3,
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
    /// The payload of one <see cref="EditOp"/>, built only through <see cref="Hazard"/>,
    /// <see cref="Obstacle"/> and <see cref="Note"/>: each one fills its own fields and leaves the
    /// others null. A polygon is a ring of 3 or more vertices whose first vertex is not repeated at
    /// the end; that it is simple is up to the caller, it is not checked here. It is immutable:
    /// the factories copy the polygon.
    /// </summary>
    public sealed class EditPayload
    {
        private EditPayload(EditOp op, IReadOnlyList<FixedPoint> polygon, FixedPoint? point, byte? level, bool? confirmed, string text)
        {
            Op = op;
            Polygon = polygon;
            Point = point;
            Level = level;
            Confirmed = confirmed;
            Text = text;
        }

        /// <summary>The <see cref="EditOp"/> this payload was built for.</summary>
        public EditOp Op { get; }

        /// <summary>Set for <c>hazard.set</c> and <c>obstacle.set</c>; null for a note.</summary>
        public IReadOnlyList<FixedPoint> Polygon { get; }

        /// <summary>Set for <c>note.set</c> only.</summary>
        public FixedPoint? Point { get; }

        /// <summary>Set for <c>hazard.set</c> only; an obstacle is worth 255 without carrying it.</summary>
        public byte? Level { get; }

        /// <summary>Optional, for <c>hazard.set</c> and <c>obstacle.set</c>. Set by the HUD after the confirmation dialog.</summary>
        public bool? Confirmed { get; }

        /// <summary>Set for <c>note.set</c> only.</summary>
        public string Text { get; }

        /// <summary>The payload of <c>hazard.set</c>: <c>{polygon, level, confirmed}</c>. The polygon is copied.</summary>
        public static EditPayload Hazard(FixedPoint[] polygon, byte level, bool? confirmed) =>
            new EditPayload(EditOp.HazardSet, CopyRing(polygon), null, level, confirmed, null);

        /// <summary>The payload of <c>obstacle.set</c>: <c>{polygon, confirmed}</c>. The polygon is copied.</summary>
        public static EditPayload Obstacle(FixedPoint[] polygon, bool? confirmed) =>
            new EditPayload(EditOp.ObstacleSet, CopyRing(polygon), null, null, confirmed, null);

        /// <summary>The payload of <c>note.set</c>: <c>{point, text}</c>.</summary>
        public static EditPayload Note(FixedPoint point, string text) =>
            new EditPayload(EditOp.NoteSet, null, point, null, null, text ?? throw new ArgumentNullException(nameof(text)));

        private static IReadOnlyList<FixedPoint> CopyRing(FixedPoint[] polygon)
        {
            if (polygon == null)
            {
                throw new ArgumentNullException(nameof(polygon));
            }

            if (polygon.Length < 3)
            {
                throw new ArgumentException("A polygon has at least 3 vertices.", nameof(polygon));
            }

            FixedPoint first = polygon[0];
            FixedPoint last = polygon[polygon.Length - 1];
            if (first.E == last.E && first.N == last.N)
            {
                throw new ArgumentException("The first vertex is not repeated at the end.", nameof(polygon));
            }

            return new ReadOnlyCollection<FixedPoint>((FixedPoint[])polygon.Clone());
        }
    }

    /// <summary>
    /// One edit of the log: the fields of the <c>Edit</c> schema, plus <see cref="FromHabitat"/>,
    /// which does not travel. The constructor checks the formats of
    /// docs/guide/chapters/convenciones.md, "Identificadores", and that the payload was built for
    /// <c>op</c>.
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
            Uuid = RequireUuidV4(uuid, nameof(uuid));
            if (lamport < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lamport), "The Lamport clock starts at 1.");
            }

            Lamport = lamport;
            Author = RequireAuthor(author, nameof(author));
            Creator = RequireAuthor(creator, nameof(creator));
            ZoneId = RequireZoneId(zoneId, nameof(zoneId));
            EntityId = RequireUuidV4(entityId, nameof(entityId));
            if (op != EditOp.HazardSet && op != EditOp.ObstacleSet && op != EditOp.NoteSet)
            {
                throw new ArgumentOutOfRangeException(nameof(op));
            }

            Op = op;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            if (payload.Op != op)
            {
                throw new ArgumentException($"The payload was built for {payload.Op}, not {op}.", nameof(payload));
            }

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

        private static bool IsLowerAlnumOrDash(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';

        private static bool IsLowerHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');

        /// <summary>A lowercase UUIDv4 with dashes: <c>xxxxxxxx-xxxx-4xxx-[89ab]xxx-xxxxxxxxxxxx</c>.</summary>
        private static string RequireUuidV4(string value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            bool ok = value.Length == 36 && value[14] == '4' && "89ab".IndexOf(value[19]) >= 0;
            for (int k = 0; ok && k < value.Length; k++)
            {
                ok = k == 8 || k == 13 || k == 18 || k == 23 ? value[k] == '-' : IsLowerHex(value[k]);
            }

            return ok ? value : throw new ArgumentException("Expected a lowercase UUIDv4 with dashes.", name);
        }

        /// <summary><c>^[a-z0-9-]{2,16}$</c>.</summary>
        private static string RequireAuthor(string value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            bool ok = value.Length >= 2 && value.Length <= 16;
            for (int k = 0; ok && k < value.Length; k++)
            {
                ok = IsLowerAlnumOrDash(value[k]);
            }

            return ok ? value : throw new ArgumentException("Expected ^[a-z0-9-]{2,16}$.", name);
        }

        /// <summary><c>^[a-z][a-z0-9-]{1,31}$</c>.</summary>
        private static string RequireZoneId(string value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            bool ok = value.Length >= 2 && value.Length <= 32 && value[0] >= 'a' && value[0] <= 'z';
            for (int k = 1; ok && k < value.Length; k++)
            {
                ok = IsLowerAlnumOrDash(value[k]);
            }

            return ok ? value : throw new ArgumentException("Expected ^[a-z][a-z0-9-]{1,31}$.", name);
        }
    }

    /// <summary>
    /// A live entity: the winner of one <c>entity_id</c>. <see cref="Level"/> is the
    /// <c>hazard.set</c> level, 255 for <c>obstacle.set</c> and null for a note. The constructor
    /// throws unless <c>op</c> is defined, the payload was built for it and the level matches it.
    /// </summary>
    public sealed class LiveEntity
    {
        /// <summary>The level of an <c>obstacle.set</c>.</summary>
        private const byte ObstacleLevel = 255;

        public LiveEntity(string entityId, EditOp op, string creator, byte? level, EditPayload payload)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            Creator = creator ?? throw new ArgumentNullException(nameof(creator));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            if (op != EditOp.HazardSet && op != EditOp.ObstacleSet && op != EditOp.NoteSet)
            {
                throw new ArgumentOutOfRangeException(nameof(op));
            }

            if (payload.Op != op)
            {
                throw new ArgumentException($"The payload was built for {payload.Op}, not {op}.", nameof(payload));
            }

            byte? expected = op == EditOp.HazardSet ? payload.Level : op == EditOp.ObstacleSet ? ObstacleLevel : (byte?)null;
            if (level != expected)
            {
                throw new ArgumentException($"The level of {op} must be {(expected.HasValue ? expected.ToString() : "null")}.", nameof(level));
            }

            Op = op;
            Level = level;
        }

        public string EntityId { get; }

        public EditOp Op { get; }

        public string Creator { get; }

        public byte? Level { get; }

        public EditPayload Payload { get; }
    }
}
