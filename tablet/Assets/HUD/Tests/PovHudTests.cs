using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MartianMap.Terrain;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    public sealed class PovHudTests
    {
        private GameObject _root;
        private PovHud _hud;
        private Reader _reader;
        private byte[] _jpeg;

        private sealed class Reader : IPovImageReader
        {
            public Func<string, Task<byte[]>> Read { get; set; }
            public Task<byte[]> ReadAsync(string path) => Read(path);
        }

        internal static byte[] SyntheticJpeg(int width = 8, int height = 4)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                var pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = i % width < width / 2 ? new Color(0.7f, 0.3f, 0.1f) : new Color(0.2f, 0.5f, 0.7f);
                }

                texture.SetPixels(pixels);
                texture.Apply();
                return texture.EncodeToJPG();
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _jpeg = SyntheticJpeg();
            _reader = new Reader { Read = _ => Task.FromResult(_jpeg) };
            _root = new GameObject("POV HUD test");
            _root.SetActive(false);
            _hud = _root.AddComponent<PovHud>();
            _hud.UseImageReader(_reader);
            _root.SetActive(true);
            yield return null;
            Load(PovPhotoTests.Pin(_jpeg));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(_root);
            yield return null;
        }

        private static void Load(params PovPin[] pins)
        {
            ZoneInfo zone = MissionDefinitionTests.Zone();
            zone.Pov.AddRange(pins);
            MapEvents.Emit(new ZoneLoaded(zone, Path.Combine(Path.GetTempPath(), "martian-map-pov-test-input"), "ev1"));
        }

        [UnityTest]
        public IEnumerator LocalImageHasAnExactVisibleCreditAspectRatioAndReleasedTexture()
        {
            string credit = PovPhotoTests.Pin().Credit;
            Assert.That(_hud.PhotoCount, Is.EqualTo(1));
            TMP_Text pin = _root.GetComponentsInChildren<Button>(true).Single(button => button.name == "POV pin photo-a").GetComponentInChildren<TMP_Text>();
            Assert.That(pin.text, Does.Contain(credit));
            Task<bool> load = _hud.OpenPhotoAsync("photo-a");
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Result, Is.True);
            Assert.That(_hud.SelectedCredit, Is.EqualTo(credit));
            Assert.That(_hud.DisplayedTexture.width, Is.EqualTo(8));
            Assert.That(_hud.DisplayedTexture.isReadable, Is.False);
            Assert.That(_root.GetComponentInChildren<AspectRatioFitter>().aspectRatio, Is.EqualTo(2));
            TMP_Text provenance = _root.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "POV credit and provenance");
            Assert.That(provenance.text, Does.StartWith("Credit: " + credit));
            Assert.That(provenance.text, Does.Contain("Sol 12"));
            Assert.That(provenance.richText, Is.False);
            Texture2D texture = _hud.DisplayedTexture;
            _hud.Close();
            Assert.That(_hud.IsOpen, Is.False);
            Assert.That(_hud.DisplayedTexture, Is.Null);
            yield return null;
            Assert.That(texture == null, Is.True);
        }

        [UnityTest]
        public IEnumerator MissingAndAlteredImagesKeepTheirOwnCreditAndNeverReuseThePreviousPhoto()
        {
            Task<bool> first = _hud.OpenPhotoAsync("photo-a");
            yield return new WaitUntil(() => first.IsCompleted);
            _reader.Read = _ => Task.FromException<byte[]>(new FileNotFoundException("Synthetic missing image"));
            Task<bool> missing = _hud.OpenPhotoAsync("photo-a");
            yield return new WaitUntil(() => missing.IsCompleted);
            Assert.That(missing.Result, Is.False);
            Assert.That(_hud.DisplayedTexture, Is.Null);
            Assert.That(_hud.SelectedCredit, Is.EqualTo(PovPhotoTests.Pin().Credit));
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.PovFileUnavailable));
            _reader.Read = _ => Task.FromResult(new byte[] { 0xff, 0xd8, 0x00, 0xff, 0xd9 });
            Task<bool> altered = _hud.OpenPhotoAsync("photo-a");
            yield return new WaitUntil(() => altered.IsCompleted);
            Assert.That(altered.Result, Is.False);
            Assert.That(_hud.DisplayedTexture, Is.Null);
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.PovIntegrityFailed));
        }

        [UnityTest]
        public IEnumerator OlderLoadsCannotReplaceTheNewImageOrItsCredit()
        {
            byte[] secondBytes = SyntheticJpeg(16, 8);
            Load(PovPhotoTests.Pin(_jpeg), PovPhotoTests.Pin(secondBytes, "photo-b", "b.jpg", "Second synthetic credit"));
            var firstSource = new TaskCompletionSource<byte[]>();
            var secondSource = new TaskCompletionSource<byte[]>();
            _reader.Read = path => path.EndsWith("a.jpg", StringComparison.Ordinal) ? firstSource.Task : secondSource.Task;
            Task<bool> first = _hud.OpenPhotoAsync("photo-a");
            Task<bool> second = _hud.OpenPhotoAsync("photo-b");
            secondSource.SetResult(secondBytes);
            yield return new WaitUntil(() => second.IsCompleted);
            Assert.That(second.Result, Is.True);
            firstSource.SetResult(_jpeg);
            yield return new WaitUntil(() => first.IsCompleted);
            Assert.That(first.Result, Is.False);
            Assert.That(_hud.SelectedCredit, Is.EqualTo("Second synthetic credit"));
            Assert.That(_hud.DisplayedTexture.width, Is.EqualTo(16));
        }

        [UnityTest]
        public IEnumerator ChangingZoneOrClosingDuringAReadPreventsLateImages()
        {
            var completion = new TaskCompletionSource<byte[]>();
            _reader.Read = _ => completion.Task;
            Task<bool> load = _hud.OpenPhotoAsync("photo-a");
            Load();
            completion.SetResult(_jpeg);
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Result, Is.False);
            Assert.That(_hud.PhotoCount, Is.Zero);
            Assert.That(_hud.IsOpen, Is.False);
            Assert.That(_hud.DisplayedTexture, Is.Null);
            Assert.That(_hud.SelectedCredit, Is.Empty);
            Load(PovPhotoTests.Pin(_jpeg));
            completion = new TaskCompletionSource<byte[]>();
            load = _hud.OpenPhotoAsync("photo-a");
            _hud.Close();
            completion.SetResult(_jpeg);
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Result, Is.False);
            Assert.That(_hud.IsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator MissingCreditsAndDuplicateIdsDoNotProduceSelectableImages()
        {
            PovPin invalid = PovPhotoTests.Pin(_jpeg);
            invalid.Credit = " ";
            Load(invalid);
            Assert.That(_hud.PhotoCount, Is.Zero);
            Assert.That(_hud.OpenPhotoAsync("photo-a").Result, Is.False);
            Load(PovPhotoTests.Pin(_jpeg), PovPhotoTests.Pin(_jpeg));
            Assert.That(_hud.PhotoCount, Is.Zero);
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.PovInvalidMetadata));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItemSnapshotsAndLongCreditsRemainLiteralAndScrollable()
        {
            string credit = string.Join(" ", Enumerable.Repeat("Synthetic <b>credit</b> text.", 60));
            PovPin pin = PovPhotoTests.Pin(_jpeg, credit: credit);
            Load(pin);
            pin.Credit = "Changed after ZoneLoaded";
            Task<bool> load = _hud.OpenPhotoAsync("photo-a");
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Result, Is.True);
            Assert.That(_hud.SelectedCredit, Is.EqualTo(credit));
            Canvas.ForceUpdateCanvases();
            ScrollRect scroll = _root.GetComponentsInChildren<ScrollRect>().Single();
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(0.01));
            Assert.That(scroll.content.GetComponent<TMP_Text>().richText, Is.False);
        }

        [UnityTest]
        public IEnumerator OversizedImagesAreRejectedAndEditingClosesTheViewer()
        {
            byte[] oversized = SyntheticJpeg(1025, 1);
            Load(PovPhotoTests.Pin(oversized));
            _reader.Read = _ => Task.FromResult(oversized);
            Task<bool> load = _hud.OpenPhotoAsync("photo-a");
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Result, Is.False);
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.PovImageSize));
            Assert.That(_hud.DisplayedTexture, Is.Null);
            Assert.That(_hud.SelectedCredit, Is.Not.Empty);
            MapEvents.Emit(new EditModeChanged(true));
            Assert.That(_hud.IsOpen, Is.False);
            Assert.That(_hud.OpenPhotoAsync("photo-a").Result, Is.False);
            Assert.That(_root.GetComponentsInChildren<Button>(true).Single(button => button.name == "POV gallery").gameObject.activeSelf, Is.False);
            MapEvents.Emit(new EditModeChanged(false));
            // The gallery button is hidden at the team's request, also outside edit mode.
            Assert.That(_root.GetComponentsInChildren<Button>(true).Single(button => button.name == "POV gallery").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator DisabledHudDiscardsMissedZoneStateAndLateReads()
        {
            var completion = new TaskCompletionSource<byte[]>();
            _reader.Read = _ => completion.Task;
            Task<bool> load = _hud.OpenPhotoAsync("photo-a");
            _root.SetActive(false);
            completion.SetResult(_jpeg);
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Result, Is.False);
            _root.SetActive(true);
            Assert.That(_hud.PhotoCount, Is.Zero);
            Assert.That(_hud.OpenPhotoAsync("photo-a").Result, Is.False);
            Assert.That(_hud.DisplayedTexture, Is.Null);
        }

        [UnityTest]
        public IEnumerator DefaultReaderLoadsTheActualLocalFileThroughP4()
        {
            string folder = Path.Combine(Path.GetTempPath(), "martian-map-pov-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "a.jpg");
            File.WriteAllBytes(file, _jpeg);
            var root = new GameObject("Local POV reader test");
            var hud = root.AddComponent<PovHud>();
            try
            {
                ZoneInfo zone = MissionDefinitionTests.Zone();
                zone.Pov.Add(PovPhotoTests.Pin(_jpeg));
                MapEvents.Emit(new ZoneLoaded(zone, folder, "ev1"));
                Task<bool> load = hud.OpenPhotoAsync("photo-a");
                yield return new WaitUntil(() => load.IsCompleted);
                Assert.That(load.Result, Is.True);
                Assert.That(hud.DisplayedTexture.width, Is.EqualTo(8));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                File.Delete(file);
                Directory.Delete(folder);
            }

            yield return null;
        }
    }
}
