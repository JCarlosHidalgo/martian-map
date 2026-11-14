using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MartianMap.Terrain.Editor
{
    /// <summary>
    /// Copies the repository's <c>contracts/fixtures/</c> into
    /// <c>Assets/StreamingAssets/fixtures/</c> when the project opens and before every build,
    /// so the APK carries the fixtures without a second copy in git.
    /// </summary>
    [InitializeOnLoad]
    public sealed class FixtureSync : IPreprocessBuildWithReport
    {
        static FixtureSync()
        {
            EditorApplication.delayCall += () => Sync(refresh: true);
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Sync(refresh: true);
        }

        [MenuItem("MartianMap/Sync fixtures")]
        public static void SyncFromMenu()
        {
            Sync(refresh: true);
        }

        private static void Sync(bool refresh)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var source = Path.GetFullPath(Path.Combine(projectRoot, "..", "contracts", "fixtures"));
            var target = Path.Combine(Application.streamingAssetsPath, "fixtures");

            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"[Terrain] fixture sync skipped: source={source} missing");
                return;
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            var copied = 0;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name == ".gitkeep")
                {
                    continue;
                }

                var relative = Path.GetRelativePath(source, file);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
                copied++;
            }

            Debug.Log($"[Terrain] fixtures synced files={copied} target={target}");
            if (refresh)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
