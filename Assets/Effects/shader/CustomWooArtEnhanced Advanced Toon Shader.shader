Shader "Custom/WooArt/Enhanced Toon Shader"
{
    Properties
    {
        [Header(Base Properties)]
        _Color ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.0

        [Header(Toon Shading)]
        _LightTint ("Light Tint", Color) = (1,1,1,1)
        _ShadowTint ("Shadow Tint", Color) = (0.6,0.7,0.8,1)
        _ShadowThreshold ("Shadow Threshold", Range(0,1)) = 0.4
        _ShadowFeather ("Shadow Feather", Range(0.001, 0.5)) = 0.08
        _ShadowSteps ("Shadow Steps", Range(1, 5)) = 2

        [Header(Enhanced Specular)]
        _SpecularColor("Specular Color", Color) = (1,1,1,1)
        _Glossiness("Glossiness", Range(1, 128)) = 32
        _SpecularIntensity("Specular Intensity", Range(0, 2)) = 1.0
        _SpecularThreshold("Specular Threshold", Range(0, 1)) = 0.8
        _SpecularFeather("Specular Feather", Range(0.001, 0.3)) = 0.05

        [Header(Advanced Rim Light)]
        _RimColor("Rim Color", Color) = (0.8,1,1,1)
        _RimThreshold("Rim Threshold", Range(0,1)) = 0.6
        _RimFeather("Rim Feather", Range(0.001, 0.5)) = 0.15
        _RimIntensity("Rim Intensity", Range(0, 3)) = 1.5
        _RimPower("Rim Power", Range(1, 10)) = 3

        [Header(Fresnel Effect)]
        _FresnelColor("Fresnel Color", Color) = (0.5,0.8,1,1)
        _FresnelIntensity("Fresnel Intensity", Range(0, 2)) = 0.3
        _FresnelPower("Fresnel Power", Range(1, 8)) = 2

        [Header(Enhanced Outline)]
        _OutlineColor ("Outline Color", Color) = (0.1,0.1,0.1,1)
        _OutlineWidth ("Outline Width", Range(0,0.05)) = 0.005
        _OutlineDistanceFade ("Distance Fade", Range(0, 1)) = 0.5

        [Header(Color Grading)]
        _Saturation ("Saturation", Range(0, 2)) = 1.1
        _Contrast ("Contrast", Range(0.5, 2)) = 1.1
        _ColorTemperature ("Color Temperature", Range(-1, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        // ==========================================================
        //  Main Toon Shader Pass
        // ==========================================================
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Brightness;

            float4 _LightTint;
            float4 _ShadowTint;
            float _ShadowThreshold;
            float _ShadowFeather;
            float _ShadowSteps;

            float4 _SpecularColor;
            float _Glossiness;
            float _SpecularIntensity;
            float _SpecularThreshold;
            float _SpecularFeather;

            float4 _RimColor;
            float _RimThreshold;
            float _RimFeather;
            float _RimIntensity;
            float _RimPower;

            float4 _FresnelColor;
            float _FresnelIntensity;
            float _FresnelPower;

            float _Saturation;
            float _Contrast;
            float _ColorTemperature;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
                float3 lightDir : TEXCOORD4;
            };

            // 색상 보정 함수들
            float3 ApplySaturation(float3 color, float saturation)
            {
                float luminance = dot(color, float3(0.299, 0.587, 0.114));
                return lerp(luminance.xxx, color, saturation);
            }

            float3 ApplyContrast(float3 color, float contrast)
            {
                return ((color - 0.5) * contrast) + 0.5;
            }

            float3 ApplyColorTemperature(float3 color, float temperature)
            {
                float3 warm = color * float3(1.2, 1.0, 0.8);
                float3 cool = color * float3(0.8, 0.95, 1.2);
                return lerp(cool, warm, temperature * 0.5 + 0.5);
            }

            // 툰 셰이딩용 단계형 램프
            float SteppedToonRamp(float ndotl, float threshold, float feather, float steps)
            {
                float t = smoothstep(threshold - feather, threshold + feather, ndotl);
                return floor(t * steps) / max(1.0, steps);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - o.worldPos);
                
                // 라이트 방향 계산 (Directional Light 기준)
                if (_WorldSpaceLightPos0.w == 0.0)
                    o.lightDir = normalize(_WorldSpaceLightPos0.xyz); // Directional Light
                else
                    o.lightDir = normalize(_WorldSpaceLightPos0.xyz - o.worldPos); // Point/Spot Light
                    
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 기본 텍스처와 색상
                fixed4 tex = tex2D(_MainTex, i.uv) * _Color * _Brightness;
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);
                float3 lightDir = normalize(i.lightDir);

                // 1. 툰 셰이딩 (다단계)
                float ndotl = saturate(dot(normal, lightDir) * 0.5 + 0.5); // Half Lambert
                float toonStep = SteppedToonRamp(ndotl, _ShadowThreshold, _ShadowFeather, _ShadowSteps);
                float3 toonColor = lerp(_ShadowTint.rgb, _LightTint.rgb, toonStep);

                // 2. 툰 스타일 스페큘러
                float3 halfDir = normalize(lightDir + viewDir);
                float specAngle = max(0, dot(normal, halfDir));
                float specVal = pow(specAngle, _Glossiness);
                specVal = smoothstep(_SpecularThreshold - _SpecularFeather, _SpecularThreshold + _SpecularFeather, specVal);
                specVal *= _SpecularIntensity * toonStep; // 그림자 영역에서는 스페큘러 감소
                
                float3 specularColor = specVal * _SpecularColor.rgb;

                // 3. 향상된 림 라이트
                float rimDot = 1.0 - saturate(dot(normal, viewDir));
                float rimAmt = pow(rimDot, _RimPower);
                rimAmt = smoothstep(_RimThreshold - _RimFeather, _RimThreshold + _RimFeather, rimAmt);
                rimAmt *= _RimIntensity * (toonStep * 0.5 + 0.5); // 그림자에서도 약간의 림라이트
                float3 rimColor = rimAmt * _RimColor.rgb;

                // 4. 프레넬 효과
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), _FresnelPower);
                fresnel *= _FresnelIntensity;
                float3 fresnelColor = fresnel * _FresnelColor.rgb;

                // 5. 최종 색상 조합
                float3 finalCol = tex.rgb * toonColor;
                finalCol += specularColor;
                finalCol += rimColor;
                finalCol += fresnelColor;
                
                // 기본 라이트 색상 (흰색으로 fallback)
                float3 lightColor = float3(1, 1, 1);
                #ifdef USING_DIRECTIONAL_LIGHT
                    lightColor = _LightColor0.rgb;
                #endif
                
                finalCol *= lightColor;

                // 환경광 추가
                finalCol += tex.rgb * unity_AmbientSky.rgb * 0.3;

                // 색상 보정
                finalCol = ApplySaturation(finalCol, _Saturation);
                finalCol = ApplyContrast(finalCol, _Contrast);
                finalCol = ApplyColorTemperature(finalCol, _ColorTemperature);

                return fixed4(finalCol, tex.a);
            }
            ENDCG
        }
        
        // ==========================================================
        //  Enhanced Outline Pass
        // ==========================================================
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineDistanceFade;

            struct appdataOutline
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2fOutline
            {
                float4 pos : SV_POSITION;
                float fade : TEXCOORD0;
            };

            v2fOutline vertOutline(appdataOutline v)
            {
                v2fOutline o;

                // 월드 좌표로 변환
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = normalize(UnityObjectToWorldNormal(v.normal));

                // 거리에 따른 아웃라인 두께 조절
                float dist = distance(_WorldSpaceCameraPos.xyz, worldPos);
                float distanceFactor = 1.0 / (1.0 + dist * _OutlineDistanceFade);
                float thickness = _OutlineWidth * distanceFactor;

                // 노멀 방향으로 확장
                float3 extrudedPos = worldPos + worldNormal * thickness;

                o.pos = mul(UNITY_MATRIX_VP, float4(extrudedPos, 1.0));
                o.fade = distanceFactor;
                return o;
            }

            fixed4 fragOutline(v2fOutline i) : SV_Target
            {
                fixed4 col = _OutlineColor;
                col.a *= saturate(i.fade);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}