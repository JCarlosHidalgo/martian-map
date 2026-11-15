using System;
using System.Collections.Generic;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Zones generated in code, so the map runs on a PC before P1 delivers real tiles.
    /// <see cref="CreateFixtureLike"/> follows the description of <c>tile_synth_0_0_0</c> (C1);
    /// it is a preview, not the fixture: the fixture in <c>contracts/fixtures/</c> wins.
    /// <see cref="CreateDemo"/> is a crater field of several tiles to try pan, zoom and layers.
    /// </summary>
    public static class SyntheticZone
    {
        private const int S = TileData.Size;
        private const double DemoBaseHeightM = -4450.0;

        /// <summary>One 256 m tile: a ramp of 20 m west→east and 5 m south→north, asymmetric hazard and nodata.</summary>
        public static LoadedZone CreateFixtureLike()
        {
            const double scale = 0.001;
            var zone = NewZone("synth", "Test tile", 1, 1, layers: true);
            var height = new ushort[S * S];
            var hazard = new byte[S * S];
            var ortho = new byte[S * S];
            var thermal = new byte[S * S];
            var thermalIr = new byte[S * S];

            for (var row = 0; row < S; row++)
            {
                var north = (S - 1 - row) / (float)(S - 1);
                for (var col = 0; col < S; col++)
                {
                    var east = col / (float)(S - 1);
                    var i = row * S + col;
                    var nodata = col >= S - 16 && row >= S - 16;
                    height[i] = nodata ? TileData.HeightNodata : (ushort)Math.Round((20.0 * east + 5.0 * north) / scale);

                    var redRect = east > 0.6f && east < 0.85f && north > 0.6f && north < 0.85f;
                    var amberRect = east > 0.15f && east < 0.3f && north > 0.15f && north < 0.3f;
                    hazard[i] = nodata || redRect ? (byte)255 : amberRect ? (byte)128 : (byte)0;

                    ortho[i] = (byte)(east * 255f);
                    thermal[i] = (byte)(north * 255f);
                    thermalIr[i] = ((col / 32) + (row / 32)) % 2 == 0 ? (byte)230 : (byte)40;
                }
            }

            var tile = NewTile(zone, 0, 0, scale, 0.0, height, hazard, new Dictionary<string, byte[]>
            {
                [LayerIds.Ortho] = ortho,
                [LayerIds.Thermal] = thermal,
                [LayerIds.ThermalIr] = thermalIr,
            });
            return new LoadedZone(zone, new[] { tile }, DemoConditions(), string.Empty);
        }

        /// <summary>A procedural crater field of <paramref name="tilesX"/> × <paramref name="tilesY"/> tiles at 1 m/px.</summary>
        public static LoadedZone CreateDemo(int tilesX = 4, int tilesY = 3, int seed = 42)
        {
            var zone = NewZone("demo", "Crater field", tilesX, tilesY, layers: true);
            var widthM = tilesX * TileData.Cells;
            var heightM = tilesY * TileData.Cells;
            var field = new TerrainField(seed, widthM, heightM);

            // 1 cm steps, 655 m of range: -4750..-4095 m, around the floor of Gale (~-4450 m).
            const double scale = 0.01;
            const double offset = -4750.0;
            var tiles = new List<TileData>();
            for (var ty = 0; ty < tilesY; ty++)
            {
                for (var tx = 0; tx < tilesX; tx++)
                {
                    tiles.Add(BuildDemoTile(zone, field, tx, ty, scale, offset));
                }
            }

            return new LoadedZone(zone, tiles, DemoConditions(), string.Empty);
        }

        private static TileData BuildDemoTile(ZoneInfo zone, TerrainField field, int tx, int ty, double scale, double offset)
        {
            var height = new ushort[S * S];
            var hazard = new byte[S * S];
            var ortho = new byte[S * S];
            var thermal = new byte[S * S];
            var thermalIr = new byte[S * S];
            var slope = new byte[S * S];
            var roughness = new byte[S * S];
            var e0 = tx * TileData.Cells;
            var n0 = ty * TileData.Cells;

            for (var row = 0; row < S; row++)
            {
                var n = n0 + (S - 1 - row);
                for (var col = 0; col < S; col++)
                {
                    var e = e0 + col;
                    var i = row * S + col;
                    var h = DemoBaseHeightM + field.Height(e, n);
                    height[i] = (ushort)Mathf.Clamp((float)Math.Round((h - offset) / scale), 0f, 65534f);

                    var slopeDeg = field.SlopeDeg(e, n);
                    var rough = field.Roughness(e, n);
                    var albedo = field.Albedo(e, n);
                    var sun = field.SunFacing(e, n);
                    hazard[i] = slopeDeg > 15f || rough > 0.85f ? (byte)255 : slopeDeg > 11f ? (byte)110 : (byte)0;
                    slope[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(slopeDeg), 0, 255);
                    roughness[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(rough * 70f), 0, 255);

                    var shade = Mathf.Clamp01(0.55f + sun * 0.6f);
                    ortho[i] = (byte)(Mathf.Clamp01(shade * (0.75f + 0.25f * albedo)) * 255f);
                    thermal[i] = (byte)(Mathf.Clamp01(albedo * 0.7f + rough * 0.3f) * 255f);
                    thermalIr[i] = (byte)(Mathf.Clamp01(0.3f + sun * 0.5f + 0.2f * albedo) * 255f);
                }
            }

            return NewTile(zone, tx, ty, scale, offset, height, hazard, new Dictionary<string, byte[]>
            {
                [LayerIds.Ortho] = ortho,
                [LayerIds.Thermal] = thermal,
                [LayerIds.ThermalIr] = thermalIr,
            }, slope, roughness);
        }

        private static ZoneInfo NewZone(string id, string name, int tilesX, int tilesY, bool layers)
        {
            var zone = new ZoneInfo
            {
                SchemaVersion = 1,
                ZoneId = id,
                Name = name,
                Crs = "IAU_2015:49900",
                OriginLatDeg = -4.81,
                OriginLonDeg = 137.385,
                OriginHeightM = 0.0,
                Projection = "equirectangular_local",
                HabitatEnuM = new double[] { tilesX * TileData.Cells * 0.3, tilesY * TileData.Cells * 0.4 },
                ExtentEnuM = new double[] { 0, 0, tilesX * TileData.Cells, tilesY * TileData.Cells },
                BaseMPerPx = 1.0,
            };

            for (var y = 0; y < tilesY; y++)
            {
                for (var x = 0; x < tilesX; x++)
                {
                    zone.Tiles.Add(new ZoneTileRef { Z = 0, X = x, Y = y, Version = 1, Sha256 = string.Empty });
                }
            }

            if (layers)
            {
                zone.Layers.Add(new ZoneLayerInfo { Id = LayerIds.Ortho, SourceId = "synthetic", Qualitative = true });
                zone.Layers.Add(new ZoneLayerInfo
                {
                    Id = LayerIds.Thermal, SourceId = "synthetic", Qualitative = false,
                    Unit = "J m-2 K-1 s-1/2", Scale = 2.0, Offset = 100.0,
                });
                zone.Layers.Add(new ZoneLayerInfo { Id = LayerIds.ThermalIr, SourceId = "synthetic", Qualitative = true });
            }

            return zone;
        }

        private static TileData NewTile(ZoneInfo zone, int x, int y, double scale, double offset,
            ushort[] height, byte[] hazard, Dictionary<string, byte[]> layers, byte[] slope = null, byte[] roughness = null)
        {
            var info = new TileInfo
            {
                SchemaVersion = 1,
                ZoneId = zone.ZoneId,
                Z = 0,
                X = x,
                Y = y,
                SizePx = S,
                MPerPx = 1.0,
                OriginEnuM = new double[] { x * TileData.Cells, y * TileData.Cells },
                Height = new HeightSpec { Scale = scale, Offset = offset },
                Version = 1,
            };
            foreach (var id in layers.Keys)
            {
                info.Layers.Add(new TileLayerRef { Id = id, File = $"{id}.r8" });
            }

            return new TileData(info, height, hazard, layers, slope, roughness);
        }

        private static ConditionsInfo DemoConditions()
        {
            return new ConditionsInfo
            {
                SchemaVersion = 1,
                SourceId = "synthetic",
                TUtc = "2026-11-14T00:00:00Z",
                TValidUtc = "2026-11-14T00:00:00Z",
                Quantity = "dose_rate",
                Value = 6.3,
                Unit = "uGy/h",
                Uncertainty = 0.3,
                Basis = "modelled",
                DerivedFrom = "synthetic demo",
            };
        }

        /// <summary>
        /// Deterministic crater field: rolling hills plus bowl-shaped craters with rims. The height
        /// grid (1 m, whole zone) is computed once; slope and sun facing come from it.
        /// </summary>
        private sealed class TerrainField
        {
            private readonly int _w;
            private readonly int _h;
            private readonly float[] _grid;

            public TerrainField(int seed, int widthM, int heightM)
            {
                _w = widthM + 1;
                _h = heightM + 1;
                _grid = new float[_w * _h];
                for (var n = 0; n < _h; n++)
                {
                    for (var e = 0; e < _w; e++)
                    {
                        _grid[n * _w + e] = 25f * Fbm(e * 0.0025f, n * 0.0025f, 5) + 4f * Fbm(e * 0.03f + 11f, n * 0.03f - 7f, 3);
                    }
                }

                var random = new System.Random(seed);
                var count = Mathf.Max(6, widthM * heightM / 18000);
                for (var i = 0; i < count; i++)
                {
                    var radius = (float)(8 + Math.Pow(random.NextDouble(), 3) * 90);
                    AddCrater((float)random.NextDouble() * widthM, (float)random.NextDouble() * heightM, radius);
                }
            }

            public float Height(int e, int n) => _grid[Mathf.Clamp(n, 0, _h - 1) * _w + Mathf.Clamp(e, 0, _w - 1)];

            public float SlopeDeg(int e, int n)
            {
                Gradient(e, n, out var dx, out var dy);
                return Mathf.Atan(Mathf.Sqrt(dx * dx + dy * dy)) * Mathf.Rad2Deg;
            }

            public float Roughness(float e, float n) => Mathf.Clamp01(0.5f + 0.5f * Fbm(e * 0.08f + 3f, n * 0.08f + 5f, 2));

            public float Albedo(float e, float n) => Mathf.Clamp01(0.5f + 0.5f * Fbm(e * 0.006f - 20f, n * 0.006f + 9f, 4));

            /// <summary>-1..1, how much the slope faces a sun in the north-west.</summary>
            public float SunFacing(int e, int n)
            {
                Gradient(e, n, out var dx, out var dy);
                return Mathf.Clamp((dx - dy) * 0.7f, -1f, 1f);
            }

            private void Gradient(int e, int n, out float dx, out float dy)
            {
                dx = (Height(e + 1, n) - Height(e - 1, n)) * 0.5f;
                dy = (Height(e, n + 1) - Height(e, n - 1)) * 0.5f;
            }

            private void AddCrater(float cx, float cy, float radius)
            {
                var reach = Mathf.CeilToInt(radius * 2f);
                var depth = radius * 0.22f;
                for (var n = Mathf.Max(0, (int)cy - reach); n <= Mathf.Min(_h - 1, (int)cy + reach); n++)
                {
                    for (var e = Mathf.Max(0, (int)cx - reach); e <= Mathf.Min(_w - 1, (int)cx + reach); e++)
                    {
                        var dx = e - cx;
                        var dy = n - cy;
                        var d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                        if (d > 2f)
                        {
                            continue;
                        }

                        var bowl = d < 1f ? (d * d - 1f) * depth : 0f;
                        var rim = Mathf.Exp(-(d - 1f) * (d - 1f) * 18f) * depth * 0.35f;
                        _grid[n * _w + e] += bowl + rim;
                    }
                }
            }

            private static float Fbm(float x, float y, int octaves)
            {
                var sum = 0f;
                var amplitude = 0.5f;
                for (var i = 0; i < octaves; i++)
                {
                    sum += amplitude * (Mathf.PerlinNoise(x, y) * 2f - 1f);
                    x *= 2.03f;
                    y *= 2.03f;
                    amplitude *= 0.5f;
                }

                return sum;
            }
        }
    }
}
