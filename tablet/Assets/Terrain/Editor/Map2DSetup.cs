using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MartianMap.Terrain.Editor
{
    /// <summary>
    /// Wires the 2D map into the project. Idempotent. From the menu, or in batch mode:
    /// <c>Unity -batchmode -quit -projectPath tablet -executeMethod MartianMap.Terrain.Editor.Map2DSetup.Run</c>
    /// </summary>
    public static class Map2DSetup
    {
        private const string ShaderName = "MartianMap/Terrain/TerrainMap2D";
        private const string MaterialPath = "Assets/Terrain/M_TerrainMap2D.mat";
        private const string TerrainPrefabPath = "Assets/Terrain/Terrain.prefab";
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string SandboxScenePath = "Assets/Terrain/Scenes/TerrainSandbox.unity";

        // Samsung Galaxy Tab S9 FE: 2304 × 1440 (16:10). The PC window keeps the ratio at half size.
        private const int TabletWidth = 2304;
        private const int TabletHeight = 1440;

        [MenuItem("MartianMap/Run 2D map setup")]
        public static void Run()
        {
            var material = EnsureMaterial();
            EnsureTerrainPrefab(material);
            ConfigureScene(MainScenePath, createIfMissing: false, sandboxKeys: false);
            ConfigureScene(SandboxScenePath, createIfMissing: true, sandboxKeys: true);
            ConfigureStandalonePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[Terrain] 2D map setup done");
        }

        private static Material EnsureMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"shader {ShaderName} not found or does not compile");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_TerrainMap2D" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureTerrainPrefab(Material material)
        {
            var root = PrefabUtility.LoadPrefabContents(TerrainPrefabPath);
            try
            {
                root.layer = LayerMask.NameToLayer("Terrain");

                // The old debug panel was removed: drop its now-missing component.
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

                var map = GetOrAdd<TerrainMap2D>(root);
                var bootstrap = GetOrAdd<TerrainBootstrap>(root);
                SetReference(map, "terrainMaterial", material);
                SetReference(bootstrap, "map", map);

                PrefabUtility.SaveAsPrefabAsset(root, TerrainPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureScene(string path, bool createIfMissing, bool sandboxKeys)
        {
            Scene scene;
            if (System.IO.File.Exists(path))
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            else if (createIfMissing)
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefabPath);
                PrefabUtility.InstantiatePrefab(prefab, scene);
            }
            else
            {
                Debug.LogWarning($"[Terrain] scene missing path={path}");
                return;
            }

            // A 2D map needs no sun.
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(light.gameObject);
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = 140f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.035f, 0.035f, 1f);
            camera.transform.SetPositionAndRotation(new Vector3(512f, 1000f, 384f), Quaternion.Euler(90f, 0f, 0f));
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 2000f;
            GetOrAdd<MapCamera2D>(camera.gameObject);

            // Keyboard shortcuts only in the sandbox; Main.unity gets its controls from P6's HUD.
            var keys = Object.FindAnyObjectByType<TerrainSandboxKeys>();
            if (sandboxKeys && keys == null)
            {
                var go = new GameObject("SandboxKeys");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<TerrainSandboxKeys>();
            }
            else if (!sandboxKeys && keys != null)
            {
                Object.DestroyImmediate(keys.gameObject);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
        }

        private static void ConfigureStandalonePlayer()
        {
            PlayerSettings.defaultScreenWidth = TabletWidth / 2;
            PlayerSettings.defaultScreenHeight = TabletHeight / 2;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new System.InvalidOperationException($"{target.GetType().Name} has no serialized field '{field}'");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
