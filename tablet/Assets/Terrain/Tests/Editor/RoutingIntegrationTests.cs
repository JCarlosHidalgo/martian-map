using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MartianMap.Edits;
using MartianMap.Routing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MartianMap.Terrain.Tests
{
    /// <summary>
    /// Terrain's side of the Routing/Edits integration: tiles loaded by Terrain, converted with
    /// RouteOrchestrator.ToRasters, give P3's engine the same answers as its own goldens.
    /// </summary>
    public sealed class RoutingIntegrationTests
    {
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "contracts", "fixtures"));

        private LoadedZone _zone;
        private CostParams _params;

        [UnitySetUp]
        public IEnumerator Load()
        {
            var zone = FixtureLoader.LoadSynthZoneAsync(Root);
            var costParams = FixtureLoader.LoadCostParamsAsync(Root);
            yield return Wait(Task.WhenAll(zone, costParams));
            _zone = zone.Result;
            _params = costParams.Result;
        }

        [Test]
        public void RouteOverTerrainTilesMatchesTheGolden()
        {
            var tiles = _zone.Tiles.Select(RouteOrchestrator.ToRasters).ToArray();
            var grid = Router.BuildGrid(tiles, _zone.Zone.OriginHeightM, _params);
            var route = Router.PlanRoute(grid, new EnuPoint(-100, -20), new EnuPoint(100, -20), _params);
            var golden = FixtureLoader.ParseRoute(File.ReadAllText(Path.Combine(Root, FixtureLoader.ExpectedRoutePath)));

            Assert.IsTrue(route.Found);
            Assert.AreEqual(golden.AscentM, route.AscentM, 0.01, "ascent on the ramp");
            Assert.AreEqual(golden.LengthM, route.LengthM, 0.5, "length");
            Assert.AreEqual(0, route.MaxHazard, "the straight line misses both hazard squares");
        }

        [Test]
        public void AlternativesAndFieldRunOnTerrainTiles()
        {
            var tiles = _zone.Tiles.Select(RouteOrchestrator.ToRasters).ToArray();
            var grid = Router.BuildGrid(tiles, _zone.Zone.OriginHeightM, _params);
            var alternatives = Router.Alternatives(grid, new EnuPoint(-100, -20), new EnuPoint(100, -20), _params);
            Assert.AreEqual(3, alternatives.Count, "one route per Alternative");
            Assert.IsTrue(alternatives.All(r => r.Found));

            var field = Router.ReturnField(grid, new EnuPoint(-100, -20), _params);
            Assert.AreEqual(grid.Width * grid.Height, field.TimeS.Length);
        }

        [Test]
        public void EditsRaiseTheHazardOfTheTerrainTile()
        {
            var tile = _zone.Tiles.Single();
            var log = JObject.Parse(File.ReadAllText(Path.Combine(Root, "merge", "log_in.json")));
            var edits = ((JArray)log["edits"])
                .Select(e => EditJson.TryRead((JObject)e, false, out var edit) ? edit : null)
                .Where(e => e != null)
                .ToArray();

            var origin = new EnuMetres((int)tile.OriginEnuM.x, (int)tile.OriginEnuM.y);
            var hazard = EditEngine.Apply(tile.BaseHazard, origin, edits);
            Assert.AreEqual(tile.BaseHazard.Length, hazard.Length);
            Assert.IsTrue(hazard.Where((h, i) => h > tile.BaseHazard[i]).Any(), "live entities raise some samples");
            Assert.IsTrue(hazard.Where((h, i) => h < tile.BaseHazard[i]).Count() == 0, "edits never lower the base");

            Assert.IsTrue(tile.SetEditHazard(hazard), "the tile's effective hazard changes");
            Assert.IsNotEmpty(EditEngine.LiveEntities(edits));
        }

        private static IEnumerator Wait(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("loading the fixtures took more than 30 s");
                }

                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception!.GetBaseException();
            }
        }
    }
}
