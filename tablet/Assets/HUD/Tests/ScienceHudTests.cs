using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MartianMap.Terrain;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    public sealed class ScienceHudTests
    {
        private static ZoneInfo Zone()
        {
            ZoneInfo zone = MissionDefinitionTests.Zone();
            zone.Tiles.Add(new ZoneTileRef());
            zone.Layers = new List<ZoneLayerInfo>
            {
                new ZoneLayerInfo { Id = LayerIds.Ortho, Qualitative = true },
                new ZoneLayerInfo { Id = LayerIds.Thermal, Unit = "J m-2 K-1 s-1/2" },
                new ZoneLayerInfo { Id = LayerIds.ThermalIr, Qualitative = true },
            };
            return zone;
        }

        [UnityTest]
        public IEnumerator ButtonsReachTerrainAndKeepBaseLayersExclusive()
        {
            var root = new GameObject("Science HUD test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            root.AddComponent<MissionHud>().LoadMission(asset);
            var science = root.AddComponent<ScienceHud>();
            LayerToggled? observed = null;
            Action<LayerToggled> listener = message => observed = message;
            MapEvents.LayerToggled += listener;
            try
            {
                root.SetActive(true);
                yield return null;
                var buttons = root.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("Layer ")).ToDictionary(button => button.name);
                Assert.That(buttons.Values.All(button => !button.interactable), Is.True);
                MapEvents.Emit(new ZoneLoaded(Zone(), string.Empty, "ev1"));
                buttons["Layer ortho"].onClick.Invoke();
                Assert.That(observed.Value.LayerId, Is.EqualTo(LayerIds.Ortho));
                Assert.That(observed.Value.On, Is.True);
                Assert.That(science.LayerLegend, Does.Contain("QUALITATIVE"));
                Assert.That(science.LayerLegend, Does.Not.Contain("Unit:"));
                buttons["Layer thermal"].onClick.Invoke();
                Assert.That(buttons["Layer ortho"].GetComponentInChildren<TMP_Text>().text, Does.StartWith("[OFF]"));
                Assert.That(buttons["Layer thermal"].GetComponentInChildren<TMP_Text>().text, Does.StartWith("[ON]"));
                Assert.That(science.LayerLegend, Does.Contain("100 m/px"));
                Assert.That(science.LayerLegend, Does.Contain("J m-2"));
                ZoneInfo next = Zone();
                next.Layers.RemoveAll(layer => layer.Id == LayerIds.Thermal);
                MapEvents.Emit(new ZoneLoaded(next, string.Empty, "ev1"));
                Assert.That(buttons["Layer thermal"].interactable, Is.False);
                Assert.That(observed.Value.LayerId, Is.EqualTo(LayerIds.Thermal));
                Assert.That(observed.Value.On, Is.False);
            }
            finally
            {
                MapEvents.LayerToggled -= listener;
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(asset);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RadiationHasBasisDashedModelProvenanceAndZoneFiltering()
        {
            var root = new GameObject("Radiation HUD test");
            root.SetActive(false);
            var asset = new TextAsset(MissionDefinitionTests.Golden);
            root.AddComponent<MissionHud>().LoadMission(asset);
            var science = root.AddComponent<ScienceHud>();
            try
            {
                root.SetActive(true);
                yield return null;
                MapEvents.Emit(new ZoneLoaded(Zone(), string.Empty, "ev1"));
                MapEvents.Emit(new ConditionsChanged("gale", RadiationReadingTests.Conditions()));
                Assert.That(science.Reading, Is.Null);
                var source = RadiationReadingTests.Conditions();
                MapEvents.Emit(new ConditionsChanged("synth", source));
                Assert.That(science.Reading.IsModelled, Is.True);
                Transform outline = root.GetComponentsInChildren<Transform>(true).Single(value => value.name == "Modelled dashed outline");
                Assert.That(outline.gameObject.activeSelf, Is.True);
                Button derivation = root.GetComponentsInChildren<Button>(true).Single(button => button.name == "Derivation");
                derivation.onClick.Invoke();
                // The derivation is hidden at the team's request: it never shows on screen.
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("Derived from:")), Is.False);
                source.TValidUtc = DateTime.UtcNow.AddHours(-2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                source.Modifiers.Add(new ConditionsModifier
                {
                    Type = "SEP", Factor = 2,
                    ValidFrom = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ValidTo = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                });
                MapEvents.Emit(new ConditionsChanged("synth", source));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "SCENARIO"), Is.True);
                source.Modifiers[0].ValidTo = DateTime.UtcNow.AddSeconds(-2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                MapEvents.Emit(new ConditionsChanged("synth", source));
                Assert.That(root.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "SCENARIO"), Is.False);
                source = RadiationReadingTests.Conditions();
                source.Basis = "measured";
                MapEvents.Emit(new ConditionsChanged("synth", source));
                Assert.That(science.Reading.BasisText, Is.EqualTo("MEASURED"));
                Assert.That(outline.gameObject.activeSelf, Is.False);
                Assert.That(derivation.gameObject.activeSelf, Is.False);
                ZoneInfo next = Zone();
                next.ZoneId = "gale";
                MapEvents.Emit(new ZoneLoaded(next, string.Empty, "ev1"));
                Assert.That(science.Reading, Is.Null);
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
