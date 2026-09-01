// <Image> / <Icon> 的 sprite 级效果：blur（本体像素模糊）、glow（自剪影外发光）、tint="linear"、
// 禁用去色 —— 四者折在一个 shader 里（spec 2026-09-02）。
//
// 为什么是"一个 shader 四件事"而不是四个材质轮流占 Graphic.material 槽：那个槽只有一个，
// tint 与灰度今天就是靠互相覆盖共存的，再加 blur/glow 就必然打架。折进来之后它们不再是槽，
// 而是参数，可以任意组合，且同参数集的实例仍共享一个材质、照常合批（FxMaterialCache）。
//
// 顶点侧由 FxMesh 供给：uv0 已按半径外推（会越出 sprite 的 UV 矩形，这是故意的），
// uv1 = sprite 自己的 UV 矩形，uv2 = uv/画布单位换算。矩形之外的 tap 一律当透明 ——
// 图集里那外面住着别的 sprite。
//
// 全部为 uniform 分支（`if (_Blur > 0)`），不设 shader 关键字：运行时 new Material 的变体
// 会被构建剥掉，而 uniform 分支全体 fragment 走同一路径，开销可忽略（UI-ProceduralPanel 同款）。
Shader "UI/ImageFx"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Blur ("Blur Radius (px)", Float) = 0
        _Glow ("Glow Radius (px)", Float) = 0
        _GlowColor ("Glow Color", Color) = (1,1,1,1)
        _GlowSelf ("Glow Takes Sprite Colour", Float) = 1
        _TintLinear ("Linear Light Tint", Float) = 0
        _Desaturate ("Desaturate", Float) = 0

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
        // alpha 通道走 One OneMinusSrcAlpha：HDR 显示输出下的离屏 UI 合成要求正确的直 alpha，
        // 理由见 UI-ProceduralPanel.shader 同一行的注释。
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;   // sprite 的 UV 矩形 (uMin, vMin, uMax, vMax)
                float4 texcoord2 : TEXCOORD2;   // uv / 画布单位 (du, dv, 0, 0)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 rect : TEXCOORD2;
                float2 perUnit : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _Blur;
            float _Glow;
            fixed4 _GlowColor;
            float _GlowSelf;
            float _TintLinear;
            float _Desaturate;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.rect = v.texcoord1;
                OUT.perUnit = v.texcoord2.xy;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

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
