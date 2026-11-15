using System;
using System.IO;
using System.Text.RegularExpressions;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    /// <summary>Configuration access for the HUD; Terrain owns persistence and validation.</summary>
    public interface IHudConfiguration
    {
        string HabitatUrl { get; }
        string FilesUrl { get; }
        string Author { get; }
        void Save(string habitatUrl, string filesUrl, string author);
    }

    /// <summary>Settings and first-use author registration over the C5 AppConfig contract.</summary>
    [DisallowMultipleComponent]
    public sealed class SettingsHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _font;

        private IHudConfiguration _configuration;
        private GameObject _modal;
        private TMP_InputField _habitat;
        private TMP_InputField _files;
        private TMP_InputField _author;
        private TMP_Text _status;
        private TMP_Text _authorHelp;
        private Button _close;
        private Button _save;
        private string _savedAuthor = string.Empty;
        private bool _loaded;

        public bool CanEdit => _loaded && IsAuthorValid(_savedAuthor);
        public string RegisteredAuthor => CanEdit ? _savedAuthor : string.Empty;
        public bool IsOpen => _modal != null && _modal.activeSelf;
        public string Status => _status != null ? _status.text : string.Empty;

        private sealed class TerrainConfiguration : IHudConfiguration
        {
            public string HabitatUrl => AppConfig.HabitatUrl;
            public string FilesUrl => AppConfig.FilesUrl;
            public string Author => AppConfig.Author;
            public void Save(string habitatUrl, string filesUrl, string author) =>
                AppConfig.Save(habitatUrl, filesUrl, author);
        }

        private void Awake()
        {
            _configuration ??= new TerrainConfiguration();
            BuildView();
        }

        private void OnEnable() => MapEvents.ConfigChanged += OnConfigChanged;
        private void OnDisable() => MapEvents.ConfigChanged -= OnConfigChanged;

        private void Start()
        {
            ReadConfiguration();
            _modal.SetActive(!CanEdit);
        }

        /// <summary>Allows an isolated configuration source, including test doubles.</summary>
        public void UseConfiguration(IHudConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (_modal != null)
            {
                ReadConfiguration();
            }
        }

        /// <summary>Opens the form with the current saved values, discarding unsaved input.</summary>
        public void Open()
        {
            ReadConfiguration();
            _modal.SetActive(true);
        }

        /// <summary>Closes settings once this tablet has a registered author.</summary>
        public void Close()
        {
            if (CanEdit)
            {
                _modal.SetActive(false);
            }
        }

        /// <summary>Saves through AppConfig; failures keep the form and its input visible.</summary>
        public void Save()
        {
            string author = _savedAuthor.Length > 0 ? _savedAuthor : _author.text;
            if (!IsAuthorValid(author))
            {
                _status.text = HudStrings.AuthorInvalid;
                return;
            }

            try
            {
                _configuration.Save(_habitat.text, _files.text, author);
                ReadConfiguration();
                _status.text = HudStrings.SettingsSaved;
            }
            catch (ArgumentException error)
            {
                _status.text = error.ParamName == "habitatUrl" ? HudStrings.HabitatUrlInvalid
                    : error.ParamName == "filesUrl" ? HudStrings.FilesUrlInvalid
                    : HudStrings.SettingsRejected;
            }
            catch (IOException)
            {
                _status.text = HudStrings.SettingsWriteFailed;
            }
            catch (UnauthorizedAccessException)
            {
                _status.text = HudStrings.SettingsWriteFailed;
            }
        }

        private static bool IsAuthorValid(string author) =>
            author != null && Regex.IsMatch(author, @"\A[a-z0-9-]{2,16}\z");

        private void OnConfigChanged(ConfigChanged message) => ReadConfiguration();

        private void ReadConfiguration()
        {
            try
            {
                _habitat.SetTextWithoutNotify(_configuration.HabitatUrl ?? string.Empty);
                _files.SetTextWithoutNotify(_configuration.FilesUrl ?? string.Empty);
                _savedAuthor = _configuration.Author ?? string.Empty;
                _author.SetTextWithoutNotify(_savedAuthor);
                _author.interactable = _savedAuthor.Length == 0;
                _loaded = true;
                _save.interactable = true;
                _close.interactable = CanEdit;
                _authorHelp.text = _savedAuthor.Length > 0 ? HudStrings.AuthorLocked : HudStrings.AuthorRegistration;
                _status.text = CanEdit ? HudStrings.SettingsReady : HudStrings.AuthorRequired;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is Newtonsoft.Json.JsonException || error is ArgumentException)
            {
                _loaded = false;
                _save.interactable = false;
                _close.interactable = false;
                _status.text = HudStrings.SettingsReadFailed;
            }
        }

        private void BuildView()
        {
            var canvasObject = new GameObject("Settings Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<Canvas>().sortingOrder = 50;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform canvas = (RectTransform)canvasObject.transform;
            Button launcher = Button("Settings", canvas, HudStrings.Settings, Open);
            var launcherRect = (RectTransform)launcher.transform;
            launcherRect.anchorMin = launcherRect.anchorMax = Vector2.one;
            launcherRect.pivot = Vector2.one;
            launcherRect.anchoredPosition = new Vector2(-35, -28);
            launcherRect.sizeDelta = new Vector2(200, 54);

            _modal = new GameObject("Settings modal", typeof(RectTransform), typeof(Image));
            _modal.transform.SetParent(canvas, false);
            Stretch((RectTransform)_modal.transform, 0);
            _modal.GetComponent<Image>().color = new Color(0, 0, 0, 0.78f);
            var card = new GameObject("Settings card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            card.transform.SetParent(_modal.transform, false);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(800, 870);
            card.GetComponent<Image>().color = new Color(0.045f, 0.065f, 0.085f);
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 26, 26);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Label("Title", rect, HudStrings.SettingsTitle, 28, 42);
            Label("Connection help", rect, HudStrings.ConnectionHelp, 21, 68);
            _habitat = Input("Habitat URL", rect, HudStrings.HabitatUrl);
            _files = Input("Files URL", rect, HudStrings.FilesUrl);
            _author = Input("Author", rect, HudStrings.Author);
            _author.characterLimit = 16;
            _authorHelp = Label("Author help", rect, string.Empty, 21, 90);
            _status = Label("Settings status", rect, string.Empty, 21, 75);
            _save = Button("Save settings", rect, HudStrings.SaveSettings, Save);
            _close = Button("Close settings", rect, HudStrings.CloseSettings, Close);
            _modal.SetActive(false);
        }

        private TMP_InputField Input(string name, Transform parent, string caption)
        {
            Label(name + " caption", parent, caption, 21, 28);
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = 58;
            root.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.2f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            Stretch((RectTransform)viewport.transform, 12);
            TMP_Text text = Label("Text", viewport.transform, string.Empty, 23, 0);
            Stretch(text.rectTransform, 0);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var input = root.GetComponent<TMP_InputField>();
            input.textViewport = (RectTransform)viewport.transform;
            input.textComponent = text;
            input.targetGraphic = root.GetComponent<Image>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            return input;
        }

        private Button Button(string name, Transform parent, string caption, UnityEngine.Events.UnityAction action)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = 58;
            root.GetComponent<Image>().color = new Color(0.6f, 0.24f, 0.1f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(action);
            TMP_Text label = Label("Label", root.transform, caption, 23, 0);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform, 0);
            return button;
        }

        private TMP_Text Label(string name, Transform parent, string caption, int size, float height)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = new Color(0.91f, 0.94f, 0.96f);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.text = caption;
            if (height > 0)
            {
                root.AddComponent<LayoutElement>().preferredHeight = height;
            }

            return text;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
