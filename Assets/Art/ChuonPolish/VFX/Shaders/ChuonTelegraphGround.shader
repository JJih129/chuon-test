// 보스 바닥 텔레그래프 데칼 (반복 10) - BossGroundTelegraph 가 바닥에 투영한 평면 메시에 그린다.
// _Shape 0 = 직선/사각(공격 히트박스 발자국), 1 = 원형(충격파/도약). _Fill 0→1 이 피격 순간에 가장자리에 닿는다(정직한 큐).
// 프리멀티플라이드 알파: 안쪽 채움은 바닥을 살짝 덮고, 테두리·파면은 HDR 로 블룸을 받는다.
Shader "ChuOn/TelegraphGround"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (3, 0.6, 0.1, 1)
        _Fill ("Fill (0..1 = time to impact)", Range(0, 1)) = 0
        _Shape ("Shape (0 rect, 1 circle)", Float) = 0
        _Aspect ("Rect length / width", Float) = 1
        _Urgency ("Urgency (0..1)", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Shadow ("Shadow mode", Range(0, 1)) = 0
        _EdgeSoftness ("Edge softness", Range(0.002, 0.2)) = 0.035
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TelegraphGround"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fill;
                half _Shape;
                half _Aspect;
                half _Urgency;
                half _Opacity;
                half _Shadow;
                half _EdgeSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // 0..1 띠: d 가 [a, b] 안이면 1, 소프트 경계
            float band(float d, float a, float b, float s)
            {
                return smoothstep(a - s, a + s, d) * (1.0 - smoothstep(b - s, b + s, d));
            }

            half4 Rect(float2 uv)
            {
                float s = _EdgeSoftness;
                float aspect = max(_Aspect, 0.05);
                // 가장자리까지 거리 (폭 단위로 통일: uv.y 1 = 길이 = aspect × 폭 이므로 aspect 를 곱한다).
                // 반복 16: 예전엔 aspect 로 나눠서 긴 레인(잡몹 돌진·터렛 조준, aspect 8~14)은 edgeDist 가 전부 테두리 폭 안에 들어가
                // 판 전체가 HDR 테두리로 칠해졌다 (3:24 '포화된 빨간 판'). 보스 사각(aspect ≈ 1~2)은 차이가 작아 안 보였다.
                float dx = 0.5 - abs(uv.x - 0.5);
                float dy = min(uv.y, 1.0 - uv.y) * aspect;
                float edgeDist = min(dx, dy);
                float inside = smoothstep(0.0, s, edgeDist);
                float rim = inside * (1.0 - smoothstep(s * 0.6, s * 2.2, edgeDist));

                // 채움: 보스 쪽(y=0)에서 먼 끝으로 차오름 + 밝은 파면
                float fillMask = 1.0 - smoothstep(_Fill - 0.01, _Fill + 0.01, uv.y);
                float front = exp(-pow((uv.y - _Fill) * aspect * 18.0, 2.0)) * step(0.001, _Fill);

                // 진행 방향으로 흐르는 쉐브론(가는 사선) — 위험 방향을 읽히게
                float chev = frac((uv.y * aspect * 2.2 - abs(uv.x - 0.5) * 1.4) - _Time.y * 1.6);
                float chevron = smoothstep(0.0, 0.08, chev) * (1.0 - smoothstep(0.12, 0.2, chev));

                // 모서리 브래킷
                float2 c = abs(uv - 0.5);
                float corner = step(0.38, c.x) * step(0.5 - 0.12 / aspect, c.y);

                // 채움은 LDR 로 옅게 (바닥이 비쳐 보이게), 테두리/파면/쉐브론만 HDR (블룸)
                half3 ldr = _Color.rgb / max(1.0, max(_Color.r, max(_Color.g, _Color.b)));
                float a = inside * (0.05 + 0.13 * fillMask);
                float glow = rim * (0.45 + 0.45 * _Urgency) + front * 0.9 * inside + corner * rim * 0.7 + chevron * inside * 0.16;
                return half4(ldr * a + _Color.rgb * glow, saturate(a + glow * 0.2));
            }

            half4 Circle(float2 uv)
            {
                float s = _EdgeSoftness;
                float2 p = (uv - 0.5) * 2.0;
                float r = length(p);
                float inside = 1.0 - smoothstep(1.0 - s, 1.0, r);
                float rim = band(r, 0.93, 1.0, s * 0.5);
                float fillMask = 1.0 - smoothstep(_Fill - 0.012, _Fill + 0.012, r);
                float front = exp(-pow((r - _Fill) * 22.0, 2.0)) * step(0.001, _Fill) * inside;

                // 테두리 눈금 (천천히 회전) + 안쪽 동심원 그리드
                float ang = atan2(p.y, p.x) / 6.28318 + 0.5;
                float ticks = step(0.5, frac(ang * 48.0 + _Time.y * 0.15)) * band(r, 0.86, 0.91, 0.006);
                float rings = (1.0 - smoothstep(0.0, 0.025, abs(frac(r * 4.0) - 0.5) - 0.47)) * inside * 0.35;

                half3 ldr = _Color.rgb / max(1.0, max(_Color.r, max(_Color.g, _Color.b)));
                float a = inside * (0.05 + 0.14 * fillMask) + rings * 0.06;
                float glow = rim * (0.5 + 0.4 * _Urgency) + front * 1.0 + ticks * 0.4;

                // 그림자 모드 (도약 내려찍기): 어두운 원판이 바닥을 덮는다
                float shadowA = _Shadow * inside * (0.35 + 0.35 * _Fill) * (1.0 - smoothstep(0.3, 1.0, r) * 0.6);
                half3 col = ldr * a * (1.0 - _Shadow * 0.7) + _Color.rgb * glow;
                return half4(col, saturate(a + glow * 0.2 + shadowA));
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = _Shape > 0.5 ? Circle(i.uv) : Rect(i.uv);
                return c * _Opacity;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
