using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MartianMap.Routing;
using Newtonsoft.Json;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The single fixture loader of the tablet (C5, "Entregables en H2"; Convenciones, "Unity"):
    /// reads the JSON of <c>contracts/fixtures/</c> into the C4 types of Routing and the C5 types of
    /// Terrain. Routing reads no JSON by design, so the mapping lives here. At run time the fixtures
    /// are the copy in <c>StreamingAssets/fixtures/</c> (FixtureSync). Each owner's fake event bus
    /// builds on it.
    /// </summary>
    public static class FixtureLoader
    {
        public const string CostParamsPath = "routing/cost_params.json";
        public const string ExpectedRoutePath = "routing/expected_route.json";
        public const string ExpectedPlanPath = "routing/expected_plan.json";
        public const string ZonePath = "zone/zone.json";
        public const string ConditionsPath = "conditions/conditions.json";
        public const string SynthTileFolder = "tile_synth_0_0_0";

        /// <summary><c>StreamingAssets/fixtures</c>, valid on Android too.</summary>
        public static string DefaultRoot => LocalFiles.Combine(Application.streamingAssetsPath, "fixtures");

        public static async Task<CostParams> LoadCostParamsAsync(string root = null) =>
            ParseCostParams(await ReadAsync(root, CostParamsPath));

        public static async Task<Route> LoadExpectedRouteAsync(string root = null) =>
            ParseRoute(await ReadAsync(root, ExpectedRoutePath));

        /// <summary>The plans of <c>expected_plan.json</c>, one per <see cref="Alternative"/>, in that order.</summary>
        public static async Task<IReadOnlyList<Plan>> LoadExpectedPlansAsync(string root = null) =>
            ParsePlans(await ReadAsync(root, ExpectedPlanPath));

        public static async Task<ZoneInfo> LoadZoneAsync(string root = null) =>
            JsonConvert.DeserializeObject<ZoneInfo>(await ReadAsync(root, ZonePath));

        public static async Task<ConditionsInfo> LoadConditionsAsync(string root = null) =>
            JsonConvert.DeserializeObject<ConditionsInfo>(await ReadAsync(root, ConditionsPath));

        /// <summary>
        /// The synthetic zone of the contracts: the C1 tile <c>tile_synth_0_0_0</c> drawn as zone
        /// <c>synth</c>. Takes <c>zone/zone.json</c> and <c>conditions/conditions.json</c> when they
        /// exist; until P1 delivers them, the zone is built from the shared synthetic coordinates of
        /// the guide (Convenciones, "Cómo se fija un contrato").
        /// </summary>
        public static async Task<LoadedZone> LoadSynthZoneAsync(string root = null)
        {
            root ??= DefaultRoot;
            var tileFolder = LocalFiles.Combine(root, SynthTileFolder);
            var tileJson = await LocalFiles.ReadBytesAsync(LocalFiles.Combine(tileFolder, "tile.json"));
            var tile = await ZoneFolderLoader.LoadTileAsync(tileFolder);

            var zoneBytes = await LocalFiles.TryReadBytesAsync(LocalFiles.Combine(root, ZonePath));
            var zone = zoneBytes != null
                ? JsonConvert.DeserializeObject<ZoneInfo>(Encoding.UTF8.GetString(zoneBytes))
                : SynthZoneFromGuide(tile.Info, Sha256.Hex(tileJson));

            ConditionsInfo conditions = null;
            var conditionsBytes = await LocalFiles.TryReadBytesAsync(LocalFiles.Combine(root, ConditionsPath));
            if (conditionsBytes != null)
            {
                conditions = JsonConvert.DeserializeObject<ConditionsInfo>(Encoding.UTF8.GetString(conditionsBytes));
            }

            Debug.Log($"[Terrain] contract fixture loaded zone_id={zone.ZoneId} zone_json={(zoneBytes != null ? "fixture" : "guide")} " +
                      $"conditions={(conditions != null ? "fixture" : "none")}");
            return new LoadedZone(zone, new[] { tile }, conditions, LocalFiles.Combine(root, "zone", "pov"));
        }

        /// <summary>Zone <c>synth</c> as the guide fixes it: one 256 m tile centred on the origin.</summary>
        public static ZoneInfo SynthZoneFromGuide(TileInfo tile, string tileJsonSha256)
        {
            var zone = new ZoneInfo
            {
                SchemaVersion = 1,
                ZoneId = "synth",
                Name = "Synthetic zone (contract fixture)",
                Crs = "IAU_2015:49900",
                OriginLatDeg = -4.81,
                OriginLonDeg = 137.385,
                OriginHeightM = 12.5,
                Projection = "equirectangular_local",
                HabitatEnuM = new[] { -100.0, -20.0 },
                ExtentEnuM = new[] { -128.0, -128.0, 128.0, 128.0 },
                BaseMPerPx = 1.0,
            };
            zone.Tiles.Add(new ZoneTileRef { Z = tile.Z, X = tile.X, Y = tile.Y, Version = tile.Version, Sha256 = tileJsonSha256 });
            zone.Layers.Add(new ZoneLayerInfo { Id = LayerIds.Ortho, SourceId = "synthetic", Qualitative = true });
            zone.Layers.Add(new ZoneLayerInfo
            {
                Id = LayerIds.Thermal, SourceId = "synthetic", Qualitative = false,
                Unit = "J m-2 K-1 s-1/2", Scale = 2.0, Offset = 100.0,
            });
            zone.Layers.Add(new ZoneLayerInfo { Id = LayerIds.ThermalIr, SourceId = "synthetic", Qualitative = true });
            return zone;
        }

        public static CostParams ParseCostParams(string json)
        {
            var d = Deserialize<CostParamsJson>(json, CostParamsPath);
            return new CostParams(
                d.SchemaVersion,
                d.V0Mps.ToPercentiles(),
                new SensitivityRange(d.B.P50, d.B.Low, d.B.High),
                d.C,
                d.WalkCeilingMps,
                d.MaxSlopeDeg,
                d.ReliefBlockCm,
                d.ReliefPenaltyFromCm,
                checked((byte)d.HazardRedMin),
                checked((byte)d.HazardAmberMin),
                d.FTerreno,
                d.FRugMin,
                d.AmberPenaltySPerM,
                d.AltSafestAmberMult,
                d.AltGentlestSPerM);
        }

        public static Route ParseRoute(string json) => Deserialize<RouteJson>(json, ExpectedRoutePath).ToRoute();

        public static IReadOnlyList<Plan> ParsePlans(string json)
        {
            var d = Deserialize<PlansJson>(json, ExpectedPlanPath);
            return d.Plans.Select(p => p.ToPlan()).ToList();
        }

        private static async Task<string> ReadAsync(string root, string relative)
        {
            var bytes = await LocalFiles.ReadBytesAsync(LocalFiles.Combine(root ?? DefaultRoot, relative));
            return Encoding.UTF8.GetString(bytes);
        }

        private static T Deserialize<T>(string json, string name) where T : class
        {
            return JsonConvert.DeserializeObject<T>(json) ?? throw new InvalidDataException($"{name} is empty");
        }

        private sealed class PercentilesJson
        {
            [JsonProperty("p10")] public double P10 { get; set; }
            [JsonProperty("p50")] public double P50 { get; set; }
            [JsonProperty("p90")] public double P90 { get; set; }

            public Percentiles ToPercentiles() => new Percentiles(P10, P50, P90);
        }

        private sealed class RangeJson
        {
            [JsonProperty("p50")] public double P50 { get; set; }
            [JsonProperty("low")] public double Low { get; set; }
            [JsonProperty("high")] public double High { get; set; }
        }

        private sealed class CostParamsJson
        {
            [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
            [JsonProperty("v0_mps")] public PercentilesJson V0Mps { get; set; }
            [JsonProperty("b")] public RangeJson B { get; set; }
            [JsonProperty("c")] public double C { get; set; }
            [JsonProperty("walk_ceiling_mps")] public double WalkCeilingMps { get; set; }
            [JsonProperty("max_slope_deg")] public double MaxSlopeDeg { get; set; }
            [JsonProperty("relief_block_cm")] public double ReliefBlockCm { get; set; }
            [JsonProperty("relief_penalty_from_cm")] public double ReliefPenaltyFromCm { get; set; }
            [JsonProperty("hazard_red_min")] public int HazardRedMin { get; set; }
            [JsonProperty("hazard_amber_min")] public int HazardAmberMin { get; set; }
            [JsonProperty("f_terreno")] public double FTerreno { get; set; }
            [JsonProperty("f_rug_min")] public double FRugMin { get; set; }
            [JsonProperty("amber_penalty_s_per_m")] public double AmberPenaltySPerM { get; set; }
            [JsonProperty("alt_safest_amber_mult")] public double AltSafestAmberMult { get; set; }
            [JsonProperty("alt_gentlest_s_per_m")] public double AltGentlestSPerM { get; set; }
        }

        private sealed class RouteJson
        {
            [JsonProperty("found")] public bool Found { get; set; }
            [JsonProperty("cells")] public int[] Cells { get; set; }
            [JsonProperty("polyline_enu_m")] public double[][] PolylineEnuM { get; set; }
            [JsonProperty("length_m")] public double LengthM { get; set; }
            [JsonProperty("ascent_m")] public double AscentM { get; set; }
            [JsonProperty("amber_m")] public double AmberM { get; set; }
            [JsonProperty("eta_s")] public PercentilesJson EtaS { get; set; }
            [JsonProperty("max_hazard")] public int MaxHazard { get; set; }

            public Route ToRoute()
            {
                if (!Found)
                {
                    return Route.NotFound;
                }

                var polyline = (PolylineEnuM ?? new double[0][])
                    .Select(p => new EnuPoint3(p[0], p[1], p.Length > 2 ? p[2] : 0.0))
                    .ToArray();
                return new Route(true, Cells ?? new int[0], polyline, LengthM, AscentM, AmberM,
                    EtaS?.ToPercentiles() ?? default, checked((byte)MaxHazard));
            }
        }

        private sealed class PlanJson
        {
            [JsonProperty("order")] public string[] Order { get; set; }
            [JsonProperty("dropped")] public string[] Dropped { get; set; }
            [JsonProperty("legs")] public RouteJson[] Legs { get; set; }
            [JsonProperty("total_s")] public PercentilesJson TotalS { get; set; }
            [JsonProperty("dose_uGy")] public double DoseUGy { get; set; }
            [JsonProperty("limit_s")] public double LimitS { get; set; }
            [JsonProperty("return_ok")] public bool ReturnOk { get; set; }

            public Plan ToPlan() => new Plan(
                Order ?? new string[0],
                Dropped ?? new string[0],
                (Legs ?? new RouteJson[0]).Select(l => l.ToRoute()).ToArray(),
                TotalS?.ToPercentiles() ?? default,
                DoseUGy,
                LimitS,
                ReturnOk);
        }

        private sealed class PlansJson
        {
            [JsonProperty("plans")] public List<PlanJson> Plans { get; set; } = new List<PlanJson>();
        }
    }
}
