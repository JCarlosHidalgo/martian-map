using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Brings the zone's layers into routing without touching Routing's cost formula: Terrain turns
    /// them into amber hazard, merged into the single hazard raster (max), which Routing already
    /// penalizes (<c>amber_penalty_s_per_m</c>) and FX already shows. Low thermal inertia means
    /// fine, unconsolidated material (dust, loose sand) where a walker sinks and slips.
    /// <list type="bullet">
    /// <item><b>Thermal inertia, quantitative</b> (with scale and offset, J m⁻² K⁻¹ s⁻½): below
    /// <see cref="SoftThermalInertia"/> is amber.</item>
    /// <item><b>Thermal inertia, qualitative</b> (a relative 0–255 stretch, such as THEMIS qualTI;
    /// brighter = higher inertia): the lowest <see cref="SoftFraction"/> of the zone is amber.</item>
    /// <item><b>Thermal IR</b>, only when the zone has no thermal inertia. It is taken as nighttime
    /// IR (the THEMIS product used as a thermal inertia proxy): loose material keeps little heat and
    /// is coldest at night, so the darkest <see cref="SoftFraction"/> of the zone is amber.</item>
    /// <item><b>Optical</b> is not used: its brightness follows illumination and shadows, not
    /// whether the ground can be walked, so it would mark shadows as hazard.</item>
    /// </list>
    /// A layer sample of 0 means no data and never adds hazard. Thresholds are design inferences.
    /// </summary>
    public static class LayerHazard
    {
        /// <summary>Thermal inertia below which the ground counts as soft (J m⁻² K⁻¹ s⁻½).</summary>
        public const double SoftThermalInertia = 150.0;

        /// <summary>Fraction of the zone with the lowest relative thermal inertia (or coldest night IR) that counts as soft.</summary>
        public const float SoftFraction = 0.10f;

        /// <summary>The hazard of soft ground: amber, crossable with a penalty.</summary>
        public const byte SoftGround = 128;

        /// <summary>Sets every z = 0 tile's layer hazard; returns the tiles whose hazard changed.</summary>
        public static List<TileData> Apply(LoadedZone zone)
        {
            var tiles = zone.Tiles.Where(t => t.Info.Z == 0).ToList();
            var thermal = zone.Zone.Layers.Find(l => l.Id == LayerIds.Thermal);
            var quantitative = thermal != null && !thermal.Qualitative && thermal.Scale.HasValue && thermal.Offset.HasValue && thermal.Scale.Value > 0;
            var marked = 0;

            if (quantitative)
            {
                // TI = offset + scale · sample, so TI < threshold ⇔ sample < (threshold − offset) / scale.
                var limit = (SoftThermalInertia - thermal.Offset.Value) / thermal.Scale.Value;
                var changed = Set(tiles, LayerIds.Thermal, sample => sample < limit, ref marked);
                Debug.Log($"[Terrain] layer hazard zone_id={zone.Zone.ZoneId} source=thermal_inertia below={SoftThermalInertia} samples_amber={marked}");
                return changed;
            }

            var layerId = thermal != null ? LayerIds.Thermal
                : zone.Zone.Layers.Exists(l => l.Id == LayerIds.ThermalIr) ? LayerIds.ThermalIr
                : null;
            if (layerId != null)
            {
                var threshold = LowThreshold(tiles, layerId);
                var changed = Set(tiles, layerId, sample => threshold > 0 && sample <= threshold, ref marked);
                Debug.Log($"[Terrain] layer hazard zone_id={zone.Zone.ZoneId} source={layerId} lowest={SoftFraction:P0} threshold={threshold} samples_amber={marked}");
                return changed;
            }

            return Set(tiles, null, null, ref marked);
        }

        private static List<TileData> Set(IEnumerable<TileData> tiles, string layerId, Func<int, bool> soft, ref int marked)
        {
            var changed = new List<TileData>();
            foreach (var tile in tiles)
            {
                var overlay = layerId != null ? Mark(tile, layerId, soft, ref marked) : null;
                if (tile.SetLayerHazard(overlay))
                {
                    changed.Add(tile);
                }
            }

            return changed;
        }

        private static byte[] Mark(TileData tile, string layerId, Func<int, bool> soft, ref int marked)
        {
            if (!tile.Layers.TryGetValue(layerId, out var samples))
            {
                return null;
            }

            var overlay = new byte[samples.Length];
            var any = false;
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                if (sample != 0 && soft(sample))
                {
                    overlay[i] = SoftGround;
                    marked++;
                    any = true;
                }
            }

            return any ? overlay : null;
        }

        /// <summary>The sample value at or below which the lowest <see cref="SoftFraction"/> of the zone lies (0: no data).</summary>
        private static int LowThreshold(IReadOnlyList<TileData> tiles, string layerId)
        {
            var histogram = new long[256];
            long total = 0;
            foreach (var tile in tiles)
            {
                if (!tile.Layers.TryGetValue(layerId, out var samples))
                {
                    continue;
                }

                foreach (var sample in samples)
                {
                    if (sample != 0)
                    {
                        histogram[sample]++;
                        total++;
                    }
                }
            }

            if (total == 0)
            {
                return 0;
            }

            var target = (long)Math.Ceiling(total * SoftFraction);
            long count = 0;
            for (var value = 1; value < 256; value++)
            {
                count += histogram[value];
                if (count >= target)
                {
                    return value;
                }
            }

            return 255;
        }
    }
}
