// 추온 KeepColour 마스크 (반복 11)
// ChuonActionColourFeature 가 렌더링 레이어 'KeepColour' 비트가 켜진 불투명 렌더러를 이 머티리얼로 덮어 그려
// R8 마스크를 만든다. 장면 깊이보다 뒤에 있는(가려진) 조각은 버려서, 기둥 뒤의 추온이 흑백 세계를 뚫고 보이지 않게 한다.
Shader "Hidden/ChuOn/KeepColourMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "KeepColourMask"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 suv = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneRaw = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float fragEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // 장면보다 6 cm 이상 뒤면 가려진 것 (스키닝 오차 여유)
                clip(sceneEye + 0.06 - fragEye);
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
