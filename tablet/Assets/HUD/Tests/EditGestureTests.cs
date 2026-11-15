#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using MartianMap.Terrain;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MartianMap.HUD
{
    public sealed class EditGestureTests
    {
        private sealed class RegisteredConfiguration : IHudConfiguration
        {
            public string HabitatUrl => string.Empty;
            public string FilesUrl => string.Empty;
            public string Author => "ev1";
            public void Save(string habitatUrl, string filesUrl, string author) { }
        }

        [UnityTest]
        public IEnumerator MapTapsAddVerticesWhileDragControlsAndSettingsDoNot()
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
                var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EditHud>()).Single();
                var settings = hud.GetComponent<SettingsHud>();
                settings.UseConfiguration(new RegisteredConfiguration());
                settings.Close();
                float deadline = Time.realtimeSinceStartup + 20;
                while (!hud.EnterEditMode() && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(hud.IsEditing, Is.True);
                Camera camera = MapQuery.Camera;
                Vector2 screen = camera.WorldToScreenPoint(ZoneProjection.ToScene(Vector2.zero));
                mouse = InputSystem.AddDevice<Mouse>();
                mouse.MakeCurrent();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                yield return null;
                Assert.That(hud.DraftVertices.Count, Is.EqualTo(1));
                Assert.That(hud.DraftVertices[0].E, Is.EqualTo(0));

                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                yield return null;
                Vector2 drag = screen + new Vector2(Screen.width * 0.15f, 0);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = drag }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = drag });
                yield return null;
                Assert.That(hud.DraftVertices.Count, Is.EqualTo(1));

                Vector2 panel = new Vector2(Screen.width * 0.15f, Screen.height * 0.65f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = panel }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = panel });
                yield return null;
                Assert.That(hud.DraftVertices.Count, Is.EqualTo(1));
                settings.Open();
                yield return null;
                Assert.That(hud.IsEditing, Is.False);
                Assert.That(hud.DraftVertices, Is.Empty);
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
    }
}
#endif
