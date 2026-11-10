// 퍼펙트 회피 잔상 (Chuon / TSUKASA 톤)
// - 정면은 그래파이트 코어, 중간 띠는 세라믹 화이트, 실루엣은 TSUKASA 레드 림
//   -> 어두운 로비(화이트+레드가 읽힘)와 밝은 튜토리얼 도시(그래파이트+레드가 읽힘) 모두에서 판독 가능
// - 1패스(SRPDefaultUnlit): 깊이만 기록 -> 2패스(UniversalForward)에서 자기 겹침 없이 깔끔한 실루엣
// - _Fade 는 MaterialPropertyBlock 으로 잔상마다 갱신 (공유 머티리얼 1개)
Shader "Chuon/DodgeGhost"
{
    Properties
    {
        _CoreColor ("Core (graphite)", Color) = (0.11, 0.115, 0.13, 1)
        _BodyColor ("Body (ceramic white)", Color) = (0.94, 0.95, 0.97, 1)
        [HDR] _RimColor ("Rim (TSUKASA red)", Color) = (1.35, 0.18, 0.18, 1)
        _BandStart ("Ceramic band start", Range(0, 1)) = 0.18
        _BandEnd ("Ceramic band end", Range(0, 1)) = 0.62
        _RimPower ("Rim power", Range(0.5, 8)) = 2.6
        _CoreAlpha ("Core alpha", Range(0, 1)) = 0.62
        _ScanDensity ("Scanline density (per m)", Float) = 38
        _ScanStrength ("Scanline strength", Range(0, 1)) = 0.22
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _CoreColor;
            half4 _BodyColor;
            half4 _RimColor;
            half _BandStart;
            half _BandEnd;
            half _RimPower;
            half _CoreAlpha;
            float _ScanDensity;
            half _ScanStrength;
            half _Fade;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "GhostColor"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = 1.0h - saturate(abs(dot(n, v)));

                // 그래파이트 코어 -> 세라믹 띠 (주광 방향으로 살짝 형태감)
                half band = smoothstep(_BandStart, _BandEnd, fres);
                half lambert = saturate(dot(n, _MainLightPosition.xyz)) * 0.35h + 0.65h;
                half3 col = lerp(_CoreColor.rgb * lambert, _BodyColor.rgb, band);

                // TSUKASA 레드 림
                half rim = saturate(pow(fres, _RimPower) * 1.8h);
                col = lerp(col, _RimColor.rgb, rim);

                // 얇은 가로 스캔라인 (위상 잔상 느낌)
                half scan = 1.0h - _ScanStrength * step(0.5h, frac(i.positionWS.y * _ScanDensity));

                half alpha = lerp(_CoreAlpha, 1.0h, max(band * 0.6h, rim)) * scan * _Fade;
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
