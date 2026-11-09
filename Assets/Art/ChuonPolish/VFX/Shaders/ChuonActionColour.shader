// 추온 액션 컬러 (반복 11) — 전체 화면 후처리 (ChuonActionColourFeature 가 포스트 직전에 블릿)
// 1) 흑백 '흡수': 화면 외곽에서 중심(칼날/추온)으로 반경이 줄어들며 세계가 흑백으로 빠진다.
//    KeepColour 마스크(추온·Type-D·칼) 와 HDR 고휘도(검기·핵심 VFX) 는 색을 유지한다.
// 2) 포스트 이벤트: 색수차(패리), 비네트(패리 어둡게 / 저체력 붉게), 채도 딥(브레이크), 쿨 틴트(퍼펙트 회피).
// 모든 세기는 C# 쪽에서 접근성 '화면 번쩍임' 슬라이더로 이미 곱해져 들어온다.
Shader "Hidden/ChuOn/ActionColour"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "ActionColour"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_ChuonKeepMaskTex);

            float4 _AC_Sweep;      // x 흑백 세기, y 스윕 반경, z 스윕 경계 폭, w 스윕 링 밝기
            float4 _AC_Center;     // xy 중심 uv, z 화면 종횡비, w 마스크 사용(0/1)
            float4 _AC_Post;       // x 색수차, y 비네트 세기, z 틴트 세기, w 채도 딥
            float4 _AC_Keep;       // x 고휘도 유지 시작, y 고휘도 유지 끝, z 흑백 대비, w 흑백 밝기
            float4 _AC_VignetteColor;
            float4 _AC_Tint;
            float4 _AC_RingColor;

            half Luma(half3 c)
            {
                return dot(c, half3(0.2126h, 0.7152h, 0.0722h));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // --- 색수차 (화면 중심 기준 방사) ---
                half3 col;
                float ca = _AC_Post.x;
                if (ca > 0.00001)
                {
                    float2 dir = (uv - 0.5) * ca;
                    col.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir).r;
                    col.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                    col.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - dir).b;
                }
                else
                {
                    col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                }

                half lum = Luma(col);

                // --- 흑백 흡수 스윕 ---
                float2 d = uv - _AC_Center.xy;
                d.x *= _AC_Center.z;
                float r = length(d);
                float radius = _AC_Sweep.y;
                float soft = max(0.0001, _AC_Sweep.z);
                float drained = smoothstep(radius - soft * 0.5, radius + soft * 0.5, r);

                half keep = 0;
                if (_AC_Center.w > 0.5)
                    keep = SAMPLE_TEXTURE2D_X(_ChuonKeepMaskTex, sampler_PointClamp, uv).r;
                keep = max(keep, smoothstep(_AC_Keep.x, _AC_Keep.y, lum));

                float desat = saturate(_AC_Sweep.x * drained * (1.0 - keep));
                // 누아르 흑백: 약한 대비 + 아주 약한 청색 기운 (사이버펑크 톤)
                half g = lum * _AC_Keep.w;
                half gs = saturate(g);
                half gc = gs * gs * (3.0h - 2.0h * gs);
                g = g <= 1.0h ? lerp(g, gc, _AC_Keep.z) : g;
                half3 bw = g * half3(0.95h, 0.99h, 1.06h);
                col = lerp(col, bw, desat);

                // 스윕 전선의 얇은 빛 링 (흡수되는 경계)
                float front = (r - radius) / max(0.0001, soft * 0.12);
                float ring = exp(-front * front) * _AC_Sweep.w * step(0.001, _AC_Sweep.x) * (1.0 - keep);
                col += _AC_RingColor.rgb * ring;

                // --- 채도 딥 (브레이크 시작) ---
                col = lerp(col, Luma(col).xxx, saturate(_AC_Post.w));

                // --- 틴트 (퍼펙트 회피: 차가운 색) ---
                col = lerp(col, col * _AC_Tint.rgb + _AC_Tint.a * Luma(col) * half3(0.0h, 0.04h, 0.09h), saturate(_AC_Post.z));

                // --- 비네트 (패리 어둡게 / 저체력 붉게) ---
                float2 vd = (uv - 0.5) * float2(_AC_Center.z, 1.0) / max(1.0, _AC_Center.z) * 2.0;
                float v = smoothstep(0.45, 1.25, length(vd));
                float va = saturate(_AC_Post.y) * v;
                col = col * (1.0h - va * 0.85h) + _AC_VignetteColor.rgb * (va * _AC_VignetteColor.a);

                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
