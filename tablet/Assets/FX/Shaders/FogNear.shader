Shader "MartianMap/FX/FogNear"
{
    Properties
    {
        _FogTint ("Fog tint", Color) = (0.9, 0.34, 0.12, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "AnalyticFog"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_HazardZoneTex);
            SAMPLER(sampler_HazardZoneTex);
            float4 _HeatTexRect;
            float _FogFarAmberMin;
            float _FogFarRedMin;

            CBUFFER_START(UnityPerMaterial)
                half4 _FogTint;
                half _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 enuM : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.enuM = world.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = (input.enuM - _HeatTexRect.xy) * _HeatTexRect.zw;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                half hazard = SAMPLE_TEXTURE2D(_HazardZoneTex, sampler_HazardZoneTex, saturate(uv)).r;
                half amber = step(_FogFarAmberMin, hazard);
                half red = step(_FogFarRedMin, hazard);
                float2 p = input.enuM * 0.028;
                half wave = 0.5h + 0.5h * sin(p.x + _Time.y * 0.3) * sin(p.y - _Time.y * 0.23);
                half density = saturate(amber * (0.45h + red * 0.45h) * (0.55h + 0.45h * wave));
                return half4(_FogTint.rgb, density * _Opacity * inside);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
