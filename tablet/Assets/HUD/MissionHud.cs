using System;
using MartianMap.Routing;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    /// <summary>C6 mission panel and the first HUD-to-Terrain route request, using the C5 bus.</summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class MissionHud : MonoBehaviour
    {
        [SerializeField] private TextAsset _missionAsset;
        [SerializeField] private TMP_FontAsset _font;

        private TMP_Text _title;
        private TMP_Text _budget;
        private TMP_Text _status;
        private TMP_Text _zoneLabel;
        private TMP_Text _route;
        private RectTransform _stopList;
        private Button _planButton;
        private readonly Button[] _optionButtons = new Button[3];
        private readonly TMP_Text[] _optionLabels = new TMP_Text[3];
        private Button _allButton;
        private int? _focused;
        private static readonly Color OptionColor = new Color(0.11f, 0.15f, 0.19f, 1f);
        private static readonly Color OptionFocusedColor = new Color(0.24f, 0.32f, 0.40f, 1f);
        private ZoneInfo _zone;
        private EnuPoint _home;
        private EnuPoint _start;
        private bool _ready;

        public MissionDefinition Mission { get; private set; }
        public EnuPoint? AstronautPosition => _ready ? _start : (EnuPoint?)null;
        public Alternative SelectedAlternative { get; private set; } = Alternative.Fastest;
        public event Action RouteRequesting;
        public string Status { get; private set; } = HudStrings.WaitingForMission;

        private void Awake() => BuildView();

        private void OnEnable()
        {
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.RouteSelected += OnRouteSelected;
            RoutePointsTool.PointsChanged += UpdatePlanButton;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.RouteSelected -= OnRouteSelected;
            RoutePointsTool.PointsChanged -= UpdatePlanButton;
        }

        private void Start() => LoadMission(_missionAsset);

        /// <summary>Loads an assigned TextAsset on desktop and Android without filesystem assumptions.</summary>
        public void LoadMission(TextAsset asset)
        {
            _missionAsset = asset;
            BuildView();
            _ready = false;
            SelectedAlternative = Alternative.Fastest;
            UpdatePlanButton();
            _route.text = HudStrings.RouteHelp;
            try
            {
                if (asset == null)
                {
                    throw new ArgumentException(HudStrings.MissionDataUnavailable);
                }

                Mission = MissionDefinition.Parse(asset.text);
                _title.text = Mission.Name;
                _budget.text = HudFormatting.Format(HudStrings.MissionBudget,
                    Mission.Stops.Count, Mission.Mode == StopMode.Tsp ? HudStrings.Tsp : HudStrings.Op,
                    HudFormatting.Duration(Mission.Budget.TS), HudFormatting.Dose(Mission.Budget.DoseUGy));
                DrawStops();
                ResolveZone();
            }
            catch (ArgumentException error)
            {
                Mission = null;
                _title.text = HudStrings.MissionUnavailable;
                _budget.text = string.Empty;
                DrawStops();
                SetStatus(HudStrings.MissionLoadFailed);
                Debug.LogWarning("[HUD] mission rejected detail=" + error.Message);
            }
        }

        /// <summary>Sends typed C4 inputs. Terrain owns routing execution and its error handling.</summary>
        public void RequestRoute()
        {
            if (!_ready || Mission == null)
            {
                return;
            }

            var stops = new Stop[Mission.Stops.Count];
            for (int index = 0; index < stops.Length; index++)
            {
                stops[index] = Mission.Stops[index].Value;
            }

            _route.text = HudStrings.RouteOptionsPending;
            SetStatus(HudStrings.RouteRequested);
            RouteRequesting?.Invoke();
            MapEvents.Emit(new RouteRequested(Array.AsReadOnly(stops), _home, Mission.Budget, Mission.Mode, _start, SelectedAlternative));
        }

        /// <summary>Requests an alternative through Terrain using the current astronaut position.</summary>
        public bool SelectAlternative(Alternative alternative)
        {
            if (!_ready || (int)alternative < 0 || (int)alternative > 2)
            {
                return false;
            }

            SelectedAlternative = alternative;
            RequestRoute();
            return true;
        }

        /// <summary>Moves the simulated astronaut inside the active zone, retaining the return base.</summary>
        public bool MoveAstronaut(Vector2 enuM)
        {
            if (!_ready || float.IsNaN(enuM.x) || float.IsInfinity(enuM.x) ||
                float.IsNaN(enuM.y) || float.IsInfinity(enuM.y))
            {
                return false;
            }

            double[] bounds = _zone.ExtentEnuM;
            if (enuM.x < bounds[0] || enuM.y < bounds[1] || enuM.x >= bounds[2] || enuM.y >= bounds[3])
            {
                return false;
            }

            _start = new EnuPoint(enuM.x, enuM.y);
            _route.text = HudStrings.RoutePositionChanged;
            SetStatus(HudStrings.AstronautMoved);
            MapEvents.Emit(new AstronautMoved(enuM));
            return true;
        }

        private void OnZoneLoaded(ZoneLoaded message)
        {
            SelectedAlternative = Alternative.Fastest;
            _zone = message.Zone;
            BuildView();
            _route.text = HudStrings.RouteHelp;
            HideOptions();
            ResolveZone();
        }

        private void ResolveZone()
        {
            _ready = false;
            UpdatePlanButton();
            _zoneLabel.text = _zone == null ? HudStrings.NoZone : HudStrings.ZonePrefix + _zone.ZoneId + (_zone.ZoneId == "synth" ? HudStrings.SyntheticSuffix : string.Empty);
            if (Mission == null || _zone == null)
            {
                SetStatus(Mission == null ? HudStrings.WaitingForMission : HudFormatting.Format(HudStrings.WaitingForZone, Mission.ZoneId));
                return;
            }

            try
            {
                _home = Mission.ResolveHome(_zone);
                _start = _home;
                _ready = true;
                UpdatePlanButton();
                SetStatus(HudFormatting.Format(HudStrings.Ready, _home.E, _home.N));
                MapEvents.Emit(new AstronautMoved(new Vector2((float)_start.E, (float)_start.N)));
            }
            catch (ArgumentException error)
            {
                SetStatus(_zone.ZoneId != Mission.ZoneId
                    ? HudFormatting.Format(HudStrings.ZoneMismatch, Mission.ZoneId, _zone.ZoneId)
                    : HudStrings.InvalidMissionPoints);
                Debug.LogWarning("[HUD] mission zone rejected detail=" + error.Message);
            }
        }

        private void OnRouteSelected(RouteSelected message)
        {
            // The panel shows only the three options (colour, distance, time) of every route,
            // two-point routes included, whatever zone the mission targets.
            ShowOptions(message);
            if (!_ready || !RoutePresentation.ValidOptions(message))
            {
                return;
            }

            SelectedAlternative = message.Selected;
            SetStatus(HudStrings.RouteReceived);
        }

        /// <summary>PLAN ROUTE: the A–B points if both are on the map, otherwise the mission.</summary>
        private void OnPlanClicked()
        {
            if (RoutePointsTool.HasBothPoints)
            {
                _route.text = HudStrings.RoutePlanning;
                RoutePointsTool.PlanPoints();
                return;
            }

            RequestRoute();
        }

        private void UpdatePlanButton()
        {
            if (_planButton != null)
            {
                _planButton.interactable = _ready || RoutePointsTool.HasBothPoints;
            }
        }

        private void ShowOptions(RouteSelected message)
        {
            if (!RoutePresentation.ValidOptions(message))
            {
                _route.text = HudStrings.RouteOptionsInvalid;
                HideOptions();
                return;
            }

            _route.text = RoutePresentation.AnyFound(message) ? HudStrings.RouteOptionsHeader : HudStrings.RouteAllUnreachable;
            for (var index = 0; index < 3; index++)
            {
                _optionLabels[index].text = RoutePresentation.OptionLine(message, index);
                _optionButtons[index].gameObject.SetActive(true);
            }

            _allButton.gameObject.SetActive(true);
            Focus(null, emit: false);
        }

        private void HideOptions()
        {
            foreach (var button in _optionButtons)
            {
                if (button != null)
                {
                    button.gameObject.SetActive(false);
                }
            }

            if (_allButton != null)
            {
                _allButton.gameObject.SetActive(false);
            }

            _focused = null;
        }

        /// <summary>Shows one alternative alone on the map (index) or all three (null).</summary>
        private void Focus(int? index, bool emit)
        {
            _focused = index;
            for (var i = 0; i < 3; i++)
            {
                _optionButtons[i].GetComponent<Image>().color = _focused == i ? OptionFocusedColor : OptionColor;
            }

            _allButton.GetComponent<Image>().color = _focused == null ? OptionFocusedColor : OptionColor;
            if (emit)
            {
                MapEvents.Emit(index == null ? RouteFocused.ShowAll() : RouteFocused.Only((Alternative)index.Value));
            }
        }

        private Button OptionButton(string name, Transform parent, out TMP_Text label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<LayoutElement>().preferredHeight = 54;
            buttonObject.GetComponent<Image>().color = OptionColor;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            label = Label("Label", buttonObject.transform, string.Empty, 20);
            label.richText = true;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Stretch(label.rectTransform, new Vector2(14, 4), new Vector2(-14, -4));
            buttonObject.SetActive(false);
            return button;
        }

        private void SetStatus(string value)
        {
            Status = value;
            if (_status != null)
            {
                _status.text = value;
            }
        }

        private void DrawStops()
        {
            foreach (Transform child in _stopList)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            if (Mission == null)
            {
                return;
            }

            foreach (MissionStop stop in Mission.Stops)
            {
                TMP_Text text = Label("Stop " + stop.Value.Id, _stopList, HudFormatting.Format(HudStrings.MissionStop,
                    stop.Value.Id, stop.Name, stop.Objective, stop.Value.EnuM.E, stop.Value.EnuM.N,
                    stop.Value.ScienceValue, HudFormatting.Duration(stop.Value.DwellS)), 21);
                text.gameObject.AddComponent<LayoutElement>().preferredHeight = 155;
                text.margin = new Vector4(12, 10, 12, 10);
            }
        }

        private void BuildView()
        {
            if (_title != null)
            {
                return;
            }

            var canvasObject = new GameObject("Mission Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<Canvas>().sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var canvas = (RectTransform)canvasObject.transform;

            RectTransform header = Panel("Header", canvas, new Vector2(0, 1), Vector2.one, new Vector2(20, -90), new Vector2(-20, -20));
            TMP_Text heading = Label("Brand", header, HudStrings.Brand, 28);
            Stretch(heading.rectTransform, new Vector2(22, 8), new Vector2(-22, -8));

            RectTransform body = Panel("Mission panel", canvas, Vector2.zero, new Vector2(0, 1), new Vector2(20, 20), new Vector2(560, -110));
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 20, 20);
            layout.spacing = 14;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            _zoneLabel = FixedLabel("Zone", body, HudStrings.NoZone, 19, 32);
            _zoneLabel.color = new Color(1, 0.67f, 0.39f);
            // Hidden at the team's request: the "ZONE / <id>" line is not shown. It is still updated,
            // so other code keeps working; the vertical layout skips inactive children.
            _zoneLabel.gameObject.SetActive(false);
            _title = FixedLabel("Mission title", body, HudStrings.WaitingForMission, 32, 85);
            _budget = FixedLabel("Budget", body, string.Empty, 21, 72);
            TMP_Text objectivesHeading = FixedLabel("Objectives heading", body, HudStrings.Objectives, 19, 30);

            var scrollObject = new GameObject("Objectives", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(body, false);
            scrollObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            viewportObject.GetComponent<Image>().color = Color.clear;
            var viewport = (RectTransform)viewportObject.transform;
            Stretch(viewport, Vector2.zero, Vector2.zero);
            _stopList = new GameObject("Stops", typeof(RectTransform)).GetComponent<RectTransform>();
            _stopList.SetParent(viewport, false);
            _stopList.anchorMin = new Vector2(0, 1);
            _stopList.anchorMax = Vector2.one;
            _stopList.pivot = new Vector2(0.5f, 1);
            _stopList.sizeDelta = Vector2.zero;
            var stopsLayout = _stopList.gameObject.AddComponent<VerticalLayoutGroup>();
            stopsLayout.childControlHeight = true;
            stopsLayout.childControlWidth = true;
            stopsLayout.childForceExpandHeight = false;
            stopsLayout.spacing = 10;
            _stopList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _stopList;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _status = FixedLabel("Status", body, Status, 20, 64);
            _route = FixedLabel("Route", body, HudStrings.RouteHelp, 20, 60);
            _route.richText = true;

            // At the team's request the panel shows only the route options and PLAN ROUTE: the mission
            // details stay built (and updated) but hidden; the layout skips inactive children.
            _title.gameObject.SetActive(false);
            _budget.gameObject.SetActive(false);
            objectivesHeading.gameObject.SetActive(false);
            scrollObject.SetActive(false);
            _status.gameObject.SetActive(false);

            for (var index = 0; index < 3; index++)
            {
                var alternative = index;
                _optionButtons[index] = OptionButton("Route option " + index, body, out _optionLabels[index]);
                _optionButtons[index].onClick.AddListener(() => Focus(alternative, emit: true));
            }

            _allButton = OptionButton("Route option all", body, out TMP_Text allLabel);
            allLabel.text = HudStrings.RouteShowAll;
            allLabel.alignment = TextAlignmentOptions.Center;
            _allButton.onClick.AddListener(() => Focus(null, emit: true));

            // Push PLAN ROUTE to the bottom of the panel.
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(body, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1;
            var buttonObject = new GameObject("Plan route", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(body, false);
            buttonObject.GetComponent<LayoutElement>().preferredHeight = 62;
            buttonObject.GetComponent<Image>().color = new Color(0.77f, 0.32f, 0.13f);
            _planButton = buttonObject.GetComponent<Button>();
            _planButton.targetGraphic = buttonObject.GetComponent<Image>();
            _planButton.interactable = false;
            _planButton.onClick.AddListener(OnPlanClicked);
            TMP_Text buttonLabel = Label("Label", buttonObject.transform, HudStrings.PlanRoute, 23);
            buttonLabel.alignment = TextAlignmentOptions.Center;
            Stretch(buttonLabel.rectTransform, Vector2.zero, Vector2.zero);
            FixedLabel("Units", body, HudStrings.Units, 17, 40).gameObject.SetActive(false);
            CollapsiblePanel.Attach(body, leftSide: true, _font); // last child: collapsible at the team's request

            if (EventSystem.current == null)
            {
                var events = new GameObject("HUD EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = new Color(0.045f, 0.065f, 0.085f, 0.96f);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private TMP_Text FixedLabel(string name, Transform parent, string value, int size, float height)
        {
            TMP_Text text = Label(name, parent, value, size);
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private TMP_Text Label(string name, Transform parent, string value, int size)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(parent, false);
            var text = label.GetComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = new Color(0.91f, 0.94f, 0.96f);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }
    }
}
