using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MartianMap.Terrain
{
    /// <summary>
    /// A route between two points of the map: a short tap sets A, the next sets B; the route is
    /// planned only when the HUD's PLAN ROUTE calls <see cref="PlanPoints"/>, so it can be planned
    /// again (after an edit, say) without placing the points again. The third tap starts again with
    /// a new A. <see cref="RouteOrchestrator.PlanBetween"/> computes it; P5's <c>Routes</c> draws it. A tap is a press released within 0.35 s that moved
    /// less than 12 px, so it never fights the camera's drag nor P6's long press, and it is ignored
    /// over the HUD and in edit mode. A and B are drawn as screen-size markers (no materials, so they
    /// render the same on the tablet).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoutePointsTool : MonoBehaviour
    {
        private const float MaxTapSeconds = 0.35f;
        private const float MaxTapPixels = 12f;
        private const float MarkerPixels = 26f;

        [SerializeField] private RouteOrchestrator orchestrator;

        private static RoutePointsTool _instance;

        /// <summary>True when A and B are both on the map: PLAN ROUTE plans between them.</summary>
        public static bool HasBothPoints => _instance != null && _instance._a.HasValue && _instance._b.HasValue;

        /// <summary>Raised when A or B is set or cleared.</summary>
        public static event System.Action PointsChanged;

        /// <summary>Plans the three alternatives between A and B; false if there are no two points.</summary>
        public static bool PlanPoints()
        {
            if (!HasBothPoints || _instance.orchestrator == null)
            {
                return false;
            }

            _instance.PlanAsync(_instance._a.Value, _instance._b.Value);
            return true;
        }

        private Vector2? _a;
        private Vector2? _b;
        private bool _editMode;

        private bool _pressing;
        private Vector2 _pressPosition;
        private float _pressTime;
        private bool _pressOverUi;
        private int _maxTouches;

        private Texture2D _disc;
        private string _toast;
        private float _toastUntil;

        /// <summary>Shown when B cannot be reached from A (English, like all UI text).</summary>
        public const string UnreachableMessage = "Destination unreachable: no safe route from A to B.";
        private const float ToastSeconds = 4f;

        private void Awake()
        {
            if (orchestrator == null)
            {
                orchestrator = GetComponent<RouteOrchestrator>();
            }

            _disc = BuildDisc(64);
            _instance = this;
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
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            if (_disc != null)
            {
                Destroy(_disc);
            }
        }

        private void OnEditModeChanged(EditModeChanged e)
        {
            _editMode = e.On;
            _pressing = false;
        }

        private void OnZoneLoaded(ZoneLoaded e)
        {
            _a = null;
            _b = null;
            PointsChanged?.Invoke();
        }

        private void Update()
        {
            if (Touch.activeTouches.Count > 0 || _maxTouches > 0)
            {
                UpdateTouch();
            }
            else
            {
                UpdateMouse();
            }
        }

        private void UpdateTouch()
        {
            var touches = Touch.activeTouches;
            if (touches.Count == 0)
            {
                _maxTouches = 0;
                _pressing = false;
                return;
            }

            _maxTouches = Mathf.Max(_maxTouches, touches.Count);
            var touch = touches[0];
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began && touches.Count == 1)
            {
                BeginPress(touch.screenPosition);
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended && touches.Count == 1 && _maxTouches == 1)
            {
                EndPress(touch.screenPosition);
            }
        }

        private void UpdateMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                BeginPress(mouse.position.ReadValue());
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                EndPress(mouse.position.ReadValue());
            }
        }

        private void BeginPress(Vector2 position)
        {
            _pressing = true;
            _pressPosition = position;
            _pressTime = Time.unscaledTime;
            _pressOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void EndPress(Vector2 position)
        {
            if (!_pressing)
            {
                return;
            }

            _pressing = false;
            var isTap = Time.unscaledTime - _pressTime <= MaxTapSeconds &&
                        (position - _pressPosition).sqrMagnitude <= MaxTapPixels * MaxTapPixels;
            if (!isTap || _pressOverUi || _editMode || !MapQuery.TryPick(position, out var enu))
            {
                return;
            }

            if (_a == null || _b != null)
            {
                _a = enu;
                _b = null;
                Debug.Log($"[Terrain] route point A=({enu.x:F0},{enu.y:F0}); tap B");
                PointsChanged?.Invoke();
                return;
            }

            _b = enu;
            Debug.Log($"[Terrain] route point B=({enu.x:F0},{enu.y:F0}); press PLAN ROUTE");
            PointsChanged?.Invoke();
        }

        private async void PlanAsync(Vector2 a, Vector2 b)
        {
            var found = await orchestrator.PlanBetween(a, b);
            if (found == false)
            {
                Debug.LogWarning($"[Terrain] destination unreachable from=({a.x:F0},{a.y:F0}) to=({b.x:F0},{b.y:F0})");
                ShowToast(UnreachableMessage);
            }
        }

        private void ShowToast(string message)
        {
            _toast = message;
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            DrawToast();
            if (_a == null && _b == null)
            {
                return;
            }

            var camera = MapQuery.Camera;
            if (camera == null)
            {
                return;
            }

            var scale = Mathf.Clamp((Screen.dpi > 0 ? Screen.dpi : 96f) / 160f, 1f, 3f);
            if (_a.HasValue)
            {
                DrawMarker(camera, _a.Value, new Color(0.25f, 0.95f, 0.45f), "A", scale);
            }

            if (_b.HasValue)
            {
                DrawMarker(camera, _b.Value, new Color(1f, 0.55f, 0.15f), "B", scale);
            }
        }

        /// <summary>A banner at the top centre of the map while the toast lasts.</summary>
        private void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil)
            {
                return;
            }

            var scale = Mathf.Clamp((Screen.dpi > 0 ? Screen.dpi : 96f) / 160f, 1f, 3f);
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(15 * scale),
                wordWrap = true,
                padding = new RectOffset(12, 12, 8, 8),
            };
            style.normal.textColor = Color.white;
            var width = Mathf.Min(Screen.width * 0.4f, 420f * scale);
            var height = 48f * scale;
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.16f, width, height);
            var previous = GUI.color;
            GUI.color = new Color(0.85f, 0.18f, 0.12f, 0.95f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, _toast, style);
            GUI.color = previous;
        }

        private void DrawMarker(Camera camera, Vector2 enu, Color color, string label, float scale)
        {
            var screen = camera.WorldToScreenPoint(ZoneProjection.ToScene(enu));
            var size = MarkerPixels * scale;
            var rect = new Rect(screen.x - size * 0.5f, Screen.height - screen.y - size * 0.5f, size, size);
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), _disc);
            GUI.color = color;
            GUI.DrawTexture(rect, _disc);
            GUI.color = Color.black;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(14 * scale),
            };
            GUI.Label(rect, label, style);
            GUI.color = previous;
        }

        private static Texture2D BuildDisc(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "T_RouteMarker" };
            var c = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    var alpha = Mathf.Clamp01((1f - d) * size * 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
