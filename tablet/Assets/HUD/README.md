# Mission HUD

Open `HUDSandbox.unity` with Unity **6000.3.28f1** and press Play. The sandbox loads P1's
synthetic contract tile from `contracts/fixtures/`, displays the C6 mission, and uses the
existing Terrain camera for pan and zoom. The mission panel identifies synthetic data.

The panel lists science objectives, stop coordinates, science values, dwell times, and the
time and absorbed-dose budgets. The assigned `SyntheticMission.json` is an exact copy of
the C6 fixture. Its 50 microgray budget is a project design value, not a NASA exposure limit.

`Plan route` stays disabled until a matching zone arrives through `MapEvents.ZoneLoaded`.
It then emits `RouteRequested` with C4 `Stop`, `Budget`, `EnuPoint`, and `StopMode` values.
The first route starts at the mission base. Terrain and Routing own route execution; the
sandbox does not fabricate a computed route. A received `RouteSelected` updates the result.

The reader rejects duplicate stop ids, invalid required fields, nonfinite values, and
home/stop coordinates outside the zone bounds. When `base_enu_m` is omitted, it uses
`ZoneInfo.HabitatEnuM`. Bounds checks do not establish terrain walkability: that requires
the actual tile/routing integration. Unknown fields are ignored when reading.

## Regenerate the sandbox

Use **MartianMap > HUD > Create mission sandbox**. This updates only the HUD prefab,
font/settings assets, and sandbox scene under `Assets/HUD/`. It does not edit `Main.unity`
or the Android build scene list. Save any open scene before running this command.

Inter is distributed with its SIL Open Font License in `Fonts/Inter-LICENSE.txt`.
TextMeshPro settings live under this folder because the skeleton has no imported TMP
essential resources. Existing project TMP settings take precedence when regenerating.
`Shaders/` contains the official TMP essential shaders with their original GUIDs and the
package's Unity Companion License. Avoid importing another copy of those same resources.

## Tests

In **Window > General > Test Runner > PlayMode**, run `MartianMap.HUD` tests. They read the
actual mission asset, cover the habitat fallback and invalid input, and exercise the live
Canvas/button/event flow with a matching and mismatching zone.

From PowerShell at the repository root:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe' `
  -batchmode -nographics -projectPath "$PWD\tablet" `
  -runTests -testPlatform PlayMode -testFilter MartianMap.HUD `
  -testResults "$env:TEMP\martian-map-hud-tests.xml" `
  -logFile "$env:TEMP\martian-map-hud-tests.log"
```

Close the editor first when running batch tests against the same project.

## Integration

P4 can keep the existing HUD prefab instance in `Main.unity`: it retains its original GUID.
The prefab now has `MissionHud`, an assigned mission asset, and a font. Its Canvas and
controls are created at runtime. There must be only one active EventSystem; the HUD creates
one only when the scene does not already have one.

This first increment covers the mission panel and route handoff. Layer controls, radiation
indicators, POV media, settings, astronaut relocation, and edit tools remain separate work.
