// 추온 궁극기 파티클 (위상단절검) — UltimateTemp_* 임시 VFX 대체용
// 텍스처 알파를 '세기'로 보고 화이트 코어 ↔ 츠카사 레드 가장자리 램프로 칠한다 (ChuonBladeTrail / ScreenCut 팔레트와 동일 계열).
// 연기·파편은 Core/Edge 를 그래파이트·세라믹 색으로 두고 프리멀티플라이드 알파 블렌드(_SrcBlend=One, _DstBlend=OneMinusSrcAlpha)로 쓴다.
// _Intensity / _Alpha 는 UltimateSlashBurstSpawner 의 MaterialPropertyBlock 과 같은 이름.
Shader "ChuOn/UltimateParticle"
{
    Properties
    {
        _MainTex ("Mask (alpha = intensity)", 2D) = "white" {}
        [HDR] _CoreColor ("Core (white-hot)", Color) = (3.0, 2.95, 2.9, 1)
        [HDR] _EdgeColor ("Edge (TSUKASA red)", Color) = (2.4, 0.16, 0.12, 1)
        _CoreThreshold ("Core threshold", Range(0, 1)) = 0.55
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 1
        _ScrollSpeed ("Scroll (unused, spawner compat)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "UltimateParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _CoreColor;
                half4 _EdgeColor;
                half _CoreThreshold;
                half _Intensity;
                half _Alpha;
                half _ScrollSpeed;
                half _SrcBlend;
                half _DstBlend;
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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                half a = saturate(m * i.color.a * _Alpha);
                half coreMix = smoothstep(_CoreThreshold, 1.0h, m);
                half3 ramp = lerp(_EdgeColor.rgb, _CoreColor.rgb, coreMix) * i.color.rgb * _Intensity;
                // 가산(One One) 이든 프리멀티(One OneMinusSrcAlpha) 든 같은 식: rgb 는 알파로 미리 곱한다.
                return half4(ramp * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
