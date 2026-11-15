using MartianMap.Routing;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    /// <summary>Compares the three C4 plans and requests selection through the C5 bus.</summary>
    [RequireComponent(typeof(MissionHud))]
    [DisallowMultipleComponent]
    public sealed class RouteAlternativesHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _font;

        private readonly Button[] _options = new Button[3];
        private readonly TMP_Text[] _labels = new TMP_Text[3];
        private readonly Plan[] _plans = new Plan[3];
        private MissionHud _mission;
        private Button _launcher;
        private GameObject _modal;
        private string _message = HudStrings.RouteOptionsEmpty;
        private bool _hasResults;
        private bool _pending;
        private bool _wasReady;
        private bool _editing;

        private void Awake()
        {
            _mission = GetComponent<MissionHud>();
            BuildView();
            Refresh();
        }

        private void OnEnable()
        {
            MapEvents.RouteSelected += OnRouteSelected;
            _mission.RouteRequesting += OnRouteRequesting;
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.AstronautMoved += OnAstronautMoved;
            MapEvents.EditModeChanged += OnEditModeChanged;
        }

        private void OnDisable()
        {
            MapEvents.RouteSelected -= OnRouteSelected;
            _mission.RouteRequesting -= OnRouteRequesting;
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.AstronautMoved -= OnAstronautMoved;
            MapEvents.EditModeChanged -= OnEditModeChanged;
        }

        private void LateUpdate()
        {
            bool ready = _mission.AstronautPosition.HasValue && !_editing;
            if (ready != _wasReady)
            {
                if (!ready)
                {
                    ResetResults(HudStrings.RouteOptionsEmpty);
                }

                Refresh();
                _wasReady = ready;
            }
        }

        public void Open()
        {
            if (_editing)
            {
                return;
            }

            Refresh();
            _modal.SetActive(true);
        }

        public void Close() => _modal.SetActive(false);

        private void OnEditModeChanged(EditModeChanged message)
        {
            _editing = message.On;
            Close();
            Refresh();
        }

        private void OnZoneLoaded(ZoneLoaded message) => ResetResults(HudStrings.RouteOptionsEmpty);
        private void OnAstronautMoved(AstronautMoved message) => ResetResults(HudStrings.RoutePositionChanged);

        private void OnRouteRequesting()
        {
            ResetResults(HudStrings.RouteOptionsPending);
            _pending = true;
            Refresh();
        }

        private void OnRouteSelected(RouteSelected message)
        {
            if (!_mission.AstronautPosition.HasValue)
            {
                return;
            }

            if (message.Plans == null || message.Plans.Count != 3 || (int)message.Selected < 0 || (int)message.Selected > 2)
            {
                ResetResults(HudStrings.RouteOptionsInvalid);
                return;
            }

            for (int index = 0; index < 3; index++)
            {
                _plans[index] = message.Plans[index];
            }

            _hasResults = true;
            _pending = false;
            Refresh();
        }

        private void ResetResults(string message)
        {
            _hasResults = _pending = false;
            _message = message;
            for (int index = 0; index < 3; index++)
            {
                _plans[index] = null;
            }

            Refresh();
        }

        private void Refresh()
        {
            bool ready = _mission.AstronautPosition.HasValue && !_editing;
            _launcher.interactable = ready;
            for (int index = 0; index < 3; index++)
            {
                var alternative = (Alternative)index;
                string suffix = _mission.SelectedAlternative != alternative ? string.Empty
                    : _hasResults ? HudStrings.RouteOptionSelected : _pending ? HudStrings.RouteOptionRequested : string.Empty;
                _labels[index].text = HudFormatting.Format(HudStrings.RouteOption,
                    RoutePresentation.Name(alternative), suffix,
                    _hasResults ? RoutePresentation.Details(_plans[index]) : _message);
                _options[index].interactable = ready;
            }
        }

        private void BuildView()
        {
            var root = new GameObject("Route alternatives Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 35;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var canvas = (RectTransform)root.transform;
            _launcher = Button("Route options", canvas, HudStrings.RouteOptions, Open, 60, out _);
            var launcher = (RectTransform)_launcher.transform;
            launcher.anchorMin = launcher.anchorMax = new Vector2(0.5f, 0);
            launcher.anchoredPosition = new Vector2(0, 110);
            launcher.sizeDelta = new Vector2(330, 60);
            // Hidden at the team's request: the mission panel lists the route options instead.
            _launcher.gameObject.SetActive(false);
            _modal = new GameObject("Route alternatives modal", typeof(RectTransform), typeof(Image));
            _modal.transform.SetParent(canvas, false);
            Stretch((RectTransform)_modal.transform, 0);
            _modal.GetComponent<Image>().color = new Color(0, 0, 0, 0.78f);
            var card = new GameObject("Route alternatives card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            card.transform.SetParent(_modal.transform, false);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900, 790);
            card.GetComponent<Image>().color = new Color(0.045f, 0.065f, 0.085f);
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Label("Route heading", rect, HudStrings.RouteOptionsTitle, 28, 42);
            Label("Route help", rect, HudStrings.RouteOptionsHelp, 21, 60);
            for (int index = 0; index < 3; index++)
            {
                var alternative = (Alternative)index;
                _options[index] = Button("Alternative " + alternative, rect, string.Empty,
                    () => _mission.SelectAlternative(alternative), 155, out _labels[index]);
            }

            Button("Close route options", rect, HudStrings.CloseSettings, Close, 58, out _);
            _modal.SetActive(false);
        }

        private Button Button(string name, Transform parent, string caption, UnityEngine.Events.UnityAction action, float height, out TMP_Text label)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = height;
            root.GetComponent<Image>().color = new Color(0.14f, 0.22f, 0.27f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(action);
            label = Label("Label", root.transform, caption, 21, 0);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Stretch(label.rectTransform, 14);
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
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
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
