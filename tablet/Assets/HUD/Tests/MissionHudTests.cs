using System;
using System.Collections;
using System.Linq;
using MartianMap.Terrain;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace MartianMap.HUD
{
    public sealed class MissionHudTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SandboxLoadsTheContractTileAndEnablesItsMission()
        {
            Scene scene = EditorSceneManager.LoadSceneInPlayMode("Assets/HUD/HUDSandbox.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                yield return null;
                MissionHud hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MissionHud>()).Single();
                float deadline = Time.realtimeSinceStartup + 20;
                while (!hud.Status.StartsWith("Ready.", StringComparison.Ordinal) && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(hud.Status, Does.StartWith("Ready."));
                Assert.That(hud.Mission.ZoneId, Is.EqualTo("synth"));
                Assert.That(hud.GetComponentInChildren<Button>().interactable, Is.True);
                TerrainMap2D map = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TerrainMap2D>()).Single();
                Assert.That(map.Current.Zone.ZoneId, Is.EqualTo("synth"));
                Assert.That(map.Current.Tiles.Count, Is.EqualTo(1));
            }
            finally
            {
                SceneManager.UnloadSceneAsync(scene);
            }

            yield return null;
        }
#endif

        [UnityTest]
        public IEnumerator RouteControlWaitsForTheMatchingZoneAndEmitsC4Inputs()
        {
            var root = new GameObject("HUD test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            var hud = root.AddComponent<MissionHud>();
            RouteRequested? observed = null;
            Action<RouteRequested> listener = message => observed = message;
            MapEvents.RouteRequested += listener;
            try
            {
                hud.LoadMission(asset);
                root.SetActive(true);
                yield return null;
                var button = root.GetComponentInChildren<Button>();
                Assert.That(button.interactable, Is.False);
                hud.RequestRoute();
                Assert.That(observed.HasValue, Is.False);
                MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
                Assert.That(button.interactable, Is.True);
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("simulated")), Is.True);
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>().Where(text => text.name.StartsWith("Stop ", StringComparison.Ordinal)))
                {
                    text.ForceMeshUpdate();
                    float rightEdge = text.rectTransform.rect.xMax - text.margin.z;
                    Assert.That(text.textInfo.characterInfo.Where(character => character.isVisible)
                        .All(character => character.topRight.x <= rightEdge + 1), Is.True, "Stop text extends beyond its panel.");
                }

                button.onClick.Invoke();
                Assert.That(observed.HasValue, Is.True);
                Assert.That(observed.Value.Stops.Count, Is.EqualTo(2));
                Assert.That(observed.Value.Home.E, Is.EqualTo(-100));
                Assert.That(observed.Value.Budget.DoseUGy, Is.EqualTo(50));
                Assert.That(hud.Status, Does.Contain("Waiting for a result"));
                ZoneInfo wrongZone = MissionDefinitionTests.Zone();
                wrongZone.ZoneId = "gale";
                MapEvents.Emit(new ZoneLoaded(wrongZone, string.Empty, "ev1"));
                Assert.That(button.interactable, Is.False);
                Assert.That(hud.Status, Does.Contain("active zone is gale"));
            }
            finally
            {
                MapEvents.RouteRequested -= listener;
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(asset);
            }

            yield return null;
        }
    }
}
