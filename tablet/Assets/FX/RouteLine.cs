using MartianMap.Routing;
using MartianMap.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace MartianMap.FX
{
    /// <summary>
    /// Draws the three route alternatives of <see cref="RouteSelected"/>: the selected one in green,
    /// the other two in cyan and purple (in Alternative order), each as a bright core and a soft
    /// halo. With fewer than three plans it draws <c>RouteSelected.Polyline</c> alone. Line widths
    /// are kept in screen pixels, so the routes stay visible at any zoom. <see cref="RouteFocused"/>
    /// (from the HUD) shows one alternative alone, in its colour, or all three again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RouteLine : MonoBehaviour
    {
        private const int Slots = 3;

        [SerializeField] private Material routeMaterial;
        [SerializeField] private Color selectedColor = new Color(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private Color firstAlternativeColor = new Color(0.25f, 0.85f, 1f, 1f);
        [SerializeField] private Color secondAlternativeColor = new Color(0.72f, 0.35f, 1f, 1f);

        [Tooltip("Minimum core width in metres.")]
        [SerializeField, Min(0.1f)] private float widthM = 1.2f;

        [Tooltip("Core width of the selected route in screen pixels; the alternatives are 75 % of it.")]
        [SerializeField, Min(1f)] private float widthPixels = 5f;

        private readonly LineRenderer[] _cores = new LineRenderer[Slots];
        private readonly LineRenderer[] _halos = new LineRenderer[Slots];
        private readonly Material[] _coreMaterials = new Material[Slots];
        private readonly Material[] _haloMaterials = new Material[Slots];
        private readonly float[] _widthScale = new float[Slots];
        private RouteSelected? _last;

        private void Awake()
        {
            if (routeMaterial == null)
            {
                Debug.LogError("[FX] Assign M_RouteGlow to Routes.prefab.", this);
                enabled = false;
                return;
            }

            for (var i = 0; i < Slots; i++)
            {
                _coreMaterials[i] = new Material(routeMaterial);
                _haloMaterials[i] = new Material(routeMaterial);
                _cores[i] = CreateLine($"RouteCore{i}", _coreMaterials[i]);
                _halos[i] = CreateLine($"RouteHalo{i}", _haloMaterials[i]);
            }
        }

        private void OnEnable()
        {
            MapEvents.RouteSelected += OnRouteSelected;
            MapEvents.RouteFocused += OnRouteFocused;
            MapEvents.ZoneLoaded += OnZoneLoaded;
        }

        private void OnDisable()
        {
            MapEvents.RouteSelected -= OnRouteSelected;
            MapEvents.RouteFocused -= OnRouteFocused;
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            Clear();
        }

        private void OnDestroy()
        {
            for (var i = 0; i < Slots; i++)
            {
                if (_coreMaterials[i] != null) Destroy(_coreMaterials[i]);
                if (_haloMaterials[i] != null) Destroy(_haloMaterials[i]);
            }
        }

        private void LateUpdate()
        {
            // Keep the width in screen pixels for the current zoom (orthographic map camera).
            var camera = MapQuery.Camera;
            var metresPerPixel = camera != null && camera.orthographic && Screen.height > 0
                ? camera.orthographicSize * 2f / Screen.height
                : 0f;
            for (var i = 0; i < Slots; i++)
            {
                if (_cores[i] == null || _cores[i].positionCount == 0)
                {
                    continue;
                }

                var core = Mathf.Max(widthM, widthPixels * metresPerPixel) * _widthScale[i];
                _cores[i].widthMultiplier = core;
                _halos[i].widthMultiplier = core * 3f;
            }
        }

        private LineRenderer CreateLine(string name, Material material)
        {
            var child = new GameObject(name);
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = widthM;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        private void OnRouteSelected(RouteSelected e)
        {
            _last = e;
            DrawPlans(e, null);
        }

        private void OnRouteFocused(RouteFocused e)
        {
            if (_last.HasValue)
            {
                DrawPlans(_last.Value, e.All ? (int?)null : (int)e.Alternative);
            }
        }

        /// <summary>Draws all three plans, or only <paramref name="only"/>, keeping each one's colour.</summary>
        private void DrawPlans(RouteSelected e, int? only)
        {
            if (e.Plans == null || e.Plans.Count != Slots)
            {
                Show(e.Polyline);
                return;
            }

            Clear();

            // Slot 0 is the selected plan (green); the others follow in Alternative order.
            var selected = Mathf.Clamp((int)e.Selected, 0, Slots - 1);
            var slot = 1;
            for (var index = 0; index < Slots; index++)
            {
                var visible = only == null || only.Value == index;
                var polyline = visible ? RouteSelected.ScenePolyline(e.Plans[index]) : null;
                if (index == selected)
                {
                    Draw(0, polyline, selectedColor, 1f, 0.36f);
                }
                else
                {
                    Draw(slot, polyline, slot == 1 ? firstAlternativeColor : secondAlternativeColor,
                        only == null ? 0.75f : 1f, 0.30f + 0.01f * slot);
                    slot++;
                }
            }
        }

        private void OnZoneLoaded(ZoneLoaded e)
        {
            _last = null;
            Clear();
        }

        /// <summary>Draws one polyline as the selected route and clears the alternatives.</summary>
        public void Show(Vector3[] polyline)
        {
            Clear();
            Draw(0, polyline, selectedColor, 1f, 0.36f);
        }

        public void Clear()
        {
            for (var i = 0; i < Slots; i++)
            {
                if (_cores[i] != null) _cores[i].positionCount = 0;
                if (_halos[i] != null) _halos[i].positionCount = 0;
            }
        }

        private void Draw(int slot, Vector3[] polyline, Color color, float widthScale, float lift)
        {
            var core = _cores[slot];
            var halo = _halos[slot];
            if (core == null || halo == null)
            {
                return;
            }

            if (polyline == null || polyline.Length < 2)
            {
                core.positionCount = 0;
                halo.positionCount = 0;
                return;
            }

            _coreMaterials[slot].SetColor("_Color", color);
            _haloMaterials[slot].SetColor("_Color", new Color(color.r, color.g, color.b, color.a * 0.28f));
            _widthScale[slot] = widthScale;
            core.positionCount = polyline.Length;
            halo.positionCount = polyline.Length;
            for (var i = 0; i < polyline.Length; i++)
            {
                var p = polyline[i];
                core.SetPosition(i, new Vector3(p.x, p.y + lift, p.z));
                halo.SetPosition(i, new Vector3(p.x, p.y + lift - 0.02f, p.z));
            }
        }
    }
}
