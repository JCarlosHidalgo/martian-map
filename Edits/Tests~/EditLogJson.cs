using System;
using System.IO;
using System.Text.Json;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// Reads an <c>edit_log</c> (<c>{schema_version, edits[]}</c>, docs/guide/chapters/habitat.md, "Log
    /// de ediciones y merge (D5)") into <see cref="Edit"/>s, for the tests only: <c>Edits</c> reads no
    /// JSON. <c>from_habitat</c> does not travel, so the caller says what it is.
    /// </summary>
    internal static class EditLogJson
    {
        internal static Edit[] Read(string path, bool fromHabitat)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = doc.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1)
            {
                throw new InvalidDataException($"{path}: schema_version is not 1.");
            }

            JsonElement edits = root.GetProperty("edits");
            var result = new Edit[edits.GetArrayLength()];
            int k = 0;
            foreach (JsonElement e in edits.EnumerateArray())
            {
                string opText = e.GetProperty("op").GetString();
                if (!EditOpText.TryParse(opText, out EditOp op))
                {
                    throw new InvalidDataException($"{path}: unknown op '{opText}'.");
                }

                result[k++] = new Edit(
                    e.GetProperty("uuid").GetString(),
                    e.GetProperty("lamport").GetInt64(),
                    e.GetProperty("author").GetString(),
                    e.GetProperty("creator").GetString(),
                    e.GetProperty("zone_id").GetString(),
                    e.GetProperty("entity_id").GetString(),
                    op,
                    Payload(op, e.GetProperty("payload")),
                    e.GetProperty("tombstone").GetBoolean(),
                    fromHabitat);
            }

            return result;
        }

        /// <summary>The <c>origin_enu_m</c> of a <c>tile.json</c>, in whole metres.</summary>
        internal static EnuMetres TileOrigin(string tileJsonPath)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(tileJsonPath));
            JsonElement o = doc.RootElement.GetProperty("origin_enu_m");
            return new EnuMetres(o[0].GetInt32(), o[1].GetInt32());
        }

        private static EditPayload Payload(EditOp op, JsonElement p)
        {
            switch (op)
            {
                case EditOp.HazardSet:
                    return EditPayload.Hazard(Ring(p.GetProperty("polygon")), p.GetProperty("level").GetByte(), Confirmed(p));
                case EditOp.ObstacleSet:
                    return EditPayload.Obstacle(Ring(p.GetProperty("polygon")), Confirmed(p));
                default:
                    return EditPayload.Note(Point(p.GetProperty("point")), p.GetProperty("text").GetString());
            }
        }

        private static bool? Confirmed(JsonElement p) =>
            p.TryGetProperty("confirmed", out JsonElement c) ? c.GetBoolean() : (bool?)null;

        private static FixedPoint[] Ring(JsonElement polygon)
        {
            var ring = new FixedPoint[polygon.GetArrayLength()];
            int k = 0;
            foreach (JsonElement v in polygon.EnumerateArray())
            {
                ring[k++] = Point(v);
            }

            return ring;
        }

        private static FixedPoint Point(JsonElement v) =>
            v.GetArrayLength() == 2 ? new FixedPoint(v[0].GetInt32(), v[1].GetInt32()) : throw new InvalidDataException("A point is [e, n].");
    }
}
