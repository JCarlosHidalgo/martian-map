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
