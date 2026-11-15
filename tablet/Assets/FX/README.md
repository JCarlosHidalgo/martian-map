# P5 FX integration

The current tablet renders a 2D terrain map. `Shaders/FxTerrainHooks.hlsl` is included by P4's
`TerrainMap2D.shader` and implements the far hazard tint, heatmap ramp, scanlines and grid in that
same opaque terrain pass. The height contours are already in P4's terrain shader. The three C5
Shader Graph slots are represented by these HLSL hooks in the current 2D renderer; there is no
terrain Shader Graph asset to attach Sub Graph assets to in this branch.

`FX.prefab` sets the global tint and hologram parameters and draws one transparent analytic fog
quad over the active zone. It samples P4's `_HazardZoneTex` using `_HeatTexRect`. P4's current
`TerrainMap2D` only initializes `_HazardZoneTex` to black; the real near fog needs P4 to publish
the cost grid hazard texture when building or updating that grid. The preview supplies a mock
texture. The optional `SparseDust` particles are capped at 12 and disabled
in the prefab until E3 measures their cost. `Routes.prefab` listens to `MapEvents.RouteSelected` and draws the
chosen scene polyline with one core and one halo. Both prefabs are owned by P5. They do not modify
P4's terrain material or `Main.unity`.

In Unity, run **MartianMap → FX → Set up preview**. This adds the prefabs and a fixture route event
source to `TerrainSandbox.unity`; then press Play. The preview uses the synthetic tile and route
fixtures, creates two hazard spots and a mock R8 heatmap, and sets 30 fps. P4 should place
`FX.prefab` and `Routes.prefab` once in `Main.unity` when
integrating. Do not put `FxPreviewEvents` in the main scene.

No new Unity package is needed: the shaders use the URP package already in `Packages/manifest.json`.
`ingest/probe_c6.py` uses Python's standard library and `curl` to cache E14 source responses in
`ingest/cache/h0/`. The C6 fixture is deliberately synthetic; P1 generates the live conditions.

The remaining device gates are E1 (Vulkan/GLES3 × render scale 0.6/0.75/1, p95 ≤ 33.3 ms), E3
(bloom/fog/hologram overdraw and p95) and E4 (five minutes of camera motion and rotation without
artifacts). They require a Unity build and the target tablet; source inspection cannot establish
their results. If overdraw exceeds E3, set `nearOpacity` to zero and leave `enableDust` off in
`FX.prefab`, keeping the single-pass far tint.
