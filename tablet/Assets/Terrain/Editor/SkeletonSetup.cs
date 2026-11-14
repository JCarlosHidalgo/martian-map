using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

namespace MartianMap.Terrain.Editor
{
    /// <summary>
    /// Idempotent project setup for the tablet skeleton (docs/guide/chapters/reparto.md,
    /// "Antes de H0", step 3). Run from the menu or in batch mode:
    /// <c>Unity -batchmode -quit -projectPath tablet -executeMethod MartianMap.Terrain.Editor.SkeletonSetup.Run</c>
    /// </summary>
    public static class SkeletonSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private static readonly string[] Layers = { "Terrain", "Routes", "FX", "Pins" };

        [MenuItem("MartianMap/Run skeleton setup")]
        public static void Run()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            ConfigureLayers();
            ConfigurePlayer();
            var terrain = EnsurePrefab("Assets/Terrain/Terrain.prefab", "Terrain", "Terrain");
            var hud = EnsurePrefab("Assets/HUD/HUD.prefab", "HUD", "UI");
            EnsureMainScene(terrain, hud);
            AssetDatabase.SaveAssets();
            Debug.Log("[Terrain] skeleton setup done");
        }

        private static void ConfigureLayers()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            foreach (var name in Layers)
            {
                if (LayerMask.NameToLayer(name) >= 0)
                {
                    continue;
                }

                // User layers start at 8.
                for (var i = 8; i < layers.arraySize; i++)
                {
                    var slot = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(slot.stringValue))
                    {
                        slot.stringValue = name;
                        break;
                    }
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePlayer()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "MartianMap";
            PlayerSettings.productName = "Martian Map";
            PlayerSettings.SetApplicationIdentifier(android, "com.martianmap.tablet");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Medium);

            // The habitat and the mocks speak plain HTTP; the default blocks every request on Android.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            // Landscape tablet.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Input System only (0 = old, 1 = new, 2 = both).
            var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var serialized = new SerializedObject(playerSettings);
            var handler = serialized.FindProperty("activeInputHandler");
            if (handler != null)
            {
                handler.intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static GameObject EnsurePrefab(string path, string name, string layer)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(name) { layer = LayerMask.NameToLayer(layer) };
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void EnsureMainScene(GameObject terrain, GameObject hud)
        {
            if (File.Exists(MainScenePath))
            {
                AddToBuildSettings();
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 400f, -600f),
                    Quaternion.Euler(35f, 0f, 0f));
                camera.nearClipPlane = 1f;
                camera.farClipPlane = 20000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }

            PrefabUtility.InstantiatePrefab(terrain, scene);
            PrefabUtility.InstantiatePrefab(hud, scene);

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            EditorSceneManager.MoveGameObjectToScene(eventSystem, scene);

            EditorSceneManager.SaveScene(scene, MainScenePath);
            AddToBuildSettings();
        }

        private static void AddToBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }
    }
}
