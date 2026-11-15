using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MartianMap.Terrain.Editor
{
    /// <summary>
    /// The benchmark scene and APK that P4 provides to P3 (E5, E7; later E9 adds its own button).
    /// Batch: <c>Unity -batchmode -quit -projectPath tablet -executeMethod MartianMap.Terrain.Editor.BenchmarkSetup.CreateScene</c>
    /// </summary>
    public static class BenchmarkSetup
    {
        public const string ScenePath = "Assets/Terrain/Scenes/BenchmarkSandbox.unity";

        [MenuItem("MartianMap/Benchmark/Create benchmark scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log($"[Terrain] benchmark scene already exists path={ScenePath}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            camera.orthographic = true;

            new GameObject("Benchmark", typeof(BenchmarkRunner));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Terrain] benchmark scene created path={ScenePath}");
        }

        /// <summary>An APK with only the benchmark scene: <c>tablet/Builds/benchmark.apk</c>.</summary>
        [MenuItem("MartianMap/Benchmark/Build benchmark APK")]
        public static void BuildApk()
        {
            CreateScene();
            var output = Path.Combine("Builds", "benchmark.apk");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"benchmark APK failed: result={report.summary.result} errors={report.summary.totalErrors}");
            }

            Debug.Log($"[Terrain] benchmark apk built path={output} size_mb={report.summary.totalSize / (1024f * 1024f):F1}");
        }
    }
}
