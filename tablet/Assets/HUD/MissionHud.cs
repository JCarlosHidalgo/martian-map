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
        private ZoneInfo _zone;
        private EnuPoint _home;
        private bool _ready;

        public MissionDefinition Mission { get; private set; }
        public string Status { get; private set; } = HudStrings.WaitingForMission;

        private void Awake() => BuildView();

        private void OnEnable()
        {
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.RouteSelected += OnRouteSelected;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.RouteSelected -= OnRouteSelected;
        }

        private void Start() => LoadMission(_missionAsset);

        /// <summary>Loads an assigned TextAsset on desktop and Android without filesystem assumptions.</summary>
        public void LoadMission(TextAsset asset)
        {
            _missionAsset = asset;
            BuildView();
            _ready = false;
            _planButton.interactable = false;
            _route.text = HudStrings.NoRoute;
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

            SetStatus(HudStrings.RouteRequested);
            MapEvents.Emit(new RouteRequested(Array.AsReadOnly(stops), _home, Mission.Budget, Mission.Mode, _home, Alternative.Fastest));
        }

        private void OnZoneLoaded(ZoneLoaded message)
        {
            _zone = message.Zone;
            BuildView();
            _route.text = HudStrings.NoRoute;
            ResolveZone();
        }

        private void ResolveZone()
        {
            _ready = false;
            _planButton.interactable = false;
            _zoneLabel.text = _zone == null ? HudStrings.NoZone : HudStrings.ZonePrefix + _zone.ZoneId + (_zone.ZoneId == "synth" ? HudStrings.SyntheticSuffix : string.Empty);
            if (Mission == null || _zone == null)
            {
                SetStatus(Mission == null ? HudStrings.WaitingForMission : HudFormatting.Format(HudStrings.WaitingForZone, Mission.ZoneId));
                return;
            }

            try
            {
                _home = Mission.ResolveHome(_zone);
                _ready = true;
                _planButton.interactable = true;
                SetStatus(HudFormatting.Format(HudStrings.Ready, _home.E, _home.N));
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
            if (!_ready || message.Plans == null || (int)message.Selected < 0 || (int)message.Selected >= message.Plans.Count)
            {
                return;
            }

            Plan plan = message.Plans[(int)message.Selected];
            string alternative = message.Selected == Alternative.Fastest ? HudStrings.Fastest
                : message.Selected == Alternative.Safest ? HudStrings.Safest : HudStrings.Gentlest;
            _route.text = HudFormatting.Format(HudStrings.RouteSummary,
                alternative, HudFormatting.Duration(plan.TotalS.P50), HudFormatting.Duration(plan.TotalS.P10),
                HudFormatting.Duration(plan.TotalS.P90), HudFormatting.Dose(plan.DoseUGy),
                plan.ReturnOk ? HudStrings.Available : HudStrings.Unavailable);
            SetStatus(HudStrings.RouteReceived);
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
            _title = FixedLabel("Mission title", body, HudStrings.WaitingForMission, 32, 85);
            _budget = FixedLabel("Budget", body, string.Empty, 21, 72);
            FixedLabel("Objectives heading", body, HudStrings.Objectives, 19, 30);

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
            _route = FixedLabel("Route", body, HudStrings.NoRoute, 20, 115);
            var buttonObject = new GameObject("Plan route", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(body, false);
            buttonObject.GetComponent<LayoutElement>().preferredHeight = 62;
            buttonObject.GetComponent<Image>().color = new Color(0.77f, 0.32f, 0.13f);
            _planButton = buttonObject.GetComponent<Button>();
            _planButton.targetGraphic = buttonObject.GetComponent<Image>();
            _planButton.interactable = false;
            _planButton.onClick.AddListener(RequestRoute);
            TMP_Text buttonLabel = Label("Label", buttonObject.transform, HudStrings.PlanRoute, 23);
            buttonLabel.alignment = TextAlignmentOptions.Center;
            Stretch(buttonLabel.rectTransform, Vector2.zero, Vector2.zero);
            FixedLabel("Units", body, HudStrings.Units, 17, 40);

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
