using System;
using MartianMap.Terrain;
using NUnit.Framework;

namespace MartianMap.HUD
{
    public sealed class PovPhotoTests
    {
        internal static PovPin Pin(byte[] bytes = null, string id = "photo-a", string file = "a.jpg", string credit = "Martian Map test suite (synthetic image)") => new PovPin
        {
            Id = id, SourceId = "synth-test-camera", EnuM = new double[] { 0, 0 },
            TUtc = "2026-11-14T12:00:00Z", Sol = 12, File = file, MediaType = "image/jpeg",
            Sha256 = Sha256.Hex(bytes ?? Array.Empty<byte>()), Version = 1, Credit = credit,
            Url = "https://example.org/science/" + id,
        };

        [Test]
        public void SnapshotRetainsTheExactCreditAndCoordinatesWhenProducerObjectsChange()
        {
            PovPin pin = Pin();
            Assert.That(PovPhoto.TryCreate(pin, MissionDefinitionTests.Zone().ExtentEnuM, out PovPhoto photo), Is.True);
            pin.Credit = "Changed credit";
            pin.EnuM[0] = 50;
            pin.File = "other.jpg";
            Assert.That(photo.Credit, Is.EqualTo("Martian Map test suite (synthetic image)"));
            Assert.That(photo.EnuM.x, Is.Zero);
            Assert.That(photo.File, Is.EqualTo("a.jpg"));
            Assert.That(photo.CapturedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(photo.Sol, Is.EqualTo(12));
        }

        [Test]
        public void MissingCreditsUnsafeFilesAndInvalidC1MetadataAreRejected()
        {
            foreach (Action<PovPin> corrupt in new Action<PovPin>[]
            {
                pin => pin.Credit = null, pin => pin.Credit = " ", pin => pin.Id = "",
                pin => pin.SourceId = "", pin => pin.MediaType = "image/png", pin => pin.Version = 0,
                pin => pin.Sol = -1, pin => pin.EnuM = null, pin => pin.EnuM = new double[] { 0 },
                pin => pin.EnuM[0] = double.NaN, pin => pin.EnuM[0] = 129,
                pin => pin.Sha256 = "", pin => pin.Sha256 = new string('A', 64),
                pin => pin.Sha256 = new string('a', 64) + "\n",
                pin => pin.File = "../a.jpg", pin => pin.File = "sub/a.jpg", pin => pin.File = "sub\\a.jpg",
                pin => pin.File = "image%2f.jpg", pin => pin.File = "http://example.org/a.jpg",
                pin => pin.TUtc = "2026-02-30T12:00:00Z", pin => pin.TUtc = "2026-11-14T12:00:00+00:00",
            })
            {
                PovPin pin = Pin();
                corrupt(pin);
                Assert.That(PovPhoto.TryCreate(pin, MissionDefinitionTests.Zone().ExtentEnuM, out _), Is.False);
            }
        }

        [TestCase("C:\\cache\\pov", "C:/cache/pov/a.jpg")]
        [TestCase("/tmp/pov", "/tmp/pov/a.jpg")]
        [TestCase("file:///tmp/pov/", "file:///tmp/pov/a.jpg")]
        [TestCase("jar:file:///data/app/base.apk!/assets/pov", "jar:file:///data/app/base.apk!/assets/pov/a.jpg")]
        public void LocalPathsSupportDesktopAndPackagedAndroidFiles(string folder, string expected)
        {
            Assert.That(PovPhoto.TryLocalPath(folder, "a.jpg", out string path), Is.True);
            Assert.That(path, Is.EqualTo(expected));
        }

        [Test]
        public void ImageReadsStayLocalAndOnlyHttpSourceLinksCanOpen()
        {
            Assert.That(PovPhoto.TryLocalPath("https://example.org/pov", "a.jpg", out _), Is.False);
            Assert.That(PovPhoto.TryLocalPath("", "a.jpg", out _), Is.False);
            Assert.That(PovPhoto.TryLocalPath("/tmp", "../a.jpg", out _), Is.False);
            Assert.That(PovPhoto.TrySourceUrl("https://example.org/item", out _), Is.True);
            Assert.That(PovPhoto.TrySourceUrl("http://example.org/item", out _), Is.True);
            foreach (string url in new[] { "javascript:alert(1)", "file:///tmp/image.jpg", "data:text/plain,test", "/relative" })
            {
                Assert.That(PovPhoto.TrySourceUrl(url, out _), Is.False);
            }
        }
    }
}
