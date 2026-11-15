using System.Collections.Generic;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    public sealed partial class PovHud
    {
        [SerializeField] private TMP_FontAsset _font;

        private sealed class PinView
        {
            public PovPhoto Photo;
            public RectTransform Rect;
        }

        private readonly List<PinView> _pins = new List<PinView>();
        private RectTransform _mapCanvas;
        private GameObject _modal;
        private Button _launcher;
        private TMP_Text _launcherLabel;
        private TMP_Text _title;
        private TMP_Text _status;
        private TMP_Text _provenance;
        private RawImage _image;
        private AspectRatioFitter _imageAspect;
        private ScrollRect _creditScroll;
        private Button _previous;
        private Button _next;
        private Button _source;

        private void LateUpdate()
        {
            Camera camera = MapQuery.Camera;
            foreach (PinView pin in _pins)
            {
                if (_editing || camera == null)
                {
                    pin.Rect.gameObject.SetActive(false);
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(ZoneProjection.ToScene(pin.Photo.EnuM, MapQuery.HeightAt(pin.Photo.EnuM)));
                bool visible = screen.z > 0 && camera.pixelRect.Contains(screen);
                pin.Rect.gameObject.SetActive(visible);
                Canvas canvas = _mapCanvas.GetComponent<Canvas>();
                Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapCanvas, screen, uiCamera, out Vector2 point))
                {
                    pin.Rect.anchoredPosition = point;
                }
            }
        }

        private void UpdateLauncher()
        {
            _launcher.gameObject.SetActive(!_editing);
            _launcher.interactable = _photos.Count > 0;
            _launcherLabel.text = HudFormatting.Format(HudStrings.PovGallery, _photos.Count);
        }

        private void RebuildPins()
        {
            ClearPins();
            foreach (PovPhoto photo in _photos)
            {
                PovPhoto item = photo;
                Button button = PhotoButton("POV pin " + item.Id, _mapCanvas,
                    HudFormatting.Format(HudStrings.PovPin, item.Id, item.Credit), () => _ = OpenPhotoAsync(item.Id), out TMP_Text label);
                label.fontSize = 16;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                float height = Mathf.Max(64, label.GetPreferredValues(label.text, 240, 0).y + 20);
                var rect = (RectTransform)button.transform;
                rect.pivot = new Vector2(0.5f, 0);
                rect.sizeDelta = new Vector2(260, height);
                _pins.Add(new PinView { Photo = item, Rect = rect });
                rect.gameObject.SetActive(false);
            }
        }

        private void ClearPins()
        {
            foreach (PinView pin in _pins)
            {
                if (pin.Rect != null)
                {
                    pin.Rect.gameObject.SetActive(false);
                    Destroy(pin.Rect.gameObject);
                }
            }

            _pins.Clear();
        }

        private void ShowMetadata(PovPhoto photo)
        {
            _title.text = HudFormatting.Format(HudStrings.PovTitle, photo.Id);
            _provenance.text = HudFormatting.Format(HudStrings.PovProvenance, photo.Credit, photo.Id, photo.SourceId,
                photo.CapturedUtc.ToString(HudStrings.Timestamp, System.Globalization.CultureInfo.InvariantCulture),
                photo.Sol.HasValue ? HudFormatting.Format(HudStrings.Sol, photo.Sol.Value) : string.Empty,
                photo.EnuM.x, photo.EnuM.y, photo.Version);
            _source.interactable = !string.IsNullOrEmpty(photo.SourceUrl);
            _previous.interactable = _next.interactable = _photos.Count > 1;
            Canvas.ForceUpdateCanvases();
            _creditScroll.verticalNormalizedPosition = 1;
        }

        private void BuildView()
        {
            _mapCanvas = PhotoCanvas("POV pins Canvas", 18);
            _launcher = PhotoButton("POV gallery", _mapCanvas, string.Empty, () => Browse(0), out _launcherLabel);
            var launcher = (RectTransform)_launcher.transform;
            launcher.anchorMin = launcher.anchorMax = new Vector2(0.5f, 0);
            launcher.anchoredPosition = new Vector2(0, 250);
            launcher.sizeDelta = new Vector2(330, 58);
            RectTransform viewer = PhotoCanvas("POV viewer Canvas", 60);
            RectTransform shade = PhotoPanel("POV modal", viewer);
            StretchPhoto(shade, 0);
            shade.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);
            _modal = shade.gameObject;
            RectTransform card = PhotoPanel("POV card", shade);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1120, 940);
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _title = PhotoLabel("POV title", card, string.Empty, 27, 42);
            _title.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform imageFrame = PhotoPanel("POV image frame", card);
            imageFrame.gameObject.AddComponent<LayoutElement>().preferredHeight = 460;
            imageFrame.GetComponent<Image>().color = Color.black;
            var image = new GameObject("POV image", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            image.transform.SetParent(imageFrame, false);
            _image = image.GetComponent<RawImage>();
            _image.raycastTarget = false;
            _imageAspect = image.GetComponent<AspectRatioFitter>();
            _imageAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _image.gameObject.SetActive(false);
            _status = PhotoLabel("POV status", card, Status, 21, 32);
            _creditScroll = CreditScroll(card);
            var actions = new GameObject("POV actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            actions.transform.SetParent(card, false);
            actions.GetComponent<LayoutElement>().preferredHeight = 58;
            var row = actions.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            _previous = PhotoButton("Previous POV", actions.transform, HudStrings.EditPrevious, () => Browse(-1), out _);
            _next = PhotoButton("Next POV", actions.transform, HudStrings.PovNext, () => Browse(1), out _);
            _source = PhotoButton("POV source page", actions.transform, HudStrings.PovSourcePage, OpenSource, out _);
            PhotoButton("Close POV", actions.transform, HudStrings.CloseSettings, Close, out _);
            _modal.SetActive(false);
        }

        private ScrollRect CreditScroll(Transform parent)
        {
            var root = new GameObject("POV credit scroll", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = 230;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            viewport.GetComponent<Image>().color = Color.clear;
            StretchPhoto((RectTransform)viewport.transform, 0);
            _provenance = PhotoLabel("POV credit and provenance", viewport.transform, string.Empty, 21, 0);
            _provenance.rectTransform.anchorMin = new Vector2(0, 1);
            _provenance.rectTransform.anchorMax = Vector2.one;
            _provenance.rectTransform.pivot = new Vector2(0.5f, 1);
            _provenance.rectTransform.sizeDelta = Vector2.zero;
            _provenance.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = _provenance.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return scroll;
        }

        private RectTransform PhotoCanvas(string name, int order)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = order;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)root.transform;
        }

        private static RectTransform PhotoPanel(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.045f, 0.065f, 0.085f);
            return (RectTransform)root.transform;
        }

        private Button PhotoButton(string name, Transform parent, string caption, UnityEngine.Events.UnityAction action, out TMP_Text label)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.14f, 0.22f, 0.27f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(action);
            label = PhotoLabel("Label", root.transform, caption, 20, 0);
            label.alignment = TextAlignmentOptions.Center;
            StretchPhoto(label.rectTransform, 10);
            return button;
        }

        private TMP_Text PhotoLabel(string name, Transform parent, string caption, int size, float height)
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

        private static void StretchPhoto(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
