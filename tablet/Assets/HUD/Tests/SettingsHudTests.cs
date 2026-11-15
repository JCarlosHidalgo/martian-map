using System;
using System.Collections;
using System.IO;
using System.Linq;
using MartianMap.Terrain;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    public sealed class SettingsHudTests
    {
        private sealed class Configuration : IHudConfiguration
        {
            public string HabitatUrl { get; set; } = string.Empty;
            public string FilesUrl { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public Exception Failure { get; set; }
            public int Saves { get; private set; }

            public void Save(string habitatUrl, string filesUrl, string author)
            {
                if (Failure != null)
                {
                    throw Failure;
                }

                HabitatUrl = habitatUrl;
                FilesUrl = filesUrl;
                Author = author;
                Saves++;
                MapEvents.Emit(new ConfigChanged(habitatUrl, filesUrl, author));
            }
        }

        private GameObject _root;
        private SettingsHud _hud;
        private Configuration _configuration;

        private void Create(string author = "")
        {
            _configuration = new Configuration { Author = author };
            _root = new GameObject("Settings HUD test");
            _root.SetActive(false);
            _hud = _root.AddComponent<SettingsHud>();
            _hud.UseConfiguration(_configuration);
            _root.SetActive(true);
        }

        private TMP_InputField Input(string name) =>
            _root.GetComponentsInChildren<TMP_InputField>(true).Single(field => field.name == name);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FirstUseRequiresAuthorAndAllowsCacheOnlyRegistration()
        {
            Create();
            yield return null;
            Assert.That(_hud.IsOpen, Is.True);
            Assert.That(_hud.CanEdit, Is.False);
            _hud.Close();
            Assert.That(_hud.IsOpen, Is.True);
            Input("Author").text = "ev1";
            _hud.Save();
            Assert.That(_configuration.Saves, Is.EqualTo(1));
            Assert.That(_configuration.HabitatUrl, Is.Empty);
            Assert.That(_configuration.FilesUrl, Is.Empty);
            Assert.That(_hud.CanEdit, Is.True);
            Assert.That(Input("Author").interactable, Is.False);
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.SettingsSaved));
            _hud.Close();
            Assert.That(_hud.IsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator RegisteredAuthorStaysFixedWhenAddressesChange()
        {
            Create("ev1");
            yield return null;
            Assert.That(_hud.IsOpen, Is.False);
            _hud.Open();
            Input("Author").text = "ev2";
            Input("Habitat URL").text = "http://192.168.1.2:8000";
            Input("Files URL").text = "http://192.168.1.2:8001";
            _hud.Save();
            Assert.That(_configuration.Author, Is.EqualTo("ev1"));
            Assert.That(_configuration.HabitatUrl, Is.EqualTo("http://192.168.1.2:8000"));
            Assert.That(_configuration.FilesUrl, Is.EqualTo("http://192.168.1.2:8001"));
            Assert.That(Input("Author").text, Is.EqualTo("ev1"));
        }

        [UnityTest]
        public IEnumerator InvalidAuthorDoesNotReachPersistence()
        {
            Create();
            yield return null;
            foreach (string invalid in new[] { "", "a", "EV1", "ev 1", "ev1!" })
            {
                Input("Author").text = invalid;
                _hud.Save();
                Assert.That(_configuration.Saves, Is.Zero);
                Assert.That(_hud.CanEdit, Is.False);
                Assert.That(_hud.Status, Is.EqualTo(HudStrings.AuthorInvalid));
            }
        }

        [UnityTest]
        public IEnumerator UrlRejectionAndDiskFailurePreserveUnsavedInput()
        {
            Create("ev1");
            yield return null;
            _hud.Open();
            foreach (var failure in new[]
            {
                (Error: (Exception)new ArgumentException("Invalid URL", "habitatUrl"), Message: HudStrings.HabitatUrlInvalid),
                (Error: (Exception)new ArgumentException("Invalid URL", "filesUrl"), Message: HudStrings.FilesUrlInvalid),
                (Error: (Exception)new IOException("Disk unavailable"), Message: HudStrings.SettingsWriteFailed),
                (Error: (Exception)new UnauthorizedAccessException("Access denied"), Message: HudStrings.SettingsWriteFailed),
            })
            {
                _configuration.Failure = failure.Error;
                Input("Habitat URL").text = "unsaved address";
                _hud.Save();
                Assert.That(_hud.Status, Is.EqualTo(failure.Message));
                Assert.That(Input("Habitat URL").text, Is.EqualTo("unsaved address"));
                Assert.That(_hud.IsOpen, Is.True);
                Assert.That(_configuration.Saves, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator ReopeningDiscardsUnsavedAddressesAndRefreshesConfigurationEvents()
        {
            Create("ev1");
            yield return null;
            _hud.Open();
            Input("Habitat URL").text = "discard this";
            _hud.Close();
            _hud.Open();
            Assert.That(Input("Habitat URL").text, Is.Empty);
            _configuration.FilesUrl = "http://habitat:8001";
            MapEvents.Emit(new ConfigChanged(string.Empty, _configuration.FilesUrl, "ev1"));
            Assert.That(Input("Files URL").text, Is.EqualTo(_configuration.FilesUrl));
        }

        [UnityTest]
        public IEnumerator ButtonsDriveTheFormAndLabelsFitThePanel()
        {
            Create("ev1");
            yield return null;
            _root.GetComponentsInChildren<Button>(true).Single(button => button.name == "Settings").onClick.Invoke();
            Assert.That(_hud.IsOpen, Is.True);
            Input("Files URL").text = "http://habitat:8001";
            _root.GetComponentsInChildren<Button>(true).Single(button => button.name == "Save settings").onClick.Invoke();
            Assert.That(_configuration.Saves, Is.EqualTo(1));
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text text in _root.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                Assert.That(text.isTextOverflowing, Is.False, text.name);
            }

            _root.GetComponentsInChildren<Button>(true).Single(button => button.name == "Close settings").onClick.Invoke();
            Assert.That(_hud.IsOpen, Is.False);
        }
    }
}
