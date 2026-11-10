// 목표 표식 (반복 16, 플레이어 리뷰 #1) — ObjectiveBeaconVisual 이 그리는 홀로그램 HUD 표식.
// 예전 표식(불투명 Unlit 원판 + 기둥 + 뚜껑)은 거대한 노란 원판으로 읽혀 츠카사 HUD 톤과 맞지 않았다.
// _Mode 0 = 바닥 링(얇은 외곽 링 + 회전 눈금 + 퍼지는 핑 + 아주 옅은 안쪽), 1 = 가는 빛기둥(코어 + 헤일로 + 위로 흐르는 대시), 2 = 아래를 가리키는 쉐브론.
// 프리멀티플라이드 알파, 코어만 약한 HDR(블룸 임계 1.1 근처) — 앰버 = 상호작용/경로, 네온 금지.
Shader "Chuon/ObjectiveBeacon"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.6, 0.99, 0.26, 1)
        _Mode ("Mode (0 ring, 1 beam, 2 chevron)", Float) = 0
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ObjectiveBeacon"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Mode;
                half _Fade;
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

            float band(float d, float a, float b, float s)
            {
                return smoothstep(a - s, a + s, d) * (1.0 - smoothstep(b - s, b + s, d));
            }

            half4 Ring(float2 uv, half3 hdr, half3 ldr)
            {
                float2 p = (uv - 0.5) * 2.0;
                float r = length(p);
                float t = _Time.y;
                float outer = band(r, 0.95, 0.985, 0.008);
                // 회전 눈금: 4 개의 호 (각 55°), 천천히 회전
                float ang = atan2(p.y, p.x) / 6.28318 + 0.5;
                float arcs = step(0.35, frac(ang * 4.0 + t * 0.05)) * band(r, 0.82, 0.85, 0.006);
                // 짧은 눈금 24 개
                float ticks = step(0.86, frac(ang * 24.0)) * band(r, 0.885, 0.93, 0.006);
                // 퍼지는 핑 (1.8 s 주기)
                float k = frac(t / 1.8);
                float ping = band(r, 0.25 + 0.72 * k - 0.012, 0.25 + 0.72 * k + 0.012, 0.01) * (1.0 - k) * (1.0 - k) * 0.55;
                float fill = (1.0 - smoothstep(0.0, 0.95, r)) * 0.05;
                float glow = outer * 0.9 + arcs * 0.5 + ticks * 0.35 + ping;
                half3 col = hdr * glow + ldr * fill;
                return half4(col, saturate(glow * 0.45 + fill));
            }

            half4 Beam(float2 uv, half3 hdr, half3 ldr)
            {
                float x = uv.x - 0.5;
                float core = exp(-x * x * 900.0);
                float halo = exp(-x * x * 60.0) * 0.18;
                float vfade = smoothstep(0.0, 0.06, uv.y) * (1.0 - smoothstep(0.45, 1.0, uv.y));
                float dash = step(0.82, frac(uv.y * 7.0 - _Time.y * 0.7)) * core * 0.6;
                float glow = (core * 0.55 + halo + dash) * vfade;
                return half4(hdr * glow, saturate(glow * 0.4));
            }

            half4 Chevron(float2 uv, half3 hdr, half3 ldr)
            {
                // 아래를 가리키는 V (두께 일정한 선 두 개)
                float2 p = uv - float2(0.5, 0.3);
                float d = abs(p.y - abs(p.x) * 0.9) * 0.74;
                float limit = 1.0 - smoothstep(0.36, 0.4, abs(p.x));
                float stroke = (1.0 - smoothstep(0.045, 0.065, d)) * limit;
                return half4(hdr * stroke, saturate(stroke * 0.85));
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 hdr = _Color.rgb;
                half3 ldr = hdr / max(1.0, max(hdr.r, max(hdr.g, hdr.b)));
                half4 c = _Mode < 0.5 ? Ring(i.uv, hdr, ldr) : (_Mode < 1.5 ? Beam(i.uv, hdr, ldr) : Chevron(i.uv, hdr, ldr));
                return c * _Fade;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
