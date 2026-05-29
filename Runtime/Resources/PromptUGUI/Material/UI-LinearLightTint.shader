// uGUI Image 的 Linear Light tint shader：
//   sprite.rgb 50% 灰 (#808080) = 中性 (输出 = tint)，越暗越偏黑、越亮越偏白。
//   sprite.a = 形状 mask，与 tint.a 相乘。
// 直接替换 UI/Default：建 Material 选此 shader 挂到 Image 上即可。
//
// 数学在 gamma (sRGB 显示) 空间算，保留 "128 = 中性" 的直觉。在 Linear color space 下
// 直接算的话，128 sRGB 采样进 shader 已变成 linear ≈ 0.216，2*0.216-1 = -0.568 会
// 把 tint 拉到接近黑色。所以要还原到显示空间的语义。
//
// 关键：sprite 纹理和 vertex color 进 shader 时所处的 color space 不一样——
//   - sprite 纹理标了 sRGB，Linear 模式下被 GPU 采样器自动转成 linear，要转回 gamma；
//   - uGUI 的 vertex color (Image.color 的 tint) 不经 sRGB 采样，进 shader 时仍是
//     gamma 原值，绝不能再 LinearToGamma（否则会被二次 gamma 编码，19,91,174 变 77,161,215）。
// 另外往复用 *Exact 版而非 UnityCG 的多项式近似，才能和硬件 sRGB framebuffer 的编码精确互逆
// （近似版会让 19,91,174 这类暗色掉到 ~15,...，肉眼可见偏差）。

Shader "UI/LinearLightTint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 sprite = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;

                // 在 gamma (sRGB 显示) 空间做 Linear Light，让美术眼中的 128 灰真的等于中性。
                // tint 两种 color space 下都已是 gamma 空间，不做转换；只有 linear 模式下的
                // sprite（被采样器 linear 化）需要转回 gamma。
                half3 tintG = IN.color.rgb;
                #ifdef UNITY_COLORSPACE_GAMMA
                half3 spriteG = sprite.rgb;
                #else
                half3 spriteG = half3(
                    LinearToGammaSpaceExact(sprite.r),
                    LinearToGammaSpaceExact(sprite.g),
                    LinearToGammaSpaceExact(sprite.b));
                #endif

                half3 rgbG = saturate(tintG + 2.0 * spriteG - 1.0);

                #ifdef UNITY_COLORSPACE_GAMMA
                half3 rgb = rgbG;
                #else
                half3 rgb = half3(
                    GammaToLinearSpaceExact(rgbG.r),
                    GammaToLinearSpaceExact(rgbG.g),
                    GammaToLinearSpaceExact(rgbG.b));
                #endif

                half a = sprite.a * IN.color.a;

                half4 color = half4(rgb, a);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
