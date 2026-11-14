using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>A hazard area from P5, in the zone's ENU metres, with its hazard value (0–255).</summary>
    public sealed class HazardPolygon
    {
        public HazardPolygon(IReadOnlyList<Vector2[]> ringsEnuM, byte value)
        {
            RingsEnuM = ringsEnuM;
            Value = value;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var ring in ringsEnuM)
            {
                foreach (var p in ring)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }

            BoundsEnuM = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public IReadOnlyList<Vector2[]> RingsEnuM { get; }
        public byte Value { get; }
        public Rect BoundsEnuM { get; }
    }

    /// <summary>
    /// Hazard areas that P5 delivers as a Shapefile, merged into the single hazard raster of each
    /// tile with <c>max(base, shape)</c>, so the far fog, the near fog and routing all see them.
    /// <para>Agreed exchange (P4 ↔ P5, to confirm in C5):</para>
    /// <list type="bullet">
    /// <item>Files: <c>StreamingAssets/hazard_shapes/{zone_id}.shp</c>, with <c>.dbf</c> and optional <c>.prj</c>.</item>
    /// <item>Geometry: Polygon, PolygonZ or PolygonM; rings even-odd (holes allowed).</item>
    /// <item>Coordinates: the zone's ENU metres (x = east, y = north) without a .prj or with a projected one;
    /// longitude/latitude in degrees (IAU_2015:49900) when the .prj is geographic.</item>
    /// <item>Value: numeric field <c>HAZARD</c> (0–255), or text field <c>LEVEL</c> = <c>amber</c> (128) /
    /// <c>red</c> (255); 255 when neither is present.</item>
    /// </list>
    /// </summary>
    public static class HazardShapes
    {
        public const string FolderName = "hazard_shapes";
        private const byte Amber = 128;
        private const byte Red = 255;

        /// <summary>Loads the hazard shapes of a zone from StreamingAssets; empty when there are none.</summary>
        public static async Task<List<HazardPolygon>> LoadForZoneAsync(ZoneInfo zone)
        {
            var basePath = LocalFiles.Combine(Application.streamingAssetsPath, FolderName, zone.ZoneId);
            var shp = await LocalFiles.TryReadBytesAsync(basePath + ".shp");
            if (shp == null)
            {
                return new List<HazardPolygon>();
            }

            var dbf = await LocalFiles.TryReadBytesAsync(basePath + ".dbf");
            var prjBytes = await LocalFiles.TryReadBytesAsync(basePath + ".prj");
            var geographic = prjBytes != null && ShapefileReader.IsGeographic(Encoding.UTF8.GetString(prjBytes));

            var result = new List<HazardPolygon>();
            foreach (var shape in ShapefileReader.ReadPolygons(shp, dbf))
            {
                var rings = new List<Vector2[]>();
                foreach (var ring in shape.Rings)
                {
                    if (ring.Length < 3)
                    {
                        continue;
                    }

                    var enu = new Vector2[ring.Length];
                    for (var i = 0; i < ring.Length; i++)
                    {
                        // Geographic: x = longitude, y = latitude.
                        enu[i] = geographic ? ZoneProjection.ToEnu(zone, ring[i].y, ring[i].x) : ring[i];
                    }

                    rings.Add(enu);
                }

                if (rings.Count > 0)
                {
                    result.Add(new HazardPolygon(rings, ValueOf(shape.Attributes)));
                }
            }

            Debug.Log($"[Terrain] hazard shapes loaded zone_id={zone.ZoneId} polygons={result.Count} geographic={geographic}");
            return result;
        }

        /// <summary>Merges the polygons into each tile's hazard; returns the tiles that changed.</summary>
        public static List<TileData> Apply(IReadOnlyList<TileData> tiles, IReadOnlyList<HazardPolygon> polygons)
        {
            var changed = new List<TileData>();
            foreach (var tile in tiles)
            {
                var overlay = Rasterize(tile, polygons);
                tile.SetHazardOverlay(overlay);
                if (overlay != null)
                {
                    changed.Add(tile);
                }
            }

            return changed;
        }

        /// <summary>Scanline fill of the polygons on the tile's samples (row 0 = north); null if none touch it.</summary>
        public static byte[] Rasterize(TileData tile, IReadOnlyList<HazardPolygon> polygons)
        {
            var origin = tile.OriginEnuM;
            var mpp = (float)tile.Info.MPerPx;
            var tileRect = new Rect(origin.x, origin.y, tile.SizeM, tile.SizeM);
            byte[] overlay = null;
            var crossings = new List<float>();

            foreach (var polygon in polygons)
            {
                if (!polygon.BoundsEnuM.Overlaps(tileRect))
                {
                    continue;
                }

                overlay ??= new byte[TileData.Size * TileData.Size];
                for (var row = 0; row < TileData.Size; row++)
                {
                    var n = origin.y + (TileData.Cells - row) * mpp;
                    if (n < polygon.BoundsEnuM.yMin || n > polygon.BoundsEnuM.yMax)
                    {
                        continue;
                    }

                    crossings.Clear();
                    foreach (var ring in polygon.RingsEnuM)
                    {
                        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                        {
                            var a = ring[j];
                            var b = ring[i];
                            if ((a.y > n) != (b.y > n))
                            {
                                crossings.Add(a.x + (n - a.y) * (b.x - a.x) / (b.y - a.y));
                            }
                        }
                    }

                    crossings.Sort();
                    for (var k = 0; k + 1 < crossings.Count; k += 2)
                    {
                        var c0 = Mathf.Max(0, Mathf.CeilToInt((crossings[k] - origin.x) / mpp));
                        var c1 = Mathf.Min(TileData.Cells, Mathf.FloorToInt((crossings[k + 1] - origin.x) / mpp));
                        for (var col = c0; col <= c1; col++)
                        {
                            var index = row * TileData.Size + col;
                            if (polygon.Value > overlay[index])
                            {
                                overlay[index] = polygon.Value;
                            }
                        }
                    }
                }
            }

            return overlay;
        }

        private static byte ValueOf(IReadOnlyDictionary<string, string> attributes)
        {
            if (attributes.TryGetValue("HAZARD", out var raw) &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return (byte)Mathf.Clamp((float)Math.Round(value), 0f, 255f);
            }

            if (attributes.TryGetValue("LEVEL", out var level))
            {
                switch (level.Trim().ToLowerInvariant())
                {
                    case "amber":
                        return Amber;
                    case "red":
                        return Red;
                }
            }

            return Red;
        }
    }
}
