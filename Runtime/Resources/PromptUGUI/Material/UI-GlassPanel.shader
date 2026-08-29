// 玻璃面板：形状仍是 UI-ProceduralPanel 那套 SDF（同一份 UI-PanelSDF.cginc），只把"填充"从
// 纯色/渐变换成"模糊后的 backdrop + 边缘折射 + 方向光高光"。
//
// 视觉取向是 Figma glass / 薄磨砂亚克力，不是 iOS liquid glass：内部完全平整、零折射，
// 折射与打光全部限制在 depth 像素宽的边缘带内 —— 这就是"薄"与"厚"的分水岭。
//
// backdrop 由 GlassBackdropSystem 通过 Shader.SetGlobalTexture 供给两档模糊（A 轻 / B 重），
// frost 在两档之间插值。_PUGUI_GlassBackdropAvailable 是全局标量而非 shader keyword：
// 画质开关翻转它不会让任何一个面板换材质（换材质 = canvas 材质重建），而且它对所有 fragment
// 取值相同，分支完全一致、GPU 不会发散。没有 backdrop 时整段采样跳过，退化成半透明面板。
Shader "UI/GlassPanel"
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

        // 七个玻璃参数打包成两个向量：少几次 SetX，且光照角在 CPU 侧就化成方向，
        // fragment 里不用跑 sin/cos。
        _GlassA ("frost / depth / dispersion / noise", Vector) = (0.5, 4, 0, 0.02)
        _GlassB ("lightDir.xy / intensity / saturation", Vector) = (0, 1, 0.6, 1.15)

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
            // 3.0: SDF 抗锯齿与边缘法线都要 ddx/ddy。Unity 6 已无 GLES2 目标，无兼容顾虑。
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
                float2 texcoord : TEXCOORD0;   // rect 局部坐标（以中心为原点，画布单位）
                float2 texcoord1: TEXCOORD1;   // rect 半尺寸（画布单位）
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float4 shape         : TEXCOORD0;   // xy = 局部坐标, zw = 半尺寸
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
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
            float4 _GlassA;
            float4 _GlassB;

            // 全局，由 GlassBackdropSystem 每帧写入；不进材质参数，否则会破坏材质共享。
            sampler2D _PUGUI_GlassBackdropA;
            sampler2D _PUGUI_GlassBackdropB;
            float _PUGUI_GlassBackdropAvailable;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.shape = float4(v.texcoord, v.texcoord1);
                OUT.color = v.color;
                return OUT;
            }

            float3 SampleBackdrop(float2 uv, float frost)
            {
                float3 light = tex2D(_PUGUI_GlassBackdropA, uv).rgb;
                float3 heavy = tex2D(_PUGUI_GlassBackdropB, uv).rgb;
                return lerp(light, heavy, frost);
            }

            // Interleaved gradient noise：一次 frac 就够，比 hash 便宜，且天然无重复感。
            float IGNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.shape.xy;
                float2 b = IN.shape.zw;

                PuguiQuad corner = PuguiResolveQuad(p, b, _CornerKind, _Radius,
                                                       _CornerH, _CornerFillet, _Shape, _HexW);
                float d = PuguiSdPanel(p, b, corner);
                float fw = max(fwidth(d), 1e-4);
                float inside = saturate(0.5 - d / fw);

                float frost      = _GlassA.x;
                float depth      = _GlassA.y;
                float dispersion = _GlassA.z;
                float noise      = _GlassA.w;
                float2 lightDir  = _GlassB.xy;
                float intensity  = _GlassB.z;
                float saturation = _GlassB.w;

                // 玻璃体：只有拿得到 backdrop 才画，否则 base 留空 —— 下面的 tint 直接落在
                // 透明底上，结果与不透明面板逐像素一致，这就是降级视觉。
                // 导数必须在分支外求：分支条件逐像素成立，而非均匀控制流里的 ddx/ddy 是未定义
                // 行为。这里只取它的**长度**（一个屏幕像素等于多少画布单位），长度与光栅 Y 轴
                // 朝向无关，所以跨平台一致；方向另外用局部空间中心差分求（见 PuguiSdNormal）。
                float2 gradScreen = float2(ddx(d), ddy(d));
                float unitsPerPixel = max(length(gradScreen), 1e-5);

                float4 base = float4(0.0, 0.0, 0.0, 0.0);
                if (_PUGUI_GlassBackdropAvailable > 0.5 && inside > 0.0)
                {
                    float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                    // 画布空间外法线：+Y 就是界面正上方，与 lightAngle 的定义同一套坐标系。
                    float2 n = PuguiPanelNormal(p, b, corner);

                    // 折射带：仅 -depth < d < 0。band 在带外沿=1、带内沿=0。
                    float band = depth > 0.0 ? saturate(1.0 + d / depth) * inside : 0.0;
                    // 二次曲线让内部严格保持平整 —— 这是"薄玻璃"而非"果冻"的关键。
                    float bevel = band * band;

                    float2 offset = n * bevel * (depth / unitsPerPixel) * 0.5 / _ScreenParams.xy;

                    float3 rgb;
                    if (dispersion > 0.0)
                    {
                        // 三通道走不同折射率，只在有色散时付这份采样代价。
                        float3 spread = float3(1.0 + dispersion * 0.5, 1.0, 1.0 - dispersion * 0.5);
                        rgb.r = SampleBackdrop(uv + offset * spread.r, frost).r;
                        rgb.g = SampleBackdrop(uv + offset * spread.g, frost).g;
                        rgb.b = SampleBackdrop(uv + offset * spread.b, frost).b;
                    }
                    else
                    {
                        rgb = SampleBackdrop(uv + offset, frost);
                    }

                    // Vibrancy：提饱和度比折射更能决定玻璃是"发亮"还是"发灰"。
                    float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                    rgb = lerp(luma.xxx, rgb, saturation);

                    // 边缘打光：正面高光 + 180° 反向补光，像管壁截面的双侧亮边。
                    // 这是"光打在玻璃上"形成的物理描边，会自动跟着 SDF 轮廓走。
                    float ndl = dot(n, lightDir);
                    float spec = pow(saturate(ndl), 4.0) + 0.35 * pow(saturate(-ndl), 4.0);
                    rgb += spec * bevel * intensity;

                    // 磨砂颗粒：兼作 dithering，挡住大面积模糊底上的色带。
                    rgb += (IGNoise(IN.screenPos.xy / max(IN.screenPos.w, 1e-5) * _ScreenParams.xy)
                            - 0.5) * noise;

                    base = float4(rgb, inside);
                }

                // 彩色玻璃：tint 压在玻璃体之上，与不透明面板的 color 语义完全一致
                // （逗号双色渐变照常，第一段在顶部）。
                float t = saturate((p.y + b.y) / max(2.0 * b.y, 1e-4));
                float4 tint = lerp(_FillBottom, _FillTop, t);
                tint.a *= inside;
                float4 col = PuguiOver(tint, base);

                // 内发光：外发光的镜像 —— 画在形状内侧、压在填充之上。
                // 排在外发光之前，让外发光的 under 合成看到「填充 + 内发光」这一个完整实心体
                // （两者除 AA 那一像素外并不相交，所以顺序只影响那一像素）。
                col = PuguiApplyInnerGlow(col, d, inside, _InnerGlowSize, _InnerGlowColor);

                // 外发光：仅在形状外侧衰减。
                col = PuguiApplyOuterGlow(col, d, inside, _GlowSize, _GlowColor);

                // 内描边：低对比背景下物理高光会消失，而 UI 的边界不能跟着消失 —— 这一层是保底。
                if (_BorderWidth > 0.0)
                {
                    float4 border = _BorderColor;
                    border.a *= inside * saturate(0.5 + (d + _BorderWidth) / fw);
                    col = PuguiOver(border, col);
                }

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
