using MartianMap.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace MartianMap.FX
{
    /// <summary>One analytic near-fog quad for the active zone and the global terrain FX controls.</summary>
    [DisallowMultipleComponent]
    public sealed class FxController : MonoBehaviour
    {
        [SerializeField] private Material nearFogMaterial;
        [SerializeField] private Material dustMaterial;
        [SerializeField] private Color fogColor = new Color(0.95f, 0.28f, 0.12f, 1f);
        [SerializeField, Range(0f, 1f)] private float farStrength = 0.55f;
        [SerializeField, Range(0f, 1f)] private float nearOpacity = 0.3f;
        [SerializeField, Range(0f, 1f)] private float hologramStrength = 0.5f;
        [SerializeField, Range(0f, 1f)] private float wireStrength = 0.35f;
        [SerializeField] private float scanDensity = 0.18f;
        [SerializeField] private bool enableDust;

        private static readonly int FogTint = Shader.PropertyToID("_FogTint");
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Hologram = Shader.PropertyToID("_FxHologramStrength");
        private static readonly int Wire = Shader.PropertyToID("_FxWireStrength");
        private static readonly int Scan = Shader.PropertyToID("_FxScanDensity");

        private Mesh _quad;
        private Material _fogInstance;
        private MeshRenderer _renderer;
        private ParticleSystem _dust;
        private bool _hazardOn = true;

        private void Awake()
        {
            if (nearFogMaterial == null)
            {
                Debug.LogError("[FX] Assign M_FogNear to FX.prefab.", this);
                enabled = false;
                return;
            }

            var quad = new GameObject("NearFogQuad");
            quad.layer = gameObject.layer;
            quad.transform.SetParent(transform, false);
            var filter = quad.AddComponent<MeshFilter>();
            _renderer = quad.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
            _quad = new Mesh { name = "FX near fog quad" };
            filter.sharedMesh = _quad;
            _fogInstance = new Material(nearFogMaterial);
            _renderer.sharedMaterial = _fogInstance;
            if (dustMaterial != null)
            {
                var dustObject = new GameObject("SparseDust");
                dustObject.layer = gameObject.layer;
                dustObject.transform.SetParent(transform, false);
                _dust = dustObject.AddComponent<ParticleSystem>();
                var main = _dust.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startLifetime = 12f;
                main.startSpeed = 0.25f;
                main.startSize = 24f;
                main.startColor = new Color(fogColor.r, fogColor.g, fogColor.b, 0.12f);
                main.maxParticles = 12;
                var emission = _dust.emission;
                emission.rateOverTime = 0.7f;
                var shape = _dust.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                var particleRenderer = dustObject.GetComponent<ParticleSystemRenderer>();
                particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                particleRenderer.sharedMaterial = dustMaterial;
                particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;
                _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            ApplySettings();
        }

        private void Start()
        {
            ApplySettings(); // Terrain may have set its defaults in Awake after this prefab.
            var terrain = FindFirstObjectByType<TerrainMap2D>();
            if (terrain != null && terrain.Current != null)
            {
                ShowZone(terrain.Current.Zone);
            }
        }

        private void OnEnable()
        {
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.LayerToggled += OnLayerToggled;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.LayerToggled -= OnLayerToggled;
            if (_renderer != null) _renderer.enabled = false;
            if (_dust != null) _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Shader.SetGlobalFloat(Hologram, 0f);
            Shader.SetGlobalFloat(Wire, 0f);
        }

        private void OnDestroy()
        {
            if (_fogInstance != null) Destroy(_fogInstance);
            if (_quad != null) Destroy(_quad);
        }

        private void OnZoneLoaded(ZoneLoaded e) => ShowZone(e.Zone);

        private void OnLayerToggled(LayerToggled e)
        {
            if (e.LayerId != LayerIds.Hazard) return;
            _hazardOn = e.On;
            if (_renderer != null) _renderer.enabled = _hazardOn && nearOpacity > 0f && _quad.vertexCount == 4;
            UpdateDust();
        }

        private void ApplySettings()
        {
            Shader.SetGlobalColor(ShaderIds.FogFarColor, fogColor);
            Shader.SetGlobalFloat(ShaderIds.FogFarStrength, Mathf.Clamp01(farStrength));
            Shader.SetGlobalFloat(Hologram, Mathf.Clamp01(hologramStrength));
            Shader.SetGlobalFloat(Wire, Mathf.Clamp01(wireStrength));
            Shader.SetGlobalFloat(Scan, Mathf.Max(0.01f, scanDensity));
            if (_fogInstance == null) return;
            _fogInstance.SetColor(FogTint, fogColor);
            _fogInstance.SetFloat(Opacity, Mathf.Clamp01(nearOpacity));
        }

        private void ShowZone(ZoneInfo zone)
        {
            if (_quad == null) return;
            var e = zone?.ExtentEnuM;
            if (e == null || e.Length != 4 || e[2] <= e[0] || e[3] <= e[1])
            {
                _quad.Clear();
                _renderer.enabled = false;
                if (_dust != null) _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }

            var west = (float)e[0];
            var south = (float)e[1];
            var east = (float)e[2];
            var north = (float)e[3];
            _quad.Clear();
            _quad.vertices = new[]
            {
                new Vector3(west, 0.12f, south), new Vector3(west, 0.12f, north),
                new Vector3(east, 0.12f, north), new Vector3(east, 0.12f, south),
            };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            _renderer.enabled = _hazardOn && nearOpacity > 0f;
            if (_dust != null)
            {
                var shape = _dust.shape;
                shape.position = new Vector3((west + east) * 0.5f, 1f, (south + north) * 0.5f);
                shape.scale = new Vector3(east - west, 0.5f, north - south);
                UpdateDust();
            }
        }

        private void UpdateDust()
        {
            if (_dust == null) return;
            if (enableDust && _hazardOn && _quad.vertexCount == 4)
            {
                if (!_dust.isPlaying) _dust.Play();
            }
            else if (_dust.isPlaying)
            {
                _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
