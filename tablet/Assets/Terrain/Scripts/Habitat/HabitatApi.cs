using System.Collections.Generic;
using System.IO;
using System.Linq;
using MartianMap.Edits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The response bodies of C3 (<c>contracts/openapi.yaml</c>, <c>contracts/schemas/*_response</c>,
    /// <c>edits_ack</c>) and the request body of POST /edits. Pure: no network, testable over the
    /// fixtures in <c>contracts/fixtures/api/</c>.
    /// </summary>
    public static class HabitatApi
    {
        /// <summary>The maximum edits per POST /edits (Convenciones, "API (C3)").</summary>
        public const int MaxEditsPerBatch = 200;

        public static ZonesResponse ParseZones(string json) =>
            JsonConvert.DeserializeObject<ZonesResponse>(json) ?? throw new InvalidDataException("empty /zones response");

        public static ZoneResponse ParseZone(string json)
        {
            var root = JObject.Parse(json);
            var zone = root["zone"] as JObject ?? throw new InvalidDataException("/zones/{id} without zone");
            var conditions = root["conditions"] as JObject;
            return new ZoneResponse(
                (int)root["zone_version"],
                zone,
                conditions);
        }

        public static ChangesResponse ParseChanges(string json, string zoneId)
        {
            var root = JObject.Parse(json);
            var edits = new List<Edit>();
            foreach (var item in root["edits"] as JArray ?? new JArray())
            {
                if (EditJson.TryRead(item as JObject, fromHabitat: true, out var edit))
                {
                    edits.Add(edit);
                }
            }

            var tiles = (root["tiles"] as JArray ?? new JArray()).ToObject<List<ZoneTileRef>>() ?? new List<ZoneTileRef>();
            return new ChangesResponse(
                (long)root["next_since"],
                (bool?)root["has_more"] ?? false,
                edits,
                tiles,
                root["conditions"] as JObject);
        }

        public static IReadOnlyList<EditAck> ParseAcks(string json)
        {
            var root = JObject.Parse(json);
            return (root["acks"] as JArray ?? new JArray())
                .Select(a => new EditAck((string)a["uuid"], (string)a["status"], (string)a["code"]))
                .ToList();
        }

        public static string EditsBody(IEnumerable<Edit> edits)
        {
            return new JObject { ["edits"] = new JArray(edits.Select(EditJson.Write)) }.ToString(Formatting.None);
        }
    }

    public sealed class ZonesResponse
    {
        [JsonProperty("next_since")] public long NextSince { get; set; }
        [JsonProperty("zones")] public List<ZoneVersionJson> Zones { get; set; } = new List<ZoneVersionJson>();
    }

    public sealed class ZoneVersionJson
    {
        [JsonProperty("zone_id")] public string ZoneId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("zone_version")] public int ZoneVersion { get; set; }
    }

    public sealed class ZoneResponse
    {
        public ZoneResponse(int zoneVersion, JObject zone, JObject conditions)
        {
            ZoneVersion = zoneVersion;
            ZoneJson = zone;
            ConditionsJson = conditions;
            Zone = zone.ToObject<ZoneInfo>();
        }

        public int ZoneVersion { get; }

        /// <summary>zone.json as the habitat served it.</summary>
        public JObject ZoneJson { get; }

        /// <summary>conditions.json as the habitat served it, or null.</summary>
        public JObject ConditionsJson { get; }

        public ZoneInfo Zone { get; }
    }

    public sealed class ChangesResponse
    {
        public ChangesResponse(long nextSince, bool hasMore, IReadOnlyList<Edit> edits, IReadOnlyList<ZoneTileRef> tiles, JObject conditions)
        {
            NextSince = nextSince;
            HasMore = hasMore;
            Edits = edits;
            Tiles = tiles;
            Conditions = conditions;
        }

        public long NextSince { get; }
        public bool HasMore { get; }

        /// <summary>The winning edits since the cursor, one per entity.</summary>
        public IReadOnlyList<Edit> Edits { get; }

        /// <summary>New tile versions (by bundle); non-empty means GET /zones/{id} again.</summary>
        public IReadOnlyList<ZoneTileRef> Tiles { get; }

        /// <summary>New conditions, only when they changed.</summary>
        public JObject Conditions { get; }
    }
}
