using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MartianMap.Terrain.Editor
{
    /// <summary>
    /// The Android build P4 runs every 4 h. From the menu, or in batch mode:
    /// <c>Unity -batchmode -quit -projectPath tablet -buildTarget Android -executeMethod MartianMap.Terrain.Editor.BuildAndroid.Run</c>
    /// Output: <c>tablet/Builds/martian-map.apk</c> (ignored by git).
    /// </summary>
    public static class BuildAndroid
    {
        [MenuItem("MartianMap/Build Android APK")]
        public static void Run()
        {
            var output = Path.Combine("Builds", "martian-map.apk");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Android build failed: result={summary.result} errors={summary.totalErrors}");
            }

            Debug.Log($"[Terrain] apk built path={output} size_mb={summary.totalSize / (1024f * 1024f):F1}");
        }
    }
}
