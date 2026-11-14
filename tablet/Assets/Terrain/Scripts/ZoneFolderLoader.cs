using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Loads a zone from a folder laid out like the <c>data/</c> of a <c>zone_full</c> bundle:
    /// <c>zone.json</c>, <c>conditions.json</c> (optional), <c>tiles/{z}/{x}/{y}/</c> and <c>pov/</c>.
    /// Works with plain paths and with StreamingAssets on Android (inside the APK).
    /// The tile path pattern is an assumption until C1b fixes it.
    /// </summary>
    public static class ZoneFolderLoader
    {
        public static async Task<LoadedZone> LoadAsync(string folder, int level = 0)
        {
            var zone = JsonConvert.DeserializeObject<ZoneInfo>(await ReadTextAsync(Combine(folder, "zone.json")))
                       ?? throw new InvalidDataException($"zone.json is empty in {folder}");

            ConditionsInfo conditions = null;
            var conditionsText = await TryReadTextAsync(Combine(folder, "conditions.json"));
            if (conditionsText != null)
            {
                conditions = JsonConvert.DeserializeObject<ConditionsInfo>(conditionsText);
            }

            var tiles = new List<TileData>();
            foreach (var reference in zone.Tiles)
            {
                if (reference.Z != level)
                {
                    continue;
                }

                var tileFolder = Combine(folder, "tiles", reference.Z.ToString(), reference.X.ToString(), reference.Y.ToString());
                try
                {
                    tiles.Add(await LoadTileAsync(tileFolder));
                }
                catch (FileNotFoundException missing)
                {
                    // A tile not downloaded yet: show the rest of the zone.
                    Debug.LogWarning($"[Terrain] tile skipped zone_id={zone.ZoneId} z={reference.Z} x={reference.X} y={reference.Y} missing={missing.FileName}");
                }
            }

            Debug.Log($"[Terrain] zone loaded zone_id={zone.ZoneId} tiles={tiles.Count} z={level} folder={folder}");
            return new LoadedZone(zone, tiles, conditions, Combine(folder, "pov"));
        }

        public static async Task<TileData> LoadTileAsync(string tileFolder)
        {
            var info = JsonConvert.DeserializeObject<TileInfo>(await ReadTextAsync(Combine(tileFolder, "tile.json")))
                       ?? throw new InvalidDataException($"tile.json is empty in {tileFolder}");

            var height = ToUInt16(await ReadBytesAsync(Combine(tileFolder, info.Height.File)));
            var hazard = await ReadBytesAsync(Combine(tileFolder, "hazard.r8"));
            var layers = new Dictionary<string, byte[]>();
            foreach (var layer in info.Layers)
            {
                layers[layer.Id] = await ReadBytesAsync(Combine(tileFolder, layer.File));
            }

            return new TileData(info, height, hazard, layers);
        }

        private static ushort[] ToUInt16(byte[] littleEndian)
        {
            if (littleEndian.Length % 2 != 0)
            {
                throw new InvalidDataException($"uint16 raster has an odd length {littleEndian.Length}");
            }

            var samples = new ushort[littleEndian.Length / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)(littleEndian[2 * i] | (littleEndian[2 * i + 1] << 8));
            }

            return samples;
        }

        private static string Combine(params string[] parts) => LocalFiles.Combine(parts);

        private static Task<byte[]> ReadBytesAsync(string path) => LocalFiles.ReadBytesAsync(path);

        private static async Task<string> ReadTextAsync(string path)
        {
            return Encoding.UTF8.GetString(await ReadBytesAsync(path));
        }

        private static async Task<string> TryReadTextAsync(string path)
        {
            var bytes = await LocalFiles.TryReadBytesAsync(path);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }
    }
}
