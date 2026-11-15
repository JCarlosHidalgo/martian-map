using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MartianMap.Terrain;
using UnityEngine;

namespace MartianMap.HUD
{
    /// <summary>Sandbox only: starts the existing Terrain map with the shared synthetic contract tile.</summary>
    public sealed class HudSandboxDriver : MonoBehaviour
    {
        [SerializeField] private TerrainMap2D _map;

        private async void Start()
        {
            try
            {
#if UNITY_EDITOR
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../contracts/fixtures"));
                LoadedZone zone = await FixtureLoader.LoadSynthZoneAsync(root);
#else
                LoadedZone zone = await FixtureLoader.LoadSynthZoneAsync();
#endif
                _map.Show(zone);
                if (zone.Conditions == null)
                {
                    MapEvents.Emit(new ConditionsChanged("synth", SimulatedConditions()));
                }

                Debug.Log("[HUD] sandbox loaded zone=synth source=contract_fixture");
            }
            catch (Exception error)
            {
                Debug.LogError("[HUD] sandbox could not load the contract tile detail=" + error.Message);
            }
        }

        private static ConditionsInfo SimulatedConditions()
        {
            DateTime now = DateTime.UtcNow;
            const string format = "yyyy-MM-dd'T'HH:mm:ss'Z'";
            return new ConditionsInfo
            {
                SchemaVersion = 1,
                SourceId = "synthetic-hud-demo",
                TUtc = now.ToString(format, CultureInfo.InvariantCulture),
                TValidUtc = now.ToString(format, CultureInfo.InvariantCulture),
                Quantity = "dose_rate",
                Value = 6,
                Unit = "uGy/h",
                Uncertainty = 0.2,
                Basis = "modelled",
                DerivedFrom = HudStrings.SyntheticConditions,
                Modifiers = new List<ConditionsModifier>
                {
                    new ConditionsModifier
                    {
                        Type = "WSAEnlilSimulations",
                        Factor = 0.95,
                        ValidFrom = now.AddHours(-1).ToString(format, CultureInfo.InvariantCulture),
                        ValidTo = now.AddHours(1).ToString(format, CultureInfo.InvariantCulture),
                    },
                },
            };
        }
    }
}
