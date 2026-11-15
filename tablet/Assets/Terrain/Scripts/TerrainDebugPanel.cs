using UnityEngine;
using UnityEngine.InputSystem;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Developer overlay to test Terrain in any scene with the Terrain prefab, Main included:
    /// layer toggles (through <see cref="MapEvents.LayerToggled"/>, as the HUD sends them), the fake
    /// heat field, the zone source (cache, contract tile, demo) and the frame time. No mission
    /// indicators: those are P6's HUD. It sits at the bottom centre, between P6's mission (left) and
    /// science (right) panels. F1 shows and hides it; it starts visible in the editor and hidden on
    /// the tablet, which has no F1. Not part of C5.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainDebugPanel : MonoBehaviour
    {
        private const float Width = 300f;

        private static readonly string[] AlwaysOffered = { LayerIds.Topo, LayerIds.Hazard, LayerIds.Heat };
        private static readonly string[] BaseLayers = { LayerIds.Ortho, LayerIds.Thermal, LayerIds.ThermalIr };

        [SerializeField] private TerrainMap2D map;
        [SerializeField] private TerrainBootstrap bootstrap;
        [SerializeField] private TerrainSandboxKeys keys;

        private bool _visible;
        private float _smoothedFrameMs = 16f;
        private float _scale = 1f;
        private float _height = 340f;
        private GUIStyle _box;
        private GUIStyle _label;
        private GUIStyle _toggle;
        private GUIStyle _button;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        private void Awake()
        {
            _visible = Application.isEditor;
            if (map == null)
            {
                map = FindAnyObjectByType<TerrainMap2D>();
            }

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<TerrainBootstrap>();
            }

            if (keys == null)
            {
                keys = FindAnyObjectByType<TerrainSandboxKeys>();
            }
        }

        private void OnEnable()
        {
            MapCamera2D.ScreenBlocker = IsOverPanel;
        }

        private void OnDisable()
        {
            if (MapCamera2D.ScreenBlocker == IsOverPanel)
            {
                MapCamera2D.ScreenBlocker = null;
            }
        }

        private void Update()
        {
            _smoothedFrameMs = Mathf.Lerp(_smoothedFrameMs, Time.unscaledDeltaTime * 1000f, 0.05f);
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                _visible = !_visible;
            }
        }

        /// <summary>The panel's area in GUI units (origin top-left), bottom centre of the screen.</summary>
        private Rect PanelRect()
        {
            var width = Screen.width / _scale;
            var height = Screen.height / _scale;
            return new Rect((width - Width) * 0.5f, height - _height - 12f, Width, _height);
        }

        // Screen pixels have their origin at the bottom-left; GUI units at the top-left.
        private bool IsOverPanel(Vector2 screenPosition)
        {
            if (!_visible)
            {
                return false;
            }

            var r = PanelRect();
            var gui = new Vector2(screenPosition.x / _scale, (Screen.height - screenPosition.y) / _scale);
            return r.Contains(gui);
        }

        private void OnGUI()
        {
            if (!_visible || map == null)
            {
                return;
            }

            EnsureStyles();
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

            GUILayout.BeginArea(PanelRect(), _box);
            var zone = map.Current?.Zone;
            GUILayout.Label(zone != null ? $"{zone.Name} ({zone.ZoneId})" : "No zone", _label);
            GUILayout.Label($"{_smoothedFrameMs:F1} ms  ({1000f / Mathf.Max(_smoothedFrameMs, 0.01f):F0} fps)  {Screen.width}×{Screen.height}", _label);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            foreach (var id in AlwaysOffered)
            {
                LayerToggle(id);
            }

            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            if (zone != null)
            {
                foreach (var id in BaseLayers)
                {
                    if (zone.Layers.Exists(l => l.Id == id))
                    {
                        LayerToggle(id);
                    }
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (keys != null && GUILayout.Button("Fake heat field from habitat (H)", _button))
            {
                keys.ToggleFakeHeat();
            }

            if (bootstrap != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cache (C)", _button))
                {
                    _ = bootstrap.LoadAsync(TerrainBootstrap.ZoneSource.Cache);
                }

                if (GUILayout.Button("Contract (X)", _button))
                {
                    _ = bootstrap.LoadAsync(TerrainBootstrap.ZoneSource.ContractFixture);
                }

                if (GUILayout.Button("Demo (Z)", _button))
                {
                    _ = bootstrap.LoadAsync(TerrainBootstrap.ZoneSource.Demo);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("F1 hides this panel · keys 1–6 layers", _label);
            if (Event.current.type == EventType.Repaint)
            {
                var used = GUILayoutUtility.GetLastRect().yMax + 14f;
                if (Mathf.Abs(used - _height) > 1f && used > 120f)
                {
                    _height = used;
                }
            }

            GUILayout.EndArea();
        }

        private void LayerToggle(string id)
        {
            var on = map.LayerStates.TryGetValue(id, out var state) && state;
            var next = GUILayout.Toggle(on, $"  {id}", _toggle);
            if (next != on)
            {
                MapEvents.Emit(new LayerToggled(id, next));
            }
        }

        private void EnsureStyles()
        {
            // ~1 on a desktop monitor, ~1.6 on the Tab S9 FE.
            var dpi = Screen.dpi > 0 ? Screen.dpi : 96f;
            _scale = Mathf.Clamp(dpi / 160f, 1f, 3f);
            if (_box != null)
            {
                return;
            }

            _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 14, fixedHeight = 24 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 13, fixedHeight = 28 };
        }
    }
}
