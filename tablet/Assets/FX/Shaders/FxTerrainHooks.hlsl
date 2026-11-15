// Owner: P5 (tablet/Assets/FX/). Hooks the 2D terrain shader calls, so FX styles the map without
// touching the terrain shader (P4) and without adding overdraw. P4 wrote these defaults so the map
// renders before FX exists; P5 replaces the bodies and keeps the signatures (a signature change is
// a C5 change). In 2D these hooks take the place of the SG_FogFar, SG_Heatmap and SG_Hologram Sub Graphs.
#ifndef MARTIANMAP_FX_TERRAIN_HOOKS_INCLUDED
#define MARTIANMAP_FX_TERRAIN_HOOKS_INCLUDED

// hazard01: the C1 hazard sample read as 0–1. amberMin/redMin: thresholds from cost_params.json / 255.
// fogColor/strength: _FogFarColor and _FogFarStrength. enuM: [e, n] of the pixel, in metres.
float3 FxApplyHazard(float3 color, float hazard01, float amberMin, float redMin, float4 fogColor, float strength, float2 enuM)
{
    float red = step(redMin, hazard01);
    float amber = step(amberMin, hazard01) * (1.0 - red);
    float stripes = 0.8 + 0.2 * step(0.5, frac((enuM.x + enuM.y) * 0.125));
    color = lerp(color, float3(1.0, 0.68, 0.12), amber * strength * 0.7 * stripes);
    color = lerp(color, fogColor.rgb, red * strength * stripes);
    return color;
}

// heatRaw: the _HeatTex sample read as 0–1; 1 (255) means "no way back". Values 0–254 map to 0.._HeatMaxS.
float3 FxApplyHeat(float3 color, float heatRaw)
{
    float noReturn = step(0.999, heatRaw);
    float t = saturate(heatRaw * 255.0 / 254.0);
    float3 ramp = t < 0.5 ? lerp(float3(0.1, 0.8, 0.35), float3(1.0, 0.85, 0.15), t * 2.0)
                          : lerp(float3(1.0, 0.85, 0.15), float3(0.95, 0.25, 0.1), (t - 0.5) * 2.0);
    float3 heated = lerp(color, ramp, 0.45);
    return lerp(heated, color * 0.35, noReturn);
}

// The 2D terrain is seen from above, so its hologram uses scanlines and an anti-aliased grid.
// P4 already draws the height contours in the same terrain pass; no extra transparent overlay.
float _FxHologramStrength;
float _FxScanDensity;
float _FxWireStrength;

float3 FxApplyMapStyle(float3 color, float2 enuM, float2 uv)
{
    float2 cells = uv * 256.0;
    float2 edge = abs(frac(cells + 0.5) - 0.5);
    float2 width = max(fwidth(cells), float2(1e-4, 1e-4));
    float grid = 1.0 - min(edge.x / width.x, edge.y / width.y);
    grid = saturate(grid) * saturate(1.0 - max(width.x, width.y) / 2.0);
    float scan = 0.5 + 0.5 * sin(enuM.y * max(_FxScanDensity, 0.01) - _Time.y * 1.8);
    float glow = _FxHologramStrength * (scan * 0.06 + grid * _FxWireStrength * 0.22);
    return saturate(color + glow * float3(0.10, 0.75, 1.0));
}

#endif
