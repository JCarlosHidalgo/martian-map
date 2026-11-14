using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Writes a zone as an unpacked <c>zone_full</c> bundle (C1b): <c>bundle.json</c> + <c>data/</c>
    /// with every sha256 filled in. A test and demo tool: the real bundles come from Earth (P1).
    /// </summary>
    public static class ZoneBundleWriter
    {
        public static void Write(LoadedZone zone, string bundleFolder, int zoneVersion)
        {
            if (Directory.Exists(bundleFolder))
            {
                Directory.Delete(bundleFolder, true);
            }

            var data = Path.Combine(bundleFolder, "data");
            var files = new List<BundleFile>();
            var info = zone.Zone;
            info.Tiles.Clear();

            foreach (var tile in zone.Tiles)
            {
                var t = tile.Info;
                t.Version = zoneVersion;
                var tileFiles = new Dictionary<string, byte[]>
                {
                    [t.Height.File] = ToBytes(tile.Height),
                    ["hazard.r8"] = tile.BaseHazard,
                };
                foreach (var layer in t.Layers)
                {
                    tileFiles[layer.File] = tile.Layers[layer.Id];
                }

                t.Sha256 = new Dictionary<string, string>();
                foreach (var file in tileFiles)
                {
                    t.Sha256[file.Key] = Sha256.Hex(file.Value);
                }

                var relative = $"tiles/{t.Z}/{t.X}/{t.Y}";
                foreach (var file in tileFiles)
                {
                    files.Add(WriteFile(data, $"{relative}/{file.Key}", file.Value, "application/octet-stream"));
                }

                var tileJson = Json(t);
                files.Add(WriteFile(data, $"{relative}/tile.json", tileJson, "application/json"));
                info.Tiles.Add(new ZoneTileRef { Z = t.Z, X = t.X, Y = t.Y, Version = zoneVersion, Sha256 = Sha256.Hex(tileJson) });
            }

            files.Add(WriteFile(data, "zone.json", Json(info), "application/json"));
            if (zone.Conditions != null)
            {
                files.Add(WriteFile(data, "conditions.json", Json(zone.Conditions), "application/json"));
            }

            long total = 0;
            foreach (var file in files)
            {
                total += file.Size;
            }

            var bundle = new BundleInfo
            {
                SchemaVersion = 1,
                BundleId = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                Source = "earth",
                ZoneId = info.ZoneId,
                ZoneVersion = zoneVersion,
                Kind = "zone_full",
                CreatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                DelayS = 0,
                DeliverAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Files = files,
                PayloadOxum = $"{total}.{files.Count}",
            };
            File.WriteAllBytes(Path.Combine(bundleFolder, "bundle.json"), Json(bundle));
        }

        private static BundleFile WriteFile(string data, string relative, byte[] bytes, string mediaType)
        {
            var path = Path.Combine(data, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return new BundleFile { Path = $"data/{relative}", Size = bytes.Length, Sha256 = Sha256.Hex(bytes), MediaType = mediaType };
        }

        private static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented));

        private static byte[] ToBytes(ushort[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                bytes[2 * i] = (byte)(samples[i] & 0xFF);
                bytes[2 * i + 1] = (byte)(samples[i] >> 8);
            }

            return bytes;
        }
    }
}
