using System;
using System.Collections;
using System.Linq;
using MartianMap.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace MartianMap.HUD
{
    public sealed class AstronautHudTests
    {
        [Test]
        public void HoldFiresOnceAtTheTimeThreshold()
        {
            var hold = new MapHoldGesture();
            hold.Begin(1, Vector2.zero, 1);
            Assert.That(hold.Hold(1, Vector2.zero, 1.49f, 0.5f, 18), Is.False);
            Assert.That(hold.Hold(1, Vector2.zero, 1.5f, 0.5f, 18), Is.True);
            Assert.That(hold.Hold(1, Vector2.zero, 2, 0.5f, 18), Is.False);
        }

        [Test]
        public void DragCannotBecomeAHoldByReturningToItsOrigin()
        {
            var hold = new MapHoldGesture();
            hold.Begin(1, Vector2.zero, 0);
            Assert.That(hold.Hold(1, new Vector2(19, 0), 0.1f, 0.5f, 18), Is.False);
            Assert.That(hold.Hold(1, Vector2.zero, 1, 0.5f, 18), Is.False);
        }

        [Test]
        public void PointerChangeCancellationAndInvalidCoordinatesAbortTheHold()
        {
            var hold = new MapHoldGesture();
            hold.Begin(1, Vector2.zero, 0);
            Assert.That(hold.Hold(2, Vector2.zero, 1, 0.5f, 18), Is.False);
            Assert.That(hold.Hold(1, Vector2.zero, 2, 0.5f, 18), Is.False);
            hold.Begin(1, Vector2.zero, 0);
            hold.Cancel();
            Assert.That(hold.Hold(1, Vector2.zero, 1, 0.5f, 18), Is.False);
            hold.Begin(1, Vector2.zero, 0);
            Assert.That(hold.Hold(1, new Vector2(float.NaN, 0), 1, 0.5f, 18), Is.False);
            Assert.That(hold.Hold(1, Vector2.zero, 2, 0.5f, 18), Is.False);
        }

        [UnityTest]
        public IEnumerator MovedPositionBecomesRouteStartWhileHomeStaysAtTheMissionBase()
        {
            var root = new GameObject("Astronaut mission test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            var hud = root.AddComponent<MissionHud>();
            hud.LoadMission(asset);
            RouteRequested? route = null;
            AstronautMoved? moved = null;
            Action<RouteRequested> onRoute = message => route = message;
            Action<AstronautMoved> onMove = message => moved = message;
            MapEvents.RouteRequested += onRoute;
            MapEvents.AstronautMoved += onMove;
            try
            {
                root.SetActive(true);
                yield return null;
                Assert.That(hud.MoveAstronaut(Vector2.zero), Is.False);
                MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
                Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(-100));
                Assert.That(moved.Value.EnuM, Is.EqualTo(new Vector2(-100, -20)));
                Assert.That(hud.MoveAstronaut(new Vector2(10, 20)), Is.True);
                Assert.That(moved.Value.EnuM, Is.EqualTo(new Vector2(10, 20)));
                hud.RequestRoute();
                Assert.That(route.Value.Start.E, Is.EqualTo(10));
                Assert.That(route.Value.Start.N, Is.EqualTo(20));
                Assert.That(route.Value.Home.E, Is.EqualTo(-100));
                Assert.That(route.Value.Home.N, Is.EqualTo(-20));
                Assert.That(route.Value.Budget.DoseUGy, Is.EqualTo(50));
                foreach (var invalid in new[] { new Vector2(128, 0), new Vector2(-129, 0), new Vector2(0, 128), new Vector2(0, -129), new Vector2(float.NaN, 0), new Vector2(0, float.PositiveInfinity) })
                {
                    Assert.That(hud.MoveAstronaut(invalid), Is.False);
                    Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(10));
                }

                ZoneInfo other = MissionDefinitionTests.Zone();
                other.ZoneId = "gale";
                MapEvents.Emit(new ZoneLoaded(other, string.Empty, "ev1"));
                Assert.That(hud.AstronautPosition.HasValue, Is.False);
                Assert.That(hud.MoveAstronaut(Vector2.zero), Is.False);
                MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
                Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(-100));
            }
            finally
            {
                MapEvents.RouteRequested -= onRoute;
                MapEvents.AstronautMoved -= onMove;
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(asset);
            }

            yield return null;
        }

#if UNITY_EDITOR
        private sealed class RegisteredConfiguration : IHudConfiguration
        {
            public string HabitatUrl => string.Empty;
            public string FilesUrl => string.Empty;
            public string Author => "ev1";
            public void Save(string habitatUrl, string filesUrl, string author) =>
                throw new InvalidOperationException("This test does not save configuration.");
        }

        [UnityTest]
        public IEnumerator SandboxAcceptsMapHoldAndRejectsDragControlsAndEditMode()
        {
            Scene scene = EditorSceneManager.LoadSceneInPlayMode("Assets/HUD/HUDSandbox.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            Mouse mouse = null;
            var previousInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            var previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            bool previousRunInBackground = Application.runInBackground;
            try
            {
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                yield return null;
                MissionHud hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MissionHud>()).Single();
                float deadline = Time.realtimeSinceStartup + 20;
                while (!hud.AstronautPosition.HasValue && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(hud.AstronautPosition.HasValue, Is.True);
                var settings = hud.GetComponent<SettingsHud>();
                settings.UseConfiguration(new RegisteredConfiguration());
                settings.Close();
                Camera camera = MapQuery.Camera;
                Vector2 screen = camera.WorldToScreenPoint(ZoneProjection.ToScene(Vector2.zero));
                Assert.That(MapQuery.TryPick(screen, out Vector2 expected), Is.True);
                mouse = InputSystem.AddDevice<Mouse>();
                mouse.MakeCurrent();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                yield return new WaitForSecondsRealtime(0.85f);
                Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(expected.x).Within(0.01));
                Assert.That(hud.AstronautPosition.Value.N, Is.EqualTo(expected.y).Within(0.01));
                Transform marker = hud.GetComponentsInChildren<Transform>().Single(child => child.name == "Astronaut marker");
                Assert.That(marker.gameObject.activeSelf, Is.True);
                Vector2 markerScreen = RectTransformUtility.WorldToScreenPoint(null, marker.position);
                Vector2 positionScreen = camera.WorldToScreenPoint(ZoneProjection.ToScene(expected));
                Assert.That(Vector2.Distance(markerScreen, positionScreen), Is.LessThan(2));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;

                var before = hud.AstronautPosition.Value;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen + new Vector2(Screen.width * 0.15f, 0) }.WithButton(MouseButton.Left));
                yield return new WaitForSecondsRealtime(0.85f);
                Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(before.E));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;

                settings.Open();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                yield return new WaitForSecondsRealtime(0.85f);
                Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(before.E));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;
                settings.Close();
                MapEvents.Emit(new EditModeChanged(true));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                yield return new WaitForSecondsRealtime(0.85f);
                Assert.That(hud.AstronautPosition.Value.E, Is.EqualTo(before.E));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                MapEvents.Emit(new EditModeChanged(false));
            }
            finally
            {
                if (mouse != null)
                {
                    InputSystem.RemoveDevice(mouse);
                }

                InputSystem.settings.editorInputBehaviorInPlayMode = previousInputBehavior;
                InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
                Application.runInBackground = previousRunInBackground;
                MapEvents.Emit(new EditModeChanged(false));
                SceneManager.UnloadSceneAsync(scene);
            }

            yield return null;
        }
#endif
    }
}
