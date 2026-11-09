// 추온 검기 리본 (반복 10)
// SwordTrailMeshRenderer 가 매 프레임 칼날 기부(TrailBase)/끝(TrailTip)을 샘플링해 만든 리본을 그린다.
// UV: x = 나이(0 = 현재 칼날, 1 = 가장 오래된 꼬리), y = 0 손잡이 쪽 ~ 1 칼끝.
// 칼끝 쪽이 하얗게 달아오르고(HDR → 블룸), 손잡이 쪽과 꼬리 쪽은 붉은 가장자리로 사라진다 (츠카사 팔레트).
Shader "ChuOn/BladeTrail"
{
    Properties
    {
        [HDR] _CoreColor ("Core (white-hot)", Color) = (3.2, 2.9, 2.7, 1)
        [HDR] _EdgeColor ("Edge (red)", Color) = (1.4, 0.1, 0.05, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _HiltFade ("Hilt fade (0..1 of span)", Range(0.01, 1)) = 0.55
        _TipSoftness ("Tip soft edge", Range(0.001, 0.3)) = 0.06
        _AgePower ("Age falloff power", Range(0.3, 4)) = 1.35
        _CoreWidth ("Core width (age)", Range(0.02, 1)) = 0.45
        _StreakAmount ("Motion streaks", Range(0, 1)) = 0.35
        _StreakSeed ("Streak seed", Float) = 0
        [Toggle] _SwapUV ("Swap UV (u = span, v = age)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BladeTrail"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half _Intensity;
                half _HiltFade;
                half _TipSoftness;
                half _AgePower;
                half _CoreWidth;
                half _StreakAmount;
                float _StreakSeed;
                half _SwapUV;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            // 칼날 방향(y)으로만 변하는 값 노이즈 → 휘두른 방향을 따라 흐르는 가는 결
            float streakNoise(float y)
            {
                float x = y * 38.0 + _StreakSeed * 17.0;
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(hash11(i), hash11(i + 1.0), f);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = lerp(i.uv, i.uv.yx, _SwapUV);
                float age = saturate(uv.x);
                float span = saturate(uv.y);

                // 손잡이 쪽 → 투명, 칼끝 가장자리는 살짝 부드럽게
                float hilt = smoothstep(0.0, _HiltFade, span);
                float tip = 1.0 - smoothstep(1.0 - _TipSoftness, 1.0, span);
                // 꼬리(오래된 샘플)로 갈수록 사라짐
                float ageFade = pow(saturate(1.0 - age), _AgePower);

                // 백열 코어: 현재 칼날(나이 0) 쪽이 가장 하얗고, 칼끝 쪽으로 넓다. 붉은색은 꼬리/가장자리에만 얇게.
                float lead = saturate(1.0 - age / max(_CoreWidth, 0.001));
                float tipBand = smoothstep(0.12, 0.85, span);
                float core = saturate(lead * (0.55 + 0.45 * tipBand) * 1.35);
                float bladeLine = exp(-age * 22.0) * smoothstep(0.3, 0.95, span);   // 칼날 바로 뒤의 가는 흰 선
                float redEdge = (1.0 - smoothstep(0.0, 0.35, span)) * 0.6 + smoothstep(0.55, 1.0, age);

                float streak = lerp(1.0, 0.55 + 0.9 * streakNoise(span), _StreakAmount);

                float alpha = hilt * tip * ageFade * i.color.a;
                half3 white = _CoreColor.rgb;
                half3 red = _EdgeColor.rgb;
                half3 col = lerp(white * 0.55, red, saturate(redEdge)) ;
                col = lerp(col, white, core) + white * bladeLine * 0.8;
                col *= alpha * streak * _Intensity * i.color.rgb;
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
