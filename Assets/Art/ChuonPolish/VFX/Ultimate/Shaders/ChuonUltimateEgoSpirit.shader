// 추온 궁극기 '위상단절검' — EGO 칼날 정령 실루엣 (Ult_EgoSpirit 메시)
// 세라믹 화이트 반투명 몸 + 츠카사 레드 프레넬 가장자리. 아래(발)부터 위로 드러나는 _Reveal 전선은 붉은 선.
// 평면 셰이딩 메시라 면마다 프레넬이 끊겨 도자기 파편 같은 각진 윤곽이 된다. 시간은 스크립트가 _Phase 로 넣는다 (시간 정지 중에도 흐름).
Shader "ChuOn/UltimateEgoSpirit"
{
    Properties
    {
        _BodyColor ("Body (ceramic white)", Color) = (0.93, 0.91, 0.88, 0.22)
        [HDR] _EdgeColor ("Edge (TSUKASA red)", Color) = (3.0, 0.22, 0.16, 1)
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 2.4
        _HeightMax ("Object-space height", Float) = 1.08
        _Reveal ("Reveal (0 = hidden, 1 = full)", Range(0, 1.2)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Phase ("Phase (unscaled seconds)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "EgoSpirit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BodyColor;
                half4 _EdgeColor;
                half _FresnelPower;
                float _HeightMax;
                half _Reveal;
                half _Alpha;
                half _Intensity;
                float _Phase;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float height : TEXCOORD2;
                float worldY : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                o.height = saturate(v.positionOS.y / max(_HeightMax, 1e-3));
                o.worldY = posWS.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 vdir = normalize(i.viewWS);
                half fres = pow(1.0h - saturate(abs(dot(n, vdir))), _FresnelPower);

                // 아래에서 위로 드러남: 전선 위는 숨김, 전선 바로 아래는 붉은 띠
                half h = (half)i.height;
                half front = _Reveal * 1.1h - h;
                half shown = smoothstep(0.0h, 0.05h, front);
                half frontLine = (1.0h - smoothstep(0.0h, 0.06h, abs(front - 0.025h))) * step(h, 0.98h);

                // 발끝은 안개처럼 녹는다
                half feet = smoothstep(0.02h, 0.3h, h);

                // 아주 느린 세로 맥동 (스크립트 시간)
                half scan = 0.88h + 0.12h * sin(i.worldY * 9.0 - _Phase * 3.0);

                half bodyA = _BodyColor.a * (0.55h + 0.45h * (1.0h - fres)) * scan;
                half edge = fres * (0.75h + 0.25h * scan);
                half3 col = _BodyColor.rgb * bodyA + _EdgeColor.rgb * edge + _EdgeColor.rgb * frontLine * 1.4h;
                half mask = shown * feet * _Alpha;
                half a = saturate(bodyA + edge * 0.35h) * mask;
                return half4(col * mask * _Intensity, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
