using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Map queries for the HUD (C5, "Eventos"). In 2D the map lies on the plane Y = 0, so
    /// <see cref="HeightAt"/> is 0 everywhere; the real elevation is <see cref="TryElevationAt"/>.
    /// </summary>
    public static class MapQuery
    {
        private static TerrainMap2D _map;
        private static Camera _camera;

        /// <summary>The camera that draws the map; <see cref="Camera.main"/> when none is set.</summary>
        public static Camera Camera
        {
            get => _camera != null ? _camera : Camera.main;
            set => _camera = value;
        }

        /// <summary>The ENU point of the map under a screen position, or false if it is outside the zone.</summary>
        public static bool TryPick(Vector2 screenPosition, out Vector2 enuM)
        {
            enuM = default;
            var camera = Camera;
            if (camera == null || _map == null || _map.Current == null)
            {
                return false;
            }

            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out var distance))
            {
                return false;
            }

            enuM = ZoneProjection.ToEnu(ray.GetPoint(distance));
            return _map.ExtentEnu.Contains(enuM);
        }

        /// <summary>The scene Y of the map at a point: 0 in the 2D map.</summary>
        public static float HeightAt(Vector2 enuM) => 0f;

        /// <summary>The height above the areoid at a point, in metres; false where there is no data.</summary>
        public static bool TryElevationAt(Vector2 enuM, out float metresAboveAreoid)
        {
            metresAboveAreoid = 0f;
            return _map != null && _map.TryElevationAt(enuM, out metresAboveAreoid);
        }

        internal static void Attach(TerrainMap2D map) => _map = map;

        internal static void Detach(TerrainMap2D map)
        {
            if (_map == map)
            {
                _map = null;
            }
        }
    }
}
