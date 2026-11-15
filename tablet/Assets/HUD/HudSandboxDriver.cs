using System;
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
                _map.Show(await FixtureLoader.LoadSynthZoneAsync(root));
#else
                _map.Show(await FixtureLoader.LoadSynthZoneAsync());
#endif
                Debug.Log("[HUD] sandbox loaded zone=synth source=contract_fixture");
            }
            catch (Exception error)
            {
                Debug.LogError("[HUD] sandbox could not load the contract tile detail=" + error.Message);
            }
        }
    }
}
