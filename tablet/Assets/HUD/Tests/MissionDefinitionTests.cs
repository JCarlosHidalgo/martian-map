using System;
using System.IO;
using MartianMap.Routing;
using MartianMap.Terrain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MartianMap.HUD
{
    public sealed class MissionDefinitionTests
    {
        internal static string Golden => File.ReadAllText(Path.Combine(Application.dataPath, "HUD/Missions/SyntheticMission.json"));

        internal static ZoneInfo Zone() => new ZoneInfo
        {
            ZoneId = "synth",
            ExtentEnuM = new double[] { -128, -128, 128, 128 },
            HabitatEnuM = new double[] { -90, -10 },
        };

        [Test]
        public void ConsumerMissionMatchesTheC6ProducerFixtureByteForByte()
        {
            string fixture = Path.GetFullPath(Path.Combine(Application.dataPath, "../../contracts/fixtures/mission/mission.json"));
            string asset = Path.Combine(Application.dataPath, "HUD/Missions/SyntheticMission.json");
            Assert.That(File.ReadAllBytes(asset), Is.EqualTo(File.ReadAllBytes(fixture)));
        }

        [Test]
        public void GoldenMissionBecomesC4Inputs()
        {
            // Shared synthetic coordinates and budgets from the contract guide.
            MissionDefinition mission = MissionDefinition.Parse(Golden);
            Assert.That(mission.ZoneId, Is.EqualTo("synth"));
            Assert.That(mission.Stops.Count, Is.EqualTo(2));
            Assert.That(mission.Stops[0].Value.Id, Is.EqualTo("s1"));
            Assert.That(mission.Stops[0].Value.EnuM.E, Is.EqualTo(0));
            Assert.That(mission.Stops[1].Value.EnuM.E, Is.EqualTo(100));
            Assert.That(mission.Stops[1].Value.ScienceValue, Is.EqualTo(8));
            Assert.That(mission.Stops[1].Value.DwellS, Is.EqualTo(600));
            Assert.That(mission.Budget.TS, Is.EqualTo(14400));
            Assert.That(mission.Budget.DoseUGy, Is.EqualTo(50));
            Assert.That(mission.Mode, Is.EqualTo(StopMode.Tsp));
            Assert.That(mission.ResolveHome(Zone()).E, Is.EqualTo(-100));
        }

        [Test]
        public void IntegerValuedJsonNumbersRemainCompatibleWithTheSchema()
        {
            var json = JObject.Parse(Golden);
            json["schema_version"] = 1.0;
            json["stops"][0]["science_value"] = 5.0;
            Assert.That(MissionDefinition.Parse(json.ToString()).Stops[0].Value.ScienceValue, Is.EqualTo(5));
        }

        [Test]
        public void OmittedBaseUsesTheActiveZoneHabitat()
        {
            var json = JObject.Parse(Golden);
            json.Remove("base_enu_m");
            json["mode"] = "OP";
            MissionDefinition mission = MissionDefinition.Parse(json.ToString());
            Assert.That(mission.ResolveHome(Zone()).E, Is.EqualTo(-90));
            Assert.That(mission.ResolveHome(Zone()).N, Is.EqualTo(-10));
            Assert.That(mission.Mode, Is.EqualTo(StopMode.Op));
        }

        [TestCase("schema_version")]
        [TestCase("zone_id")]
        [TestCase("name")]
        [TestCase("stops")]
        [TestCase("budget")]
        [TestCase("mode")]
        public void MissingRootFieldsAreRejected(string field)
        {
            var json = JObject.Parse(Golden);
            json.Remove(field);
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(json.ToString()));
        }

        [Test]
        public void DuplicateStopIdsAreRejected()
        {
            var json = JObject.Parse(Golden);
            json["stops"][1]["id"] = "s1";
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(json.ToString()));
        }

        [TestCase("null")]
        [TestCase("[0]")]
        [TestCase("[0, 0, 0]")]
        [TestCase("[true, 0]")]
        public void InvalidBaseCoordinatesAreRejected(string value)
        {
            var json = JObject.Parse(Golden);
            json["base_enu_m"] = JToken.Parse(value);
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(json.ToString()));
        }

        [Test]
        public void NonFiniteNumbersNegativeBudgetsAndInvalidScienceValuesAreRejected()
        {
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(Golden.Replace("14400", "1e999")));
            var json = JObject.Parse(Golden);
            json["budget"]["t_s"] = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => MissionDefinition.Parse(json.ToString()));
            json = JObject.Parse(Golden);
            json["stops"][0]["science_value"] = 11;
            Assert.Throws<ArgumentOutOfRangeException>(() => MissionDefinition.Parse(json.ToString()));
        }

        [Test]
        public void WrongZonesAndStopsOutsideBoundsAreRejected()
        {
            MissionDefinition mission = MissionDefinition.Parse(Golden);
            ZoneInfo zone = Zone();
            zone.ZoneId = "gale";
            Assert.Throws<ArgumentException>(() => mission.ResolveHome(zone));
            var json = JObject.Parse(Golden);
            json["stops"][0]["enu_m"] = new JArray(500, 0);
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(json.ToString()).ResolveHome(Zone()));
        }

        [Test]
        public void UnknownFieldsAreIgnoredButDuplicateJsonKeysAndTrailingContentAreRejected()
        {
            var json = JObject.Parse(Golden);
            json["future_field"] = 1;
            Assert.That(MissionDefinition.Parse(json.ToString()).Stops.Count, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(Golden + "{}"));
            Assert.Throws<ArgumentException>(() => MissionDefinition.Parse(Golden.Replace("\"schema_version\": 1", "\"schema_version\": 1, \"schema_version\": 1")));
        }
    }
}
