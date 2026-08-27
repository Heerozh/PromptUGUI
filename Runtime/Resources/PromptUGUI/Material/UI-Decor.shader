// 装饰原语（<Decor>）：无 sprite，靠 SDF 在 fragment 里画角括号 / 指示三角 / 强调线，
// 加一圈可选外发光。
//
// 与 UI-ProceduralPanel 的分工完全一致：形状输入（局部坐标 / 半尺寸）走顶点通道，
// 视觉参数（种类 / 笔宽 / 颜色 / 发光）走材质 —— 于是同一份 class= 出来的四个角括号
// 共用一个材质、合到一个 draw call；改颜色只换材质、不脏顶点。
//
// **朝向也在顶点里**：DecorPanel 把局部坐标翻/转到规范朝向再写进 TEXCOORD0，所以
// 「左上角的括号」和「右下角的括号」是同一份材质。见 UI-PanelSDF.cginc 的装饰段。
//
// 比面板 shader 少三样：无内描边（描边的描边没有语义）、无玻璃（内层玻璃采同一张
// backdrop，两层长得一样）、因此也不需要法线 —— 面板那边最重的一块这里整个不存在。
Shader "UI/Decor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _FillTop    ("Fill Top",    Color) = (1,1,1,1)
        _FillBottom ("Fill Bottom", Color) = (1,1,1,1)
        _GlowColor  ("Glow Color",  Color) = (1,1,1,1)

        // 1 = bracket, 2 = tick, 3 = line（与 PromptUGUI.Parser.DecorKind 数值一致）
        _Kind      ("Kind", Float) = 1
        _Thickness ("Stroke Thickness", Float) = 2
        _GlowSize  ("Glow Size", Float) = 0

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
                float2 texcoord : TEXCOORD0;   // 规范朝向的局部坐标（像素）
                float2 texcoord1: TEXCOORD1;   // 规范朝向的半尺寸（像素）
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
            fixed4 _GlowColor;
            float _Kind;
            float _Thickness;
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

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.shape.xy;
                float2 b = IN.shape.zw;

                // uniform 分支：全体 fragment 同路径，开销可忽略（同面板的 _BorderWidth 分支）。
                float d;
                if (_Kind < 1.5)      d = PuguiSdBracket(p, b, _Thickness);
                else if (_Kind < 2.5) d = PuguiSdTick(p, b);
                else                  d = PuguiSdBoxAt(p, float2(0.0, 0.0), b);

                float fw = max(fwidth(d), 1e-4);
                float inside = saturate(0.5 - d / fw);

                // 填充：纵向渐变，t=1 在顶部（与逗号色值语法"第一段是顶部色"一致）。
                float t = saturate((p.y + b.y) / max(2.0 * b.y, 1e-4));
                float4 col = lerp(_FillBottom, _FillTop, t);
                col.a *= inside;

                if (_GlowSize > 0.0)
                {
                    float g = saturate(1.0 - d / _GlowSize);
                    float4 glow = _GlowColor;
                    glow.a *= g * g * (1.0 - inside);
                    col = PuguiOver(col, glow);
                }

                col *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                // 装饰不当遮罩源（decor spec §6.2），这段只在被祖先 Mask 的材质变体里出现；
                // 与面板同样按形状实心区裁，免得外发光把裁剪范围撑大一圈。
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
