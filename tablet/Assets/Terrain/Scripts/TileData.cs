using System;
using System.Collections.Generic;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// One C1 tile in memory: the raw rasters as they come in the files (row 0 = north edge) and the
    /// GPU textures built from them (row 0 = south edge, so UV (0, 0) is the SW corner).
    /// </summary>
    public sealed class TileData : IDisposable
    {
        public const int Size = 257;
        public const int Cells = Size - 1;
        public const ushort HeightNodata = 65535;

        /// <param name="slope">uint8 degrees (C1 <c>slope.r8</c>); zeros when the tile has none.</param>
        /// <param name="roughness">uint8 cm (C1 <c>roughness.r8</c>); zeros when the tile has none.</param>
        public TileData(TileInfo info, ushort[] height, byte[] hazard, IReadOnlyDictionary<string, byte[]> layers,
            byte[] slope = null, byte[] roughness = null)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Height = Check(height, nameof(height));
            BaseHazard = Check(hazard, nameof(hazard));
            Hazard = (byte[])BaseHazard.Clone();
            Layers = layers ?? new Dictionary<string, byte[]>();
            Slope = slope != null ? Check(slope, nameof(slope)) : new byte[Size * Size];
            Roughness = roughness != null ? Check(roughness, nameof(roughness)) : new byte[Size * Size];
            foreach (var pair in Layers)
            {
                Check(pair.Value, pair.Key);
            }

            ComputeRange();
        }

        private byte[] _shapeHazard;
        private byte[] _editHazard;

        public TileInfo Info { get; }

        /// <summary>uint16 samples, north-up rows; <see cref="HeightNodata"/> where there is no data.</summary>
        public ushort[] Height { get; }

        /// <summary>uint8 hazard 0–255 as Earth wrote it, north-up rows.</summary>
        public byte[] BaseHazard { get; }

        /// <summary>The effective hazard: <c>max(base, P5 shapes, edits)</c>, north-up rows.</summary>
        public byte[] Hazard { get; }

        /// <summary>uint8 layers by <c>layer_id</c>, north-up rows.</summary>
        public IReadOnlyDictionary<string, byte[]> Layers { get; }

        /// <summary>uint8 slope in degrees, north-up rows (Routing input).</summary>
        public byte[] Slope { get; }

        /// <summary>uint8 relief in cm, north-up rows (Routing input).</summary>
        public byte[] Roughness { get; }

        public float MinHeightM { get; private set; }
        public float MaxHeightM { get; private set; }
        public bool HasData { get; private set; }

        public float SizeM => (float)(Cells * Info.MPerPx);
        public Vector2 OriginEnuM => new Vector2((float)Info.OriginEnuM[0], (float)Info.OriginEnuM[1]);

        public Texture2D HeightTex { get; private set; }
        public Texture2D NodataTex { get; private set; }
        public Texture2D HazardTex { get; private set; }
        public Texture2D LayerTex0 { get; private set; }
        public Texture2D LayerTex1 { get; private set; }
        public Texture2D LayerTex2 { get; private set; }

        /// <summary>Height above the areoid at a sample, or false at nodata. Row 0 is north.</summary>
        public bool TryHeightAtSample(int row, int col, out float metres)
        {
            var raw = Height[row * Size + col];
            if (raw == HeightNodata)
            {
                metres = 0f;
                return false;
            }

            metres = (float)(Info.Height.Offset + Info.Height.Scale * raw);
            return true;
        }

        /// <summary>Bilinear height above the areoid at a local position in metres from the SW corner.</summary>
        public bool TryHeightAtLocal(float eastM, float northM, out float metres)
        {
            var mpp = (float)Info.MPerPx;
            var c = Mathf.Clamp(eastM / mpp, 0f, Cells);
            var rFromSouth = Mathf.Clamp(northM / mpp, 0f, Cells);
            var c0 = Mathf.Min((int)c, Cells - 1);
            var s0 = Mathf.Min((int)rFromSouth, Cells - 1);
            var fc = c - c0;
            var fs = rFromSouth - s0;

            // North-up rows: south index s maps to row Cells - s.
            if (!TryHeightAtSample(Cells - s0, c0, out var h00) ||
                !TryHeightAtSample(Cells - s0, c0 + 1, out var h10) ||
                !TryHeightAtSample(Cells - s0 - 1, c0, out var h01) ||
                !TryHeightAtSample(Cells - s0 - 1, c0 + 1, out var h11))
            {
                metres = 0f;
                return false;
            }

            metres = Mathf.Lerp(Mathf.Lerp(h00, h10, fc), Mathf.Lerp(h01, h11, fc), fs);
            return true;
        }

        /// <summary>
        /// P5's hazard shapes for this tile (rasterized), or null to remove them. The effective hazard
        /// is <c>max(base, shapes, edits)</c>: one raster for fog and routing. Main thread.
        /// </summary>
        public void SetHazardOverlay(byte[] overlay)
        {
            _shapeHazard = overlay != null ? Check(overlay, nameof(overlay)) : null;
            RecomputeHazard();
        }

        /// <summary>
        /// The hazard of the live edits on this tile, as <c>Edits.EditEngine.Apply</c> returns it
        /// (already <c>max(base, entities)</c>), or null when there are none. Main thread.
        /// </summary>
        /// <returns>True if the effective hazard changed.</returns>
        public bool SetEditHazard(byte[] editHazard)
        {
            _editHazard = editHazard != null ? Check(editHazard, nameof(editHazard)) : null;
            return RecomputeHazard();
        }

        private bool RecomputeHazard()
        {
            var changed = false;
            for (var i = 0; i < Hazard.Length; i++)
            {
                var value = BaseHazard[i];
                if (_shapeHazard != null && _shapeHazard[i] > value)
                {
                    value = _shapeHazard[i];
                }

                if (_editHazard != null && _editHazard[i] > value)
                {
                    value = _editHazard[i];
                }

                if (Hazard[i] != value)
                {
                    Hazard[i] = value;
                    changed = true;
                }
            }

            if (changed && HazardTex != null)
            {
                HazardTex.SetPixelData(FlipRows(Hazard), 0);
                HazardTex.Apply(false, false);
            }

            return changed;
        }
        }

        /// <summary>Builds the textures once; call on the main thread.</summary>
        public void BuildTextures()
        {
            if (HeightTex != null)
            {
                return;
            }

            BuildHeightTextures();
            HazardTex = BuildR8(Hazard, "T_Hazard", keepReadable: true);
            LayerTex0 = BuildLayer(LayerIds.Ortho);
            LayerTex1 = BuildLayer(LayerIds.Thermal);
            LayerTex2 = BuildLayer(LayerIds.ThermalIr);
        }

        public void Dispose()
        {
            Destroy(HeightTex);
            Destroy(NodataTex);
            Destroy(HazardTex);
            Destroy(LayerTex0);
            Destroy(LayerTex1);
            Destroy(LayerTex2);
            HeightTex = NodataTex = HazardTex = LayerTex0 = LayerTex1 = LayerTex2 = null;
        }

        private void ComputeRange()
        {
            var min = ushort.MaxValue;
            var max = ushort.MinValue;
            foreach (var raw in Height)
            {
                if (raw == HeightNodata)
                {
                    continue;
                }

                if (raw < min)
                {
                    min = raw;
                }

                if (raw > max)
                {
                    max = raw;
                }
            }

            HasData = min <= max;
            if (!HasData)
            {
                MinHeightM = MaxHeightM = (float)Info.Height.Offset;
                return;
            }

            MinHeightM = (float)(Info.Height.Offset + Info.Height.Scale * min);
            MaxHeightM = (float)(Info.Height.Offset + Info.Height.Scale * max);
        }

        /// <summary>
        /// The height texture keeps the C1 values; nodata samples are filled with the nearest valid
        /// value on their row so bilinear shading does not spike, and the mask goes to <see cref="NodataTex"/>.
        /// </summary>
        private void BuildHeightTextures()
        {
            var filled = new ushort[Height.Length];
            var mask = new byte[Height.Length];
            var fallback = HasData
                ? (ushort)Math.Round((MinHeightM - Info.Height.Offset) / Info.Height.Scale)
                : (ushort)0;

            for (var row = 0; row < Size; row++)
            {
                var last = fallback;
                for (var col = 0; col < Size; col++)
                {
                    var i = row * Size + col;
                    var raw = Height[i];
                    if (raw == HeightNodata)
                    {
                        mask[i] = 255;
                        filled[i] = last;
                    }
                    else
                    {
                        filled[i] = raw;
                        last = raw;
                    }
                }
            }

            var flipped = FlipRows(filled);
            if (SystemInfo.SupportsTextureFormat(TextureFormat.R16))
            {
                HeightTex = new Texture2D(Size, Size, TextureFormat.R16, false, true);
                HeightTex.SetPixelData(flipped, 0);
            }
            else
            {
                // Fallback (C5 plan B of E1): same 0–1 meaning, stored as float.
                var floats = new float[flipped.Length];
                for (var i = 0; i < flipped.Length; i++)
                {
                    floats[i] = flipped[i] / 65535f;
                }

                HeightTex = new Texture2D(Size, Size, TextureFormat.RFloat, false, true);
                HeightTex.SetPixelData(floats, 0);
            }

            Configure(HeightTex, "T_Height", FilterMode.Bilinear);
            HeightTex.Apply(false, true);
            NodataTex = BuildR8(mask, "T_Nodata", FilterMode.Point);
        }

        private Texture2D BuildLayer(string layerId)
        {
            return Layers.TryGetValue(layerId, out var data) ? BuildR8(data, $"T_{layerId}") : null;
        }

        private static Texture2D BuildR8(byte[] northUp, string name, FilterMode filter = FilterMode.Bilinear, bool keepReadable = false)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.R8, false, true);
            texture.SetPixelData(FlipRows(northUp), 0);
            Configure(texture, name, filter);
            texture.Apply(false, !keepReadable);
            return texture;
        }

        private static void Configure(Texture2D texture, string name, FilterMode filter)
        {
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = filter;
            texture.anisoLevel = 0;
        }

        private static T[] FlipRows<T>(T[] northUp)
        {
            var southUp = new T[northUp.Length];
            for (var row = 0; row < Size; row++)
            {
                Array.Copy(northUp, row * Size, southUp, (Size - 1 - row) * Size, Size);
            }

            return southUp;
        }

        private static T[] Check<T>(T[] samples, string name)
        {
            if (samples == null || samples.Length != Size * Size)
            {
                throw new ArgumentException($"{name} must have {Size * Size} samples, got {samples?.Length ?? 0}", name);
            }

            return samples;
        }

        private static void Destroy(UnityEngine.Object o)
        {
            if (o == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(o);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
