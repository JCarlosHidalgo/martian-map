using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Terrain's client of the habitat (C3). Only Terrain talks HTTP (Tablet, "Caché local").
    /// Every cycle (2 s): send pending edits (POST /edits, ≤ 200 per batch) and apply their acks;
    /// GET /zones?since= and download new or updated zones into the cache (tiles of z = 0 and POV
    /// images by <c>?v=</c>, each file checked against its sha256, 2 retries); GET
    /// /zones/{id}/changes?since= for the zone on screen until <c>has_more</c> is false (winners,
    /// new tile versions, conditions). Failures back off 2, 4, 8, 15 s and return to 2 s on the
    /// first success (Convenciones, "Ritmo del cliente"). With an empty URL it does nothing: the
    /// app works from its cache. A new URL (ConfigChanged) restarts the cursors' use at once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HabitatClient : MonoBehaviour
    {
        private static readonly float[] Backoff = { 2f, 4f, 8f, 15f };
        private const float PollSeconds = 2f;
        private const int ShaRetries = 2;
        private const string ZonesCursor = "zones";

        [SerializeField] private TerrainMap2D map;
        [SerializeField] private TerrainBootstrap bootstrap;
        [SerializeField] private EditOrchestrator edits;

        private readonly HashSet<string> _badZones = new HashSet<string>();
        private int _failures;
        private bool _wake;
        private bool _running;

        /// <summary>True after the last cycle reached the habitat.</summary>
        public bool Online { get; private set; }

        private void Awake()
        {
            if (map == null)
            {
                map = GetComponent<TerrainMap2D>();
            }

            if (bootstrap == null)
            {
                bootstrap = GetComponent<TerrainBootstrap>();
            }

            if (edits == null)
            {
                edits = GetComponent<EditOrchestrator>();
            }
        }

        private void OnEnable()
        {
            MapEvents.ConfigChanged += OnConfigChanged;
            if (edits != null)
            {
                edits.PendingChanged += OnPendingChanged;
            }

            if (!_running)
            {
                RunAsync();
            }
        }

        private void OnDisable()
        {
            MapEvents.ConfigChanged -= OnConfigChanged;
            if (edits != null)
            {
                edits.PendingChanged -= OnPendingChanged;
            }
        }

        private void OnConfigChanged(ConfigChanged e)
        {
            _failures = 0;
            _wake = true;
        }

        private void OnPendingChanged() => _wake = true;

        private async void RunAsync()
        {
            _running = true;
            try
            {
                while (this != null && isActiveAndEnabled)
                {
                    var habitat = AppConfig.HabitatUrl;
                    var files = string.IsNullOrEmpty(AppConfig.FilesUrl) ? habitat : AppConfig.FilesUrl;
                    if (string.IsNullOrEmpty(habitat))
                    {
                        Online = false;
                        await WaitAsync(PollSeconds);
                        continue;
                    }

                    try
                    {
                        await CycleAsync(habitat.TrimEnd('/'), files.TrimEnd('/'));
                        if (!Online || _failures > 0)
                        {
                            Debug.Log($"[Terrain] habitat online url={habitat}");
                        }

                        Online = true;
                        _failures = 0;
                    }
                    catch (Exception exception) when (exception is HabitatHttpException || exception is CacheIntegrityException ||
                                                      exception is JsonException || exception is System.IO.InvalidDataException ||
                                                      exception is InvalidCastException || exception is ArgumentException)
                    {
                        Online = false;
                        var delay = Backoff[Mathf.Min(_failures, Backoff.Length - 1)];
                        _failures++;
                        Debug.LogWarning($"[Terrain] habitat cycle failed retry_s={delay} error={exception.Message}");
                        await WaitAsync(delay);
                        continue;
                    }

                    await WaitAsync(PollSeconds);
                }
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>Waits, but wakes early when an edit is queued or the config changes.</summary>
        private async Task WaitAsync(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            _wake = false;
            while (Time.realtimeSinceStartup < until && !_wake && this != null && isActiveAndEnabled)
            {
                await Task.Yield();
            }
        }

        private async Task CycleAsync(string habitat, string files)
        {
            var cache = bootstrap.EnsureCache();
            await SendPendingAsync(habitat);

            // New or updated zones.
            var zones = HabitatApi.ParseZones(await HabitatHttp.GetTextAsync($"{habitat}/zones?since={cache.GetCursor(ZonesCursor)}"));
            var updated = new List<string>();
            foreach (var z in zones.Zones)
            {
                if (cache.ZoneVersion(z.ZoneId) < z.ZoneVersion && await SyncZoneAsync(habitat, files, z.ZoneId))
                {
                    updated.Add(z.ZoneId);
                }
            }

            cache.SetCursor(ZonesCursor, zones.NextSince);
            if (updated.Count > 0)
            {
                MapEvents.Emit(new ZonesChanged(cache.Zones));
                await ShowUpdatesAsync(updated);
            }

            // Changes of the zone on screen, if it is a habitat zone.
            var current = map.Current?.Zone.ZoneId;
            if (current != null && cache.ZoneVersion(current) > 0)
            {
                await PullChangesAsync(habitat, files, current);
            }
        }

        private async Task ShowUpdatesAsync(IReadOnlyList<string> updated)
        {
            var current = map.Current?.Zone.ZoneId;
            if (current != null && updated.Contains(current))
            {
                Debug.Log($"[Terrain] zone on screen updated zone_id={current}; reloading");
                await bootstrap.ShowCachedZoneAsync(current);
            }
            else if (!bootstrap.ShowsCachedZone)
            {
                // The app shows a fallback (contract tile or demo): a real zone replaces it.
                Debug.Log($"[Terrain] habitat zone arrived zone_id={updated[0]}; showing it");
                await bootstrap.ShowCachedZoneAsync(updated[0]);
            }
        }

        private async Task SendPendingAsync(string habitat)
        {
            if (edits == null)
            {
                return;
            }

            var pending = edits.Pending;
            for (var start = 0; start < pending.Count; start += HabitatApi.MaxEditsPerBatch)
            {
                var batch = pending.Skip(start).Take(HabitatApi.MaxEditsPerBatch).ToList();
                var body = HabitatApi.EditsBody(batch);
                string response;
                try
                {
                    response = await HabitatHttp.PostJsonAsync($"{habitat}/edits", body);
                }
                catch (HabitatHttpException tooLarge) when (tooLarge.Status == 413 && batch.Count > 1)
                {
                    // Batch over 1 MiB: send one by one, in Lamport order (C3).
                    foreach (var single in batch.OrderBy(e => e.Lamport))
                    {
                        edits.ApplyAcks(HabitatApi.ParseAcks(
                            await HabitatHttp.PostJsonAsync($"{habitat}/edits", HabitatApi.EditsBody(new[] { single }))));
                    }

                    continue;
                }

                var acks = HabitatApi.ParseAcks(response);
                edits.ApplyAcks(acks);
                Debug.Log($"[Terrain] edits sent count={batch.Count} acks={acks.Count}");
            }
        }

        /// <summary>Downloads a zone into the cache; false if it failed (logged; retried next cycle).</summary>
        private async Task<bool> SyncZoneAsync(string habitat, string files, string zoneId)
        {
            var cache = bootstrap.EnsureCache();
            var response = HabitatApi.ParseZone(await HabitatHttp.GetTextAsync($"{habitat}/zones/{zoneId}"));
            var zone = response.Zone;
            if (zone == null || zone.ZoneId != zoneId || zone.ExtentEnuM == null || zone.ExtentEnuM.Length != 4)
            {
                // Logged once per zone: the next cycles retry quietly (e.g. Prism without a zone example).
                if (_badZones.Add(zoneId))
                {
                    Debug.LogWarning($"[Terrain] zone response unusable zone_id={zoneId}: missing zone_id or extent_enu_m; retrying quietly");
                }

                return false;
            }

            _badZones.Remove(zoneId);

            var started = Time.realtimeSinceStartup;
            var downloaded = 0;
            foreach (var tile in zone.Tiles.Where(t => t.Z == 0))
            {
                if (!cache.NeedsTile(zoneId, tile))
                {
                    continue;
                }

                await DownloadTileAsync(files, zoneId, tile);
                downloaded++;
            }

            foreach (var pin in zone.Pov)
            {
                if (cache.NeedsPov(zoneId, pin))
                {
                    var image = await GetCheckedAsync($"{files}/zones/{zoneId}/pov/{pin.File}?v={pin.Version}", pin.Sha256);
                    await Task.Run(() => cache.StorePov(zoneId, pin, image));
                }
            }

            var zoneBytes = Encoding.UTF8.GetBytes(response.ZoneJson.ToString(Formatting.None));
            var conditionsBytes = response.ConditionsJson != null
                ? Encoding.UTF8.GetBytes(response.ConditionsJson.ToString(Formatting.None))
                : null;
            await Task.Run(() => cache.StoreZone(zoneId, response.ZoneVersion, zoneBytes, conditionsBytes));
            Debug.Log($"[Terrain] zone synced zone_id={zoneId} zone_version={response.ZoneVersion} tiles_downloaded={downloaded} " +
                      $"seconds={Time.realtimeSinceStartup - started:F1}");
            return true;
        }

        private async Task DownloadTileAsync(string files, string zoneId, ZoneTileRef tile)
        {
            var cache = bootstrap.EnsureCache();
            var basePath = $"{files}/tiles/{zoneId}/{tile.Z}/{tile.X}/{tile.Y}";
            var tileJson = await GetCheckedAsync($"{basePath}/tile.json?v={tile.Version}", tile.Sha256);
            var info = JsonConvert.DeserializeObject<TileInfo>(Encoding.UTF8.GetString(tileJson))
                       ?? throw new System.IO.InvalidDataException($"empty tile.json {basePath}");

            var names = info.Sha256.Count > 0
                ? info.Sha256.Keys.ToList()
                : new List<string> { info.Height.File, "hazard.r8" }.Concat(info.Layers.Select(l => l.File)).ToList();
            var tileFiles = new Dictionary<string, byte[]>();
            foreach (var name in names)
            {
                info.Sha256.TryGetValue(name, out var sha);
                tileFiles[name] = await GetCheckedAsync($"{basePath}/{name}?v={tile.Version}", sha);
            }

            await Task.Run(() => cache.StoreTile(zoneId, tile, tileJson, tileFiles));
        }

        /// <summary>GET a file and check its sha256; a mismatch is retried twice, then fails the cycle.</summary>
        private static async Task<byte[]> GetCheckedAsync(string url, string sha256)
        {
            for (var attempt = 0; ; attempt++)
            {
                var bytes = await HabitatHttp.GetBytesAsync(url);
                if (Sha256.Matches(bytes, sha256))
                {
                    return bytes;
                }

                if (attempt >= ShaRetries)
                {
                    throw new CacheIntegrityException($"{url} does not match its sha256 after {ShaRetries + 1} tries");
                }

                Debug.LogWarning($"[Terrain] sha256 mismatch url={url} attempt={attempt + 1}; retrying");
            }
        }

        private async Task PullChangesAsync(string habitat, string files, string zoneId)
        {
            var cache = bootstrap.EnsureCache();
            var cursor = $"changes/{zoneId}";
            var newTiles = false;
            JObject conditions = null;
            ChangesResponse page;
            do
            {
                page = HabitatApi.ParseChanges(
                    await HabitatHttp.GetTextAsync($"{habitat}/zones/{zoneId}/changes?since={cache.GetCursor(cursor)}&limit=500"),
                    zoneId);
                edits?.ReceiveWinners(zoneId, page.Edits);
                newTiles |= page.Tiles.Count > 0;
                conditions = page.Conditions ?? conditions;
                cache.SetCursor(cursor, page.NextSince);
            }
            while (page.HasMore);

            if (conditions != null)
            {
                var bytes = Encoding.UTF8.GetBytes(conditions.ToString(Formatting.None));
                cache.StoreConditions(zoneId, bytes);
                var parsed = conditions.ToObject<ConditionsInfo>();
                if (parsed != null)
                {
                    MapEvents.Emit(new ConditionsChanged(zoneId, parsed));
                }
            }

            // New tile versions come with a bundle: GET /zones/{id} again (C3) and reload.
            if (newTiles && await SyncZoneAsync(habitat, files, zoneId))
            {
                MapEvents.Emit(new ZonesChanged(cache.Zones));
                await bootstrap.ShowCachedZoneAsync(zoneId);
            }
        }
    }
}
