// AuroraBorealis_URP.shader
// URP 移植版 — 來源：Assets/AuroraBorealisPack/Shaders/AuroraBorealis.shader
// 原檔為 Shader Forge 生成的 Built-in RP shader。
//
// 移植重點（完整對照見 _Claude/notes/aurora-urp-port.md）：
//   * 原檔雖標記 LightMode=ForwardBase，但完全未使用光照計算，
//     實質是 emissive-only 的加法疊加特效 → 改寫為 URP Unlit。
//   * 原檔 Property 宣告 _Falloff 卻採樣 _Fallof（typo，Inspector 欄位形同失效）。
//     此版統一為 _Falloff；舊材質請用 AuroraUrpMaterialUpgrader 轉換。
//   * 移除 ShadowCaster pass（加法疊加的發光體投影不合理）。
//   * 移除 #pragma only_renderers（原檔排除了 Vulkan / Metal / D3D12）。
//   * _TimeEditor 為 Shader Forge 編輯器專用變數，執行期恆為 0，已移除。
//   * 所有 material property 收進 UnityPerMaterial CBUFFER 以支援 SRP Batcher。
//
// 數學邏輯與原檔逐行等價，未做任何視覺調整。

Shader "AuroraBorealis_Pack/AuroraBorealis (URP)"
{
    Properties
    {
        _Falloff ("Falloff", 2D) = "white" {}
        _Ramp ("Ramp", 2D) = "white" {}
        _Noise ("Noise", 2D) = "white" {}
        _NoiseEvolutionSpeed ("NoiseEvolutionSpeed", Float) = 1
        [MaterialToggle] _InvertEvolutionDirection ("InvertEvolutionDirection", Float) = 1
        [MaterialToggle] _InvertNoiseTexture ("InvertNoiseTexture", Float) = 0
        _MovementSpeed ("MovementSpeed", Float) = 1
        _Sharpness ("Sharpness", Range(0, 1)) = 0.1
        _Color ("Color", 2D) = "white" {}
        _Color_Multiplier ("Color_Multiplier", Float) = 1
        _Dark_Multiplier ("Dark_Multiplier", Float) = 1
        _Dark_Tint ("Dark_Tint", Color) = (1,1,1,1)
        _Middle_Multiplier ("Middle_Multiplier", Float) = 1
        _Middle_Tint ("Middle_Tint", Color) = (1,1,1,1)
        _Highlight_Multiplier ("Highlight_Multiplier", Float) = 1
        _Highlight_Tint ("Highlight_Tint", Color) = (1,1,1,1)
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One   // 加法疊加，與原檔一致
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            // Core.hlsl 內部已引入 Common.hlsl / Input.hlsl / ShaderVariablesFunctions.hlsl，
            // 因此 TRANSFORM_TEX、_Time、GetCameraPositionWS、IS_FRONT_VFACE 等皆可直接使用。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise);   SAMPLER(sampler_Noise);
            TEXTURE2D(_Ramp);    SAMPLER(sampler_Ramp);
            TEXTURE2D(_Color);   SAMPLER(sampler_Color);
            TEXTURE2D(_Falloff); SAMPLER(sampler_Falloff);

            // SRP Batcher 相容：所有 material property（貼圖本體除外，只放 _ST）
            // 都必須宣告在此 CBUFFER 內。
            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float4 _Ramp_ST;
                float4 _Color_ST;
                float4 _Falloff_ST;
                float4 _Dark_Tint;
                float4 _Middle_Tint;
                float4 _Highlight_Tint;
                float  _NoiseEvolutionSpeed;
                float  _MovementSpeed;
                float  _Sharpness;
                float  _Color_Multiplier;
                float  _Dark_Multiplier;
                float  _Middle_Multiplier;
                float  _Highlight_Multiplier;
                float  _InvertEvolutionDirection;
                float  _InvertNoiseTexture;
                float  _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                float4 color      : COLOR;
            };

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                o.uv0        = v.uv0;
                o.uv1        = v.uv1;
                o.color      = v.color;
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag (Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // ---- 雙面法線翻轉（對應原檔的 VFACE 處理）----
                float  faceSign  = IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 normalWS  = normalize(i.normalWS) * faceSign;
                float3 viewDirWS = normalize(GetCameraPositionWS() - i.positionWS);

                // ---- 噪聲取樣：UV1.x 沿弧長捲動、UV1.y 驅動噪聲演化 ----
                float2 uv1     = i.uv1.xy;
                float4 timeVec = _Time;          // (t/20, t, 2t, 3t)
                float  vCoord  = uv1.y;

                float evoSpeed = lerp(_NoiseEvolutionSpeed, -_NoiseEvolutionSpeed,
                                      _InvertEvolutionDirection);
                float noiseV   = (vCoord * 0.05) + ((timeVec.y / 1000.0) * evoSpeed);
                float2 noiseUV = float2(uv1.x + (timeVec.x * _MovementSpeed * 0.1), noiseV);

                float4 noiseTex = SAMPLE_TEXTURE2D(_Noise, sampler_Noise,
                                                   TRANSFORM_TEX(noiseUV, _Noise));
                float  noise    = lerp(noiseTex.r, 1.0 - noiseTex.r, _InvertNoiseTexture);

                // ---- 由噪聲與高度差構出「簾幕」帶狀遮罩 ----
                // 兩個線性斜坡相乘 → 在 noise ≈ vCoord 附近形成一條窄帶，
                // sharpK 越大帶越窄。原檔的 -0.005 偏移讓帶寬不會退化成零。
                float sharpK  = _Sharpness * 20.0 + 5.0;
                float delta   = noise - vCoord;
                float curtain = (1.0 - (delta * sharpK)) * ((delta + 0.005) * sharpK) * 3.0;

                clip(saturate(curtain * 100.0) - 0.5);

                // ---- 垂直漸層 ramp（以 vertex color .r 當查表座標）----
                float2 rampUV  = float2(0.5, 1.0 - i.color.r);
                float4 rampTex = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp,
                                                  TRANSFORM_TEX(rampUV, _Ramp));
                float  shaped  = (curtain * rampTex.r) / 3.0;

                // ---- 三段配色：由 _Color 貼圖的 v = 0.05 / 0.5 / 0.95 取樣 ----
                // 先存成區域變數再傳入 TRANSFORM_TEX；該巨集會對引數做 .xy swizzle，
                // 直接餵 float2(...) 建構式在部分編譯器上可能出錯。
                float2 uvHighlight = float2(0.5, 0.05);
                float2 uvMiddle    = float2(0.5, 0.50);
                float2 uvDark      = float2(0.5, 0.95);

                float4 colHighlight = SAMPLE_TEXTURE2D(_Color, sampler_Color,
                                        TRANSFORM_TEX(uvHighlight, _Color));
                float4 colMiddle    = SAMPLE_TEXTURE2D(_Color, sampler_Color,
                                        TRANSFORM_TEX(uvMiddle, _Color));
                float4 colDark      = SAMPLE_TEXTURE2D(_Color, sampler_Color,
                                        TRANSFORM_TEX(uvDark, _Color));

                float vc     = 1.0 - i.color.r;
                float hiMask = vc * -3.333333 + 1.0;

                float3 tinted =
                      colHighlight.rgb * saturate(hiMask)
                                       * _Highlight_Multiplier * _Highlight_Tint.rgb
                    + colMiddle.rgb    * saturate((1.0 - hiMask) * (1.0 - vc))
                                       * _Middle_Multiplier * _Middle_Tint.rgb
                    + colDark.rgb      * pow(saturate(vc), 0.5)
                                       * _Dark_Multiplier * _Dark_Tint.rgb;

                // Sharpness 越高整體越亮，補償帶狀變窄造成的亮度損失
                float exposure = _Color_Multiplier
                               * saturate(_Sharpness + 0.5)
                               * (saturate(_Sharpness * 2.0 - 1.0) + 1.0);

                // ---- 沿 UV1.x 的兩端淡出 ----
                float uEdge   = uv1.x;
                float endFade = saturate(lerp(uEdge, 1.0 - uEdge, step(0.5, uEdge)) / 0.1);

                // ---- Falloff 貼圖：UV 刻意為 (uv1.y, uv1.x)，與原檔一致 ----
                float2 falloffUV = float2(uv1.y, uv1.x);
                float  falloff   = SAMPLE_TEXTURE2D(_Falloff, sampler_Falloff,
                                        TRANSFORM_TEX(falloffUV, _Falloff)).r;

                // ---- 視角衰減（原檔 pow 指數為 1，化簡後等同 N·V）----
                float ndotv = saturate(dot(normalWS, viewDirWS));

                // ---- UV0.x 的橫向柔邊（厚度方向）----
                float sideFade = (1.0 - i.uv0.x) * i.uv0.x * 4.0;

                float3 emissive = saturate(shaped * endFade)
                                * tinted
                                * exposure
                                * ndotv
                                * falloff
                                * sideFade;

                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
