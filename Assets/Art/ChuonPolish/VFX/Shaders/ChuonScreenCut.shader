// 추온 화면 베기 (반복 14S) — 화면 자체가 검에 베인다. 보로노이 유리 파편(ScreenShatter)을 대체.
// ChuonActionColourFeature 가 포스트 처리 뒤 캡처한 정지 프레임(_BlitTexture) 위에 전체 화면 1회로 그린다.
//  1) 선 그리기 : 직선 베기 2~3 개가 차례로 화면을 가로지른다 (흰 코어 + 얇은 츠카사 레드 발광, 양 끝 테이퍼,
//                 머리 글린트, 짧은 줄무늬/스피드 라인). 두께는 1080p 기준 단위라 4K 에서도 같은 비율·AA.
//  2) 분리      : 화면이 바로 그 직선들로 나뉜 큰 다각형 조각이 된다. 조각은 선의 부호 마스크(최대 8)로
//                 해석적으로 정의 → 목적지 픽셀마다 각 조각의 역변환을 시험해 덮는 조각을 찾는다 (노이즈·메시 없음).
//                 조각은 불투명한 채 선 방향으로 미끄러지며 살짝 벌어지고 (잔상 없음), 틈은 흰 빛(2 프레임) → 그래파이트
//                 단색(베기 선 근처는 어두운 레드). 분리 순간 정지 프레임 2 프레임 반전. 끝나면 라이브로 하드 컷.
//  3) 접근성(calm): 이동·번쩍임 없이 베인 선에서 바깥으로 라이브 화면이 번져 나오는 크로스페이드.
// 출력은 프리멀티플라이드 알파 (Blend One OneMinusSrcAlpha).
Shader "Hidden/ChuOn/ScreenCut"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "ScreenCut"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define CUT_MAX 3

            float4 _CutParams;            // x 종횡비, y 1 픽셀(종횡비 공간), z calm 0..1, w 선 개수
            float4 _CutLine[CUT_MAX];     // xy 선 위의 점, zw 그리는 방향(단위 벡터)        — 종횡비 공간 (x: ±aspect, y: ±1)
            float4 _CutSpan[CUT_MAX];     // x 화면 진입 파라미터, y 이탈 파라미터, z 그리기 진행 0..1, w 번쩍임 0..1
            float4 _CutMove[CUT_MAX];     // x 선 방향 미끄러짐, y 법선 방향 벌어짐, z 회전(rad) — 이미 진행도가 곱해짐
            float4 _CutState;             // x 분리 진행 0..1, y 전체 알파 (calm 크로스페이드 외엔 1), z 틈 흰 빛 0..1, w 정지 프레임 어둡게
            float4 _CutFx;                // x 선 알파, y 스피드 라인, z calm 크로스페이드 전선, w 줄무늬
            float4 _CutFx2;               // x 조각 절단면 발광, y 반전 플래시(1~2 프레임), z calm 경계 폭, w 정지 프레임 채도 빼기
            float4 _CutCoreColor;
            float4 _CutEdgeColor;
            float4 _CutGapColor;          // 틈 바탕 (그래파이트) — 선 근처는 어두운 레드 쪽으로

            static const float U1080 = 2.0 / 1080.0;   // 1080p 의 1 픽셀 (종횡비 공간)

            float2 Rot(float2 v, float a)
            {
                float s, c;
                sincos(a, s, c);
                return float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            float Hash(float n)
            {
                return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
            }

            half3 SampleCapture(float2 p, float aspect)
            {
                float2 uv = p / float2(aspect, 1.0) * 0.5 + 0.5;
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
            }

            // 정지 프레임 그레이드: 어둡게 + 채도 빼기 (선이 빛나도록), 분리 순간 1~2 프레임 반전
            half3 Grade(half3 c)
            {
                half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                c = lerp(c, l.xxx, saturate(_CutFx2.w));
                c *= 1.0 - _CutState.w;
                // 반전은 감마 근사 공간에서 (선형 그대로 뒤집으면 어두운 화면이 전부 흰색으로 날아간다)
                half3 g = sqrt(saturate(c));
                half3 inv = (1.0 - g) * (1.0 - g);
                return lerp(c, inv, saturate(_CutFx2.y));
            }

            // 반폭 halfWidth 띠의 픽셀 커버리지. 1 픽셀보다 얇으면 세기로 환산해 계단·깜빡임이 없다.
            float Band(float d, float halfWidth, float px)
            {
                float w = max(halfWidth, 0.0);
                return saturate((w + 0.5 * px - abs(d)) / px) * saturate(2.0 * w / px);
            }

            void Over(inout float3 rgb, inout float a, float3 col, float alpha)
            {
                alpha = saturate(alpha);
                rgb = rgb * (1.0 - alpha) + col * alpha;
                a = a * (1.0 - alpha) + alpha;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float aspect = _CutParams.x;
                float px = max(_CutParams.y, 1e-5);
                float calm = _CutParams.z;
                int count = (int)(_CutParams.w + 0.5);
                float2 q = (input.texcoord * 2.0 - 1.0) * float2(aspect, 1.0);
                float split = _CutState.x;

                // 목적지 기준 가장 가까운 베기 선까지 거리 (틈 빛/크로스페이드 범위)
                float lineDist = 1e5;
                [unroll] for (int j = 0; j < CUT_MAX; j++)
                {
                    if (j < count)
                    {
                        float2 Nj = float2(-_CutLine[j].w, _CutLine[j].z);
                        lineDist = min(lineDist, abs(dot(q - _CutLine[j].xy, Nj)));
                    }
                }

                // ---------------------------------------------------------- 1) 조각
                float3 acc = 0.0;
                float cov = 0.0;
                float cutCov = 1.0;   // 화면 외곽은 무시한 커버리지 — 베기 틈(<1)과 외곽에 드러난 틈을 구분
                if (split <= 0.0 || count == 0)
                {
                    acc = Grade(SampleCapture(q, aspect));
                    cov = 1.0;
                }
                else
                {
                    int masks = 1 << count;
                    float rimAmt = _CutFx2.x;
                    cutCov = 0.0;
                    [unroll] for (int m = 0; m < 8; m++)
                    {
                        if (m < masks)
                        {
                            float2 disp = 0.0;
                            float rot = 0.0;
                            [unroll] for (int i = 0; i < CUT_MAX; i++)
                            {
                                if (i < count)
                                {
                                    float s = ((m >> i) & 1) ? 1.0 : -1.0;
                                    float2 D = _CutLine[i].zw;
                                    float2 N = float2(-D.y, D.x);
                                    disp += s * (_CutMove[i].x * D + _CutMove[i].y * N);
                                    rot += s * _CutMove[i].z;
                                }
                            }

                            // 이 조각의 역변환: 목적지 → 원래 화면 위치
                            float2 p = Rot(q - disp, -rot);
                            float inside = 1e5;
                            [unroll] for (int k = 0; k < CUT_MAX; k++)
                            {
                                if (k < count)
                                {
                                    float s = ((m >> k) & 1) ? 1.0 : -1.0;
                                    float2 N = float2(-_CutLine[k].w, _CutLine[k].z);
                                    inside = min(inside, s * dot(p - _CutLine[k].xy, N));
                                }
                            }
                            float cutDist = inside;   // 이 조각의 절단면까지 거리
                            cutCov += saturate(cutDist / px + 0.5);
                            float2 b = float2(aspect, 1.0) - abs(p);
                            inside = min(inside, min(b.x, b.y));

                            float c = saturate(inside / px + 0.5);
                            if (c > 0.0)
                            {
                                float3 sc = Grade(SampleCapture(p, aspect));
                                // 절단면: 아주 얇은 흰 날 + 그 안쪽 레드 발광 (화면 외곽선엔 없음)
                                float e = max(cutDist, 0.0);
                                float redEdge = exp(-e / (4.5 * U1080)) * rimAmt;
                                float whiteEdge = Band(e, 1.1 * U1080, px) * rimAmt;
                                sc = lerp(sc, _CutEdgeColor.rgb, saturate(redEdge * _CutEdgeColor.a));
                                sc = lerp(sc, _CutCoreColor.rgb, saturate(whiteEdge));
                                acc += sc * c;
                                cov += c;
                            }
                        }
                    }
                    acc /= max(cov, 1.0);
                    cov = min(cov, 1.0);
                }

                float pieceAlpha = _CutState.y;
                if (calm > 0.0)
                {
                    // 접근성: 베인 선에서 바깥으로 라이브 화면이 번져 나온다 (이동·번쩍임 없음)
                    float soft = max(_CutFx2.z, 1e-3);
                    float keep = saturate((lineDist - _CutFx.z) / soft);
                    pieceAlpha *= lerp(1.0, keep, calm);
                }

                float3 rgb = acc * cov * pieceAlpha;
                float a = cov * pieceAlpha;

                // 틈: 불투명 단색 (조각은 불투명 → 라이브 화면과 겹친 잔상 없음).
                // 분리 순간 흰 빛 → 그래파이트, 베기 선 근처는 어두운 레드로 살짝 물든다.
                float gap = (1.0 - cov) * step(0.0001, split);
                float nearCut = saturate(1.0 - lineDist / 0.25) * (cutCov < 0.999 ? 1.0 : 0.0);   // 외곽 틈은 그래파이트만
                float3 gapCol = lerp(_CutGapColor.rgb, _CutEdgeColor.rgb * 0.2, nearCut * 0.45);
                gapCol = lerp(gapCol, _CutCoreColor.rgb, saturate(_CutState.z));
                rgb += gapCol * gap * _CutState.y;
                a += gap * _CutState.y;

                // ---------------------------------------------------------- 2) 베기 선
                float lineAlpha = _CutFx.x;
                if (lineAlpha > 0.001)
                {
                    float3 lRgb = 0.0;
                    float lA = 0.0;
                    [unroll] for (int i = 0; i < CUT_MAX; i++)
                    {
                        if (i < count && _CutSpan[i].z > 0.0)
                        {
                            float prog = _CutSpan[i].z;
                            float flash = _CutSpan[i].w;
                            float2 D = _CutLine[i].zw;
                            float2 N = float2(-D.y, D.x);
                            float2 rel = q - _CutLine[i].xy;
                            float along = dot(rel, D);
                            float across = dot(rel, N);
                            float tA = _CutSpan[i].x;
                            float tB = _CutSpan[i].y;
                            float len = max(tB - tA, 1e-3);
                            float head = lerp(tA, tB, prog);
                            float behind = head - along;           // > 0: 이미 그려진 쪽
                            float u = (along - tA) / len;

                            float vis = saturate(behind / px + 0.5) * saturate((along - tA) / px + 0.5);
                            if (vis > 0.0)
                            {
                                // 애니풍 테이퍼: 시작점과 머리 쪽이 가늘어진다
                                float taper = smoothstep(0.0, 0.16, u) * smoothstep(0.0, 0.07, behind / len);
                                float drawing = 1.0 - step(1.0, prog);
                                float glint = drawing * exp(-max(behind, 0.0) / 0.16);

                                float coreHalf = (1.0 + 2.4 * flash + 2.2 * glint) * U1080 * taper;
                                float edgeSigma = (1.8 + 5.5 * flash) * U1080 * lerp(0.35, 1.0, taper);

                                // 줄무늬: 베기 뒤로 넓고 흐린 빛 띠 (선이 그려지는 순간만)
                                float streak = exp(-(across * across) / (0.04 * 0.04)) * _CutFx.w * flash * flash * taper;
                                Over(lRgb, lA, _CutCoreColor.rgb, streak * 0.13 * vis);

                                float edge = exp(-abs(across) / edgeSigma) * lerp(0.4, 1.0, taper);
                                Over(lRgb, lA, _CutEdgeColor.rgb, edge * _CutEdgeColor.a * vis);

                                float core = Band(across, coreHalf, px);
                                Over(lRgb, lA, _CutCoreColor.rgb, core * vis);
                            }

                            // 스피드 라인: 베기와 평행한 짧고 가는 선 몇 개 (선이 지나간 뒤 아주 짧게)
                            float speed = _CutFx.y * flash;
                            if (speed > 0.001)
                            {
                                [unroll] for (int k = 0; k < 4; k++)
                                {
                                    float h0 = Hash(i * 17.0 + k * 3.0 + 1.0);
                                    float h1 = Hash(i * 31.0 + k * 7.0 + 2.0);
                                    float h2 = Hash(i * 53.0 + k * 11.0 + 3.0);
                                    float side = (k & 1) ? 1.0 : -1.0;
                                    float off = side * (0.05 + 0.26 * h0);
                                    float a0 = lerp(tA, tB, 0.08 + 0.5 * h1);
                                    float sl = len * (0.18 + 0.22 * h2);
                                    float lu = (along - a0) / sl;
                                    float revealed = saturate((head - a0) / (0.6 * sl));
                                    float segment = smoothstep(0.0, 0.25, lu) * smoothstep(1.0, 0.6, lu) * step(0.0, lu) * step(lu, 1.0);
                                    float w = Band(across - off, 0.55 * U1080, px);
                                    Over(lRgb, lA, _CutCoreColor.rgb, w * segment * revealed * speed * 0.55);
                                }
                            }
                        }
                    }
                    lA *= lineAlpha;
                    lRgb *= lineAlpha;
                    rgb = rgb * (1.0 - lA) + lRgb;
                    a = a * (1.0 - lA) + lA;
                }

                return half4(rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
