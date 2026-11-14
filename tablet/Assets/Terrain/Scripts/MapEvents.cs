using System;
using System.Collections.Generic;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The scene event bus (C5, "Eventos"). One <c>event Action&lt;T&gt;</c> per event; emit only on
    /// the main thread. RouteRequested, RouteSelected, EditRequested, EditRejected and
    /// EntitiesChanged carry C4 types and are added when the Routing/Edits signature is in main.
    /// </summary>
    public static class MapEvents
    {
        public static event Action<TileLoaded> TileLoaded;
        public static event Action<ZoneLoaded> ZoneLoaded;
        public static event Action<ConditionsChanged> ConditionsChanged;
        public static event Action<StopHovered> StopHovered;
        public static event Action<LayerToggled> LayerToggled;
        public static event Action<AstronautMoved> AstronautMoved;
        public static event Action<EditModeChanged> EditModeChanged;
        public static event Action<ZonesChanged> ZonesChanged;
        public static event Action<ZoneRequested> ZoneRequested;
        public static event Action<ConfigChanged> ConfigChanged;

        public static void Emit(TileLoaded e) => Raise(TileLoaded, e);
        public static void Emit(ZoneLoaded e) => Raise(ZoneLoaded, e);
        public static void Emit(ConditionsChanged e) => Raise(ConditionsChanged, e);
        public static void Emit(StopHovered e) => Raise(StopHovered, e);
        public static void Emit(LayerToggled e) => Raise(LayerToggled, e);
        public static void Emit(AstronautMoved e) => Raise(AstronautMoved, e);
        public static void Emit(EditModeChanged e) => Raise(EditModeChanged, e);
        public static void Emit(ZonesChanged e) => Raise(ZonesChanged, e);
        public static void Emit(ZoneRequested e) => Raise(ZoneRequested, e);
        public static void Emit(ConfigChanged e) => Raise(ConfigChanged, e);

        private static void Raise<T>(Action<T> handler, T payload)
        {
            handler?.Invoke(payload);
        }
    }

    /// <summary>Known <c>layer_id</c> values (C5, "Capas").</summary>
    public static class LayerIds
    {
        public const string Topo = "topo";
        public const string Ortho = "ortho";
        public const string Thermal = "thermal";
        public const string ThermalIr = "thermal_ir";
        public const string Hazard = "hazard";
        public const string Heat = "heat";
    }

    public readonly struct TileLoaded
    {
        public readonly string ZoneId;
        public readonly int Z;
        public readonly int X;
        public readonly int Y;

        public TileLoaded(string zoneId, int z, int x, int y)
        {
            ZoneId = zoneId;
            Z = z;
            X = x;
            Y = y;
        }
    }

    public readonly struct ZoneLoaded
    {
        public readonly ZoneInfo Zone;
        public readonly string PovFolder;
        public readonly string Author;

        public ZoneLoaded(ZoneInfo zone, string povFolder, string author)
        {
            Zone = zone;
            PovFolder = povFolder;
            Author = author;
        }
    }

    public readonly struct ConditionsChanged
    {
        public readonly string ZoneId;
        public readonly ConditionsInfo Conditions;

        public ConditionsChanged(string zoneId, ConditionsInfo conditions)
        {
            ZoneId = zoneId;
            Conditions = conditions;
        }
    }

    public readonly struct StopHovered
    {
        public readonly string StopId;

        public StopHovered(string stopId)
        {
            StopId = stopId;
        }
    }

    public readonly struct LayerToggled
    {
        public readonly string LayerId;
        public readonly bool On;

        public LayerToggled(string layerId, bool on)
        {
            LayerId = layerId;
            On = on;
        }
    }

    public readonly struct AstronautMoved
    {
        /// <summary>[e, n] in metres.</summary>
        public readonly Vector2 EnuM;

        public AstronautMoved(Vector2 enuM)
        {
            EnuM = enuM;
        }
    }

    public readonly struct EditModeChanged
    {
        public readonly bool On;

        public EditModeChanged(bool on)
        {
            On = on;
        }
    }

    public readonly struct ZoneSummary
    {
        public readonly string ZoneId;
        public readonly string Name;
        public readonly int ZoneVersion;

        public ZoneSummary(string zoneId, string name, int zoneVersion)
        {
            ZoneId = zoneId;
            Name = name;
            ZoneVersion = zoneVersion;
        }
    }

    public readonly struct ZonesChanged
    {
        public readonly IReadOnlyList<ZoneSummary> Zones;

        public ZonesChanged(IReadOnlyList<ZoneSummary> zones)
        {
            Zones = zones;
        }
    }

    public readonly struct ZoneRequested
    {
        public readonly string ZoneId;

        public ZoneRequested(string zoneId)
        {
            ZoneId = zoneId;
        }
    }

    public readonly struct ConfigChanged
    {
        public readonly string HabitatUrl;
        public readonly string FilesUrl;
        public readonly string Author;

        public ConfigChanged(string habitatUrl, string filesUrl, string author)
        {
            HabitatUrl = habitatUrl;
            FilesUrl = filesUrl;
            Author = author;
        }
    }
}
