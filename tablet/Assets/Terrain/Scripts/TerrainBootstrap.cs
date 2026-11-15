using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Starts the app side of Terrain: frame pacing, the local cache, the zones that ship inside the
    /// APK, and the first zone on screen. Answers <see cref="MapEvents.ZoneRequested"/> from the cache.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainBootstrap : MonoBehaviour
    {
        public enum ZoneSource
        {
            /// <summary>The local cache, after importing the preloaded zones (the app's normal path).</summary>
            Cache,

            /// <summary>A procedural crater field of several tiles, generated in code.</summary>
            Demo,

            /// <summary>One tile like the C1 synthetic fixture: ramp, asymmetric hazard, nodata corner.</summary>
            FixtureLike,

            /// <summary>A zone folder inside StreamingAssets with zone.json at its root (no bundle.json).</summary>
            StreamingAssetsFolder,
        }

        [SerializeField] private TerrainMap2D map;
        [SerializeField] private ZoneSource source = ZoneSource.Cache;

        [Tooltip("Unpacked zone_full bundles under StreamingAssets/zones/ to import on launch. Missing ones are skipped.")]
        [SerializeField] private string[] preloadedZones = { "gale", "demo" };

        [Tooltip("Folder under StreamingAssets, used when the source is StreamingAssetsFolder.")]
        [SerializeField] private string streamingAssetsFolder = "zones/gale/data";

        [Tooltip("Explicit frame rate with vSyncCount = 0 (D1). 2D is light; drop to 30 if the tablet heats up.")]
        [SerializeField] private int targetFrameRate = 60;

        private LocalCache _cache;
        private bool _loading;
        private bool _costParamsLoaded;

        public ZoneSource Source => source;

        /// <summary>The local cache, or null before <see cref="ZoneSource.Cache"/> was first used.</summary>
        public LocalCache Cache => _cache;

        /// <summary>The <c>cost_params.json</c> in use (C4), or null if it could not be read.</summary>
        public MartianMap.Routing.CostParams CostParams { get; private set; }

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (map == null)
            {
                map = GetComponentInChildren<TerrainMap2D>();
            }
        }

        private void OnEnable()
        {
            MapEvents.ZoneRequested += OnZoneRequested;
        }

        private void OnDisable()
        {
            MapEvents.ZoneRequested -= OnZoneRequested;
        }

        private async void Start()
        {
            if (map == null)
            {
                Debug.LogError("[Terrain] bootstrap has no TerrainMap2D");
                return;
            }

            try
            {
                await LoadAsync(source);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Terrain] zone load failed source={source} error={exception.Message}; showing demo zone");
                map.Show(SyntheticZone.CreateDemo());
            }
        }

        /// <summary>Loads and shows a zone from the given source.</summary>
        public async Task LoadAsync(ZoneSource from)
        {
            source = from;
            var started = Time.realtimeSinceStartup;
            await LoadCostParamsOnceAsync();
            LoadedZone zone;
            switch (from)
            {
                case ZoneSource.Cache:
                    zone = await LoadFromCacheAsync(null);
                    break;
                case ZoneSource.FixtureLike:
                    zone = SyntheticZone.CreateFixtureLike();
                    break;
                case ZoneSource.StreamingAssetsFolder:
                    zone = await ZoneFolderLoader.LoadAsync(LocalFiles.Combine(Application.streamingAssetsPath, streamingAssetsFolder));
                    break;
                default:
                    zone = SyntheticZone.CreateDemo();
                    break;
            }

            await ApplyHazardShapesAsync(zone);
            map.Show(zone);
            Debug.Log($"[Terrain] bootstrap source={from} zone_id={zone.Zone.ZoneId} seconds={Time.realtimeSinceStartup - started:F2}");
        }

        /// <summary>
        /// Opens the cache, imports the preloaded zones, emits ZonesChanged and loads
        /// <paramref name="zoneId"/>, or the last active zone, or the first one. With an empty cache
        /// it falls back to the generated demo zone.
        /// </summary>
        private async Task<LoadedZone> LoadFromCacheAsync(string zoneId)
        {
            if (_cache == null)
            {
                _cache = LocalCache.OpenDefault();
                foreach (var preloaded in preloadedZones)
                {
                    await ImportPreloadedAsync(preloaded);
                }
            }

            var zones = _cache.Zones;
            MapEvents.Emit(new ZonesChanged(zones));
            if (zones.Count == 0)
            {
                Debug.LogWarning("[Terrain] cache is empty and no zone ships in the APK; showing the generated demo zone");
                return SyntheticZone.CreateDemo();
            }

            var target = zoneId ?? _cache.ActiveZone;
            if (target == null || zones.All(z => z.ZoneId != target))
            {
                target = zones[0].ZoneId;
            }

            var zone = await _cache.LoadZoneAsync(target);
            _cache.SetActiveZone(target);
            return zone;
        }

        private async Task ImportPreloadedAsync(string zoneId)
        {
            try
            {
                var result = await PreloadedZones.ImportFromStreamingAssetsAsync(_cache, zoneId);
                Debug.Log($"[Terrain] preloaded zone zone_id={zoneId} result={result}");
            }
            catch (Exception exception)
            {
                // A bad bundle never blocks the app: the zone stays as it was in the cache.
                Debug.LogError($"[Terrain] preloaded zone rejected zone_id={zoneId} error={exception.Message}");
            }
        }

        private async void OnZoneRequested(ZoneRequested request)
        {
            if (_loading || _cache == null)
            {
                return;
            }

            _loading = true;
            try
            {
                var zone = await LoadFromCacheAsync(request.ZoneId);
                await ApplyHazardShapesAsync(zone);
                map.Show(zone);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Terrain] zone request failed zone_id={request.ZoneId} error={exception.Message}");
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// Reads <c>cost_params.json</c> from StreamingAssets/fixtures (C6: at run time it is a copy of
        /// the fixture) and hands its hazard thresholds to the map. Without it the map keeps 96/192.
        /// </summary>
        private async Task LoadCostParamsOnceAsync()
        {
            if (_costParamsLoaded)
            {
                return;
            }

            _costParamsLoaded = true;
            try
            {
                CostParams = await FixtureLoader.LoadCostParamsAsync();
                map.ApplyCostParams(CostParams);
                Debug.Log($"[Terrain] cost params loaded hazard_amber_min={CostParams.HazardAmberMin} hazard_red_min={CostParams.HazardRedMin}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Terrain] cost params unavailable error={exception.Message}; keeping default hazard thresholds");
            }
        }

        /// <summary>Merges P5's hazard shapes for this zone, if any; a bad file never blocks the zone.</summary>
        private static async Task ApplyHazardShapesAsync(LoadedZone zone)
        {
            try
            {
                var shapes = await HazardShapes.LoadForZoneAsync(zone.Zone);
                if (shapes.Count > 0)
                {
                    var changed = HazardShapes.Apply(zone.Tiles, shapes);
                    Debug.Log($"[Terrain] hazard shapes applied zone_id={zone.Zone.ZoneId} tiles_changed={changed.Count}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Terrain] hazard shapes rejected zone_id={zone.Zone.ZoneId} error={exception.Message}");
            }
        }
    }
}
