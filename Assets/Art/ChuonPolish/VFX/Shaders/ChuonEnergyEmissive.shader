// Type-D 에너지 코어 / 바이저 발광 / 회로 라인용 HDR 발광 셰이더 (반복 10).
// 불투명 언릿: _Color x _Intensity (HDR → 블룸). 프레넬 테두리 + 느린 흐름 무늬로 '살아있는' 코어를 만든다.
Shader "ChuOn/EnergyEmissive"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.6, 0.9, 1.6, 1)
        [HDR] _RimColor ("Rim color", Color) = (1.5, 2.2, 3.0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 2
        _FlowAmount ("Flow pattern", Range(0, 1)) = 0.35
        _FlowSpeed ("Flow speed", Float) = 0.6
        _FlowScale ("Flow scale", Float) = 18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "EnergyEmissive"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _Intensity;
                half _FresnelPower;
                half _FlowAmount;
                half _FlowSpeed;
                half _FlowScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; float3 posOS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                o.posOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float ndv = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float fres = pow(1.0 - ndv, _FresnelPower);
                float flow = sin(i.posOS.y * _FlowScale + _Time.y * _FlowSpeed * 6.2831) * 0.5 + 0.5;
                flow = lerp(1.0, 0.7 + 0.6 * flow, _FlowAmount);
                half3 col = (_Color.rgb * flow + _RimColor.rgb * fres) * _Intensity;
                return half4(col, 1);
            }
            ENDHLSL
        }

        // 그림자/깊이 (DepthOnly) - 코어가 SSAO/깊이 림에 참여
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
