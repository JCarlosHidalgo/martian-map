using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The tablet's local cache (Tablet, "Caché local"): C1 tiles and POV images as files under
    /// <c>{root}/{zone_id}/</c>, laid out like a bundle's <c>data/</c>, plus a catalog. Every file is
    /// checked against its sha256 before it is stored. A tile is written to a temporary folder and
    /// swapped in, so a crash never leaves half a tile. Thread-safe; the store calls can run off the
    /// main thread.
    /// </summary>
    public sealed class LocalCache
    {
        private readonly object _gate = new object();
        private readonly ICatalogStore _store;
        private readonly CacheCatalog _catalog;

        public LocalCache(string root, ICatalogStore store)
        {
            Root = root;
            Directory.CreateDirectory(root);
            _store = store;
            _catalog = store.Load();
        }

        /// <summary>The default cache: <c>persistentDataPath/zones/</c> with <c>persistentDataPath/catalog.json</c>.</summary>
        public static LocalCache OpenDefault()
        {
            var root = Path.Combine(Application.persistentDataPath, "zones");
            return new LocalCache(root, new JsonCatalogStore(Path.Combine(Application.persistentDataPath, "catalog.json")));
        }

        public string Root { get; }

        public string ActiveZone
        {
            get
            {
                lock (_gate)
                {
                    return _catalog.ActiveZone;
                }
            }
        }

        /// <summary>The complete zones (their zone.json is stored), sorted by id.</summary>
        public IReadOnlyList<ZoneSummary> Zones
        {
            get
            {
                lock (_gate)
                {
                    return _catalog.Zones.Values
                        .Where(z => z.ZoneVersion > 0)
                        .OrderBy(z => z.ZoneId, StringComparer.Ordinal)
                        .Select(z => new ZoneSummary(z.ZoneId, z.Name, z.ZoneVersion))
                        .ToList();
                }
            }
        }

        public string ZoneFolder(string zoneId) => Path.Combine(Root, zoneId);

        /// <summary>The cached <c>zone_version</c> of a complete zone, or 0.</summary>
        public int ZoneVersion(string zoneId)
        {
            lock (_gate)
            {
                return _catalog.Zones.TryGetValue(zoneId, out var zone) ? zone.ZoneVersion : 0;
            }
        }

        /// <summary>True unless the tile is cached with this exact version and tile.json sha256.</summary>
        public bool NeedsTile(string zoneId, ZoneTileRef tile)
        {
            lock (_gate)
            {
                return !_catalog.Zones.TryGetValue(zoneId, out var zone) ||
                       !zone.Tiles.TryGetValue(CachedZone.TileKey(tile.Z, tile.X, tile.Y), out var cached) ||
                       cached.Version != tile.Version ||
                       !string.Equals(cached.Sha256, tile.Sha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>True unless the POV image is cached with this version and sha256.</summary>
        public bool NeedsPov(string zoneId, PovPin pin)
        {
            lock (_gate)
            {
                return !_catalog.Zones.TryGetValue(zoneId, out var zone) ||
                       !zone.Pov.TryGetValue(pin.File, out var cached) ||
                       cached.Version != pin.Version ||
                       !string.Equals(cached.Sha256, pin.Sha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Stores one tile: <paramref name="tileJson"/> must match <c>tile.sha256</c> from zone.json, and
        /// every file named in tile.json's <c>sha256</c> must be in <paramref name="files"/> and match.
        /// </summary>
        /// <exception cref="CacheIntegrityException">A hash does not match or a file is missing.</exception>
        public void StoreTile(string zoneId, ZoneTileRef tile, byte[] tileJson, IReadOnlyDictionary<string, byte[]> files)
        {
            var label = $"{zoneId}/{tile.Z}/{tile.X}/{tile.Y}";
            if (!Sha256.Matches(tileJson, tile.Sha256))
            {
                throw new CacheIntegrityException($"tile.json of {label} does not match its sha256");
            }

            var info = JsonConvert.DeserializeObject<TileInfo>(System.Text.Encoding.UTF8.GetString(tileJson))
                       ?? throw new CacheIntegrityException($"tile.json of {label} is empty");

            foreach (var expected in info.Sha256)
            {
                if (!files.TryGetValue(expected.Key, out var bytes))
                {
                    throw new CacheIntegrityException($"tile {label} is missing {expected.Key}");
                }

                if (!Sha256.Matches(bytes, expected.Value))
                {
                    throw new CacheIntegrityException($"{expected.Key} of {label} does not match its sha256");
                }
            }

            var folder = Path.Combine(ZoneFolder(zoneId), "tiles", tile.Z.ToString(), tile.X.ToString(), tile.Y.ToString());
            var staging = folder + ".tmp";
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, "tile.json"), tileJson);
            foreach (var file in files)
            {
                File.WriteAllBytes(Path.Combine(staging, SafeName(file.Key)), file.Value);
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.Move(staging, folder);

            lock (_gate)
            {
                var zone = ZoneEntry(zoneId);
                zone.Tiles[CachedZone.TileKey(tile.Z, tile.X, tile.Y)] = new CachedFile
                {
                    Version = tile.Version,
                    Sha256 = tile.Sha256 ?? string.Empty,
                };
                _store.Save(_catalog);
            }
        }

        /// <summary>Stores one POV image after checking its sha256.</summary>
        /// <exception cref="CacheIntegrityException">The hash does not match.</exception>
        public void StorePov(string zoneId, PovPin pin, byte[] image)
        {
            if (!Sha256.Matches(image, pin.Sha256))
            {
                throw new CacheIntegrityException($"POV {zoneId}/{pin.File} does not match its sha256");
            }

            var folder = Path.Combine(ZoneFolder(zoneId), "pov");
            Directory.CreateDirectory(folder);
            WriteAtomically(Path.Combine(folder, SafeName(pin.File)), image);

            lock (_gate)
            {
                ZoneEntry(zoneId).Pov[pin.File] = new CachedFile { Version = pin.Version, Sha256 = pin.Sha256 ?? string.Empty };
                _store.Save(_catalog);
            }
        }

        /// <summary>
        /// Stores zone.json (and conditions.json if given) and marks the zone complete at
        /// <paramref name="zoneVersion"/>. Call it after the tiles, so a zone only appears once its
        /// tiles are there. Tiles no longer listed in zone.json are deleted.
        /// </summary>
        public void StoreZone(string zoneId, int zoneVersion, byte[] zoneJson, byte[] conditionsJson)
        {
            var zone = JsonConvert.DeserializeObject<ZoneInfo>(System.Text.Encoding.UTF8.GetString(zoneJson))
                       ?? throw new CacheIntegrityException($"zone.json of {zoneId} is empty");
            if (zone.ZoneId != zoneId)
            {
                throw new CacheIntegrityException($"zone.json says zone_id={zone.ZoneId}, expected {zoneId}");
            }

            var folder = ZoneFolder(zoneId);
            Directory.CreateDirectory(folder);
            WriteAtomically(Path.Combine(folder, "zone.json"), zoneJson);
            if (conditionsJson != null)
            {
                WriteAtomically(Path.Combine(folder, "conditions.json"), conditionsJson);
            }

            lock (_gate)
            {
                var entry = ZoneEntry(zoneId);
                var listed = new HashSet<string>(zone.Tiles.Select(t => CachedZone.TileKey(t.Z, t.X, t.Y)));
                foreach (var stale in entry.Tiles.Keys.Where(k => !listed.Contains(k)).ToList())
                {
                    entry.Tiles.Remove(stale);
                    var staleFolder = Path.Combine(folder, "tiles", stale.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(staleFolder))
                    {
                        Directory.Delete(staleFolder, true);
                    }
                }

                entry.Name = zone.Name;
                entry.ZoneVersion = zoneVersion;
                _store.Save(_catalog);
            }
        }

        /// <summary>Loads a cached zone for drawing; tiles not yet cached are left out.</summary>
        public Task<LoadedZone> LoadZoneAsync(string zoneId, int level = 0)
        {
            if (ZoneVersion(zoneId) == 0)
            {
                throw new InvalidOperationException($"zone {zoneId} is not in the cache");
            }

            return ZoneFolderLoader.LoadAsync(ZoneFolder(zoneId), level);
        }

        public void SetActiveZone(string zoneId)
        {
            lock (_gate)
            {
                _catalog.ActiveZone = zoneId;
                _store.Save(_catalog);
            }
        }

        /// <summary>A <c>since</c> cursor; 0 when it was never set.</summary>
        public long GetCursor(string stream)
        {
            lock (_gate)
            {
                return _catalog.Cursors.TryGetValue(stream, out var value) ? value : 0L;
            }
        }

        public void SetCursor(string stream, long value)
        {
            lock (_gate)
            {
                _catalog.Cursors[stream] = value;
                _store.Save(_catalog);
            }
        }

        /// <summary>Deletes a zone's files and its catalog entry.</summary>
        public void RemoveZone(string zoneId)
        {
            lock (_gate)
            {
                _catalog.Zones.Remove(zoneId);
                _catalog.Cursors.Remove($"changes/{zoneId}");
                if (_catalog.ActiveZone == zoneId)
                {
                    _catalog.ActiveZone = null;
                }

                _store.Save(_catalog);
            }

            var folder = ZoneFolder(zoneId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private CachedZone ZoneEntry(string zoneId)
        {
            if (!_catalog.Zones.TryGetValue(zoneId, out var zone))
            {
                zone = new CachedZone { ZoneId = zoneId };
                _catalog.Zones[zoneId] = zone;
            }

            return zone;
        }

        private static void WriteAtomically(string path, byte[] data)
        {
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, data);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        /// <summary>A file name from the network or a bundle must not escape its folder.</summary>
        private static string SafeName(string name)
        {
            var file = Path.GetFileName(name);
            if (string.IsNullOrEmpty(file) || file != name || file == "." || file == "..")
            {
                throw new CacheIntegrityException($"unsafe file name '{name}'");
            }

            return file;
        }
    }
}
