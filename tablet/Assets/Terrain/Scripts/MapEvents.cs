using System;
using System.Collections.Generic;
using MartianMap.Edits;
using MartianMap.Routing;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The scene event bus (C5, "Eventos"). One <c>event Action&lt;T&gt;</c> per event; emit only on
    /// the main thread. The route and edit events carry the C4 types of Routing and Edits.
    /// </summary>
    public static class MapEvents
    {
        public static event Action<TileLoaded> TileLoaded;
        public static event Action<ZoneLoaded> ZoneLoaded;
        public static event Action<ConditionsChanged> ConditionsChanged;
        public static event Action<RouteRequested> RouteRequested;
        public static event Action<RouteSelected> RouteSelected;
        public static event Action<EditRequested> EditRequested;
        public static event Action<EditRejected> EditRejected;
        public static event Action<EntitiesChanged> EntitiesChanged;
        public static event Action<RouteFocused> RouteFocused;
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
        public static void Emit(RouteRequested e) => Raise(RouteRequested, e);
        public static void Emit(RouteSelected e) => Raise(RouteSelected, e);
        public static void Emit(EditRequested e) => Raise(EditRequested, e);
        public static void Emit(EditRejected e) => Raise(EditRejected, e);
        public static void Emit(EntitiesChanged e) => Raise(EntitiesChanged, e);
        public static void Emit(RouteFocused e) => Raise(RouteFocused, e);
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

    /// <summary>HUD → Terrain: plan a mission, or pick another alternative of it.</summary>
    public readonly struct RouteRequested
    {
        public readonly IReadOnlyList<Stop> Stops;
        public readonly EnuPoint Home;
        public readonly Budget Budget;
        public readonly StopMode Mode;
        public readonly EnuPoint Start;
        public readonly Alternative Alternative;

        public RouteRequested(IReadOnlyList<Stop> stops, EnuPoint home, Budget budget, StopMode mode, EnuPoint start, Alternative alternative)
        {
            Stops = stops ?? throw new ArgumentNullException(nameof(stops));
            Home = home;
            Budget = budget;
            Mode = mode;
            Start = start;
            Alternative = alternative;
        }
    }

    /// <summary>
    /// Terrain → FX, HUD: one <see cref="Plan"/> per <see cref="Alternative"/> (in that order), the
    /// chosen one, and its polyline in scene coordinates. In the 2D map the polyline lies on the
    /// map plane (Y = 0); the height of each vertex stays in the plan's routes.
    /// </summary>
    public readonly struct RouteSelected
    {
        public readonly IReadOnlyList<Plan> Plans;
        public readonly Alternative Selected;
        public readonly Vector3[] Polyline;

        public RouteSelected(IReadOnlyList<Plan> plans, Alternative selected, Vector3[] polyline)
        {
            Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            Selected = selected;
            Polyline = polyline ?? throw new ArgumentNullException(nameof(polyline));
        }

        /// <summary>The scene polyline of a plan: its legs joined, on the map plane (Y = 0), without repeated joints.</summary>
        public static Vector3[] ScenePolyline(Plan plan)
        {
            var points = new List<Vector3>();
            foreach (var leg in plan.Legs)
            {
                if (!leg.Found)
                {
                    continue;
                }

                foreach (var p in leg.PolylineEnuM)
                {
                    var v = new Vector3((float)p.E, 0f, (float)p.N);
                    if (points.Count == 0 || points[points.Count - 1] != v)
                    {
                        points.Add(v);
                    }
                }
            }

            return points.ToArray();
        }

        /// <summary>The scene polyline of one route, on the map plane (Y = 0).</summary>
        public static Vector3[] ScenePolyline(Route route)
        {
            var points = new Vector3[route.PolylineEnuM.Count];
            for (var i = 0; i < points.Length; i++)
            {
                var p = route.PolylineEnuM[i];
                points[i] = new Vector3((float)p.E, 0f, (float)p.N);
            }

            return points;
        }
    }

    /// <summary>
    /// HUD → FX: which of the selected route's alternatives the map shows: all three, or only one.
    /// A new <see cref="RouteSelected"/> shows all three again.
    /// </summary>
    public readonly struct RouteFocused
    {
        public readonly bool All;
        public readonly Alternative Alternative;

        private RouteFocused(bool all, Alternative alternative)
        {
            All = all;
            Alternative = alternative;
        }

        public static RouteFocused ShowAll() => new RouteFocused(true, MartianMap.Routing.Alternative.Fastest);

        public static RouteFocused Only(Alternative alternative) => new RouteFocused(false, alternative);
    }

    /// <summary>HUD → Terrain: the astronaut created, changed or deleted an entity. Terrain completes the edit.</summary>
    public readonly struct EditRequested
    {
        public readonly EditOp Op;
        public readonly string EntityId;
        public readonly EditPayload Payload;
        public readonly bool Tombstone;

        public EditRequested(EditOp op, string entityId, EditPayload payload, bool tombstone)
        {
            Op = op;
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Tombstone = tombstone;
        }
    }

    /// <summary>Terrain → HUD: the habitat rejected an edit (e.g. <c>confirmation_required</c>).</summary>
    public readonly struct EditRejected
    {
        public readonly string EntityId;
        public readonly string Code;

        public EditRejected(string entityId, string code)
        {
            EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
            Code = code ?? string.Empty;
        }
    }

    /// <summary>Terrain → HUD, FX: the live entities of the zone after a local edit or <c>changes</c>.</summary>
    public readonly struct EntitiesChanged
    {
        public readonly IReadOnlyList<LiveEntity> Entities;

        public EntitiesChanged(IReadOnlyList<LiveEntity> entities)
        {
            Entities = entities ?? throw new ArgumentNullException(nameof(entities));
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
