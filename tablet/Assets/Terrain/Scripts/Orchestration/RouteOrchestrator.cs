using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MartianMap.Routing;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Route orchestration (Tablet, "Orquestación"): Terrain is the only caller of Routing.
    /// On <see cref="MapEvents.RouteRequested"/> it hands Routing the tiles (with the single hazard
    /// raster: Earth + P5 shapes + edits), <c>cost_params.json</c> and the current conditions, runs
    /// <c>BuildGrid</c>, <c>ReturnField</c> and <c>OrderStops</c> on a background thread, then on the
    /// main thread fills <c>_HeatTex</c>/<c>_HazardZoneTex</c> and emits <see cref="MapEvents.RouteSelected"/>.
    /// A request that only changes the alternative reuses the plans. A tile change (edit, shapes)
    /// drops the grid and plans the last request again.
    /// Until P3 implements Routing, zone <c>synth</c> answers with <c>expected_plan.json</c>
    /// (the walking skeleton's fixture route); other zones log that routing is not available.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RouteOrchestrator : MonoBehaviour
    {
        private const string FixtureZone = "synth";

        /// <summary>
        /// Dose rate used when the zone has no conditions.json: the top of the guide's expected base
        /// rate (4–10 µGy/h, E14), so the duration limit is shortened rather than ignored.
        /// </summary>
        public const double FallbackDoseRateUGyH = 10.0;

        [SerializeField] private TerrainMap2D map;
        [SerializeField] private TerrainBootstrap bootstrap;

        private RouteRequested? _last;
        private RouteRequested? _queued;
        private IReadOnlyList<Plan> _plans;
        private string _plansZone;
        private CostGrid _grid;
        private string _gridZone;
        private bool _busy;
        private bool _warnedNotImplemented;

        /// <summary>True while a plan is being computed.</summary>
        public bool Busy => _busy;

        private void Awake()
        {
            if (map == null)
            {
                map = GetComponent<TerrainMap2D>();
            }

            if (bootstrap == null)
            {
                bootstrap = GetComponent<TerrainBootstrap>();
            }
        }

        private void OnEnable()
        {
            MapEvents.RouteRequested += OnRouteRequested;
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.TileLoaded += OnTileLoaded;
        }

        private void OnDisable()
        {
            MapEvents.RouteRequested -= OnRouteRequested;
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.TileLoaded -= OnTileLoaded;
        }

        private void OnZoneLoaded(ZoneLoaded e)
        {
            _grid = null;
            _gridZone = null;
            _plans = null;
            _plansZone = null;
            _last = null;
        }

        /// <summary>A tile changed (edit or P5 shapes): the grid is stale, plan the last request again.</summary>
        private void OnTileLoaded(TileLoaded e)
        {
            if (_grid == null || e.ZoneId != _gridZone)
            {
                return;
            }

            _grid = null;
            _plans = null;
            if (_last.HasValue)
            {
                Request(_last.Value);
            }
        }

        private void OnRouteRequested(RouteRequested request) => Request(request);

        private async void Request(RouteRequested request)
        {
            var zone = map != null ? map.Current : null;
            if (zone == null)
            {
                Debug.LogWarning("[Terrain] route request ignored: no zone on screen");
                return;
            }

            // Only the alternative changed: the three plans are already there.
            if (_plans != null && _plansZone == zone.Zone.ZoneId && _last.HasValue && SameMission(_last.Value, request))
            {
                _last = request;
                EmitSelected(_plans, request.Alternative);
                return;
            }

            if (_busy)
            {
                _queued = request;
                return;
            }

            _busy = true;
            try
            {
                _last = request;
                await PlanAsync(zone, request);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Terrain] route planning failed zone_id={zone.Zone.ZoneId} error={exception.Message}");
            }
            finally
            {
                _busy = false;
            }

            if (_queued.HasValue)
            {
                var next = _queued.Value;
                _queued = null;
                Request(next);
            }
        }

        private async Task PlanAsync(LoadedZone zone, RouteRequested request)
        {
            var zoneId = zone.Zone.ZoneId;
            var costParams = bootstrap != null ? bootstrap.CostParams : null;
            costParams ??= await FixtureLoader.LoadCostParamsAsync();
            var conditions = ToRoutingConditions(zone.Conditions);
            var originHeight = zone.Zone.OriginHeightM;
            var tUtc = DateTime.UtcNow; // the EVA departs when Terrain processes the request (C4)
            var tiles = zone.Tiles.Where(t => t.Info.Z == 0).Select(ToRasters).ToArray();
            var stops = request.Stops.ToArray();
            var grid = _gridZone == zoneId ? _grid : null;
            var started = Time.realtimeSinceStartup;

            PlanResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    var g = grid ?? Router.BuildGrid(tiles, originHeight, costParams);
                    var field = Router.ReturnField(g, request.Home, costParams);
                    var plans = Router.OrderStops(g, request.Start, stops, field, request.Budget, conditions,
                        tUtc, request.Mode, costParams);
                    return new PlanResult(g, plans, field.TimeS.ToArray(), g.Hazard.ToArray());
                });
            }
            catch (NotImplementedException)
            {
                if (!_warnedNotImplemented)
                {
                    _warnedNotImplemented = true;
                    Debug.LogWarning("[Terrain] Routing is not implemented yet (P3); using the fixture route where there is one");
                }

                if (zoneId != FixtureZone)
                {
                    Debug.LogWarning($"[Terrain] no route for zone_id={zoneId}: Routing is not implemented and only zone {FixtureZone} has a fixture plan");
                    return;
                }

                var fixturePlans = await FixtureLoader.LoadExpectedPlansAsync();
                _plans = fixturePlans;
                _plansZone = zoneId;
                EmitSelected(fixturePlans, request.Alternative);
                Debug.Log($"[Terrain] route from fixture zone_id={zoneId} plans={fixturePlans.Count}");
                return;
            }

            // Back on the main thread: the zone may have changed meanwhile.
            if (map.Current == null || map.Current.Zone.ZoneId != zoneId)
            {
                return;
            }

            _grid = result.Grid;
            _gridZone = zoneId;
            _plans = result.Plans;
            _plansZone = zoneId;

            var g0 = result.Grid;
            var node00 = new Vector2((float)g0.OriginEnuM.E, (float)g0.OriginEnuM.N);
            map.SetHeatField(result.HeatS, g0.Width, g0.Height, g0.CellM, node00, (float)request.Budget.TS);
            map.SetHazardZone(result.Hazard, g0.Width, g0.Height);
            EmitSelected(result.Plans, request.Alternative);
            Debug.Log($"[Terrain] route planned zone_id={zoneId} nodes={g0.Width}x{g0.Height} " +
                      $"seconds={Time.realtimeSinceStartup - started:F2}");
        }

        /// <summary>
        /// A route between two map points (RoutePointsTool): <c>Router.Alternatives</c> gives the
        /// three alternatives, each wrapped as a one-leg <see cref="Plan"/>, and
        /// <see cref="MapEvents.RouteSelected"/> shows the fastest. While Routing is not implemented
        /// (P3) the answer is a provisional straight line from A to B over the terrain, with its
        /// length, ascent, amber metres, highest hazard and walking times; it is logged as such.
        /// </summary>
        public async void PlanBetween(Vector2 aEnuM, Vector2 bEnuM)
        {
            var zone = map != null ? map.Current : null;
            if (zone == null || _busy)
            {
                return;
            }

            _busy = true;
            try
            {
                var zoneId = zone.Zone.ZoneId;
                var costParams = (bootstrap != null ? bootstrap.CostParams : null) ?? await FixtureLoader.LoadCostParamsAsync();
                var tiles = zone.Tiles.Where(t => t.Info.Z == 0).Select(ToRasters).ToArray();
                var originHeight = zone.Zone.OriginHeightM;
                var grid = _gridZone == zoneId ? _grid : null;
                var a = new EnuPoint(aEnuM.x, aEnuM.y);
                var b = new EnuPoint(bEnuM.x, bEnuM.y);
                IReadOnlyList<Route> routes;
                try
                {
                    var result = await Task.Run(() =>
                    {
                        var g = grid ?? Router.BuildGrid(tiles, originHeight, costParams);
                        return (g, Router.Alternatives(g, a, b, costParams));
                    });
                    _grid = result.g;
                    _gridZone = zoneId;
                    routes = result.Item2;
                }
                catch (NotImplementedException)
                {
                    var straight = StraightRoute(aEnuM, bEnuM, costParams, (float)originHeight);
                    routes = new[] { straight, straight, straight };
                    Debug.LogWarning($"[Terrain] Routing is not implemented yet (P3): provisional straight route " +
                                     $"length_m={straight.LengthM:F0} ascent_m={straight.AscentM:F1} max_hazard={straight.MaxHazard}");
                }

                if (map.Current == null || map.Current.Zone.ZoneId != zoneId)
                {
                    return;
                }

                var rate = (zone.Conditions != null && zone.Conditions.Value > 0) ? zone.Conditions.Value : FallbackDoseRateUGyH;
                var plans = routes.Select(r => new Plan(
                    new[] { "B" },
                    Array.Empty<string>(),
                    new[] { r },
                    r.EtaS,
                    rate * r.EtaS.P50 / 3600.0,
                    double.PositiveInfinity,
                    r.Found)).ToList();

                _plans = plans;
                _plansZone = zoneId;
                _last = null; // a point-to-point route is not a mission: the next mission request plans anew
                EmitSelected(plans, Alternative.Fastest);
                Debug.Log($"[Terrain] point route zone_id={zoneId} from=({aEnuM.x:F0},{aEnuM.y:F0}) to=({bEnuM.x:F0},{bEnuM.y:F0}) found={routes[0].Found}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Terrain] point route failed error={exception.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// The provisional route while Routing is not implemented: a straight line sampled every 2 m
        /// over the terrain. It does not avoid hazard; it only measures it. Walking times are
        /// <c>length / v0</c> at the three speeds of cost_params.json.
        /// </summary>
        private Route StraightRoute(Vector2 a, Vector2 b, CostParams costParams, float originHeight)
        {
            var distance = Vector2.Distance(a, b);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
            var points = new EnuPoint3[steps + 1];
            double length = 0, ascent = 0, amber = 0;
            byte maxHazard = 0;
            var amberMin = costParams.HazardAmberMin;
            var redMin = costParams.HazardRedMin;
            for (var k = 0; k <= steps; k++)
            {
                var p = Vector2.Lerp(a, b, k / (float)steps);
                var u = map.TryElevationAt(p, out var h) ? h - originHeight : 0f;
                points[k] = new EnuPoint3(p.x, p.y, u);
                map.TryHazardAt(p, out var hazard);
                maxHazard = (byte)Mathf.Max(maxHazard, hazard);
                if (k == 0)
                {
                    continue;
                }

                var prev = points[k - 1];
                var du = u - prev.U;
                var segment = Math.Sqrt(Math.Pow(p.x - prev.E, 2) + Math.Pow(p.y - prev.N, 2) + du * du);
                length += segment;
                ascent += Math.Abs(du);
                if (hazard >= amberMin && hazard < redMin)
                {
                    amber += segment;
                }
            }

            var v0 = costParams.V0Mps;
            var eta = new Percentiles(length / v0.P10, length / v0.P50, length / v0.P90);
            return new Route(true, Array.Empty<int>(), points, length, ascent, amber, eta, maxHazard);
        }

        private static void EmitSelected(IReadOnlyList<Plan> plans, Alternative alternative)
        {
            var index = (int)alternative;
            if (plans == null || index < 0 || index >= plans.Count)
            {
                Debug.LogWarning($"[Terrain] no plan for alternative={alternative}");
                return;
            }

            MapEvents.Emit(new RouteSelected(plans, alternative, RouteSelected.ScenePolyline(plans[index])));
        }

        private static bool SameMission(RouteRequested a, RouteRequested b)
        {
            if (a.Mode != b.Mode || a.Stops.Count != b.Stops.Count ||
                !a.Home.E.Equals(b.Home.E) || !a.Home.N.Equals(b.Home.N) ||
                !a.Start.E.Equals(b.Start.E) || !a.Start.N.Equals(b.Start.N) ||
                !a.Budget.TS.Equals(b.Budget.TS) || !a.Budget.DoseUGy.Equals(b.Budget.DoseUGy))
            {
                return false;
            }

            for (var i = 0; i < a.Stops.Count; i++)
            {
                var x = a.Stops[i];
                var y = b.Stops[i];
                if (x.Id != y.Id || !x.EnuM.E.Equals(y.EnuM.E) || !x.EnuM.N.Equals(y.EnuM.N) ||
                    x.ScienceValue != y.ScienceValue || !x.DwellS.Equals(y.DwellS))
                {
                    return false;
                }
            }

            return true;
        }

        private static TileRasters ToRasters(TileData tile)
        {
            var origin = tile.OriginEnuM;
            return new TileRasters(new EnuPoint(origin.x, origin.y), tile.Height, tile.Info.Height.Scale,
                tile.Info.Height.Offset, tile.Slope, tile.Roughness, tile.Hazard);
        }

        /// <summary>
        /// conditions.json → Routing's Conditions. Without data, or with a rate Routing rejects, it uses
        /// <see cref="FallbackDoseRateUGyH"/> and logs it.
        /// </summary>
        public static Conditions ToRoutingConditions(ConditionsInfo info)
        {
            if (info == null || !(info.Value > 0) || double.IsInfinity(info.Value) || double.IsNaN(info.Value))
            {
                Debug.LogWarning($"[Terrain] no usable conditions; planning with a conservative {FallbackDoseRateUGyH} uGy/h");
                return new Conditions(FallbackDoseRateUGyH, Array.Empty<ConditionModifier>());
            }

            var modifiers = new List<ConditionModifier>();
            foreach (var m in info.Modifiers)
            {
                if (!TryParseUtc(m.ValidFrom, out var from) || !TryParseUtc(m.ValidTo, out var to))
                {
                    Debug.LogWarning($"[Terrain] conditions modifier skipped type={m.Type} valid_from={m.ValidFrom} valid_to={m.ValidTo}");
                    continue;
                }

                try
                {
                    modifiers.Add(new ConditionModifier(m.Factor, from, to));
                }
                catch (ArgumentException invalid)
                {
                    Debug.LogWarning($"[Terrain] conditions modifier skipped type={m.Type} error={invalid.Message}");
                }
            }

            return new Conditions(info.Value, modifiers.ToArray());
        }

        private static bool TryParseUtc(string text, out DateTime value)
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);
        }

        private sealed class PlanResult
        {
            public PlanResult(CostGrid grid, IReadOnlyList<Plan> plans, float[] heatS, byte[] hazard)
            {
                Grid = grid;
                Plans = plans;
                HeatS = heatS;
                Hazard = hazard;
            }

            public CostGrid Grid { get; }
            public IReadOnlyList<Plan> Plans { get; }
            public float[] HeatS { get; }
            public byte[] Hazard { get; }
        }
    }
}
