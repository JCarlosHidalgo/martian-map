using MartianMap.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace MartianMap.FX
{
    /// <summary>Draws RouteSelected.Polyline as a bright core and one soft halo.</summary>
    [DisallowMultipleComponent]
    public sealed class RouteLine : MonoBehaviour
    {
        [SerializeField] private Material routeMaterial;
        [SerializeField] private Color color = new Color(0.1f, 0.8f, 1f, 1f);
        [SerializeField, Min(0.1f)] private float widthM = 1.2f;

        private LineRenderer _core;
        private LineRenderer _halo;
        private Material _coreMaterial;
        private Material _haloMaterial;

        private void Awake()
        {
            if (routeMaterial == null)
            {
                Debug.LogError("[FX] Assign M_RouteGlow to Routes.prefab.", this);
                enabled = false;
                return;
            }

            _coreMaterial = new Material(routeMaterial);
            _coreMaterial.SetColor("_Color", color);
            _haloMaterial = new Material(routeMaterial);
            _haloMaterial.SetColor("_Color", new Color(color.r, color.g, color.b, color.a * 0.28f));
            _core = CreateLine("RouteCore", widthM, _coreMaterial);
            _halo = CreateLine("RouteHalo", widthM * 3f, _haloMaterial);
        }

        private void OnEnable()
        {
            MapEvents.RouteSelected += OnRouteSelected;
            MapEvents.ZoneLoaded += OnZoneLoaded;
        }

        private void OnDisable()
        {
            MapEvents.RouteSelected -= OnRouteSelected;
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            Clear();
        }

        private void OnDestroy()
        {
            if (_coreMaterial != null) Destroy(_coreMaterial);
            if (_haloMaterial != null) Destroy(_haloMaterial);
        }

        private LineRenderer CreateLine(string name, float width, Material material)
        {
            var child = new GameObject(name);
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        private void OnRouteSelected(RouteSelected e) => Show(e.Polyline);

        private void OnZoneLoaded(ZoneLoaded e) => Clear();

        public void Show(Vector3[] polyline)
        {
            if (_core == null || _halo == null) return;
            if (polyline == null || polyline.Length < 2)
            {
                Clear();
                return;
            }

            _core.positionCount = polyline.Length;
            _halo.positionCount = polyline.Length;
            for (var i = 0; i < polyline.Length; i++)
            {
                var p = polyline[i];
                _core.SetPosition(i, new Vector3(p.x, p.y + 0.32f, p.z));
                _halo.SetPosition(i, new Vector3(p.x, p.y + 0.30f, p.z));
            }
        }

        public void Clear()
        {
            if (_core != null) _core.positionCount = 0;
            if (_halo != null) _halo.positionCount = 0;
        }
    }
}
