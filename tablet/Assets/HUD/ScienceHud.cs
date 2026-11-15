using System;
using System.Collections.Generic;
using System.Linq;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    /// <summary>Layer controls and C6 radiation presentation. All data arrive through C5 events.</summary>
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(MissionHud))]
    [DisallowMultipleComponent]
    public sealed class ScienceHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _font;
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        private readonly Dictionary<string, TMP_Text> _buttonLabels = new Dictionary<string, TMP_Text>();
        private readonly Dictionary<string, bool> _selected = new Dictionary<string, bool>
        {
            [LayerIds.Topo] = true,
            [LayerIds.Ortho] = false,
            [LayerIds.Thermal] = false,
            [LayerIds.ThermalIr] = false,
        };

        private ZoneInfo _zone;
        private ConditionsInfo _conditions;
        private TMP_Text _legend;
        private TMP_Text _basis;
        private TMP_Text _rate;
        private TMP_Text _flags;
        private TMP_Text _source;
        private TMP_Text _modifiers;
        private TMP_Text _forbush;
        private TMP_Text _derivation;
        private TMP_Text _synthetic;
        private Button _derivationButton;
        private GameObject _dashedOutline;
        private bool _showDerivation;
        private float _nextRefresh;

        public RadiationReading Reading { get; private set; }
        public string LayerLegend => _legend == null ? HudStrings.NoLayerData : _legend.text;

        private void Awake() => BuildView();

        private void OnEnable()
        {
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.ConditionsChanged += OnConditionsChanged;
            MapEvents.LayerToggled += OnLayerToggled;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.ConditionsChanged -= OnConditionsChanged;
            MapEvents.LayerToggled -= OnLayerToggled;
        }

        private void Update()
        {
            if (_conditions != null && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 1;
                RefreshRadiation(DateTime.UtcNow);
            }
        }

        public void ToggleLayer(string layerId)
        {
            if (!_selected.ContainsKey(layerId))
            {
                throw new ArgumentException("Unsupported HUD layer.", nameof(layerId));
            }

            if (Available(layerId))
            {
                MapEvents.Emit(new LayerToggled(layerId, !_selected[layerId]));
            }
        }

        public void RefreshRadiation(DateTime nowUtc)
        {
            BuildView();
            try
            {
                Reading = _conditions == null ? null : RadiationReading.Create(_conditions, nowUtc);
            }
            catch (ArgumentException error)
            {
                _conditions = null;
                Reading = null;
                Debug.LogWarning("[HUD] radiation conditions rejected detail=" + error.Message);
            }

            _basis.text = Reading?.BasisText ?? HudStrings.NoData;
            _rate.text = Reading?.RateText ?? HudStrings.RadiationUnavailable;
            _source.text = Reading?.SourceText ?? string.Empty;
            _modifiers.text = Reading?.ModifiersText ?? string.Empty;
            _flags.text = Reading == null ? string.Empty : string.Join(" · ",
                new[] { Reading.IsScenario ? HudStrings.Scenario : null, Reading.IsPending ? HudStrings.Pending : null }.Where(value => value != null));
            _flags.gameObject.SetActive(_flags.text.Length > 0);
            _forbush.gameObject.SetActive(Reading?.HasForbush == true);
            _dashedOutline.SetActive(Reading?.IsModelled == true);
            _derivationButton.gameObject.SetActive(Reading?.IsModelled == true);
            _derivationButton.GetComponentInChildren<TMP_Text>(true).text = _showDerivation ? HudStrings.HideDerivation : HudStrings.ShowDerivation;
            _derivation.text = Reading?.DerivationText ?? string.Empty;
            _derivation.gameObject.SetActive(Reading?.IsModelled == true && _showDerivation);
            _synthetic.gameObject.SetActive(_conditions != null &&
                (_zone?.ZoneId == "synth" || _conditions.SourceId.StartsWith("synthetic-", StringComparison.Ordinal)));
        }

        private void OnZoneLoaded(ZoneLoaded message)
        {
            if (_zone?.ZoneId != message.Zone?.ZoneId)
            {
                _conditions = null;
                _showDerivation = false;
            }

            _zone = message.Zone;
            BuildView();
            foreach (string layer in _selected.Keys.ToArray())
            {
                if (_selected[layer] && !Available(layer))
                {
                    MapEvents.Emit(new LayerToggled(layer, false));
                }
            }

            UpdateLayers();
            RefreshRadiation(DateTime.UtcNow);
        }

        private void OnConditionsChanged(ConditionsChanged message)
        {
            if (_zone == null || message.ZoneId != _zone.ZoneId)
            {
                return;
            }

            _conditions = message.Conditions;
            _showDerivation = false;
            RefreshRadiation(DateTime.UtcNow);
        }

        private void OnLayerToggled(LayerToggled message)
        {
            if (!_selected.ContainsKey(message.LayerId))
            {
                return;
            }

            if (message.On && message.LayerId != LayerIds.Topo)
            {
                _selected[LayerIds.Ortho] = false;
                _selected[LayerIds.Thermal] = false;
                _selected[LayerIds.ThermalIr] = false;
            }

            _selected[message.LayerId] = message.On;
            UpdateLayers();
        }

        private bool Available(string layer) => _zone != null && (layer == LayerIds.Topo
            ? _zone.Tiles != null && _zone.Tiles.Count > 0
            : _zone.Layers != null && _zone.Layers.Any(value => value != null && value.Id == layer));

        private void UpdateLayers()
        {
            if (_legend == null)
            {
                return;
            }

            foreach (var pair in _buttons)
            {
                bool available = Available(pair.Key);
                pair.Value.interactable = available;
                _buttonLabels[pair.Key].text = HudFormatting.Format(!available ? HudStrings.LayerMissing
                    : _selected[pair.Key] ? HudStrings.LayerOn : HudStrings.LayerOff, LayerName(pair.Key));
                pair.Value.GetComponent<Image>().color = _selected[pair.Key] && available
                    ? new Color(0.65f, 0.26f, 0.1f) : new Color(0.12f, 0.17f, 0.21f);
            }

            if (_zone == null)
            {
                _legend.text = HudStrings.NoLayerData;
                return;
            }

            string active = new[] { LayerIds.Ortho, LayerIds.Thermal, LayerIds.ThermalIr }
                .FirstOrDefault(layer => _selected[layer] && Available(layer));
            var parts = new List<string>();
            if (active != null)
            {
                ZoneLayerInfo layer = _zone.Layers.First(value => value != null && value.Id == active);
                parts.Add(LayerName(active));
                parts.Add(layer.Qualitative ? HudStrings.QualitativeLegend
                    : active == LayerIds.Thermal ? HudStrings.ThermalLegend
                    : active == LayerIds.Ortho ? HudStrings.OpticalLegend : HudStrings.InfraredLegend);
                if (!layer.Qualitative && !string.IsNullOrEmpty(layer.Unit))
                {
                    parts.Add(HudFormatting.Format(HudStrings.LayerUnits, layer.Unit));
                }
            }

            if (_selected[LayerIds.Topo] && Available(LayerIds.Topo))
            {
                parts.Add(HudStrings.TopographyLegend);
            }

            _legend.text = parts.Count == 0 ? HudStrings.LayersOff : string.Join("\n", parts);
        }

        private void BuildView()
        {
            if (_legend != null)
            {
                return;
            }

            Canvas canvas = GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                return;
            }

            RectTransform panel = Rect("Science panel", canvas.transform);
            panel.anchorMin = new Vector2(1, 0);
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(-520, 20);
            panel.offsetMax = new Vector2(-20, -110);
            panel.gameObject.AddComponent<Image>().color = new Color(0.045f, 0.065f, 0.085f, 0.96f);
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = Rect("Viewport", panel);
            Stretch(viewport, new Vector2(12, 12), new Vector2(-12, -12));
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect("Science content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            Vertical(content, 12, 12);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            Label("Layers heading", content, HudStrings.LayersHeading, 25);
            RectTransform row = Row(content);
            LayerButton(row, LayerIds.Topo);
            LayerButton(row, LayerIds.Ortho);
            row = Row(content);
            LayerButton(row, LayerIds.Thermal);
            LayerButton(row, LayerIds.ThermalIr);
            _legend = Label("Layer legend", content, HudStrings.NoLayerData, 19);
            Label("Radiation heading", content, HudStrings.RadiationHeading, 25);
            RectTransform card = Rect("Radiation card", content);
            card.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.1f, 0.13f);
            Vertical(card, 14, 10);
            _basis = Label("Basis", card, HudStrings.NoData, 24);
            _basis.color = new Color(1, 0.68f, 0.4f);
            _flags = Label("Scenario flags", card, string.Empty, 22);
            _flags.gameObject.SetActive(false);
            Label("Rate heading", card, HudStrings.SourceDoseRate, 17);
            _rate = Label("Dose rate", card, HudStrings.RadiationUnavailable, 24);
            _source = Label("Radiation source", card, string.Empty, 17);
            _modifiers = Label("Active modifiers", card, string.Empty, 19);
            _forbush = Label("Forbush rule", card, HudStrings.ForbushRule, 17);
            _forbush.gameObject.SetActive(false);
            _derivationButton = Button("Derivation", card, HudStrings.ShowDerivation, () =>
            {
                _showDerivation = !_showDerivation;
                _derivationButton.GetComponentInChildren<TMP_Text>().text = _showDerivation ? HudStrings.HideDerivation : HudStrings.ShowDerivation;
                _derivation.gameObject.SetActive(_showDerivation);
            });
            _derivationButton.gameObject.SetActive(false);
            _derivation = Label("Derivation text", card, string.Empty, 18);
            _derivation.gameObject.SetActive(false);
            _synthetic = Label("Synthetic warning", card, HudStrings.SyntheticConditions, 17);
            _synthetic.gameObject.SetActive(false);
            _dashedOutline = DashedOutline(card);
            _dashedOutline.SetActive(false);
            UpdateLayers();
        }

        private void LayerButton(Transform row, string layer)
        {
            Button button = Button("Layer " + layer, row, LayerName(layer), () => ToggleLayer(layer));
            _buttons.Add(layer, button);
            _buttonLabels.Add(layer, button.GetComponentInChildren<TMP_Text>());
        }

        private Button Button(string name, Transform parent, string title, UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.17f, 0.21f);
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = 54;
            size.preferredHeight = 54;
            size.flexibleWidth = 1;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            TMP_Text label = Label("Label", rect, title, 18);
            label.GetComponent<LayoutElement>().ignoreLayout = true;
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform, new Vector2(5, 3), new Vector2(-5, -3));
            return button;
        }

        private TMP_Text Label(string name, Transform parent, string text, int size)
        {
            RectTransform rect = Rect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.color = new Color(0.91f, 0.94f, 0.96f);
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            label.text = text;
            rect.gameObject.AddComponent<LayoutElement>().minHeight = size + 6;
            return label;
        }

        private static RectTransform Row(Transform parent)
        {
            RectTransform row = Rect("Layer row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        private static void Vertical(RectTransform rect, int padding, int spacing)
        {
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = spacing;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)) { layer = 5 }.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        private static string LayerName(string layer) => layer == LayerIds.Topo ? HudStrings.Topography
            : layer == LayerIds.Ortho ? HudStrings.Optical : layer == LayerIds.Thermal ? HudStrings.Thermal : HudStrings.Infrared;

        private static GameObject DashedOutline(RectTransform parent)
        {
            RectTransform outline = Rect("Modelled dashed outline", parent);
            Stretch(outline, Vector2.zero, Vector2.zero);
            outline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            for (int side = 0; side < 4; side++)
            {
                for (int index = 0; index < 12; index++)
                {
                    RectTransform dash = Rect("Dash", outline);
                    bool horizontal = side < 2;
                    float edge = side % 2;
                    dash.anchorMin = horizontal ? new Vector2(index / 12f, edge) : new Vector2(edge, index / 12f);
                    dash.anchorMax = horizontal ? new Vector2((index + 0.55f) / 12f, edge) : new Vector2(edge, (index + 0.55f) / 12f);
                    dash.offsetMin = new Vector2(2, 2);
                    dash.offsetMax = horizontal ? new Vector2(-2, 4) : new Vector2(4, -2);
                    Image image = dash.gameObject.AddComponent<Image>();
                    image.color = new Color(1, 0.68f, 0.4f);
                    image.raycastTarget = false;
                }
            }

            return outline.gameObject;
        }
    }
}
