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

        // 四个色槽各是一条 7-uniform 的线性渐变（UI-PanelSDF.cginc「线性渐变」一节）：4 个色标、
        // 位置、曲线(xyz)+色标数(w)、方向。纯色 = 色标数 1，只读第一个色。
        _Fill0 ("Fill Stop 0", Color) = (0,0,0,0)
        _Fill1 ("Fill Stop 1", Color) = (0,0,0,0)
        _Fill2 ("Fill Stop 2", Color) = (0,0,0,0)
        _Fill3 ("Fill Stop 3", Color) = (0,0,0,0)
        _FillStops ("Fill Stops", Vector) = (0,1,1,1)
        _FillCurves ("Fill Curves (E0,E1,E2,count)", Vector) = (1,1,1,1)
        _FillDir ("Fill Direction", Vector) = (0,-1,0,0)
        _Border0 ("Border Stop 0", Color) = (1,1,1,1)
        _Border1 ("Border Stop 1", Color) = (1,1,1,1)
        _Border2 ("Border Stop 2", Color) = (1,1,1,1)
        _Border3 ("Border Stop 3", Color) = (1,1,1,1)
        _BorderStops ("Border Stops", Vector) = (0,1,1,1)
        _BorderCurves ("Border Curves", Vector) = (1,1,1,1)
        _BorderDir ("Border Direction", Vector) = (0,-1,0,0)
        _Glow0 ("Glow Stop 0", Color) = (1,1,1,1)
        _Glow1 ("Glow Stop 1", Color) = (1,1,1,1)
        _Glow2 ("Glow Stop 2", Color) = (1,1,1,1)
        _Glow3 ("Glow Stop 3", Color) = (1,1,1,1)
        _GlowStops ("Glow Stops", Vector) = (0,1,1,1)
        _GlowCurves ("Glow Curves", Vector) = (1,1,1,1)
        _GlowDir ("Glow Direction", Vector) = (0,-1,0,0)
        _InnerGlow0 ("Inner Glow Stop 0", Color) = (1,1,1,1)
        _InnerGlow1 ("Inner Glow Stop 1", Color) = (1,1,1,1)
        _InnerGlow2 ("Inner Glow Stop 2", Color) = (1,1,1,1)
        _InnerGlow3 ("Inner Glow Stop 3", Color) = (1,1,1,1)
        _InnerGlowStops ("Inner Glow Stops", Vector) = (0,1,1,1)
        _InnerGlowCurves ("Inner Glow Curves", Vector) = (1,1,1,1)
        _InnerGlowDir ("Inner Glow Direction", Vector) = (0,-1,0,0)
        // 第五个色槽：噪声雾的颜色（同时是它的方向遮罩，spec 2026-09-17 haze）。fBm 只乘在它的 alpha 上。
        _Haze0 ("Haze Stop 0", Color) = (1,1,1,1)
        _Haze1 ("Haze Stop 1", Color) = (1,1,1,1)
        _Haze2 ("Haze Stop 2", Color) = (1,1,1,1)
        _Haze3 ("Haze Stop 3", Color) = (1,1,1,1)
        _HazeStops ("Haze Stops", Vector) = (0,1,1,1)
        _HazeCurves ("Haze Curves", Vector) = (1,1,1,1)
        _HazeDir ("Haze Direction", Vector) = (0,-1,0,0)

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
        // 曝光倍数（spec 2026-09-12）：1 = 不变，越大越白。
        _Intensity   ("Intensity",     Float) = 1
        // 噪声雾（spec 2026-09-17 haze）：斑块特征尺寸 px（0 = 无雾）与流速 px/s。
        _HazeSize    ("Haze Size",     Float) = 0
        _HazeDrift   ("Haze Drift",    Float) = 0
        _HazeDensity ("Haze Density",  Float) = 0.5

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
        // 分离的 alpha 混合因子不是可选项：RGB 照常 source-over，**alpha 通道必须走 One
        // OneMinusSrcAlpha**。开了 HDR 显示输出后，URP 不再把 overlay UI 直接画进 backbuffer，
        // 而是画进一张清成透明黑的离屏 RGBA8，再由 SceneUIComposition 合成 —— 那段代码把这张图
        // 当作「预乘 RGB + 直 alpha」：先 rgb/a 反预乘，再 rgb*a + scene*(1-a)。
        // 若 alpha 通道也用 SrcAlpha 混，写进去的是 a*a：RGB 因为除回来而恰好抵消，但场景的透出量
        // 变成 1-a²，于是每个 0<a<1 的像素都多漏进 a(1-a) 份背景（a=0.5 时高达 25%）。
        // 实心填充与描边 (a=1) 毫发无损，**外发光那整条渐变**、玻璃、以及 1px AA 边却被背景冲淡，
        // 只剩贴着形状边缘 a→1 的那一圈还是干净的 —— 正是「SDR 正常、HDR 上渐变没了只剩实色描边」。
        // SDR 路径从不读 dst alpha，这个写法在那边逐位不变。TMP 用 `Blend One OneMinusSrcAlpha`
        // （直接输出预乘色）绕开了同一个坑，所以文字一直是对的。
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
                float4 texcoord1: TEXCOORD1;   // xy = rect 半尺寸（像素）, zw = 进度裁切 (code, e)，见 PuguiSdCut
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 shape         : TEXCOORD0;   // xy = 局部坐标, zw = 半尺寸
                float4 worldPosition : TEXCOORD1;
                float2 cut           : TEXCOORD2;   // 进度裁切 (code, e)；code 0 = 不切
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _ClipRect;

            PUGUI_RAMP_UNIFORMS(_Fill)
            PUGUI_RAMP_UNIFORMS(_Border)
            PUGUI_RAMP_UNIFORMS(_Glow)
            PUGUI_RAMP_UNIFORMS(_InnerGlow)
            PUGUI_RAMP_UNIFORMS(_Haze)
            float4 _Radius;
            float4 _CornerH;
            float4 _CornerKind;
            float4 _CornerFillet;
            float _Shape;
            float _HexW;
            float _BorderWidth;
            float _GlowSize;
            float _InnerGlowSize;
            float _Intensity;
            float _HazeSize;
            float _HazeDrift;
            float _HazeDensity;
            // 全局，HazeClock 每帧写（未缩放秒）；没有任何带 hazeDrift 的材质时从不写、恒为 0。
            float _PuguiUnscaledTime;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.shape = float4(v.texcoord, v.texcoord1.xy);
                OUT.cut = v.texcoord1.zw;
                OUT.color = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.shape.xy;
                float2 b = IN.shape.zw;

                PuguiQuad corner = PuguiResolveQuad(p, b, _CornerKind, _Radius,
                                                       _CornerH, _CornerFillet, _Shape, _HexW);
                // 进度裁切（spec 2026-09-18 §5.2）：先切再求导，下面每一层看到的都是切完的形状。
                float d = PuguiSdCut(PuguiSdPanel(p, b, corner), p, IN.cut.x, IN.cut.y);
                float fw = max(fwidth(d), 1e-4);

                float inside = saturate(0.5 - d / fw);

                // 填充：线性渐变 —— 方向、色标、曲线都在 ramp 里（见 PuguiGradient）。
                float4 col = PuguiGradient(p, b, PUGUI_RAMP(_Fill));
                col.a *= inside;

                // 噪声雾：压在填充之上、内发光与描边之下，只在形状内侧（spec 2026-09-17 haze §5.2）。
                // 颜色走与其他四个色槽同一条渐变线（rect 定义），噪声本身在 Canvas 空间采样（§5.3）。
                // _HazeSize==0 时整段跳过：uniform 分支，无雾面板逐位不变。
                if (_HazeSize > 0.0)
                {
                    float4 haze = PuguiGradient(p, b, PUGUI_RAMP(_Haze));
                    haze.a *= inside * PuguiHazeWeight(IN.worldPosition.xy, _HazeSize, _HazeDrift, _PuguiUnscaledTime, _HazeDensity);
                    col = PuguiOver(haze, col);
                }

                // 内发光：外发光的镜像 —— 画在形状内侧、压在填充之上。
                // 排在外发光之前，让外发光的 under 合成看到「填充 + 内发光」这一个完整实心体
                // （两者除 AA 那一像素外并不相交，所以顺序只影响那一像素）。
                col = PuguiApplyInnerGlow(col, d, inside, _InnerGlowSize, PuguiGradient(p, b, PUGUI_RAMP(_InnerGlow)));

                // 外发光：仅在形状外侧衰减。
                col = PuguiApplyOuterGlow(col, d, inside, _GlowSize, PuguiGradient(p, b, PUGUI_RAMP(_Glow)));

                // 内描边：向内绘制（border-box 直觉），压在填充之上。
                // _BorderWidth==0 时必须整段跳过 —— 否则下面的覆盖率退化成边缘 AA 带，
                // 会凭空多出一圈 1px 描边。这是 uniform 分支，全体 fragment 同路径，开销可忽略。
                if (_BorderWidth > 0.0)
                {
                    float4 border = PuguiGradient(p, b, PUGUI_RAMP(_Border));
                    border.a *= inside * saturate(0.5 + (d + _BorderWidth) / fw);
                    col = PuguiOver(border, col);
                }

                // 曝光：作用在这个面画出来的全部东西上（填充 + 两层发光 + 描边的合成），恰好一次，
                // 且在顶点色之前 —— 淡出就是淡出、*Modulate 压暗的是点亮后的表面，不会「冷却」回本色。
                col = PuguiExpose(col, _Intensity);

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
