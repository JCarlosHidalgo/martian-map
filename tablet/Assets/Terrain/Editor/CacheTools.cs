using System.IO;
using UnityEditor;
using UnityEngine;

namespace MartianMap.Terrain.Editor
{
    /// <summary>Editor helpers to test the local cache on a PC.</summary>
    public static class CacheTools
    {
        [MenuItem("MartianMap/Cache/Export demo zone as preloaded bundle")]
        public static void ExportDemoZone()
        {
            var folder = Path.Combine(Application.streamingAssetsPath, PreloadedZones.FolderName, "demo");
            using var zone = SyntheticZone.CreateDemo();
            ZoneBundleWriter.Write(zone, folder, zoneVersion: 1);
            AssetDatabase.Refresh();
            Debug.Log($"[Terrain] demo zone exported as zone_full bundle path={folder} (git-ignored)");
        }

        [MenuItem("MartianMap/Cache/Clear local cache")]
        public static void ClearLocalCache()
        {
            var zones = Path.Combine(Application.persistentDataPath, "zones");
            var catalog = Path.Combine(Application.persistentDataPath, "catalog.json");
            if (Directory.Exists(zones))
            {
                Directory.Delete(zones, true);
            }

            if (File.Exists(catalog))
            {
                File.Delete(catalog);
            }

            Debug.Log($"[Terrain] local cache cleared path={Application.persistentDataPath}");
        }

        [MenuItem("MartianMap/Cache/Open cache folder")]
        public static void OpenCacheFolder()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }
    }
}
