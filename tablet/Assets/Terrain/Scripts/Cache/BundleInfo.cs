using System.Collections.Generic;
using Newtonsoft.Json;

namespace MartianMap.Terrain
{
    /// <summary><c>bundle.json</c> (C1b, "Bundle y spool con DELAY").</summary>
    public sealed class BundleInfo
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("bundle_id")] public string BundleId { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("zone_id")] public string ZoneId { get; set; }
        [JsonProperty("zone_version")] public int ZoneVersion { get; set; }

        /// <summary><c>zone_full</c>, <c>zone_update</c> or <c>conditions</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; }

        [JsonProperty("created_utc")] public string CreatedUtc { get; set; }
        [JsonProperty("delay_s")] public int DelayS { get; set; }
        [JsonProperty("deliver_at_utc")] public string DeliverAtUtc { get; set; }
        [JsonProperty("expires_utc")] public string ExpiresUtc { get; set; }
        [JsonProperty("files")] public List<BundleFile> Files { get; set; } = new List<BundleFile>();
        [JsonProperty("payload_oxum")] public string PayloadOxum { get; set; }
    }

    public sealed class BundleFile
    {
        /// <summary>Path inside the bundle, e.g. <c>data/zone.json</c>.</summary>
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("size")] public long Size { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
        [JsonProperty("media_type")] public string MediaType { get; set; }
    }
}
