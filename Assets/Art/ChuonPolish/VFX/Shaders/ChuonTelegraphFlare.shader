// 보스 큐 타이밍 핑 (반복 10): 정직한 큐 순간의 짧고 선명한 섬광. 카메라를 보는 쿼드에 그린다.
// 4갈래 별빛 + 코어 + 퍼지는 링. _Progress 0→1 동안 번쩍였다 사라진다 (가산, HDR).
Shader "ChuOn/TelegraphFlare"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 2.5, 0.6, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _Rotation ("Rotation (rad)", Float) = 0
        _StarSharpness ("Star sharpness", Range(4, 120)) = 40
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+40" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TelegraphFlare"
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
                half4 _Color;
                half _Progress;
                float _Rotation;
                half _StarSharpness;
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

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float cs = cos(_Rotation), sn = sin(_Rotation);
                p = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
                float r = length(p);

                float t = saturate(_Progress);
                // 처음 15% 에 최고점, 이후 빠르게 감쇠
                float env = t < 0.15 ? t / 0.15 : pow(1.0 - (t - 0.15) / 0.85, 2.2);

                float starLen = lerp(0.35, 1.0, saturate(t * 5.0));
                float star = exp(-abs(p.x) * _StarSharpness) * saturate(1.0 - abs(p.y) / starLen)
                           + exp(-abs(p.y) * _StarSharpness) * saturate(1.0 - abs(p.x) / starLen);
                // 대각 보조 광선 (짧게)
                float2 q = float2(p.x + p.y, p.x - p.y) * 0.7071;
                float diag = (exp(-abs(q.x) * _StarSharpness * 1.6) * saturate(1.0 - abs(q.y) / (starLen * 0.45))
                            + exp(-abs(q.y) * _StarSharpness * 1.6) * saturate(1.0 - abs(q.x) / (starLen * 0.45))) * 0.4;
                float core = exp(-r * r * 60.0) * 2.2 + exp(-r * 7.0) * 0.45;
                float ringR = lerp(0.12, 0.95, t);
                float ring = exp(-pow((r - ringR) * 26.0, 2.0)) * (1.0 - t) * 0.8;

                float edgeFade = 1.0 - smoothstep(0.85, 1.0, r);
                float v = (star + diag + core + ring) * env * edgeFade;
                half3 white = half3(1, 1, 1) * max(_Color.r, max(_Color.g, _Color.b)) * 0.6;
                half3 col = lerp(_Color.rgb, white, saturate(core * 0.5)) * v;
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
