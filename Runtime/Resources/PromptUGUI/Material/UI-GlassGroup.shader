// 融合玻璃组：把若干块玻璃当成一整片连续玻璃画出来。
//
// 为什么不是「各画各的再拼」：两块玻璃相邻时，用描边或缝隙去分割都很难看。这里对成员的 SDF 做
// polynomial smooth-min，交界处自然长出圆角过渡；而「哪块是主、哪块是次」改由**厚度**表达 ——
// 每块自己的 depth 在交界处按距离权重平滑过渡，形成一道斜面台阶，配合 smin 留下的折痕
// (crease) 轻微压暗，层级读得出来，却一条分割线都没有。
//
// 边缘打光作用在**融合后**的 SDF 上，所以高光自动沿着融合后的外轮廓流动，穿过交界时不断线。
Shader "UI/GlassGroup"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _BorderColor ("Border Color", Color) = (1,1,1,1)
        _GlowColor   ("Glow Color",   Color) = (1,1,1,1)
        _BorderWidth ("Border Width",  Float) = 0
        _GlowSize    ("Glow Size",     Float) = 0
        _Weld        ("Weld Radius",   Float) = 8

        _GlassA ("frost / _ / dispersion / noise", Vector) = (0.5, 0, 0, 0.02)
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
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "UI-PanelSDF.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #define PUGUI_MAX_WELD_MEMBERS 8

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;   // 组局部坐标（画布单位）
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 local         : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screenPos     : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _ClipRect;

            fixed4 _BorderColor;
            fixed4 _GlowColor;
            float _BorderWidth;
            float _GlowSize;
            float _Weld;
            float4 _GlassA;
            float4 _GlassB;

            // 逐成员数据。长度固定为 8：常量缓冲里的数组不能变长，C# 侧永远补满。
            float4 _WeldRects[PUGUI_MAX_WELD_MEMBERS];       // xy = 中心（组局部）, zw = 半尺寸
            float4 _WeldRadii[PUGUI_MAX_WELD_MEMBERS];       // TL,TR,BR,BL（pill 已在 CPU 解算）
            float4 _WeldTintTop[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldTintBottom[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldDepths[PUGUI_MAX_WELD_MEMBERS];      // x = depth
            int _WeldCount;

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
                OUT.local = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            // iq 的多项式 smooth-min：交界处以 k 为半径圆滑过渡，且比指数版便宜。
            float PuguiSmin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / max(k, 1e-4));
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            float MemberSd(int i, float2 p)
            {
                float4 rect = _WeldRects[i];
                return PuguiSdRoundBox(p - rect.xy, rect.zw, _WeldRadii[i]);
            }

            float3 SampleBackdrop(float2 uv, float frost)
            {
                float3 light = tex2D(_PUGUI_GlassBackdropA, uv).rgb;
                float3 heavy = tex2D(_PUGUI_GlassBackdropB, uv).rgb;
                return lerp(light, heavy, frost);
            }

            float IGNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.local;
                float k = max(_Weld, 1e-4);

                // 第一遍：融合形状 + 硬最小值（后者只用来当权重基准，保证指数不溢出）。
                float d = MemberSd(0, p);
                float dmin = d;
                for (int i = 1; i < _WeldCount; i++)
                {
                    float di = MemberSd(i, p);
                    dmin = min(dmin, di);
                    d = PuguiSmin(d, di, k);
                }

                // 第二遍：按「离本块表面多近」加权，把逐块的 depth 与 tint 混成逐像素的值。
                // 重算一次 SDF 而不是存数组：省掉 indexable temp，代价只是几十条 ALU。
                float s = 1.0 / k;
                float wsum = 0.0;
                float depth = 0.0;
                float4 tint = 0.0;
                float2 nrm = 0.0;
                for (int j = 0; j < _WeldCount; j++)
                {
                    float dj = MemberSd(j, p);
                    float w = exp(-(dj - dmin) * s);
                    float4 rect = _WeldRects[j];
                    float tj = saturate((p.y - (rect.y - rect.w)) / max(2.0 * rect.w, 1e-4));
                    tint += lerp(_WeldTintBottom[j], _WeldTintTop[j], tj) * w;
                    depth += _WeldDepths[j].x * w;
                    // 融合形状的法线 = 各块法线按同一组权重的混合。这正是 smin 对梯度做的事，
                    // 于是焊缝处法线平滑过渡、高光自然绕着融合后的外轮廓走 —— 而且不用多求一次
                    // SDF。解析法线的理由（跨平台一致 + 分支安全）见 UI-PanelSDF.cginc。
                    nrm += PuguiSdNormal(p - rect.xy, rect.zw, _WeldRadii[j]) * w;
                    wsum += w;
                }
                float inv = 1.0 / max(wsum, 1e-5);
                tint *= inv;
                depth *= inv;
                float nlen = length(nrm);
                float2 fusedNormal = nlen < 1e-6 ? float2(0.0, 1.0) : nrm / nlen;

                float fw = max(fwidth(d), 1e-4);
                float inside = saturate(0.5 - d / fw);

                // smin 把 d 压到硬最小值之下的那一点点，正是交界处的折痕 —— 只在焊缝附近非零。
                float crease = saturate((dmin - d) / k);

                float frost      = _GlassA.x;
                float dispersion = _GlassA.z;
                float noise      = _GlassA.w;
                float2 lightDir  = _GlassB.xy;
                float intensity  = _GlassB.z;
                float saturation = _GlassB.w;

                // 分支外求导：分支条件逐像素成立，非均匀控制流里的 ddx/ddy 未定义。这里只取
                // 长度（一屏幕像素等于多少画布单位），与光栅 Y 轴朝向无关，跨平台一致。
                float2 gradScreen = float2(ddx(d), ddy(d));
                float unitsPerPixel = max(length(gradScreen), 1e-5);

                float4 base = float4(0.0, 0.0, 0.0, 0.0);
                if (_PUGUI_GlassBackdropAvailable > 0.5 && inside > 0.0)
                {
                    float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                    float2 n = fusedNormal;

                    float band = depth > 0.0 ? saturate(1.0 + d / depth) * inside : 0.0;
                    float bevel = band * band;

                    float2 offset = n * bevel * (depth / unitsPerPixel) * 0.5 / _ScreenParams.xy;

                    float3 rgb;
                    if (dispersion > 0.0)
                    {
                        float3 spread = float3(1.0 + dispersion * 0.5, 1.0, 1.0 - dispersion * 0.5);
                        rgb.r = SampleBackdrop(uv + offset * spread.r, frost).r;
                        rgb.g = SampleBackdrop(uv + offset * spread.g, frost).g;
                        rgb.b = SampleBackdrop(uv + offset * spread.b, frost).b;
                    }
                    else
                    {
                        rgb = SampleBackdrop(uv + offset, frost);
                    }

                    float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                    rgb = lerp(luma.xxx, rgb, saturation);

                    float ndl = dot(n, lightDir);
                    float spec = pow(saturate(ndl), 4.0) + 0.35 * pow(saturate(-ndl), 4.0);
                    rgb += spec * bevel * intensity;

                    // 接触阴影：焊缝里侧压暗一点点，让两块的厚度差读得出来。没有它，融合处会平得
                    // 像同一块板，主次关系就丢了。
                    rgb *= 1.0 - crease * 0.45 * inside;

                    rgb += (IGNoise(uv * _ScreenParams.xy) - 0.5) * noise;

                    base = float4(rgb, inside);
                }

                tint.a *= inside;
                float4 col = PuguiOver(tint, base);

                if (_GlowSize > 0.0)
                {
                    float g = saturate(1.0 - d / _GlowSize);
                    float4 glow = _GlowColor;
                    glow.a *= g * g * (1.0 - inside);
                    col = PuguiOver(col, glow);
                }

                // 保底描边沿融合后的外轮廓走，交界内部自然没有它 —— 这正是要的效果。
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
