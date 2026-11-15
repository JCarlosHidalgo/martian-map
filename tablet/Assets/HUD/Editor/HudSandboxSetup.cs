#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using MartianMap.Terrain;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MartianMap.HUD
{
    /// <summary>Builds only P6 assets. Does not edit Main.unity, Terrain assets, or build settings.</summary>
    public static class HudSandboxSetup
    {
        public const string ScenePath = "Assets/HUD/HUDSandbox.unity";
        public const string PrefabPath = "Assets/HUD/HUD.prefab";
        public const string MissionPath = "Assets/HUD/Missions/SyntheticMission.json";
        private const string FontPath = "Assets/HUD/Fonts/InterSdf.asset";

        [MenuItem("MartianMap/HUD/Create mission sandbox")]
        public static void CreateSandbox()
        {
            EnsureTextSettings(null);
            TMP_FontAsset font = EnsureFont();
            EnsureTextSettings(font);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var hud = root.GetComponent<MissionHud>() ?? root.AddComponent<MissionHud>();
                var data = new SerializedObject(hud);
                data.FindProperty("_font").objectReferenceValue = font;
                data.FindProperty("_missionAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(MissionPath);
                data.ApplyModifiedPropertiesWithoutUndo();
                var science = root.GetComponent<ScienceHud>() ?? root.AddComponent<ScienceHud>();
                var scienceData = new SerializedObject(science);
                scienceData.FindProperty("_font").objectReferenceValue = font;
                scienceData.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(MapCamera2D)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.055f);
            var map = new GameObject("Synthetic Terrain", typeof(TerrainMap2D)).GetComponent<TerrainMap2D>();
            var mapData = new SerializedObject(map);
            mapData.FindProperty("terrainMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Terrain/M_TerrainMap2D.mat");
            mapData.ApplyModifiedPropertiesWithoutUndo();
            var driver = new GameObject("HUD Sandbox", typeof(HudSandboxDriver)).GetComponent<HudSandboxDriver>();
            var driverData = new SerializedObject(driver);
            driverData.FindProperty("_map").objectReferenceValue = map;
            driverData.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[HUD] mission sandbox created path=" + ScenePath);
        }

        private static TMP_FontAsset EnsureFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing != null)
            {
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/HUD/Fonts/Inter-Regular.ttf");
            if (source == null)
            {
                throw new FileNotFoundException("HUD Inter font source is missing.");
            }

            var font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic);
            font.name = "InterSdf";
            string characters = new string(Enumerable.Range(32, 95).Select(value => (char)value).ToArray()) + "µ±·–";
            if (!font.TryAddCharacters(characters, out string missing))
            {
                throw new InvalidDataException("HUD font is missing characters: " + missing);
            }

            AssetDatabase.CreateAsset(font, FontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D texture in font.atlasTextures)
            {
                AssetDatabase.AddObjectToAsset(texture, font);
            }

            EditorUtility.SetDirty(font);
            return font;
        }

        private static void EnsureTextSettings(TMP_FontAsset font)
        {
            var existing = Resources.Load<TMP_Settings>("TMP Settings");
            if (existing != null)
            {
                if (font != null && TMP_Settings.defaultFontAsset == null &&
                    AssetDatabase.GetAssetPath(existing).StartsWith("Assets/HUD/", StringComparison.Ordinal))
                {
                    var existingData = new SerializedObject(existing);
                    existingData.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
                    existingData.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssets();
                }

                return;
            }

            Directory.CreateDirectory("Assets/HUD/Resources");
            var settings = ScriptableObject.CreateInstance<TMP_Settings>();
            AssetDatabase.CreateAsset(settings, "Assets/HUD/Resources/TMP Settings.asset");
            var leading = new TextAsset(string.Empty) { name = "LeadingCharacters" };
            var following = new TextAsset(string.Empty) { name = "FollowingCharacters" };
            AssetDatabase.AddObjectToAsset(leading, settings);
            AssetDatabase.AddObjectToAsset(following, settings);
            var data = new SerializedObject(settings);
            data.FindProperty("assetVersion").stringValue = "2";
            data.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
            data.FindProperty("m_leadingCharacters").objectReferenceValue = leading;
            data.FindProperty("m_followingCharacters").objectReferenceValue = following;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
