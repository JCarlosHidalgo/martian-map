Shader "MartianMap/FX/RouteGlow"
{
    Properties
    {
        _Color ("Route color", Color) = (0.1, 0.8, 1, 0.8)
        _PulseSpeed ("Pulse speed", Range(0, 8)) = 1.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "RouteGlow"
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _PulseSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half pulse = 0.8h + 0.2h * sin(input.uv.x * 24.0h - _Time.y * _PulseSpeed);
                half center = 1.0h - abs(input.uv.y * 2.0h - 1.0h);
                return half4(_Color.rgb * pulse, _Color.a * (0.55h + 0.45h * center));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
