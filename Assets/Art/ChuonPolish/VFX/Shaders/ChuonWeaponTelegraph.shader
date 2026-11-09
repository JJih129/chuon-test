// 보스 무기 날 발광 스윕 (반복 10): 검 렌더러에 추가 재질로 한 번 더 그린다 (가산, HDR → 블룸).
// _AxisOS/_AxisRange 로 칼날 축(손잡이→칼끝)을 오브젝트 공간에서 정의하고, _Sweep 위치의 밝은 띠가 칼끝으로 달린다.
// 지나간 자리는 은은하게 남고(_Charge), 가장자리는 프레넬로 빛난다.
Shader "ChuOn/WeaponTelegraph"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 2.6, 0.6, 1)
        _Sweep ("Sweep (0 hilt .. 1 tip)", Range(-0.2, 1.2)) = 0
        _Charge ("Charge (afterglow)", Range(0, 1)) = 0
        _Glow ("Overall", Range(0, 2)) = 1
        _AxisOS ("Blade axis (object)", Vector) = (0, 0, 1, 0)
        _AxisRange ("Axis min/max (object)", Vector) = (-1, 1, 0, 0)
        _BandWidth ("Band width", Range(0.01, 0.5)) = 0.09
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 2.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WeaponTelegraph"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Sweep;
                half _Charge;
                half _Glow;
                float4 _AxisOS;
                float4 _AxisRange;
                half _BandWidth;
                half _FresnelPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float axis : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float d = dot(v.positionOS.xyz, normalize(_AxisOS.xyz));
                o.axis = (d - _AxisRange.x) / max(0.0001, _AxisRange.y - _AxisRange.x);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = i.axis;
                float band = exp(-pow((t - _Sweep) / max(_BandWidth, 0.001), 2.0));
                float behind = saturate((_Sweep - t) * 4.0) * step(t, _Sweep);
                float ndv = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float fresnel = pow(1.0 - ndv, _FresnelPower);
                float tipBias = smoothstep(0.05, 0.9, t);
                float v = band * (1.4 + fresnel) + (behind * 0.35 + _Charge * 0.5) * (0.35 + fresnel) * tipBias;
                return half4(_Color.rgb * v * _Glow, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
