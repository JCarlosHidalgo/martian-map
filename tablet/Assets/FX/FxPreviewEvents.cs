using System;
using System.Threading.Tasks;
using MartianMap.Routing;
using MartianMap.Terrain;
using UnityEngine;

namespace MartianMap.FX
{
    /// <summary>Fixture-driven event source for the FX sandbox. Never add this to Main.unity.</summary>
    public sealed class FxPreviewEvents : MonoBehaviour
    {
        private Texture2D _mockHazard;
        private Texture2D _mockHeat;

        private async void Start()
        {
            try
            {
                Application.targetFrameRate = 30;
                // Let Terrain's synthetic zone load before sending a mock route.
                var deadline = Time.realtimeSinceStartup + 10f;
                TerrainMap2D map;
                do
                {
                    map = FindFirstObjectByType<TerrainMap2D>();
                    if (map != null && map.Current != null) break;
                    await Task.Yield();
                } while (Time.realtimeSinceStartup < deadline);

                BuildMockTextures();
                MapEvents.Emit(new LayerToggled(LayerIds.Hazard, true));
                MapEvents.Emit(new LayerToggled(LayerIds.Heat, true));
                var route = await FixtureLoader.LoadExpectedRouteAsync();
                var plans = await FixtureLoader.LoadExpectedPlansAsync();
                MapEvents.Emit(new RouteSelected(plans, Alternative.Fastest, RouteSelected.ScenePolyline(route)));
            }
            catch (Exception error)
            {
                Debug.LogError($"[FX] Preview fixtures unavailable: {error}", this);
            }
        }

        private void OnDestroy()
        {
            if (_mockHazard != null) Destroy(_mockHazard);
            if (_mockHeat != null) Destroy(_mockHeat);
        }

        private void BuildMockTextures()
        {
            const int size = 129;
            var hazard = new byte[size * size];
            var heat = new byte[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var e = -128 + 2 * x;
                    var n = -128 + 2 * y;
                    var red = (e - 70) * (e - 70) + (n - 40) * (n - 40) < 25 * 25;
                    var amber = (e + 75) * (e + 75) + (n + 70) * (n + 70) < 20 * 20;
                    hazard[y * size + x] = red ? (byte)255 : amber ? (byte)128 : (byte)0;
                    heat[y * size + x] = (byte)Mathf.Clamp(
                        Mathf.RoundToInt(Vector2.Distance(new Vector2(e, n), new Vector2(-100f, -20f)) * 0.75f),
                        0, 254);
                }
            }

            _mockHazard = CreateR8("FX preview hazard", size, hazard);
            _mockHeat = CreateR8("FX preview heat", size, heat);
            Shader.SetGlobalTexture(ShaderIds.HazardZoneTex, _mockHazard);
            Shader.SetGlobalTexture(ShaderIds.HeatTex, _mockHeat);
            Shader.SetGlobalVector(ShaderIds.HeatTexRect, new Vector4(-129f, -129f, 1f / 258f, 1f / 258f));
        }

        private static Texture2D CreateR8(string name, int size, byte[] values)
        {
            var texture = new Texture2D(size, size, TextureFormat.R8, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixelData(values, 0);
            texture.Apply(false, true);
            return texture;
        }
    }
}
