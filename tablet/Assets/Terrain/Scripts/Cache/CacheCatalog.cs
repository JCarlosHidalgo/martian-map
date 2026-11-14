using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MartianMap.Terrain
{
    /// <summary>
    /// What the local cache holds: zones with their <c>zone_version</c>, each tile and POV image with
    /// its version and <c>sha256</c>, and the <c>since</c> cursors of the habitat client.
    /// </summary>
    public sealed class CacheCatalog
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schema_version")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>The zone the app showed last.</summary>
        [JsonProperty("active_zone")] public string ActiveZone { get; set; }

        [JsonProperty("zones")] public Dictionary<string, CachedZone> Zones { get; set; } = new Dictionary<string, CachedZone>();

        /// <summary>Cursors by stream: <c>zones</c> for <c>GET /zones</c>, <c>changes/{zone_id}</c> per zone.</summary>
        [JsonProperty("cursors")] public Dictionary<string, long> Cursors { get; set; } = new Dictionary<string, long>();
    }

    public sealed class CachedZone
    {
        [JsonProperty("zone_id")] public string ZoneId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>0 until <c>zone.json</c> is stored: a zone with tiles but version 0 is incomplete.</summary>
        [JsonProperty("zone_version")] public int ZoneVersion { get; set; }

        /// <summary>By <c>"z/x/y"</c>.</summary>
        [JsonProperty("tiles")] public Dictionary<string, CachedFile> Tiles { get; set; } = new Dictionary<string, CachedFile>();

        /// <summary>By file name.</summary>
        [JsonProperty("pov")] public Dictionary<string, CachedFile> Pov { get; set; } = new Dictionary<string, CachedFile>();

        public static string TileKey(int z, int x, int y) => $"{z}/{x}/{y}";
    }

    public sealed class CachedFile
    {
        [JsonProperty("version")] public int Version { get; set; }

        /// <summary>For a tile, the sha256 of its <c>tile.json</c> (as listed in <c>zone.json</c>).</summary>
        [JsonProperty("sha256")] public string Sha256 { get; set; }
    }

    /// <summary>Where the catalog lives. JSON now (plan C of E8); SQLite can replace it behind this interface.</summary>
    public interface ICatalogStore
    {
        CacheCatalog Load();

        void Save(CacheCatalog catalog);
    }

    /// <summary>The catalog as one JSON file, written to a temporary file and then swapped in.</summary>
    public sealed class JsonCatalogStore : ICatalogStore
    {
        private readonly string _path;

        public JsonCatalogStore(string path)
        {
            _path = path;
        }

        public CacheCatalog Load()
        {
            if (!File.Exists(_path))
            {
                return new CacheCatalog();
            }

            var catalog = JsonConvert.DeserializeObject<CacheCatalog>(File.ReadAllText(_path));
            return catalog ?? new CacheCatalog();
        }

        public void Save(CacheCatalog catalog)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(catalog, Formatting.Indented));
            if (!File.Exists(_path))
            {
                File.Move(temporary, _path);
                return;
            }

            try
            {
                File.Replace(temporary, _path, null);
            }
            catch (System.Exception exception) when (exception is IOException || exception is System.PlatformNotSupportedException)
            {
                // Some Android file systems do not support an atomic replace: fall back to delete + move.
                UnityEngine.Debug.LogWarning($"[Terrain] catalog atomic replace unavailable error={exception.Message}; using delete+move");
                File.Delete(_path);
                File.Move(temporary, _path);
            }
        }
    }
}
