// 추온 화면 갈라짐 / 유리 갈라짐 (반복 11)
// 캡처한 프레임을 Blender 보로노이 파편 메시(ScreenShatter_Shards.fbx) 위에 그대로 매핑해 덮는다.
//   uv0 = 화면 uv(원래 위치), uv1 = 파편 중심 uv, uv2 = (x: 가장자리 0..1, y: 파편 시드 0..1)
// 1) _ShatterCrack: 충격점에서 균열선이 퍼진다 (파편은 아직 제자리 → 화면이 깨진 유리처럼 보임)
// 2) _ShatterBurst: 파편이 회전·깊이·중력과 함께 바깥으로 튀며, 틈으로 다음 프레임(라이브 화면)이 드러난다.
// 정점 위치는 셰이더에서 직접 클립 공간으로 계산 (카메라/행렬과 무관, 드로우 1회).
Shader "Hidden/ChuOn/ScreenShatter"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ScreenShatter"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ShatterTex);
            SAMPLER(sampler_ShatterTex);

            float4 _ShatterImpact;   // xy 충격점 uv, z 화면 종횡비, w 클립 y 부호 (+1/-1)
            float4 _ShatterState;    // x 균열 진행 0..1, y 폭발 진행 0..1, z 전체 알파, w 색수차
            float4 _ShatterCrackColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float edge : TEXCOORD1;
                float crackMask : TEXCOORD2;
                float shade : TEXCOORD3;
                float flight : TEXCOORD4;
                float2 caDir : TEXCOORD5;
            };

            float3 RotateAxis(float3 p, float3 axis, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return p * c + cross(axis, p) * s + axis * dot(axis, p) * (1.0 - c);
            }

            float Hash(float n)
            {
                return frac(sin(n * 127.1 + 311.7) * 43758.5453);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float aspect = _ShatterImpact.z;
                float2 uv = input.uv0;
                float2 shardCenter = input.uv1;
                float seed = input.uv2.y;

                // 종횡비 보정 공간 (x 를 실제 화면 비율로 늘림)
                float2 p = (uv * 2.0 - 1.0) * float2(aspect, 1.0);
                float2 c = (shardCenter * 2.0 - 1.0) * float2(aspect, 1.0);
                float2 impact = (_ShatterImpact.xy * 2.0 - 1.0) * float2(aspect, 1.0);

                float2 away = c - impact;
                float dist = length(away);
                float2 dir = dist > 0.0001 ? away / dist : float2(Hash(seed) - 0.5, 0.5);

                // 충격점 가까운 파편이 먼저 튄다
                float burst = _ShatterState.y;
                float delay = saturate(dist / (2.2 * aspect)) * 0.32;
                float t = saturate((burst - delay) / max(0.0001, 1.0 - delay));
                float te = 1.0 - pow(1.0 - t, 2.2);   // ease-out

                float3 local = float3(p - c, 0.0);
                float3 axis = normalize(float3(Hash(seed * 3.1) - 0.5, Hash(seed * 5.7) - 0.5, (Hash(seed * 9.3) - 0.5) * 0.6) + 1e-4);
                float spin = (2.0 + 5.0 * Hash(seed * 7.9)) * (Hash(seed * 1.3) > 0.5 ? 1.0 : -1.0);
                float3 rotated = RotateAxis(local, axis, spin * te);

                float speed = 0.55 + 0.9 * Hash(seed * 2.3) + 0.6 * (1.0 - saturate(dist / 1.6));
                float3 world;
                world.xy = c + dir * te * speed * 1.6 + rotated.xy;
                world.y -= t * t * 0.9;                     // 중력
                world.z = rotated.z - te * (0.35 + 0.9 * Hash(seed * 4.1));   // 카메라 쪽(-z)으로

                // 원근: 카메라는 z = -D 에서 +z 를 본다
                const float D = 2.0;
                float persp = D / max(0.25, D + world.z);
                float2 proj = world.xy * persp;

                o.positionCS = float4(proj.x / aspect, proj.y * _ShatterImpact.w, 0.5, 1.0);
                o.uv = uv;
                o.edge = input.uv2.x;
                // 균열은 충격점에서 바깥으로 자란다
                float crackReach = _ShatterState.x * (2.4 * aspect);
                o.crackMask = saturate((crackReach - dist) * 4.0);
                // 회전한 파편 면의 밝기 변화 (유리 반사 느낌)
                float3 n = RotateAxis(float3(0, 0, -1), axis, spin * te);
                o.shade = 0.82 + 0.38 * saturate(dot(n, normalize(float3(-0.4, 0.6, -0.7))));
                o.flight = t;
                o.caDir = dir;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float flight = i.flight;
                float ca = _ShatterState.w * (0.25 + flight);
                float2 off = i.caDir * ca * float2(1.0 / _ShatterImpact.z, 1.0);
                half3 col;
                col.r = SAMPLE_TEXTURE2D(_ShatterTex, sampler_ShatterTex, i.uv + off).r;
                col.g = SAMPLE_TEXTURE2D(_ShatterTex, sampler_ShatterTex, i.uv).g;
                col.b = SAMPLE_TEXTURE2D(_ShatterTex, sampler_ShatterTex, i.uv - off).b;
                col *= lerp(1.0, i.shade, saturate(flight * 3.0));

                // 균열선: 화면 공간 두께가 일정하도록 fwidth 사용
                // (fan 삼각형의 edge 값은 외곽선에서 1 → 두께 ≈ 1 px 코어 + 3 px 희미한 번짐)
                float w = max(fwidth(i.edge), 1e-4);
                // 외곽선까지의 화면 거리(px). 비행 중엔 MSAA 없는 파편 실루엣이 계단지므로(4K 확인)
                // 선을 1px 안쪽으로 옮기고 가장 바깥 ~1.5px 를 알파로 페이드 (커버리지 AA).
                float edgePx = (1.0 - i.edge) / w;
                float inFlight = saturate(flight * 6.0);
                float linePx = max(edgePx - inFlight, 0.0);
                float crackLine = 1.0 - smoothstep(0.4, 1.1, linePx);
                float glow = 1.0 - smoothstep(0.0, 3.0, linePx);
                float edgeCoverage = lerp(1.0, smoothstep(0.0, 1.5, edgePx), inFlight);
                // 충격점 가까운 균열이 더 밝다
                float crack = i.crackMask * saturate(crackLine * (0.55 + 0.45 * i.crackMask) + glow * 0.18);
                // 비행 중엔 파편 가장자리가 빛을 받아 살짝 반짝임
                crack = max(crack, crackLine * 0.35 * saturate(flight * 4.0) * (1.0 - flight));
                col = lerp(col, _ShatterCrackColor.rgb, saturate(crack * _ShatterCrackColor.a));

                float alpha = _ShatterState.z * (1.0 - smoothstep(0.55, 1.0, flight)) * edgeCoverage;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
