// 程序化面板：无 sprite，靠圆角矩形 SDF 在 fragment 里画出填充 / 内描边 / 外发光。
//
// 形状输入走顶点通道而非材质，这是刻意的：局部坐标 (TEXCOORD0) 与半尺寸 (TEXCOORD1) 逐面板不同，
// 而 radius / 描边 / 发光 / 颜色逐 *样式* 相同 —— 于是 class="card" 的一堆不同尺寸面板能共用
// 同一个材质实例（见 ProceduralMaterialCache），可以合批；同时改颜色只换材质、不脏顶点，
// 不触发 Canvas 重建（uGUI 掉帧的头号来源）。
//
// 保留 RectMask2D 裁剪 (_ClipRect)、Mask 蒙版 (Stencil)、AlphaClip —— 与 UI/Default 一致。
Shader "UI/ProceduralPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _FillTop     ("Fill Top",     Color) = (0,0,0,0)
        _FillBottom  ("Fill Bottom",  Color) = (0,0,0,0)
        _BorderColor ("Border Color", Color) = (1,1,1,1)
        _GlowColor   ("Glow Color",   Color) = (1,1,1,1)

        // xyzw = top-left, top-right, bottom-right, bottom-left (CSS border-radius 顺序)
        _Radius      ("Radius TL/TR/BR/BL", Vector) = (0,0,0,0)
        _Pill        ("Pill Sentinel", Float) = 0
        _BorderWidth ("Border Width",  Float) = 0
        _GlowSize    ("Glow Size",     Float) = 0

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
            // 3.0: SDF 抗锯齿要 fwidth (ddx/ddy)。Unity 6 已无 GLES2 目标，无兼容顾虑。
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;   // rect 局部坐标（以中心为原点，像素）
                float2 texcoord1: TEXCOORD1;   // rect 半尺寸（像素）
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 shape         : TEXCOORD0;   // xy = 局部坐标, zw = 半尺寸
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _ClipRect;

            fixed4 _FillTop;
            fixed4 _FillBottom;
            fixed4 _BorderColor;
            fixed4 _GlowColor;
            float4 _Radius;
            float _Pill;
            float _BorderWidth;
            float _GlowSize;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.shape = float4(v.texcoord, v.texcoord1);
                OUT.color = v.color;
                return OUT;
            }

            // iq 的圆角矩形 SDF，四角半径独立。p 以矩形中心为原点，b 为半尺寸。
            // 返回值：负=内部，正=外部，绝对值≈到边界的像素距离。
            float sdRoundBox(float2 p, float2 b, float4 r)
            {
                // 象限选角：右半区取 (TR, BR)，左半区取 (TL, BL)；再按上下二选一。
                float2 side = (p.x > 0.0) ? float2(r.y, r.z) : float2(r.x, r.w);
                float radius = (p.y > 0.0) ? side.x : side.y;
                float2 q = abs(p) - b + radius;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
            }

            // 直 alpha 的 source-over 合成。
            float4 over(float4 src, float4 dst)
            {
                float a = src.a + dst.a * (1.0 - src.a);
                float3 rgb = (src.rgb * src.a + dst.rgb * dst.a * (1.0 - src.a)) / max(a, 1e-5);
                return float4(rgb, a);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.shape.xy;
                float2 b = IN.shape.zw;

                // pill 在这里解算而不是在 C# 里：它依赖 rect 尺寸，提前算掉会让同一 style
                // 的不同尺寸面板拿到不同材质参数，白白丢掉材质共享。
                float shortest = min(b.x, b.y);
                float4 r = (_Pill > 0.5) ? shortest.xxxx : _Radius;
                r = clamp(r, 0.0, shortest);

                float d = sdRoundBox(p, b, r);
                float fw = max(fwidth(d), 1e-4);

                float inside = saturate(0.5 - d / fw);

                // 填充：纵向渐变，t=1 在顶部（与逗号色值语法"第一段是顶部色"一致）。
                float t = saturate((p.y + b.y) / max(2.0 * b.y, 1e-4));
                float4 col = lerp(_FillBottom, _FillTop, t);
                col.a *= inside;

                // 外发光：仅在形状外侧衰减，平方让边缘更快收住、更像光晕而不是色块。
                if (_GlowSize > 0.0)
                {
                    float g = saturate(1.0 - d / _GlowSize);
                    float4 glow = _GlowColor;
                    glow.a *= g * g * (1.0 - inside);
                    col = over(col, glow);
                }

                // 内描边：向内绘制（border-box 直觉），压在填充之上。
                // _BorderWidth==0 时必须整段跳过 —— 否则下面的覆盖率退化成边缘 AA 带，
                // 会凭空多出一圈 1px 描边。这是 uniform 分支，全体 fragment 同路径，开销可忽略。
                if (_BorderWidth > 0.0)
                {
                    float4 border = _BorderColor;
                    border.a *= inside * saturate(0.5 + (d + _BorderWidth) / fw);
                    col = over(border, col);
                }

                // 顶点色 = Graphic.color × CanvasRenderer/CanvasGroup alpha。
                // 面板自身的四种颜色都在材质里，所以这一乘就是整块面板的统一 tint / 淡入淡出。
                col *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
