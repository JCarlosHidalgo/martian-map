using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using MartianMap.Routing;
using MartianMap.Terrain;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    public sealed class RouteAlternativesHudTests
    {
        private static Plan Plan(bool returnOk = true, double amber = 12, double height = 2) => new Plan(
            new[] { "s1" }, Array.Empty<string>(),
            new[]
            {
                new Route(true, new[] { 0, 1 }, Array.Empty<EnuPoint3>(), 100, height, amber, default, 128),
                new Route(true, new[] { 1, 0 }, Array.Empty<EnuPoint3>(), 100, 3, 8, default, 128),
            }, new Percentiles(600, 900, 1200), 50, 14400, returnOk);

        [TestCase(0, "0 m")]
        [TestCase(999, "999 m")]
        [TestCase(1000, "1.00 km")]
        [TestCase(1234, "1.23 km")]
        [TestCase(-1, "unavailable")]
        public void DistancesFollowThePresentationContractInEveryLocale(double metres, string expected)
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
                Assert.That(HudFormatting.Distance(metres), Is.EqualTo(expected));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void ComparisonSumsExistingLegMetricsIncludingTheReturnLeg()
        {
            string details = RoutePresentation.Details(Plan());
            Assert.That(details, Does.Contain("ETA 15 min"));
            Assert.That(details, Does.Contain("10 min"));
            Assert.That(details, Does.Contain("20 min"));
            Assert.That(details, Does.Contain("Amber 20 m"));
            Assert.That(details, Does.Contain("Height change 5 m"));
            Assert.That(details, Does.Contain("Return available"));
            Assert.That(RoutePresentation.Details(Plan(false)), Does.Contain("Return unavailable"));
        }

        [Test]
        public void MissingUnreachableAndInvalidMetricsDoNotAppearAsZeroCostRoutes()
        {
            var plans = new[]
            {
                null,
                new Plan(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Route>(), default, 0, 0, false),
                new Plan(Array.Empty<string>(), Array.Empty<string>(), new[] { Route.NotFound }, default, 0, 0, false),
                Plan(amber: double.NaN), Plan(height: -1), Plan(amber: double.PositiveInfinity),
            };
            foreach (Plan plan in plans)
            {
                Assert.That(RoutePresentation.Details(plan), Is.EqualTo(HudStrings.RouteUnavailable));
            }

            Assert.That(HudFormatting.Distance(double.PositiveInfinity), Is.EqualTo(HudStrings.Unavailable));
            Assert.That(HudFormatting.Distance(double.NaN), Is.EqualTo(HudStrings.Unavailable));
        }

        [UnityTest]
        public IEnumerator ChoicesRequestC5AlternativesFromTheCurrentPositionAndClearOldResults()
        {
            var root = new GameObject("Route alternatives test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            var mission = root.AddComponent<MissionHud>();
            mission.LoadMission(asset);
            var comparison = root.AddComponent<RouteAlternativesHud>();
            RouteRequested? observed = null;
            Action<RouteRequested> listener = message => observed = message;
            MapEvents.RouteRequested += listener;
            try
            {
                root.SetActive(true);
                yield return null;
                comparison.Open();
                Button[] buttons = root.GetComponentsInChildren<Button>(true).Where(button => button.name.StartsWith("Alternative ", StringComparison.Ordinal)).ToArray();
                Assert.That(buttons.All(button => !button.interactable), Is.True);
                Assert.That(mission.SelectAlternative(Alternative.Safest), Is.False);
                MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
                mission.MoveAstronaut(new Vector2(10, 20));
                foreach (Alternative alternative in new[] { Alternative.Fastest, Alternative.Safest, Alternative.Gentlest })
                {
                    Button button = buttons.Single(value => value.name == "Alternative " + alternative);
                    Assert.That(button.interactable, Is.True);
                    button.onClick.Invoke();
                    Assert.That(observed.Value.Alternative, Is.EqualTo(alternative));
                    Assert.That(observed.Value.Start.E, Is.EqualTo(10));
                    Assert.That(observed.Value.Start.N, Is.EqualTo(20));
                    Assert.That(observed.Value.Home.E, Is.EqualTo(-100));
                    Assert.That(observed.Value.Budget.DoseUGy, Is.EqualTo(50));
                    Assert.That(button.GetComponentInChildren<TMP_Text>().text, Does.Contain(HudStrings.RouteOptionRequested));
                }

                MapEvents.Emit(new RouteSelected(new[] { Plan(), Plan(false, 0, 10), Plan(true, 4, 0) }, Alternative.Gentlest, Array.Empty<Vector3>()));
                Assert.That(buttons.Single(button => button.name == "Alternative Gentlest").GetComponentInChildren<TMP_Text>().text,
                    Does.Contain(HudStrings.RouteOptionSelected).And.Contain("Amber 12 m").And.Contain("Height change 3 m"));
                Assert.That(buttons.Single(button => button.name == "Alternative Safest").GetComponentInChildren<TMP_Text>().text,
                    Does.Contain("Return unavailable"));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Route").text,
                    Does.Contain("Gentlest").And.Contain("Height change 3 m"));
                mission.RequestRoute();
                Assert.That(observed.Value.Alternative, Is.EqualTo(Alternative.Gentlest));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Route").text,
                    Is.EqualTo(HudStrings.RouteOptionsPending));
                Assert.That(buttons.All(button => !button.GetComponentInChildren<TMP_Text>().text.Contains("Amber")), Is.True);
                mission.MoveAstronaut(new Vector2(20, 20));
                Assert.That(buttons[0].GetComponentInChildren<TMP_Text>().text, Does.Contain(HudStrings.RoutePositionChanged));
                Assert.That(mission.SelectAlternative((Alternative)99), Is.False);
                ZoneInfo other = MissionDefinitionTests.Zone();
                other.ZoneId = "gale";
                MapEvents.Emit(new ZoneLoaded(other, string.Empty, "ev1"));
                Assert.That(buttons.All(button => !button.interactable), Is.True);
                comparison.Close();
            }
            finally
            {
                MapEvents.RouteRequested -= listener;
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(asset);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ImmediateTerrainRepliesStayVisibleRegardlessOfBusSubscriptionOrder()
        {
            var root = new GameObject("Immediate route reply test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            var mission = root.AddComponent<MissionHud>();
            mission.LoadMission(asset);
            var comparison = root.AddComponent<RouteAlternativesHud>();
            Action<RouteRequested> cachedReply = request => MapEvents.Emit(
                new RouteSelected(new[] { Plan(), Plan(), Plan() }, request.Alternative, Array.Empty<Vector3>()));
            MapEvents.RouteRequested += cachedReply;
            try
            {
                root.SetActive(true);
                yield return null;
                MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
                comparison.Open();
                mission.SelectAlternative(Alternative.Safest);
                string text = root.GetComponentsInChildren<Button>(true).Single(button => button.name == "Alternative Safest")
                    .GetComponentInChildren<TMP_Text>().text;
                Assert.That(text, Does.Contain(HudStrings.RouteOptionSelected).And.Contain("Amber 20 m"));
                Assert.That(text, Does.Not.Contain(HudStrings.RouteOptionsPending));
            }
            finally
            {
                MapEvents.RouteRequested -= cachedReply;
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(asset);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RealGoldenPlansFitTheComparisonAndMalformedResultsClearTheDisplay()
        {
            var root = new GameObject("Golden route alternatives test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            var mission = root.AddComponent<MissionHud>();
            mission.LoadMission(asset);
            var comparison = root.AddComponent<RouteAlternativesHud>();
            try
            {
                root.SetActive(true);
                yield return null;
                MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../contracts/fixtures/routing/expected_plan.json"));
                var plans = FixtureLoader.ParsePlans(File.ReadAllText(path));
                MapEvents.Emit(new RouteSelected(plans, Alternative.Safest, Array.Empty<Vector3>()));
                comparison.Open();
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
                {
                    text.ForceMeshUpdate();
                    Assert.That(text.isTextOverflowing, Is.False, text.name);
                }

                Assert.That(mission.SelectedAlternative, Is.EqualTo(Alternative.Safest));
                MapEvents.Emit(new RouteSelected(new[] { Plan() }, Alternative.Fastest, Array.Empty<Vector3>()));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Route").text,
                    Is.EqualTo(HudStrings.RouteOptionsInvalid));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains(HudStrings.RouteOptionsInvalid)), Is.True);
                MapEvents.Emit(new RouteSelected(plans, (Alternative)99, Array.Empty<Vector3>()));
                MapEvents.Emit(new RouteSelected(new Plan[] { null, Plan(), Plan() }, Alternative.Fastest, Array.Empty<Vector3>()));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Route").text,
                    Does.Contain(HudStrings.RouteUnavailable));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(asset);
            }

            yield return null;
        }
    }
}
