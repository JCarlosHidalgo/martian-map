using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Top-down orthographic camera for the 2D map, north up. Touch: one finger pans (off in edit
    /// mode), two fingers pinch-zoom and pan. Mouse (PC tests): left drag pans (off in edit mode),
    /// middle or right drag always pans, the wheel zooms toward the cursor. The point under a finger
    /// or the cursor stays under it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class MapCamera2D : MonoBehaviour
    {
        [Tooltip("Smallest visible height of the map, in metres (maximum zoom).")]
        [SerializeField] private float minViewHeightM = 40f;

        [Tooltip("How far, in metres, the view may go past the zone edge.")]
        [SerializeField] private float marginM = 64f;

        [Tooltip("Zoom factor per mouse-wheel notch.")]
        [SerializeField] private float wheelZoomStep = 1.2f;

        [Tooltip("Camera altitude above the map plane; only affects clipping.")]
        [SerializeField] private float altitudeM = 1000f;

        private Camera _camera;
        private Rect _bounds = new Rect(0f, 0f, 256f, 256f);
        private float _maxOrthoSize = 256f;
        private bool _editMode;

        private bool _mouseDragging;
        private Vector3 _mouseGrab;

        private int _lastTouchCount;
        private Vector3 _touchGrab;
        private float _pinchStartDistance;
        private float _pinchStartSize;
        private Vector3 _pinchGrab;
        private bool _touchBlockedByUi;

        /// <summary>Visible height of the map in metres.</summary>
        public float ViewHeightM => _camera.orthographicSize * 2f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = altitudeM * 2f;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.position = new Vector3(transform.position.x, altitudeM, transform.position.z);
            MapQuery.Camera = _camera;
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.EditModeChanged += OnEditModeChanged;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.EditModeChanged -= OnEditModeChanged;
        }

        /// <summary>Fits the whole extent on screen and limits panning to it.</summary>
        public void Frame(Rect extentEnu)
        {
            _bounds = extentEnu;
            var fit = Mathf.Max(extentEnu.height * 0.5f, extentEnu.width * 0.5f / Mathf.Max(_camera.aspect, 0.01f));
            _maxOrthoSize = Mathf.Max(fit * 1.4f, minViewHeightM);
            _camera.orthographicSize = Mathf.Clamp(fit * 1.05f, minViewHeightM * 0.5f, _maxOrthoSize);
            var center = extentEnu.center;
            transform.position = new Vector3(center.x, altitudeM, center.y);
        }

        /// <summary>Centres the view on an ENU point without changing the zoom.</summary>
        public void CenterOn(Vector2 enuM)
        {
            transform.position = new Vector3(enuM.x, altitudeM, enuM.y);
            ClampPosition();
        }

        private void OnZoneLoaded(ZoneLoaded e)
        {
            var x = e.Zone.ExtentEnuM;
            Frame(Rect.MinMaxRect((float)x[0], (float)x[1], (float)x[2], (float)x[3]));
        }

        private void OnEditModeChanged(EditModeChanged e)
        {
            _editMode = e.On;
            _mouseDragging = false;
            _lastTouchCount = -1; // re-grab on the next frame
        }

        private void LateUpdate()
        {
            if (Touch.activeTouches.Count > 0 || _lastTouchCount > 0)
            {
                HandleTouch();
            }
            else
            {
                HandleMouse();
            }

            ClampPosition();
        }

        private void HandleTouch()
        {
            var touches = Touch.activeTouches;
            var count = touches.Count;

            if (count == 0)
            {
                _lastTouchCount = 0;
                _touchBlockedByUi = false;
                return;
            }

            if (_lastTouchCount <= 0)
            {
                _touchBlockedByUi = IsPointerOverUi(touches[0].screenPosition);
            }

            if (_touchBlockedByUi)
            {
                _lastTouchCount = count;
                return;
            }

            if (count == 1)
            {
                var position = touches[0].screenPosition;
                if (_lastTouchCount != 1)
                {
                    _touchGrab = ScreenToMap(position);
                }
                else if (!_editMode)
                {
                    PanSoThat(_touchGrab, position);
                }
            }
            else
            {
                var a = touches[0].screenPosition;
                var b = touches[1].screenPosition;
                var mid = (a + b) * 0.5f;
                var distance = Mathf.Max(Vector2.Distance(a, b), 1f);
                if (_lastTouchCount < 2)
                {
                    _pinchStartDistance = distance;
                    _pinchStartSize = _camera.orthographicSize;
                    _pinchGrab = ScreenToMap(mid);
                }
                else
                {
                    SetOrthoSize(_pinchStartSize * _pinchStartDistance / distance);
                    PanSoThat(_pinchGrab, mid);
                }
            }

            _lastTouchCount = count;
        }

        private void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var position = mouse.position.ReadValue();
            var panButtonDown = (!_editMode && mouse.leftButton.wasPressedThisFrame) ||
                                mouse.middleButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
            var panButtonHeld = (!_editMode && mouse.leftButton.isPressed) ||
                                mouse.middleButton.isPressed || mouse.rightButton.isPressed;

            if (panButtonDown && !IsPointerOverUi(position))
            {
                _mouseDragging = true;
                _mouseGrab = ScreenToMap(position);
            }
            else if (_mouseDragging && panButtonHeld)
            {
                PanSoThat(_mouseGrab, position);
            }
            else if (!panButtonHeld)
            {
                _mouseDragging = false;
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !IsPointerOverUi(position) && IsInsideScreen(position))
            {
                var before = ScreenToMap(position);
                SetOrthoSize(_camera.orthographicSize * (scroll > 0f ? 1f / wheelZoomStep : wheelZoomStep));
                PanSoThat(before, position);
            }
        }

        /// <summary>Moves the camera so <paramref name="mapPoint"/> ends up under <paramref name="screenPosition"/>.</summary>
        private void PanSoThat(Vector3 mapPoint, Vector2 screenPosition)
        {
            var now = ScreenToMap(screenPosition);
            var delta = mapPoint - now;
            transform.position += new Vector3(delta.x, 0f, delta.z);
        }

        private void SetOrthoSize(float size)
        {
            _camera.orthographicSize = Mathf.Clamp(size, minViewHeightM * 0.5f, _maxOrthoSize);
        }

        private Vector3 ScreenToMap(Vector2 screenPosition)
        {
            var ray = _camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            return plane.Raycast(ray, out var distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        private void ClampPosition()
        {
            var p = transform.position;
            p.x = Mathf.Clamp(p.x, _bounds.xMin - marginM, _bounds.xMax + marginM);
            p.z = Mathf.Clamp(p.z, _bounds.yMin - marginM, _bounds.yMax + marginM);
            p.y = altitudeM;
            transform.position = p;
        }

        /// <summary>Extra screen areas that must not move the map (e.g. an IMGUI overlay). Screen pixels, origin bottom-left.</summary>
        public static System.Func<Vector2, bool> ScreenBlocker { get; set; }

        private static bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (ScreenBlocker != null && ScreenBlocker(screenPosition))
            {
                return true;
            }

            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static bool IsInsideScreen(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;
    }
}
