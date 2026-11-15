using MartianMap.FX;
using MartianMap.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MartianMap.FX.Editor
{
    /// <summary>Adds FX prefabs and a fixture event source to P4's terrain sandbox.</summary>
    public static class FxPreviewSetup
    {
        private const string SandboxPath = "Assets/Terrain/Scenes/TerrainSandbox.unity";

        [MenuItem("MartianMap/FX/Set up preview")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(SandboxPath, OpenSceneMode.Single);
            AddPrefab(scene, "Assets/FX/FX.prefab", "FX");
            AddPrefab(scene, "Assets/FX/Routes.prefab", "Routes");
            var terrain = Object.FindFirstObjectByType<TerrainBootstrap>();
            if (terrain == null) throw new System.InvalidOperationException("TerrainSandbox has no TerrainBootstrap");
            var settings = new SerializedObject(terrain);
            settings.FindProperty("source").enumValueIndex = (int)TerrainBootstrap.ZoneSource.ContractFixture;
            settings.FindProperty("targetFrameRate").intValue = 30;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (Object.FindFirstObjectByType<FxPreviewEvents>() == null)
            {
                var go = new GameObject("FX preview events");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<FxPreviewEvents>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[FX] Preview ready. Open TerrainSandbox and press Play.");
        }

        private static void AddPrefab(Scene scene, string path, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new System.InvalidOperationException($"Missing prefab: {path}");
            PrefabUtility.InstantiatePrefab(asset, scene);
        }
    }
}
