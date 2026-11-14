using UnityEngine;
using UnityEngine.InputSystem;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Keyboard shortcuts to test Terrain in <c>TerrainSandbox.unity</c> only, with nothing drawn on
    /// screen: the HUD and its toggles are P6's. Layers go through <see cref="MapEvents.LayerToggled"/>,
    /// as the HUD will send them.
    /// <list type="bullet">
    /// <item>1 topo · 2 hazard · 3 heat · 4 ortho · 5 thermal · 6 thermal_ir</item>
    /// <item>H: fake heat field from the habitat (straight line at 1 m/s, a stand-in for ReturnField)</item>
    /// <item>Z: demo zone · X: fixture-like tile</item>
    /// </list>
    /// </summary>
    public sealed class TerrainSandboxKeys : MonoBehaviour
    {
        [SerializeField] private TerrainMap2D map;
        [SerializeField] private TerrainBootstrap bootstrap;

        private static readonly (Key key, string layer)[] LayerKeys =
        {
            (Key.Digit1, LayerIds.Topo),
            (Key.Digit2, LayerIds.Hazard),
            (Key.Digit3, LayerIds.Heat),
            (Key.Digit4, LayerIds.Ortho),
            (Key.Digit5, LayerIds.Thermal),
            (Key.Digit6, LayerIds.ThermalIr),
        };

        private void Awake()
        {
            if (map == null)
            {
                map = FindAnyObjectByType<TerrainMap2D>();
            }

            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<TerrainBootstrap>();
            }

            Debug.Log("[Terrain] sandbox keys: 1-6 layers, H fake heat, Z demo zone, X fixture tile");
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || map == null)
            {
                return;
            }

            foreach (var (key, layer) in LayerKeys)
            {
                if (keyboard[key].wasPressedThisFrame)
                {
                    var on = map.LayerStates.TryGetValue(layer, out var state) && state;
                    MapEvents.Emit(new LayerToggled(layer, !on));
                }
            }

            if (keyboard.hKey.wasPressedThisFrame && map.Current != null)
            {
                FakeHeatField(map.Current.Zone);
            }

            if (bootstrap != null && keyboard.zKey.wasPressedThisFrame)
            {
                _ = bootstrap.LoadAsync(TerrainBootstrap.ZoneSource.Demo);
            }

            if (bootstrap != null && keyboard.xKey.wasPressedThisFrame)
            {
                _ = bootstrap.LoadAsync(TerrainBootstrap.ZoneSource.FixtureLike);
            }
        }

        private void FakeHeatField(ZoneInfo zone)
        {
            const float cellM = 2f;
            var extent = zone.ExtentEnuM;
            var width = Mathf.Max(1, Mathf.CeilToInt((float)(extent[2] - extent[0]) / cellM) + 1);
            var height = Mathf.Max(1, Mathf.CeilToInt((float)(extent[3] - extent[1]) / cellM) + 1);
            var home = new Vector2((float)zone.HabitatEnuM[0], (float)zone.HabitatEnuM[1]);
            var node00 = new Vector2((float)extent[0], (float)extent[1]);
            var seconds = new float[width * height];
            for (var r = 0; r < height; r++)
            {
                for (var c = 0; c < width; c++)
                {
                    seconds[r * width + c] = Vector2.Distance(node00 + new Vector2(c * cellM, r * cellM), home);
                }
            }

            map.SetHeatField(seconds, width, height, cellM, node00, 0f);
            MapEvents.Emit(new LayerToggled(LayerIds.Heat, true));
        }
    }
}
