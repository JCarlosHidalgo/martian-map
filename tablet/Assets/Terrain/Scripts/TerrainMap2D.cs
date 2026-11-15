using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Draws a zone as a 2D map on the plane Y = 0: one quad per tile of the chosen level, with the
    /// tile's textures in a <see cref="MaterialPropertyBlock"/>. Owns the global shader properties
    /// that C5 gives to Terrain and listens to <see cref="MapEvents.LayerToggled"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainMap2D : MonoBehaviour
    {
        [SerializeField] private Material terrainMaterial;

        [Tooltip("Contour interval in metres.")]
        [SerializeField] private float contourIntervalM = 5f;

        [Tooltip("hazard_amber_min and hazard_red_min of cost_params.json (0–255).")]
        [SerializeField] private int hazardAmberMin = 96;
        [SerializeField] private int hazardRedMin = 192;

        [Tooltip("Default far-fog tint; FX overrides _FogFarColor and _FogFarStrength.")]
        [SerializeField] private Color hazardColor = new Color(0.95f, 0.18f, 0.12f, 1f);
        [SerializeField, Range(0f, 1f)] private float hazardStrength = 0.55f;

        private readonly List<GameObject> _tileObjects = new List<GameObject>();
        private readonly Dictionary<(int x, int y), TileData> _tilesByIndex = new Dictionary<(int, int), TileData>();
        private readonly Dictionary<string, bool> _layerOn = new Dictionary<string, bool>
        {
            [LayerIds.Topo] = true,
            [LayerIds.Hazard] = true,
            [LayerIds.Heat] = false,
            [LayerIds.Ortho] = false,
            [LayerIds.Thermal] = false,
            [LayerIds.ThermalIr] = false,
        };

        private Mesh _quad;
        private Texture2D _black;
        private Texture2D _noHeat;
        private Texture2D _heat;
        private Texture2D _hazardZone;
        private LoadedZone _current;

        public LoadedZone Current => _current;

        public IReadOnlyDictionary<string, bool> LayerStates => _layerOn;

        /// <summary>[e_min, n_min, e_max, n_max] of the active zone, or an empty rect.</summary>
        public Rect ExtentEnu { get; private set; }

        private void Awake()
        {
            _quad = BuildQuad();
            _black = SolidTexture(0);
            _noHeat = SolidTexture(255);
            SetDefaultGlobals();
            MapQuery.Attach(this);
        }

        private void OnEnable()
        {
            MapEvents.LayerToggled += OnLayerToggled;
        }

        private void OnDisable()
        {
            MapEvents.LayerToggled -= OnLayerToggled;
        }

        private void OnDestroy()
        {
            Clear();
            MapQuery.Detach(this);
            Destroy(_quad);
            Destroy(_black);
            Destroy(_noHeat);
            Destroy(_heat);
            Destroy(_hazardZone);
        }

        /// <summary>Shows a zone, replacing the current one, and emits ZoneLoaded, TileLoaded and ConditionsChanged.</summary>
        public void Show(LoadedZone zone)
        {
            Clear();
            _current = zone;
            var info = zone.Zone;
            var extent = info.ExtentEnuM;
            ExtentEnu = Rect.MinMaxRect((float)extent[0], (float)extent[1], (float)extent[2], (float)extent[3]);

            var minY = float.MaxValue;
            var maxY = float.MinValue;
            var originHeight = (float)info.OriginHeightM;
            foreach (var tile in zone.Tiles)
            {
                if (!tile.HasData)
                {
                    continue;
                }

                minY = Mathf.Min(minY, tile.MinHeightM - originHeight);
                maxY = Mathf.Max(maxY, tile.MaxHeightM - originHeight);
            }

            if (minY > maxY)
            {
                minY = maxY = 0f;
            }

            // A heat field belongs to the grid of one zone: drop the previous zone's.
            ClearHeatField();
            Shader.SetGlobalFloat(ShaderIds.ZoneOriginHeightM, originHeight);
            Shader.SetGlobalVector(ShaderIds.ZoneHeightRange, new Vector4(minY, maxY, 0f, 0f));

            foreach (var tile in zone.Tiles)
            {
                CreateTileObject(tile, originHeight);
            }

            Debug.Log($"[Terrain] zone shown zone_id={info.ZoneId} tiles={zone.Tiles.Count} height_range_m={minY:F1}..{maxY:F1}");
            MapEvents.Emit(new ZoneLoaded(info, zone.PovFolder, AppConfig.Author));
            foreach (var tile in zone.Tiles)
            {
                MapEvents.Emit(new TileLoaded(info.ZoneId, tile.Info.Z, tile.Info.X, tile.Info.Y));
            }

            if (zone.Conditions != null)
            {
                MapEvents.Emit(new ConditionsChanged(info.ZoneId, zone.Conditions));
            }
        }

        /// <summary>Height above the areoid at an ENU point, from the z = 0 tiles; false outside data.</summary>
        public bool TryElevationAt(Vector2 enuM, out float metresAboveAreoid)
        {
            metresAboveAreoid = 0f;
            if (_current == null || !ExtentEnu.Contains(enuM))
            {
                return false;
            }

            var local = enuM - ExtentEnu.min;
            var x = (int)(local.x / TileData.Cells);
            var y = (int)(local.y / TileData.Cells);
            if (!_tilesByIndex.TryGetValue((x, y), out var tile))
            {
                return false;
            }

            var tileLocal = enuM - tile.OriginEnuM;
            return tile.TryHeightAtLocal(tileLocal.x, tileLocal.y, out metresAboveAreoid);
        }

        /// <summary>
        /// Replaces P5's hazard shapes on the shown zone (empty list = remove them), re-uploads the
        /// hazard textures and emits <see cref="MapEvents.TileLoaded"/> for every tile that changed.
        /// </summary>
        public void ApplyHazardShapes(IReadOnlyList<HazardPolygon> polygons)
        {
            if (_current == null)
            {
                return;
            }

            var before = new HashSet<TileData>();
            foreach (var tile in _current.Tiles)
            {
                if (!System.Linq.Enumerable.SequenceEqual(tile.Hazard, tile.BaseHazard))
                {
                    before.Add(tile);
                }
            }

            var changed = HazardShapes.Apply(_current.Tiles, polygons ?? new List<HazardPolygon>());
            before.UnionWith(changed);
            foreach (var tile in before)
            {
                MapEvents.Emit(new TileLoaded(_current.Zone.ZoneId, tile.Info.Z, tile.Info.X, tile.Info.Y));
            }
        }

        /// <summary>Takes the hazard thresholds of <c>cost_params.json</c> (C5: _FogFarAmberMin, _FogFarRedMin).</summary>
        public void ApplyCostParams(MartianMap.Routing.CostParams costParams)
        {
            hazardAmberMin = costParams.HazardAmberMin;
            hazardRedMin = costParams.HazardRedMin;
            Shader.SetGlobalFloat(ShaderIds.FogFarAmberMin, hazardAmberMin / 255f);
            Shader.SetGlobalFloat(ShaderIds.FogFarRedMin, hazardRedMin / 255f);
        }

        public void SetLayer(string layerId, bool on)
        {
            if (!_layerOn.ContainsKey(layerId))
            {
                Debug.LogWarning($"[Terrain] unknown layer layer_id={layerId}");
                return;
            }

            // ortho, thermal and thermal_ir are exclusive (C5, "Capas").
            if (on && IsBaseLayer(layerId))
            {
                _layerOn[LayerIds.Ortho] = false;
                _layerOn[LayerIds.Thermal] = false;
                _layerOn[LayerIds.ThermalIr] = false;
            }

            _layerOn[layerId] = on;
            ApplyLayerGlobals();
        }

        private void OnLayerToggled(LayerToggled e) => SetLayer(e.LayerId, e.On);

        private static bool IsBaseLayer(string layerId) =>
            layerId == LayerIds.Ortho || layerId == LayerIds.Thermal || layerId == LayerIds.ThermalIr;

        private void CreateTileObject(TileData tile, float originHeight)
        {
            tile.BuildTextures();
            var info = tile.Info;
            var go = new GameObject($"Tile_{info.Z}_{info.X}_{info.Y}")
            {
                layer = gameObject.layer,
            };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = ZoneProjection.ToScene(tile.OriginEnuM);
            go.transform.localScale = new Vector3(tile.SizeM, 1f, tile.SizeM);

            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = terrainMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var block = new MaterialPropertyBlock();
            block.SetTexture(ShaderIds.HeightTex, tile.HeightTex);
            block.SetTexture(ShaderIds.NodataTex, tile.NodataTex);
            block.SetTexture(ShaderIds.HazardTex, tile.HazardTex);
            block.SetTexture(ShaderIds.LayerTex0, tile.LayerTex0 != null ? tile.LayerTex0 : _black);
            block.SetTexture(ShaderIds.LayerTex1, tile.LayerTex1 != null ? tile.LayerTex1 : _black);
            block.SetTexture(ShaderIds.LayerTex2, tile.LayerTex2 != null ? tile.LayerTex2 : _black);
            block.SetFloat(ShaderIds.HeightScale, (float)(info.Height.Scale * 65535.0));
            block.SetFloat(ShaderIds.HeightOffset, (float)(info.Height.Offset - originHeight));
            block.SetFloat(ShaderIds.MPerPx, (float)info.MPerPx);
            renderer.SetPropertyBlock(block);

            _tileObjects.Add(go);
            if (info.Z == 0)
            {
                _tilesByIndex[(info.X, info.Y)] = tile;
            }
        }

        private void Clear()
        {
            foreach (var go in _tileObjects)
            {
                Destroy(go);
            }

            _tileObjects.Clear();
            _tilesByIndex.Clear();
            _current?.Dispose();
            _current = null;
            ExtentEnu = Rect.zero;
        }

        private void SetDefaultGlobals()
        {
            Shader.SetGlobalFloat(ShaderIds.ContourIntervalM, contourIntervalM);
            Shader.SetGlobalFloat(ShaderIds.FogFarAmberMin, hazardAmberMin / 255f);
            Shader.SetGlobalFloat(ShaderIds.FogFarRedMin, hazardRedMin / 255f);
            Shader.SetGlobalColor(ShaderIds.FogFarColor, hazardColor);
            Shader.SetGlobalFloat(ShaderIds.FogFarStrength, hazardStrength);
            Shader.SetGlobalTexture(ShaderIds.HazardZoneTex, _black);
            ClearHeatField();
            ApplyLayerGlobals();
        }

        /// <summary>
        /// Uploads the hazard of the routing grid as <c>_HazardZoneTex</c> (C5): R8, one texel per
        /// node, rows from the south, with the same UV as <c>_HeatTex</c>. FX reads it for what it
        /// draws with its own geometry. Call it together with <see cref="SetHeatField"/>.
        /// </summary>
        public void SetHazardZone(byte[] hazardByNode, int width, int height)
        {
            if (hazardByNode == null || hazardByNode.Length != width * height)
            {
                throw new System.ArgumentException($"hazard grid must have {width * height} nodes", nameof(hazardByNode));
            }

            if (_hazardZone == null || _hazardZone.width != width || _hazardZone.height != height)
            {
                Destroy(_hazardZone);
                _hazardZone = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = "T_HazardZone",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            _hazardZone.SetPixelData(hazardByNode, 0);
            _hazardZone.Apply(false, false);
            Shader.SetGlobalTexture(ShaderIds.HazardZoneTex, _hazardZone);
        }

        /// <summary>Removes the heat field: no pixel shows heat until <see cref="SetHeatField"/> runs again.</summary>
        public void ClearHeatField()
        {
            Shader.SetGlobalTexture(ShaderIds.HeatTex, _noHeat);
            // A rect far away from any zone, so no pixel falls inside it.
            Shader.SetGlobalVector(ShaderIds.HeatTexRect, new Vector4(1e7f, 1e7f, 1f, 1f));
            Shader.SetGlobalFloat(ShaderIds.HeatMaxS, 1f);
        }

        /// <summary>
        /// Uploads the return-to-habitat field as <c>_HeatTex</c> (C5, "Propiedades globales"): one
        /// texel per grid node, <c>round(254 · min(t / maxS, 1))</c> and 255 where there is no way back.
        /// </summary>
        /// <param name="secondsByNode">Seconds per node, rows from the south; <c>float.PositiveInfinity</c> where unreachable.</param>
        /// <param name="node00EnuM">ENU of node (0, 0).</param>
        public void SetHeatField(float[] secondsByNode, int width, int height, float cellM, Vector2 node00EnuM, float maxS)
        {
            if (secondsByNode == null || secondsByNode.Length != width * height)
            {
                throw new System.ArgumentException($"heat field must have {width * height} nodes", nameof(secondsByNode));
            }

            if (maxS <= 0f)
            {
                foreach (var s in secondsByNode)
                {
                    if (!float.IsInfinity(s) && s > maxS)
                    {
                        maxS = s;
                    }
                }

                maxS = Mathf.Max(maxS, 1f);
            }

            var texels = new byte[secondsByNode.Length];
            for (var i = 0; i < texels.Length; i++)
            {
                var s = secondsByNode[i];
                texels[i] = float.IsInfinity(s) || float.IsNaN(s) ? (byte)255 : (byte)Mathf.RoundToInt(254f * Mathf.Min(s / maxS, 1f));
            }

            if (_heat == null || _heat.width != width || _heat.height != height)
            {
                Destroy(_heat);
                _heat = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = "T_Heat",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            _heat.SetPixelData(texels, 0);
            _heat.Apply(false, false);

            var e0 = node00EnuM.x - cellM * 0.5f;
            var n0 = node00EnuM.y - cellM * 0.5f;
            Shader.SetGlobalTexture(ShaderIds.HeatTex, _heat);
            Shader.SetGlobalVector(ShaderIds.HeatTexRect, new Vector4(e0, n0, 1f / (width * cellM), 1f / (height * cellM)));
            Shader.SetGlobalFloat(ShaderIds.HeatMaxS, maxS);
        }

        private void ApplyLayerGlobals()
        {
            Shader.SetGlobalFloat(ShaderIds.LayerOn0, _layerOn[LayerIds.Ortho] ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIds.LayerOn1, _layerOn[LayerIds.Thermal] ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIds.LayerOn2, _layerOn[LayerIds.ThermalIr] ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIds.TopoOn, _layerOn[LayerIds.Topo] ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIds.HazardOn, _layerOn[LayerIds.Hazard] ? 1f : 0f);
            Shader.SetGlobalFloat(ShaderIds.HeatOn, _layerOn[LayerIds.Heat] ? 1f : 0f);
        }

        /// <summary>A unit quad on XZ from (0, 0) to (1, 1), facing up, UV (0, 0) at the SW corner.</summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "TileQuad" };
            mesh.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Texture2D SolidTexture(byte value)
        {
            var texture = new Texture2D(1, 1, TextureFormat.R8, false, true) { name = $"T_Solid{value}" };
            texture.SetPixelData(new[] { value }, 0);
            texture.Apply(false, true);
            return texture;
        }
    }
}
