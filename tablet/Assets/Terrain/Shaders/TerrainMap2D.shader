// The 2D map terrain (owner P4). One pass, one shader: hillshade + elevation colour or the active
// layer, contour lines, nodata hatch, then the FX hooks (hazard, heat, style) from P5.
// The map lies on the plane Y = 0 (X = east, Z = north); the height only feeds the shading.
Shader "MartianMap/Terrain/TerrainMap2D"
{
    Properties
    {
        [NoScaleOffset] _HeightTex ("Height (R16)", 2D) = "black" {}
        [NoScaleOffset] _NodataTex ("Nodata mask (R8)", 2D) = "black" {}
        [NoScaleOffset] _HazardTex ("Hazard (R8)", 2D) = "black" {}
        [NoScaleOffset] _LayerTex0 ("Ortho (R8)", 2D) = "black" {}
        [NoScaleOffset] _LayerTex1 ("Thermal (R8)", 2D) = "black" {}
        [NoScaleOffset] _LayerTex2 ("Thermal IR (R8)", 2D) = "black" {}
        _HeightScale ("Height scale (m per unit sample)", Float) = 1
        _HeightOffset ("Height offset (m)", Float) = 0
        _MPerPx ("Metres per sample", Float) = 1
        _ShadeStrength ("Hillshade strength", Range(0, 1)) = 0.8
        _ShadeExaggeration ("Hillshade vertical exaggeration", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Map2D"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_HeightTex);
            TEXTURE2D(_NodataTex);
            TEXTURE2D(_HazardTex);
            TEXTURE2D(_LayerTex0);
            TEXTURE2D(_LayerTex1);
            TEXTURE2D(_LayerTex2);
            TEXTURE2D(_HeatTex);
            SAMPLER(sampler_terrain_linear_clamp);
            SAMPLER(sampler_terrain_point_clamp);

            CBUFFER_START(UnityPerMaterial)
                float _HeightScale;
                float _HeightOffset;
                float _MPerPx;
                float _ShadeStrength;
                float _ShadeExaggeration;
            CBUFFER_END

            // Globals (Shader.SetGlobal..., set by Terrain; _FogFar colour and strength by FX).
            float _ZoneOriginHeightM;
            float4 _ZoneHeightRange;
            float _LayerOn0;
            float _LayerOn1;
            float _LayerOn2;
            float _TopoOn;
            float _HazardOn;
            float _HeatOn;
            float _ContourIntervalM;
            float _FogFarAmberMin;
            float _FogFarRedMin;
            float4 _FogFarColor;
            float _FogFarStrength;
            float4 _HeatTexRect;

            #include "Assets/FX/Shaders/FxTerrainHooks.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 enuM : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.enuM = positionWS.xz;
                return output;
            }

            // Sample k of 257 sits at UV k / 256; the texel centre is (UV · 256 + 0.5) / 257.
            float2 SampleUv(float2 uv)
            {
                return (uv * 256.0 + 0.5) / 257.0;
            }

            float SceneY(float2 st)
            {
                return _HeightOffset + _HeightScale * SAMPLE_TEXTURE2D(_HeightTex, sampler_terrain_linear_clamp, st).r;
            }

            float3 ElevationRamp(float t)
            {
                float3 c0 = float3(0.20, 0.10, 0.07);
                float3 c1 = float3(0.52, 0.25, 0.13);
                float3 c2 = float3(0.78, 0.47, 0.26);
                float3 c3 = float3(0.95, 0.82, 0.64);
                float3 low = lerp(c0, c1, saturate(t * 3.0));
                float3 mid = lerp(c1, c2, saturate(t * 3.0 - 1.0));
                float3 high = lerp(c2, c3, saturate(t * 3.0 - 2.0));
                return t < 0.3333 ? low : (t < 0.6667 ? mid : high);
            }

            float3 ThermalRamp(float t)
            {
                float3 a = float3(0.05, 0.03, 0.25);
                float3 b = float3(0.55, 0.10, 0.55);
                float3 c = float3(0.95, 0.45, 0.10);
                float3 d = float3(1.00, 0.95, 0.60);
                float3 lo = lerp(a, b, saturate(t * 3.0));
                float3 mi = lerp(b, c, saturate(t * 3.0 - 1.0));
                float3 hi = lerp(c, d, saturate(t * 3.0 - 2.0));
                return t < 0.3333 ? lo : (t < 0.6667 ? mi : hi);
            }

            // Anti-aliased line where value/interval is an integer.
            float ContourLine(float value, float interval, float widthPx)
            {
                float f = value / max(interval, 0.01);
                float w = max(fwidth(f), 1e-5);
                float d = abs(frac(f + 0.5) - 0.5) / w;
                float stroke = 1.0 - saturate(d - widthPx * 0.5);
                // Fade when the lines get closer than a few pixels, to avoid moiré.
                return stroke * saturate(1.0 - (w - 0.15) / 0.25);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 st = SampleUv(input.uv);
                float texel = 1.0 / 257.0;

                // Hillshade from central differences of the bilinear height.
                float y = SceneY(st);
                float dydx = (SceneY(st + float2(texel, 0)) - SceneY(st - float2(texel, 0))) / (2.0 * _MPerPx);
                float dydz = (SceneY(st + float2(0, texel)) - SceneY(st - float2(0, texel))) / (2.0 * _MPerPx);
                float3 normal = normalize(float3(-dydx * _ShadeExaggeration, 1.0, -dydz * _ShadeExaggeration));
                float3 sun = normalize(float3(-1.0, 1.414, 1.0)); // from the north-west, 45° high
                float hill = saturate(dot(normal, sun) / sun.y);
                float shade = lerp(1.0, hill, _ShadeStrength);

                // Base colour: the active layer, or the elevation ramp.
                float range = max(_ZoneHeightRange.y - _ZoneHeightRange.x, 0.001);
                float3 color = ElevationRamp(saturate((y - _ZoneHeightRange.x) / range)) * shade;

                float ortho = SAMPLE_TEXTURE2D(_LayerTex0, sampler_terrain_linear_clamp, st).r;
                float thermal = SAMPLE_TEXTURE2D(_LayerTex1, sampler_terrain_linear_clamp, st).r;
                float thermalIr = SAMPLE_TEXTURE2D(_LayerTex2, sampler_terrain_linear_clamp, st).r;
                color = lerp(color, float3(1.0, 0.93, 0.86) * ortho * lerp(1.0, hill, 0.25), step(0.5, _LayerOn0));
                color = lerp(color, ThermalRamp(thermal) * lerp(1.0, hill, 0.35), step(0.5, _LayerOn1));
                color = lerp(color, thermalIr.xxx * lerp(1.0, hill, 0.25), step(0.5, _LayerOn2));

                // Contour lines on the height above the areoid: minor and every fifth one major.
                float h = y + _ZoneOriginHeightM;
                float interval = max(_ContourIntervalM, 0.5);
                float minor = ContourLine(h, interval, 1.0);
                float major = ContourLine(h, interval * 5.0, 1.6);
                float topo = step(0.5, _TopoOn);
                color = lerp(color, color * 0.55, minor * 0.6 * topo);
                color = lerp(color, color * 0.25, major * 0.85 * topo);

                // Hazard and heat (FX hooks).
                float hazard = SAMPLE_TEXTURE2D(_HazardTex, sampler_terrain_linear_clamp, st).r;
                color = lerp(color, FxApplyHazard(color, hazard, _FogFarAmberMin, _FogFarRedMin, _FogFarColor, _FogFarStrength, input.enuM), step(0.5, _HazardOn));

                float2 heatUv = (input.enuM - _HeatTexRect.xy) * _HeatTexRect.zw;
                float insideHeat = step(0.0, heatUv.x) * step(heatUv.x, 1.0) * step(0.0, heatUv.y) * step(heatUv.y, 1.0);
                float heat = SAMPLE_TEXTURE2D(_HeatTex, sampler_terrain_point_clamp, saturate(heatUv)).r;
                color = lerp(color, FxApplyHeat(color, heat), step(0.5, _HeatOn) * insideHeat);

                // Nodata: dark grey hatch.
                float nodata = step(0.5, SAMPLE_TEXTURE2D(_NodataTex, sampler_terrain_point_clamp, st).r);
                float hatch = step(0.5, frac((input.enuM.x + input.enuM.y) * 0.125));
                color = lerp(color, lerp(float3(0.08, 0.08, 0.09), float3(0.16, 0.16, 0.18), hatch), nodata);

                color = FxApplyMapStyle(color, input.enuM, input.uv);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
