// 추온 궁극기 '절단선' (위상단절검) — 대시 베기 / 광선(빔) 메시 파티클용
// 메시: Ult_CutCross (z 0..1 = 길이, 두 장의 직교 쿼드). UV: u = 폭(0..1), v = 길이(0 = 시작점, 1 = 끝).
// 렌즈형 테이퍼 + 흰 코어 + 츠카사 레드 가장자리. ScreenCut 직선 컷과 같은 언어 (곧은 선, 날카로운 끝).
// UltimateSlashBurstSpawner 가 transform 스케일 (두께, 두께, 길이) 과 _Intensity / _Alpha / _ScrollSpeed 를 넣는다.
Shader "ChuOn/UltimateCut"
{
    Properties
    {
        [HDR] _CoreColor ("Core (white-hot)", Color) = (3.4, 3.3, 3.2, 1)
        [HDR] _EdgeColor ("Edge (TSUKASA red)", Color) = (2.8, 0.18, 0.14, 1)
        _CoreWidth ("Core width (0..1 of half-width)", Range(0.02, 1)) = 0.32
        _TaperPower ("Taper power", Range(0.1, 4)) = 0.6
        _PeakAt ("Widest point along length", Range(0.05, 0.95)) = 0.38
        _EdgeSoftness ("Edge softness", Range(0.01, 1)) = 0.35
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 1
        _ScrollSpeed ("Glint travel", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "UltimateCut"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half _CoreWidth;
                half _TaperPower;
                half _PeakAt;
                half _EdgeSoftness;
                half _Intensity;
                half _Alpha;
                half _ScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;   // 파티클 메시 기본 스트림(Position, Normal, Color, UV) 과 레이아웃을 맞춘다
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half v = saturate(i.uv.y);
                // 시작점 → _PeakAt 까지 부풀고, 끝으로 바늘처럼 가늘어지는 비대칭 렌즈
                half t = v < _PeakAt ? (v / _PeakAt) : (1.0h - (v - _PeakAt) / (1.0h - _PeakAt));
                half width = pow(saturate(sin(t * 1.5707963h)), _TaperPower);
                half across = abs(i.uv.x * 2.0h - 1.0h) / max(width, 1e-3h);
                clip(1.0h - across);

                half body = 1.0h - smoothstep(1.0h - _EdgeSoftness, 1.0h, across);
                half core = 1.0h - smoothstep(_CoreWidth * 0.6h, _CoreWidth, across);
                // 바늘 끝은 코어만 남겨 '절단'의 날카로운 끝을 만든다
                core *= smoothstep(0.0h, 0.25h, width);

                // 선택적 진행 하이라이트 (기본 0 — 스포너는 2 를 넣는다: 칼끝 쪽이 살짝 밝아진다)
                half glint = _ScrollSpeed > 0.0h ? (0.75h + 0.25h * smoothstep(0.2h, 1.0h, v)) : 1.0h;

                half3 col = (_EdgeColor.rgb * body + _CoreColor.rgb * core) * glint;
                half a = saturate(i.color.a * _Alpha);
                return half4(col * i.color.rgb * a * _Intensity, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
