using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>A polygon from a Shapefile: its rings (outer and holes, even-odd) and its attributes.</summary>
    public sealed class ShapePolygon
    {
        public ShapePolygon(IReadOnlyList<Vector2[]> rings, IReadOnlyDictionary<string, string> attributes)
        {
            Rings = rings;
            Attributes = attributes;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var ring in rings)
            {
                foreach (var p in ring)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }

            Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Rings in the file's coordinates (x = east or longitude, y = north or latitude).</summary>
        public IReadOnlyList<Vector2[]> Rings { get; }

        /// <summary>The .dbf fields of this record, by upper-case field name; empty without a .dbf.</summary>
        public IReadOnlyDictionary<string, string> Attributes { get; }

        public Rect Bounds { get; }
    }

    /// <summary>
    /// Minimal ESRI Shapefile reader: Polygon (5), PolygonZ (15) and PolygonM (25) from the .shp,
    /// fields from the .dbf (dBASE III), and whether the .prj is geographic. Other shape types are skipped.
    /// Spec: https://www.esri.com/content/dam/esrisites/sitecore-archive/Files/Pdfs/library/whitepapers/pdfs/shapefile.pdf
    /// </summary>
    public static class ShapefileReader
    {
        private const int FileCode = 9994;
        private const int ShapeNull = 0;
        private const int ShapePolygon = 5;
        private const int ShapePolygonZ = 15;
        private const int ShapePolygonM = 25;

        /// <param name="shp">The .shp bytes.</param>
        /// <param name="dbf">The .dbf bytes, or null.</param>
        public static List<ShapePolygon> ReadPolygons(byte[] shp, byte[] dbf)
        {
            if (shp == null || shp.Length < 100)
            {
                throw new InvalidDataException("shapefile is shorter than its 100-byte header");
            }

            if (ReadInt32BigEndian(shp, 0) != FileCode)
            {
                throw new InvalidDataException("not a shapefile: bad file code");
            }

            var records = dbf != null ? ReadDbf(dbf) : new List<Dictionary<string, string>>();
            var polygons = new List<ShapePolygon>();
            var offset = 100;
            var index = 0;
            while (offset + 8 <= shp.Length)
            {
                var contentBytes = ReadInt32BigEndian(shp, offset + 4) * 2;
                var content = offset + 8;
                if (content + contentBytes > shp.Length)
                {
                    throw new InvalidDataException($"record {index} runs past the end of the file");
                }

                var type = BitConverter.ToInt32(shp, content);
                if (type == ShapePolygon || type == ShapePolygonZ || type == ShapePolygonM)
                {
                    var attributes = index < records.Count ? records[index] : new Dictionary<string, string>();
                    polygons.Add(new ShapePolygon(ReadRings(shp, content), attributes));
                }
                else if (type != ShapeNull)
                {
                    Debug.LogWarning($"[Terrain] shapefile record skipped index={index} shape_type={type}");
                }

                offset = content + contentBytes;
                index++;
            }

            return polygons;
        }

        /// <summary>True when the .prj text describes geographic coordinates (degrees), not a projection.</summary>
        public static bool IsGeographic(string prj)
        {
            if (string.IsNullOrWhiteSpace(prj))
            {
                return false;
            }

            var text = prj.TrimStart().ToUpperInvariant();
            return text.StartsWith("GEOGCS", StringComparison.Ordinal) || text.StartsWith("GEOGCRS", StringComparison.Ordinal);
        }

        private static Vector2[][] ReadRings(byte[] shp, int content)
        {
            // type(4) + bbox(32) + numParts(4) + numPoints(4) + parts[numParts] + points[numPoints]
            var numParts = BitConverter.ToInt32(shp, content + 36);
            var numPoints = BitConverter.ToInt32(shp, content + 40);
            var partsAt = content + 44;
            var pointsAt = partsAt + 4 * numParts;
            var rings = new Vector2[numParts][];
            for (var part = 0; part < numParts; part++)
            {
                var start = BitConverter.ToInt32(shp, partsAt + 4 * part);
                var end = part + 1 < numParts ? BitConverter.ToInt32(shp, partsAt + 4 * (part + 1)) : numPoints;
                var ring = new Vector2[Math.Max(0, end - start)];
                for (var i = 0; i < ring.Length; i++)
                {
                    var at = pointsAt + 16 * (start + i);
                    ring[i] = new Vector2((float)BitConverter.ToDouble(shp, at), (float)BitConverter.ToDouble(shp, at + 8));
                }

                rings[part] = ring;
            }

            return rings;
        }

        private static List<Dictionary<string, string>> ReadDbf(byte[] dbf)
        {
            var result = new List<Dictionary<string, string>>();
            if (dbf.Length < 32)
            {
                return result;
            }

            var count = BitConverter.ToInt32(dbf, 4);
            var headerLength = BitConverter.ToInt16(dbf, 8);
            var recordLength = BitConverter.ToInt16(dbf, 10);

            var fields = new List<(string name, int length)>();
            for (var at = 32; at + 32 <= headerLength && dbf[at] != 0x0D; at += 32)
            {
                var name = Encoding.ASCII.GetString(dbf, at, 11).TrimEnd('\0', ' ').ToUpperInvariant();
                fields.Add((name, dbf[at + 16]));
            }

            for (var r = 0; r < count; r++)
            {
                var at = headerLength + r * recordLength;
                if (at + recordLength > dbf.Length)
                {
                    break;
                }

                var record = new Dictionary<string, string>();
                var cursor = at + 1; // deletion flag
                foreach (var (name, length) in fields)
                {
                    record[name] = Encoding.UTF8.GetString(dbf, cursor, length).Trim();
                    cursor += length;
                }

                result.Add(record);
            }

            return result;
        }

        private static int ReadInt32BigEndian(byte[] data, int at)
        {
            return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
        }
    }
}
