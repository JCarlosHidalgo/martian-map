using System;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The local equirectangular projection of a zone (C1, "zone.json"):
    /// e = R · cos(lat0) · (lon − lon0), n = R · (lat − lat0).
    /// </summary>
    public static class ZoneProjection
    {
        private const double DegToRad = Math.PI / 180.0;

        public static Vector2d ToLatLon(ZoneInfo zone, Vector2 enuM)
        {
            var lat0 = zone.OriginLatDeg * DegToRad;
            var lat = lat0 + enuM.y / zone.RadiusM;
            var lon = zone.OriginLonDeg * DegToRad + enuM.x / (zone.RadiusM * Math.Cos(lat0));
            return new Vector2d(lat / DegToRad, lon / DegToRad);
        }

        public static Vector2 ToEnu(ZoneInfo zone, double latDeg, double lonDeg)
        {
            var lat0 = zone.OriginLatDeg * DegToRad;
            var e = zone.RadiusM * Math.Cos(lat0) * (lonDeg - zone.OriginLonDeg) * DegToRad;
            var n = zone.RadiusM * (latDeg - zone.OriginLatDeg) * DegToRad;
            return new Vector2((float)e, (float)n);
        }

        /// <summary>ENU [e, n] to scene coordinates on the map plane (X = east, Z = north).</summary>
        public static Vector3 ToScene(Vector2 enuM, float y = 0f) => new Vector3(enuM.x, y, enuM.y);

        public static Vector2 ToEnu(Vector3 scene) => new Vector2(scene.x, scene.z);
    }

    /// <summary>A latitude/longitude pair in degrees, kept in double precision.</summary>
    public readonly struct Vector2d
    {
        public readonly double LatDeg;
        public readonly double LonDeg;

        public Vector2d(double latDeg, double lonDeg)
        {
            LatDeg = latDeg;
            LonDeg = lonDeg;
        }

        public override string ToString() => $"{LatDeg:F5}°, {LonDeg:F5}°";
    }
}
