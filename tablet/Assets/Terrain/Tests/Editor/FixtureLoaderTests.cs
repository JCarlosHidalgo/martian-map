using System.IO;
using MartianMap.Routing;
using NUnit.Framework;
using UnityEngine;

namespace MartianMap.Terrain.Tests
{
    /// <summary>Reads the real fixtures of contracts/fixtures/ (P3's C4 goldens) into Routing types.</summary>
    public sealed class FixtureLoaderTests
    {
        private static string Fixture(string relative) =>
            File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "contracts", "fixtures", relative)));

        [Test]
        public void CostParamsKeepEveryField()
        {
            var p = FixtureLoader.ParseCostParams(Fixture(FixtureLoader.CostParamsPath));

            Assert.AreEqual(96, p.HazardAmberMin);
            Assert.AreEqual(192, p.HazardRedMin);
            Assert.AreEqual(0.70, p.V0Mps.P50, 1e-9);
            Assert.AreEqual(15.0, p.MaxSlopeDeg, 1e-9);
        }

        [Test]
        public void ExpectedRouteIsTheStraightLineOnTheRamp()
        {
            var route = FixtureLoader.ParseRoute(Fixture(FixtureLoader.ExpectedRoutePath));

            Assert.IsTrue(route.Found);
            Assert.AreEqual(101, route.Cells.Count);
            Assert.AreEqual(101, route.PolylineEnuM.Count);
            Assert.AreEqual(-100.0, route.PolylineEnuM[0].E, 1e-9);
            Assert.AreEqual(-20.0, route.PolylineEnuM[0].N, 1e-9);
            Assert.AreEqual(15.625, route.AscentM, 1e-9);
            Assert.AreEqual(0, route.MaxHazard);
        }

        [Test]
        public void ExpectedPlansComeOnePerAlternative()
        {
            var plans = FixtureLoader.ParsePlans(Fixture(FixtureLoader.ExpectedPlanPath));

            Assert.AreEqual(3, plans.Count, "one plan per Alternative");
            foreach (var plan in plans)
            {
                CollectionAssert.AreEqual(new[] { "s1", "s2" }, plan.Order);
                Assert.AreEqual(3, plan.Legs.Count);
                Assert.IsTrue(plan.ReturnOk);
                Assert.AreEqual(30000.0, plan.LimitS, 1e-9);
            }
        }

        [Test]
        public void ScenePolylineLiesOnTheMapPlaneWithoutRepeatedJoints()
        {
            var route = FixtureLoader.ParseRoute(Fixture(FixtureLoader.ExpectedRoutePath));
            var fromRoute = RouteSelected.ScenePolyline(route);
            Assert.AreEqual(new Vector3(-100f, 0f, -20f), fromRoute[0]);
            Assert.AreEqual(route.PolylineEnuM.Count, fromRoute.Length);

            var plan = FixtureLoader.ParsePlans(Fixture(FixtureLoader.ExpectedPlanPath))[(int)Alternative.Fastest];
            var fromPlan = RouteSelected.ScenePolyline(plan);
            var total = 0;
            foreach (var leg in plan.Legs)
            {
                total += leg.PolylineEnuM.Count;
            }

            Assert.Less(fromPlan.Length, total, "shared joints between legs appear once");
            for (var i = 1; i < fromPlan.Length; i++)
            {
                Assert.AreNotEqual(fromPlan[i - 1], fromPlan[i], $"repeated vertex at {i}");
                Assert.AreEqual(0f, fromPlan[i].y);
            }
        }
    }
}
