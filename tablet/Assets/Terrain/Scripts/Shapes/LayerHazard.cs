using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Brings the zone's layers into routing without touching Routing's cost formula: Terrain turns
    /// them into amber hazard, merged into the single hazard raster (max), which Routing already
    /// penalizes (<c>amber_penalty_s_per_m</c>) and FX already shows.
    /// <list type="bullet">
    /// <item><b>Thermal inertia</b> (quantitative, J m⁻² K⁻¹ s⁻½): below
    /// <see cref="SoftThermalInertia"/> the surface is fine, unconsolidated material (dust, loose
    /// sand), where a walker sinks and slips: amber.</item>
    /// <item><b>Thermal IR</b> (qualitative), only when the zone has no thermal inertia: in daytime IR,
    /// loose material heats fastest and looks brightest, so the brightest
    /// <see cref="BrightIrFraction"/> of the zone is amber.</item>
    /// <item><b>Optical</b> is not used: its brightness follows illumination and shadows, not
    /// whether the ground can be walked, so it would mark shadows as hazard.</item>
    /// </list>
    /// A layer sample of 0 means no data and never adds hazard. Thresholds are design inferences.
    /// </summary>
    public static class LayerHazard
    {
        /// <summary>Thermal inertia below which the ground counts as soft (J m⁻² K⁻¹ s⁻½).</summary>
        public const double SoftThermalInertia = 150.0;

        /// <summary>Fraction of the brightest thermal IR samples that counts as soft ground.</summary>
        public const float BrightIrFraction = 0.10f;

        /// <summary>The hazard of soft ground: amber, crossable with a penalty.</summary>
        public const byte SoftGround = 128;

        /// <summary>Sets every z = 0 tile's layer hazard; returns the tiles whose hazard changed.</summary>
        public static List<TileData> Apply(LoadedZone zone)
        {
            var tiles = zone.Tiles.Where(t => t.Info.Z == 0).ToList();
            var thermal = zone.Zone.Layers.Find(l => l.Id == LayerIds.Thermal && !l.Qualitative && l.Scale.HasValue && l.Offset.HasValue && l.Scale.Value > 0);
            var hasIr = zone.Zone.Layers.Exists(l => l.Id == LayerIds.ThermalIr);
            var changed = new List<TileData>();
            var marked = 0;

            if (thermal != null)
            {
                // TI = offset + scale · sample, so TI < threshold ⇔ sample < (threshold − offset) / scale.
                var limit = (SoftThermalInertia - thermal.Offset.Value) / thermal.Scale.Value;
                foreach (var tile in tiles)
                {
                    var overlay = Mark(tile, LayerIds.Thermal, sample => sample < limit, ref marked);
                    if (tile.SetLayerHazard(overlay))
                    {
                        changed.Add(tile);
                    }
                }

                Debug.Log($"[Terrain] layer hazard zone_id={zone.Zone.ZoneId} source=thermal_inertia below={SoftThermalInertia} samples_amber={marked}");
                return changed;
            }

            if (hasIr)
            {
                var threshold = BrightThreshold(tiles);
                foreach (var tile in tiles)
                {
                    var overlay = threshold > 0 ? Mark(tile, LayerIds.ThermalIr, sample => sample >= threshold, ref marked) : null;
                    if (tile.SetLayerHazard(overlay))
                    {
                        changed.Add(tile);
                    }
                }

                Debug.Log($"[Terrain] layer hazard zone_id={zone.Zone.ZoneId} source=thermal_ir brightest={BrightIrFraction:P0} threshold={threshold} samples_amber={marked}");
                return changed;
            }

            foreach (var tile in tiles)
            {
                if (tile.SetLayerHazard(null))
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

        /// <summary>The IR sample value above which the brightest fraction of the zone lies (0: no data).</summary>
        private static int BrightThreshold(IReadOnlyList<TileData> tiles)
        {
            var histogram = new long[256];
            long total = 0;
            foreach (var tile in tiles)
            {
                if (!tile.Layers.TryGetValue(LayerIds.ThermalIr, out var samples))
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

            var target = (long)Math.Ceiling(total * BrightIrFraction);
            long count = 0;
            for (var value = 255; value > 0; value--)
            {
                count += histogram[value];
                if (count >= target)
                {
                    return value;
                }
            }

            return 1;
        }
    }
}
