using System;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Starts the app side of Terrain: frame pacing, then the first zone. Until the local cache and
    /// the habitat client exist, the zone comes from code or from a folder in StreamingAssets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainBootstrap : MonoBehaviour
    {
        public enum ZoneSource
        {
            /// <summary>A procedural crater field of several tiles (default for PC tests).</summary>
            Demo,

            /// <summary>One tile like the C1 synthetic fixture: ramp, asymmetric hazard, nodata corner.</summary>
            FixtureLike,

            /// <summary>A zone folder inside StreamingAssets (e.g. <c>zones/gale</c> once P1 delivers it).</summary>
            StreamingAssetsFolder,
        }

        [SerializeField] private TerrainMap2D map;
        [SerializeField] private ZoneSource source = ZoneSource.Demo;

        [Tooltip("Folder under StreamingAssets, used when the source is StreamingAssetsFolder.")]
        [SerializeField] private string streamingAssetsFolder = "zones/gale";

        [Tooltip("Explicit frame rate with vSyncCount = 0 (D1). 2D is light; drop to 30 if the tablet heats up.")]
        [SerializeField] private int targetFrameRate = 60;

        public ZoneSource Source => source;

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
        public async System.Threading.Tasks.Task LoadAsync(ZoneSource from)
        {
            source = from;
            var started = Time.realtimeSinceStartup;
            LoadedZone zone;
            switch (from)
            {
                case ZoneSource.FixtureLike:
                    zone = SyntheticZone.CreateFixtureLike();
                    break;
                case ZoneSource.StreamingAssetsFolder:
                    zone = await ZoneFolderLoader.LoadAsync($"{Application.streamingAssetsPath}/{streamingAssetsFolder}");
                    break;
                default:
                    zone = SyntheticZone.CreateDemo();
                    break;
            }

            await ApplyHazardShapesAsync(zone);
            map.Show(zone);
            Debug.Log($"[Terrain] bootstrap source={from} seconds={Time.realtimeSinceStartup - started:F2}");
        }

        /// <summary>Merges P5's hazard shapes for this zone, if any; a bad file never blocks the zone.</summary>
        private static async System.Threading.Tasks.Task ApplyHazardShapesAsync(LoadedZone zone)
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
