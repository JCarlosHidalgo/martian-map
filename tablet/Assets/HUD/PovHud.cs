using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MartianMap.Terrain;
using UnityEngine;

namespace MartianMap.HUD
{
    public interface IPovImageReader
    {
        Task<byte[]> ReadAsync(string path);
    }

    /// <summary>C1 POV pins and local image viewing. Never fetches images from remote science URLs.</summary>
    [DisallowMultipleComponent]
    public sealed partial class PovHud : MonoBehaviour
    {
        private readonly List<PovPhoto> _photos = new List<PovPhoto>();
        private IPovImageReader _reader;
        private string _folder;
        private PovPhoto _selected;
        private Texture2D _texture;
        private int _loadVersion;
        private bool _editing;

        public int PhotoCount => _photos.Count;
        public string Status { get; private set; } = HudStrings.PovEmpty;
        public string SelectedCredit => _selected?.Credit ?? string.Empty;
        public Texture2D DisplayedTexture => _texture;
        public bool IsOpen => _modal != null && _modal.activeSelf;

        private sealed class LocalImageReader : IPovImageReader
        {
            public Task<byte[]> ReadAsync(string path) => LocalFiles.ReadBytesAsync(path);
        }

        private void Awake()
        {
            _reader ??= new LocalImageReader();
            BuildView();
            UpdateLauncher();
        }

        private void OnEnable()
        {
            _editing = GetComponent<EditHud>()?.IsEditing ?? false;
            UpdateLauncher();
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.EditModeChanged += OnEditModeChanged;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.EditModeChanged -= OnEditModeChanged;
            Close();
            _photos.Clear();
            _folder = string.Empty;
            ClearPins();
            Status = HudStrings.PovEmpty;
        }

        public void UseImageReader(IPovImageReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            if (_modal != null)
            {
                Close();
            }
        }

        private void OnZoneLoaded(ZoneLoaded message)
        {
            Close();
            _photos.Clear();
            _folder = message.PovFolder;
            var candidates = new List<PovPhoto>();
            int skipped = 0;
            foreach (PovPin pin in message.Zone.Pov ?? new List<PovPin>())
            {
                if (PovPhoto.TryCreate(pin, message.Zone.ExtentEnuM, out PovPhoto photo))
                {
                    candidates.Add(photo);
                }
                else
                {
                    skipped++;
                }
            }

            foreach (var group in candidates.GroupBy(photo => photo.Id, StringComparer.Ordinal))
            {
                if (group.Count() == 1)
                {
                    _photos.Add(group.Single());
                }
                else
                {
                    skipped += group.Count();
                }
            }

            _photos.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            SetStatus(_photos.Count > 0 ? HudStrings.PovReady : skipped > 0 ? HudStrings.PovInvalidMetadata : HudStrings.PovEmpty);
            RebuildPins();
            UpdateLauncher();
            if (skipped > 0)
            {
                Debug.LogWarning("[HUD] POV entries skipped count=" + skipped);
            }
        }

        private void OnEditModeChanged(EditModeChanged message)
        {
            _editing = message.On;
            if (_editing)
            {
                Close();
            }

            UpdateLauncher();
        }

        public async Task<bool> OpenPhotoAsync(string id)
        {
            PovPhoto photo = _photos.FirstOrDefault(item => item.Id == id);
            if (_editing || photo == null)
            {
                return false;
            }

            int version = ++_loadVersion;
            ReleaseImage();
            _selected = photo;
            _modal.SetActive(true);
            ShowMetadata(photo);
            SetStatus(HudStrings.PovLoading);
            if (!PovPhoto.TryLocalPath(_folder, photo.File, out string path))
            {
                SetStatus(HudStrings.PovFileUnavailable);
                return false;
            }

            Texture2D decoded = null;
            try
            {
                byte[] bytes = await _reader.ReadAsync(path);
                if (!IsCurrent(version))
                {
                    return false;
                }

                if (bytes == null || bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 ||
                    bytes[bytes.Length - 2] != 0xff || bytes[bytes.Length - 1] != 0xd9 || !Sha256.Matches(bytes, photo.Sha256))
                {
                    SetStatus(HudStrings.PovIntegrityFailed);
                    return false;
                }

                decoded = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!ImageConversion.LoadImage(decoded, bytes, true))
                {
                    SetStatus(HudStrings.PovImageInvalid);
                    return false;
                }

                if (decoded.width > 1024 || decoded.height > 1024)
                {
                    SetStatus(HudStrings.PovImageSize);
                    return false;
                }

                _texture = decoded;
                decoded = null;
                _image.texture = _texture;
                _image.gameObject.SetActive(true);
                _imageAspect.aspectRatio = (float)_texture.width / _texture.height;
                SetStatus(HudStrings.PovLoaded);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is UnityException || error is OperationCanceledException)
            {
                if (IsCurrent(version))
                {
                    SetStatus(HudStrings.PovFileUnavailable);
                    Debug.LogWarning("[HUD] POV image unavailable id=" + photo.Id + " error=" + error.GetType().Name);
                }

                return false;
            }
            finally
            {
                if (decoded != null)
                {
                    Destroy(decoded);
                }
            }
        }

        public void Close()
        {
            _loadVersion++;
            _selected = null;
            ReleaseImage();
            if (_modal != null)
            {
                _modal.SetActive(false);
            }
        }

        private bool IsCurrent(int version) => this != null && isActiveAndEnabled &&
            version == _loadVersion && _modal.activeSelf;

        private void ReleaseImage()
        {
            if (_image != null)
            {
                _image.texture = null;
                _image.gameObject.SetActive(false);
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        private void Browse(int direction)
        {
            if (_photos.Count == 0)
            {
                return;
            }

            int index = _selected == null ? 0 : _photos.IndexOf(_selected);
            index = (index + direction + _photos.Count) % _photos.Count;
            _ = OpenPhotoAsync(_photos[index].Id);
        }

        private void OpenSource()
        {
            if (_selected != null && PovPhoto.TrySourceUrl(_selected.SourceUrl, out Uri uri))
            {
                Application.OpenURL(uri.AbsoluteUri);
            }
        }

        private void SetStatus(string message)
        {
            Status = message;
            _status.text = message;
        }
    }
}
