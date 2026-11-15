using System;
using System.Collections.Generic;
using System.Linq;
using MartianMap.Edits;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The wire form of one edit (C3, <c>contracts/schemas/edit.schema.json</c>) ↔ <see cref="Edit"/>.
    /// Edits reads no JSON by design, so the mapping lives here, once, for the local log, POST /edits
    /// and changes. <c>fromHabitat</c> never travels. An edit with an unknown <c>op</c> or invalid
    /// fields is bad data: <see cref="TryRead"/> returns false and logs it (Convenciones, "Catálogo de op").
    /// </summary>
    public static class EditJson
    {
        public static JObject Write(Edit edit)
        {
            return new JObject
            {
                ["uuid"] = edit.Uuid,
                ["lamport"] = edit.Lamport,
                ["author"] = edit.Author,
                ["creator"] = edit.Creator,
                ["zone_id"] = edit.ZoneId,
                ["entity_id"] = edit.EntityId,
                ["op"] = EditOpText.ToWire(edit.Op),
                ["payload"] = WritePayload(edit.Payload),
                ["tombstone"] = edit.Tombstone,
            };
        }

        public static JObject WritePayload(EditPayload payload)
        {
            var json = new JObject();
            switch (payload.Op)
            {
                case EditOp.HazardSet:
                    json["polygon"] = WriteRing(payload.Polygon);
                    json["level"] = (int)payload.Level.GetValueOrDefault();
                    break;
                case EditOp.ObstacleSet:
                    json["polygon"] = WriteRing(payload.Polygon);
                    break;
                case EditOp.NoteSet:
                    json["point"] = WritePoint(payload.Point.GetValueOrDefault());
                    json["text"] = payload.Text;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(payload), payload.Op, "payload without a defined op");
            }

            if (payload.Confirmed.HasValue)
            {
                json["confirmed"] = payload.Confirmed.Value;
            }

            return json;
        }

        /// <summary>Reads one wire edit; false (and a log line) for an unknown op or invalid fields.</summary>
        public static bool TryRead(JObject json, bool fromHabitat, out Edit edit)
        {
            edit = null;
            if (json == null)
            {
                return false;
            }

            var opText = (string)json["op"];
            if (!EditOpText.TryParse(opText, out var op))
            {
                Debug.LogWarning($"[Terrain] edit discarded uuid={(string)json["uuid"]} unknown op={opText}");
                return false;
            }

            try
            {
                var payload = ReadPayload(op, (JObject)json["payload"]);
                edit = new Edit(
                    (string)json["uuid"],
                    (long)json["lamport"],
                    (string)json["author"],
                    (string)json["creator"],
                    (string)json["zone_id"],
                    (string)json["entity_id"],
                    op,
                    payload,
                    (bool)json["tombstone"],
                    fromHabitat);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException ||
                                              exception is InvalidCastException || exception is NullReferenceException ||
                                              exception is OverflowException)
            {
                Debug.LogWarning($"[Terrain] edit discarded uuid={(string)json["uuid"]} error={exception.Message}");
                return false;
            }
        }

        public static EditPayload ReadPayload(EditOp op, JObject json)
        {
            if (json == null)
            {
                throw new ArgumentException("payload is missing");
            }

            var confirmed = json["confirmed"] != null && json["confirmed"].Type != JTokenType.Null
                ? (bool?)(bool)json["confirmed"]
                : null;
            switch (op)
            {
                case EditOp.HazardSet:
                    var level = (int)json["level"];
                    if (level < 0 || level > 255)
                    {
                        throw new ArgumentOutOfRangeException(nameof(json), $"level {level} outside 0..255");
                    }

                    return EditPayload.Hazard(ReadRing((JArray)json["polygon"]), (byte)level, confirmed);
                case EditOp.ObstacleSet:
                    return EditPayload.Obstacle(ReadRing((JArray)json["polygon"]), confirmed);
                case EditOp.NoteSet:
                    return EditPayload.Note(ReadPoint((JArray)json["point"]), (string)json["text"]);
                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        /// <summary>An ENU point in metres → the fixed point of the edits (1/256 m, nearest).</summary>
        public static FixedPoint ToFixed(Vector2 enuM)
        {
            return new FixedPoint(
                checked((int)Math.Round(enuM.x * FixedPoint.UnitsPerMetre)),
                checked((int)Math.Round(enuM.y * FixedPoint.UnitsPerMetre)));
        }

        public static Vector2 ToEnu(FixedPoint p) =>
            new Vector2(p.E / (float)FixedPoint.UnitsPerMetre, p.N / (float)FixedPoint.UnitsPerMetre);

        private static JArray WriteRing(IReadOnlyList<FixedPoint> ring) => new JArray(ring.Select(WritePoint));

        private static JArray WritePoint(FixedPoint p) => new JArray(p.E, p.N);

        private static FixedPoint[] ReadRing(JArray ring)
        {
            if (ring == null)
            {
                throw new ArgumentException("polygon is missing");
            }

            return ring.Select(p => ReadPoint((JArray)p)).ToArray();
        }

        private static FixedPoint ReadPoint(JArray point)
        {
            if (point == null || point.Count != 2)
            {
                throw new ArgumentException("a point is [e, n]");
            }

            return new FixedPoint((int)point[0], (int)point[1]);
        }
    }
}
