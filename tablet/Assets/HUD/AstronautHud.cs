using System.Collections.Generic;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MartianMap.HUD
{
    /// <summary>Simulated astronaut marker and map-only long press, using C5 map queries and events.</summary>
    [RequireComponent(typeof(MissionHud))]
    [DisallowMultipleComponent]
    public sealed class AstronautHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private float _holdDuration = 0.65f;
        [SerializeField] private float _holdTolerancePixels = 18f;

        private readonly MapHoldGesture _hold = new MapHoldGesture();
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private MissionHud _mission;
        private RectTransform _canvas;
        private RectTransform _marker;
        private TMP_Text _label;
        private TMP_Text _hint;
        private bool _editMode;

        private void Awake()
        {
            _mission = GetComponent<MissionHud>();
            BuildView();
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            MapEvents.EditModeChanged += OnEditModeChanged;
            MapEvents.ZoneLoaded += OnZoneLoaded;
        }

        private void OnDisable()
        {
            MapEvents.EditModeChanged -= OnEditModeChanged;
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            EnhancedTouchSupport.Disable();
            _hold.Cancel();
            _marker.gameObject.SetActive(false);
            _hint.gameObject.SetActive(false);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                _hold.Cancel();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _hold.Cancel();
            }
        }

        private void OnZoneLoaded(ZoneLoaded message) => _hold.Cancel();

        private void OnEditModeChanged(EditModeChanged message)
        {
            _editMode = message.On;
            _hold.Cancel();
        }

        private void Update()
        {
            if (_editMode || !_mission.AstronautPosition.HasValue)
            {
                _hold.Cancel();
                return;
            }

            var touches = Touch.activeTouches;
            if (touches.Count > 0)
            {
                if (touches.Count != 1 || touches[0].phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    touches[0].phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    _hold.Cancel();
                    return;
                }

                var touch = touches[0];
                ProcessPointer(touch.touchId, touch.screenPosition, touch.phase == UnityEngine.InputSystem.TouchPhase.Began);
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed || mouse.middleButton.isPressed ||
                mouse.rightButton.isPressed || mouse.scroll.ReadValue().sqrMagnitude > 0)
            {
                _hold.Cancel();
                return;
            }

            ProcessPointer(-1, mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame);
        }

        private void ProcessPointer(int id, Vector2 screenPosition, bool began)
        {
            if (IsOverControls(screenPosition) || !MapQuery.TryPick(screenPosition, out Vector2 enuM))
            {
                _hold.Cancel();
                return;
            }

            if (began)
            {
                _hold.Begin(id, screenPosition, Time.unscaledTime);
            }

            float tolerance = _holdTolerancePixels * Mathf.Max(1, Screen.dpi / 160f);
            if (_hold.Hold(id, screenPosition, Time.unscaledTime, _holdDuration, tolerance))
            {
                _mission.MoveAstronaut(enuM);
            }
        }

        private bool IsOverControls(Vector2 position)
        {
            if (MapCamera2D.ScreenBlocker != null && MapCamera2D.ScreenBlocker(position))
            {
                return true;
            }

            if (EventSystem.current == null)
            {
                return false;
            }

            var pointer = new PointerEventData(EventSystem.current) { position = position };
            _hits.Clear();
            EventSystem.current.RaycastAll(pointer, _hits);
            foreach (RaycastResult hit in _hits)
            {
                if (hit.module is GraphicRaycaster)
                {
                    return true;
                }
            }

            return false;
        }

        private void LateUpdate()
        {
            var position = _mission.AstronautPosition;
            Camera camera = MapQuery.Camera;
            bool ready = position.HasValue && camera != null;
            _hint.gameObject.SetActive(ready && !_editMode);
            if (!ready)
            {
                _marker.gameObject.SetActive(false);
                return;
            }

            var enu = new Vector2((float)position.Value.E, (float)position.Value.N);
            Vector3 screen = camera.WorldToScreenPoint(ZoneProjection.ToScene(enu, MapQuery.HeightAt(enu)));
            _marker.gameObject.SetActive(screen.z > 0 && camera.pixelRect.Contains(screen));
            Canvas canvas = _canvas.GetComponent<Canvas>();
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (_marker.gameObject.activeSelf &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen, uiCamera, out Vector2 local))
            {
                _marker.anchoredPosition = local;
                _label.text = HudFormatting.Format(HudStrings.AstronautCoordinates, enu.x, enu.y);
            }
        }

        private void BuildView()
        {
            var root = new GameObject("Astronaut Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 15;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _canvas = (RectTransform)root.transform;
            _marker = new GameObject("Astronaut marker", typeof(RectTransform)).GetComponent<RectTransform>();
            _marker.SetParent(_canvas, false);
            _marker.sizeDelta = new Vector2(240, 100);
            var diamond = new GameObject("Position diamond", typeof(RectTransform), typeof(Image));
            diamond.transform.SetParent(_marker, false);
            diamond.GetComponent<RectTransform>().sizeDelta = new Vector2(28, 28);
            diamond.transform.localRotation = Quaternion.Euler(0, 0, 45);
            diamond.GetComponent<Image>().color = new Color(0.2f, 0.95f, 0.9f);
            diamond.GetComponent<Image>().raycastTarget = false;
            var outline = diamond.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2, 2);
            _label = Label("Astronaut coordinates", _marker, 18);
            _label.rectTransform.anchoredPosition = new Vector2(0, 48);
            _label.rectTransform.sizeDelta = new Vector2(240, 55);
            _hint = Label("Position help", _canvas, 18);
            _hint.text = HudStrings.AstronautHelp;
            _hint.rectTransform.anchorMin = _hint.rectTransform.anchorMax = new Vector2(0.5f, 0);
            _hint.rectTransform.anchoredPosition = new Vector2(0, 45);
            _hint.rectTransform.sizeDelta = new Vector2(700, 55);
            _marker.gameObject.SetActive(false);
        }

        private TMP_Text Label(string name, Transform parent, int size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = Color.white;
            text.richText = false;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            var shadow = root.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(2, -2);
            return text;
        }
    }
}
