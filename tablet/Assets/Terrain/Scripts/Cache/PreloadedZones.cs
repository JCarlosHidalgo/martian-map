using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Imports a <c>zone_full</c> bundle that ships unpacked inside the APK (Tablet, "Gale precargada"):
    /// <c>StreamingAssets/zones/{zone_id}/bundle.json</c> + <c>data/</c>. Runs on first launch and
    /// whenever the APK carries a newer <c>zone_version</c> than the cache. StreamingAssets cannot be
    /// listed on Android, so the files come from <c>zone.json</c> and each <c>tile.json</c>.
    /// </summary>
    public static class PreloadedZones
    {
        public const string FolderName = "zones";

        public enum Result
        {
            Imported,
            AlreadyCached,
            NotShipped,
        }

        /// <summary>Imports <c>StreamingAssets/zones/{zoneId}/</c> into the cache if it is newer.</summary>
        public static Task<Result> ImportFromStreamingAssetsAsync(LocalCache cache, string zoneId)
        {
            return ImportAsync(cache, LocalFiles.Combine(Application.streamingAssetsPath, FolderName, zoneId));
        }

        /// <summary>Imports an unpacked bundle folder (plain path or StreamingAssets URL) into the cache.</summary>
        /// <exception cref="CacheIntegrityException">A file does not match its sha256.</exception>
        public static async Task<Result> ImportAsync(LocalCache cache, string bundleFolder)
        {
            var bundleBytes = await LocalFiles.TryReadBytesAsync(LocalFiles.Combine(bundleFolder, "bundle.json"));
            if (bundleBytes == null)
            {
                return Result.NotShipped;
            }

            var bundle = JsonConvert.DeserializeObject<BundleInfo>(Encoding.UTF8.GetString(bundleBytes))
                         ?? throw new CacheIntegrityException($"bundle.json is empty in {bundleFolder}");
            if (bundle.Kind != "zone_full")
            {
                throw new CacheIntegrityException($"preloaded bundle {bundle.ZoneId} is kind={bundle.Kind}, expected zone_full");
            }

            if (cache.ZoneVersion(bundle.ZoneId) >= bundle.ZoneVersion)
            {
                return Result.AlreadyCached;
            }

            var started = Time.realtimeSinceStartup;
            var expected = new Dictionary<string, string>();
            foreach (var file in bundle.Files)
            {
                expected[file.Path] = file.Sha256;
            }

            var data = LocalFiles.Combine(bundleFolder, "data");
            var zoneJson = await ReadChecked(data, "zone.json", expected);
            var conditionsJson = await LocalFiles.TryReadBytesAsync(LocalFiles.Combine(data, "conditions.json"));
            if (conditionsJson != null && !Sha256.Matches(conditionsJson, Expected(expected, "data/conditions.json")))
            {
                throw new CacheIntegrityException($"data/conditions.json of {bundle.ZoneId} does not match bundle.json");
            }

            var zone = JsonConvert.DeserializeObject<ZoneInfo>(Encoding.UTF8.GetString(zoneJson))
                       ?? throw new CacheIntegrityException($"zone.json of {bundle.ZoneId} is empty");

            var imported = 0;
            foreach (var tile in zone.Tiles)
            {
                if (!cache.NeedsTile(zone.ZoneId, tile))
                {
                    continue;
                }

                var tileFolder = $"tiles/{tile.Z}/{tile.X}/{tile.Y}";
                var tileJson = await ReadChecked(data, $"{tileFolder}/tile.json", expected);
                var info = JsonConvert.DeserializeObject<TileInfo>(Encoding.UTF8.GetString(tileJson))
                           ?? throw new CacheIntegrityException($"{tileFolder}/tile.json of {zone.ZoneId} is empty");

                var files = new Dictionary<string, byte[]>();
                foreach (var name in TileFileNames(info))
                {
                    files[name] = await ReadChecked(data, $"{tileFolder}/{name}", expected);
                }

                await Task.Run(() => cache.StoreTile(zone.ZoneId, tile, tileJson, files));
                imported++;
            }

            foreach (var pin in zone.Pov)
            {
                if (cache.NeedsPov(zone.ZoneId, pin))
                {
                    var image = await ReadChecked(data, $"pov/{pin.File}", expected);
                    await Task.Run(() => cache.StorePov(zone.ZoneId, pin, image));
                }
            }

            await Task.Run(() => cache.StoreZone(zone.ZoneId, bundle.ZoneVersion, zoneJson, conditionsJson));
            Debug.Log($"[Terrain] preloaded zone imported zone_id={zone.ZoneId} zone_version={bundle.ZoneVersion} " +
                      $"tiles={imported} seconds={Time.realtimeSinceStartup - started:F1}");
            return Result.Imported;
        }

        /// <summary>The files of a tile: those in its sha256 map, or the C1 names when the map is empty.</summary>
        private static IEnumerable<string> TileFileNames(TileInfo info)
        {
            if (info.Sha256.Count > 0)
            {
                foreach (var name in info.Sha256.Keys)
                {
                    yield return name;
                }

                yield break;
            }

            yield return info.Height.File;
            yield return "hazard.r8";
            foreach (var layer in info.Layers)
            {
                yield return layer.File;
            }
        }

        private static async Task<byte[]> ReadChecked(string data, string relative, IReadOnlyDictionary<string, string> expected)
        {
            var bytes = await LocalFiles.ReadBytesAsync(LocalFiles.Combine(data, relative));
            var path = $"data/{relative}";
            if (!Sha256.Matches(bytes, Expected(expected, path)))
            {
                throw new CacheIntegrityException($"{path} does not match bundle.json");
            }

            return bytes;
        }

        private static string Expected(IReadOnlyDictionary<string, string> expected, string path)
        {
            return expected.TryGetValue(path, out var sha) ? sha : string.Empty;
        }
    }
}
