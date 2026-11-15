using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MartianMap.Routing;
using MartianMap.Terrain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MartianMap.HUD
{
    /// <summary>Immutable C6 mission, read by the HUD and passed to C4 as typed values.</summary>
    public sealed class MissionDefinition
    {
        private static readonly Regex ZonePattern = new Regex(@"\A[a-z][a-z0-9-]{1,31}\z");

        private MissionDefinition(string zoneId, string name, EnuPoint? home, List<MissionStop> stops, Budget budget, StopMode mode)
        {
            ZoneId = zoneId;
            Name = name;
            ExplicitHome = home;
            Stops = stops.AsReadOnly();
            Budget = budget;
            Mode = mode;
        }

        public string ZoneId { get; }
        public string Name { get; }
        public EnuPoint? ExplicitHome { get; }
        public IReadOnlyList<MissionStop> Stops { get; }
        public Budget Budget { get; }
        public StopMode Mode { get; }

        /// <summary>Reads C6 without performing file or network access. Unknown fields are ignored.</summary>
        public static MissionDefinition Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Mission JSON is empty.", nameof(json));
            }

            try
            {
                using var text = new StringReader(json);
                using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read())
                {
                    throw new ArgumentException("Mission JSON contains trailing content.");
                }

                if (Integer(root, "schema_version") != 1)
                {
                    throw new ArgumentException("Mission schema_version must be 1.");
                }

                string zoneId = Text(root, "zone_id");
                if (!ZonePattern.IsMatch(zoneId))
                {
                    throw new ArgumentException("Mission zone_id must be a lowercase zone slug.");
                }

                string name = Text(root, "name");
                EnuPoint? home = root.Property("base_enu_m") == null ? null : Point(root["base_enu_m"], "base_enu_m");
                var inputStops = root["stops"] as JArray ?? throw new ArgumentException("Mission stops must be an array.");
                var stops = new List<MissionStop>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (JToken token in inputStops)
                {
                    var stop = token as JObject ?? throw new ArgumentException("Each mission stop must be an object.");
                    string id = Text(stop, "id");
                    if (!ids.Add(id))
                    {
                        throw new ArgumentException($"Mission stop id is duplicated: {id}.");
                    }

                    var value = new Stop(id, Point(stop["enu_m"], "enu_m"), Integer(stop, "science_value"), Number(stop["dwell_s"], "dwell_s"));
                    stops.Add(new MissionStop(Text(stop, "name"), Text(stop, "objective"), value));
                }

                var budget = root["budget"] as JObject ?? throw new ArgumentException("Mission budget must be an object.");
                string mode = Text(root, "mode");
                if (mode != "TSP" && mode != "OP")
                {
                    throw new ArgumentException("Mission mode must be TSP or OP.");
                }

                return new MissionDefinition(zoneId, name, home, stops,
                    new Budget(Number(budget["t_s"], "t_s"), Number(budget["dose_uGy"], "dose_uGy")),
                    mode == "TSP" ? StopMode.Tsp : StopMode.Op);
            }
            catch (JsonException error)
            {
                throw new ArgumentException("Mission JSON is invalid: " + error.Message, nameof(json), error);
            }
            catch (OverflowException error)
            {
                throw new ArgumentException("Mission number is outside its supported range.", nameof(json), error);
            }
        }

        /// <summary>Resolves the zone habitat fallback and checks the home and every stop against zone bounds.</summary>
        public EnuPoint ResolveHome(ZoneInfo zone)
        {
            if (zone == null)
            {
                throw new ArgumentNullException(nameof(zone));
            }

            if (!string.Equals(zone.ZoneId, ZoneId, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Mission targets {ZoneId}; active zone is {zone.ZoneId}.");
            }

            var extent = zone.ExtentEnuM;
            if (extent == null || extent.Length != 4 || !Finite(extent[0]) || !Finite(extent[1]) ||
                !Finite(extent[2]) || !Finite(extent[3]) || extent[0] >= extent[2] || extent[1] >= extent[3])
            {
                throw new ArgumentException("Active zone has invalid ENU bounds.");
            }

            EnuPoint home;
            if (ExplicitHome.HasValue)
            {
                home = ExplicitHome.Value;
            }
            else
            {
                var habitat = zone.HabitatEnuM;
                if (habitat == null || habitat.Length != 2 || !Finite(habitat[0]) || !Finite(habitat[1]))
                {
                    throw new ArgumentException("Active zone must supply habitat_enu_m when the mission omits its base.");
                }

                home = new EnuPoint(habitat[0], habitat[1]);
            }

            RequireInside(home, extent, "Mission base");
            foreach (MissionStop stop in Stops)
            {
                RequireInside(stop.Value.EnuM, extent, "Mission stop " + stop.Value.Id);
            }

            return home;
        }

        private static void RequireInside(EnuPoint point, double[] extent, string label)
        {
            if (point.E < extent[0] || point.E > extent[2] || point.N < extent[1] || point.N > extent[3])
            {
                throw new ArgumentException(label + " is outside the active zone.");
            }
        }

        private static string Text(JObject parent, string name)
        {
            JToken value = parent[name];
            if (value?.Type != JTokenType.String || string.IsNullOrEmpty(value.Value<string>()))
            {
                throw new ArgumentException($"Mission {name} must be a nonempty string.");
            }

            return value.Value<string>();
        }

        private static int Integer(JObject parent, string name)
        {
            double value = Number(parent[name], name);
            if (value < int.MinValue || value > int.MaxValue || value != Math.Truncate(value))
            {
                throw new ArgumentException($"Mission {name} must be an integer.");
            }

            return (int)value;
        }

        private static double Number(JToken token, string name)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                throw new ArgumentException($"Mission {name} must be a finite number.");
            }

            double value = token.Value<double>();
            if (!Finite(value))
            {
                throw new ArgumentException($"Mission {name} must be a finite number.");
            }

            return value;
        }

        private static EnuPoint Point(JToken token, string name)
        {
            if (!(token is JArray array) || array.Count != 2)
            {
                throw new ArgumentException($"Mission {name} must contain [east, north] in metres.");
            }

            return new EnuPoint(Number(array[0], name), Number(array[1], name));
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class MissionStop
    {
        internal MissionStop(string name, string objective, Stop value)
        {
            Name = name;
            Objective = objective;
            Value = value;
        }

        public string Name { get; }
        public string Objective { get; }
        public Stop Value { get; }
    }
}
