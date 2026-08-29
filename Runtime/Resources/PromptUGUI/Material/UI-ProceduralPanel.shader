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
        _InnerGlowColor ("Inner Glow Color", Color) = (1,1,1,1)

        // 四个逐角向量一律是 xyzw = top-left, top-right, bottom-right, bottom-left
        // （CSS border-radius 顺序）。_Radius 是每个角的**水平**伸出量，圆角时即半径。
        _Radius      ("Corner Width TL/TR/BR/BL",  Vector) = (0,0,0,0)
        _CornerH     ("Corner Height TL/TR/BR/BL", Vector) = (0,0,0,0)
        _CornerKind  ("Corner Kind TL/TR/BR/BL (0 round / 1 cut / 2 notch)", Vector) = (0,0,0,0)
        // 逐角倒圆半径（cut / notch / hexagon 的顶点），0 = 尖角。
        _CornerFillet ("Corner Fillet TL/TR/BR/BL", Vector) = (0,0,0,0)
        // 整形哨兵：0 无 / 1 pill / 2 hexagon。两者都依赖 rect 尺寸，逐片元解算。
        _Shape       ("Shape Sentinel", Float) = 0
        _HexW        ("Hexagon Tip Reach (0 = auto)", Float) = 0
        _BorderWidth ("Border Width",  Float) = 0
        _GlowSize    ("Glow Size",     Float) = 0
        _InnerGlowSize ("Inner Glow Size", Float) = 0

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
            #include "UI-PanelSDF.cginc"

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
            fixed4 _InnerGlowColor;
            float4 _Radius;
            float4 _CornerH;
            float4 _CornerKind;
            float4 _CornerFillet;
            float _Shape;
            float _HexW;
            float _BorderWidth;
            float _GlowSize;
            float _InnerGlowSize;

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

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.shape.xy;
                float2 b = IN.shape.zw;

                PuguiCorner corner = PuguiResolveCorner(p, b, _CornerKind, _Radius,
                                                       _CornerH, _CornerFillet, _Shape, _HexW);
                float d = PuguiSdPanel(p, b, corner);
                float fw = max(fwidth(d), 1e-4);

                float inside = saturate(0.5 - d / fw);

                // 填充：纵向渐变，t=1 在顶部（与逗号色值语法"第一段是顶部色"一致）。
                float t = saturate((p.y + b.y) / max(2.0 * b.y, 1e-4));
                float4 col = lerp(_FillBottom, _FillTop, t);
                col.a *= inside;

                // 内发光：外发光的镜像 —— 画在形状内侧、压在填充之上。
                // 排在外发光之前，让外发光的 under 合成看到「填充 + 内发光」这一个完整实心体
                // （两者除 AA 那一像素外并不相交，所以顺序只影响那一像素）。
                col = PuguiApplyInnerGlow(col, d, inside, _InnerGlowSize, _InnerGlowColor);

                // 外发光：仅在形状外侧衰减。
                col = PuguiApplyOuterGlow(col, d, inside, _GlowSize, _GlowColor);

                // 内描边：向内绘制（border-box 直觉），压在填充之上。
                // _BorderWidth==0 时必须整段跳过 —— 否则下面的覆盖率退化成边缘 AA 带，
                // 会凭空多出一圈 1px 描边。这是 uniform 分支，全体 fragment 同路径，开销可忽略。
                if (_BorderWidth > 0.0)
                {
                    float4 border = _BorderColor;
                    border.a *= inside * saturate(0.5 + (d + _BorderWidth) / fw);
                    col = PuguiOver(border, col);
                }

                // 顶点色 = Graphic.color × CanvasRenderer/CanvasGroup alpha。
                // 面板自身的四种颜色都在材质里，所以这一乘就是整块面板的统一 tint / 淡入淡出。
                col *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                // 只有 stencil 遮罩源会打开这个关键字（uGUI 的 StencilMaterial 只在这次 draw 要写
                // stencil 时才开），所以下面这段不影响任何正常渲染路径。
                //
                // 遮罩形状取 SDF 的实心区，而不是最终 alpha：外发光画在形状**之外**
                // （glow.a *= g*g*(1-inside)），按 col.a 裁会把遮罩连光晕一起撑大一圈；而没有
                // 填充的面内部 col.a == 0，按 col.a 裁又会把中间整个裁空 —— 于是「隐形的圆角
                // 裁剪器」这个最有用的形态反而做不出来。形状就是形状，与画了什么无关。
                float maskCoverage = inside;
                #ifdef UNITY_UI_CLIP_RECT
                maskCoverage *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                clip(maskCoverage - 0.5);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
