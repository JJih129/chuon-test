// 시네마틱 홀로그램 (반복 14): 츠카사 엔딩 실루엣, 은신처 작전 지도. URP 언릿, 가산 투명.
// 톤: 흰 세라믹 빛 + 츠카사 레드 림/스캔 (네온 시안 금지). 값은 CineHologramFigure 가 MaterialPropertyBlock 으로 갱신.
Shader "Chuon/CineHologram"
{
    Properties
    {
        _Color ("Body Color", Color) = (0.86, 0.9, 0.95, 1)
        _RimColor ("Rim Color (TSUKASA red)", Color) = (1, 0.12, 0.1, 1)
        _Intensity ("Intensity", Range(0, 2)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 0.55
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.4
        _ScanDensity ("Scan lines per metre", Float) = 38
        _ScanSpeed ("Scan speed", Float) = 0.6
        _Materialize ("Materialize", Range(0, 1)) = 1
        _Glitch ("Glitch", Range(0, 1)) = 0.1
        _HeightMin ("Height Min (world)", Float) = 0
        _HeightMax ("Height Max (world)", Float) = 2
        _HoloTime ("Time (unscaled)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _Intensity;
                half _Alpha;
                half _RimPower;
                float _ScanDensity;
                float _ScanSpeed;
                half _Materialize;
                half _Glitch;
                float _HeightMin;
                float _HeightMax;
                float _HoloTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                // 글리치: 높이 띠마다 수평으로 순간 이동 (12 Hz 계단)
                float band = floor(ws.y * 7.0);
                float tick = floor(_HoloTime * 12.0);
                float r = hash11(band * 13.7 + tick);
                float shift = (r > 1.0 - _Glitch * 0.35) ? (hash11(band + tick * 3.1) - 0.5) * 0.18 * _Glitch : 0.0;
                ws += TransformObjectToWorldDir(float3(1, 0, 0)) * shift;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float h = saturate((i.positionWS.y - _HeightMin) / max(0.001, _HeightMax - _HeightMin));
                // 발끝부터 위로 맺힌다. 경계에 밝은 레드 띠.
                float edge = _Materialize * 1.08 - 0.04;
                clip(edge - h);
                half edgeBand = saturate(1.0 - (edge - h) / 0.04) * step(_Materialize, 0.999);

                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(abs(dot(n, viewDir))), _RimPower);

                float scan = frac(i.positionWS.y * _ScanDensity - _HoloTime * _ScanSpeed);
                half scanLine = smoothstep(0.0, 0.08, scan) * (1.0 - smoothstep(0.5, 0.58, scan));
                float sweep = frac(_HoloTime * 0.35);
                half sweepBand = saturate(1.0 - abs(h - sweep) / 0.03) * 0.8;

                half3 col = _Color.rgb * (0.35 + 0.45 * scanLine) + _RimColor.rgb * (rim * 1.4 + sweepBand + edgeBand * 2.0);
                half a = saturate(_Alpha * (0.35 + 0.65 * scanLine) + rim * 0.6 + edgeBand);
                return half4(col * _Intensity, a * saturate(_Intensity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
