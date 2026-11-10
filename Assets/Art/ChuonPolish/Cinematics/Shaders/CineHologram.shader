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
        // 반복 14C: 깊이 프리패스 — 겹친 부품(머리·턱·머리카락·어깨 등)의 안쪽 면이 가산으로 겹쳐 '비눗방울'처럼 보이지 않게,
        // 가장 바깥 면의 깊이만 먼저 쓰고 색 패스는 ZTest LEqual 로 그 면만 그린다. (태그 없음 = SRPDefaultUnlit, 색 패스보다 먼저)
        Pass
        {
            Name "HologramDepthPrime"
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
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

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float y : TEXCOORD0; };

            float hash11d(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            V vertDepth(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float band = floor(ws.y * 7.0);
                float tick = floor(_HoloTime * 12.0);
                float r = hash11d(band * 13.7 + tick);
                float shift = (r > 1.0 - _Glitch * 0.35) ? (hash11d(band + tick * 3.1) - 0.5) * 0.18 * _Glitch : 0.0;
                ws += TransformObjectToWorldDir(float3(1, 0, 0)) * shift;
                o.positionCS = TransformWorldToHClip(ws);
                o.y = ws.y;
                return o;
            }

            half4 fragDepth(V i) : SV_Target
            {
                float h = saturate((i.y - _HeightMin) / max(0.001, _HeightMax - _HeightMin));
                clip(_Materialize * 1.08 - 0.04 - h);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
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
                half facing = saturate(abs(dot(n, viewDir)));
                half rim = pow(1.0 - facing, _RimPower);

                // 반복 14C: 사람 실루엣이 읽히게 — 몸은 고르게 채우고(형태 음영 약간), 스캔라인은 가늘고 은은하게(밝은 선 ~12 %),
                // 화면에서 선 간격이 1~2 px 로 좁아지면(fwidth) 평균값으로 페이드해 굵은 줄무늬 앨리어싱을 없앤다.
                float sy = i.positionWS.y * _ScanDensity - _HoloTime * _ScanSpeed;
                float scan = frac(sy);
                half thin = 1.0 - smoothstep(0.0, 0.12, scan);
                half lineAA = saturate(1.6 - fwidth(sy) * 2.5);
                half scanLine = lerp(0.12, thin, lineAA);
                // 위로 훑는 세로 스캔 띠 + 미세 깜빡임 (결정적 해시, 20 Hz)
                float sweep = frac(_HoloTime * 0.32);
                half sweepBand = saturate(1.0 - abs(h - sweep) / 0.025);
                half flicker = 0.92 + 0.08 * hash11(floor(_HoloTime * 20.0)) - 0.25 * step(0.985, hash11(floor(_HoloTime * 9.0) + 7.0));
                // 발밑 프로젝터 빛 (바닥 8 %)
                half baseGlow = saturate(1.0 - h / 0.08);

                half body = 0.42 + 0.22 * facing;
                half3 col = _Color.rgb * (body + 0.28 * scanLine + 0.55 * sweepBand)
                          + _Color.rgb * rim * 0.9
                          + _RimColor.rgb * (rim * rim * 1.3 + edgeBand * 2.0 + baseGlow * 0.9 + sweepBand * 0.25);
                half a = saturate(_Alpha * (0.62 + 0.25 * scanLine) + rim * 0.55 + edgeBand + baseGlow * 0.35 + sweepBand * 0.2);
                half k = _Intensity * flicker;
                return half4(col * k, a * saturate(k));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
