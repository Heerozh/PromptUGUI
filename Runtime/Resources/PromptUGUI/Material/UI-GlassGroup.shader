// 融合玻璃组：把若干块玻璃当成一整片厚薄不均的连续玻璃画出来。
//
// 为什么不是「各画各的再拼」：两块玻璃相邻时，用描边或缝隙去分割都很难看。这里对成员的 SDF 做
// polynomial smooth-min，交界处自然长出圆角过渡；而「哪块是主、哪块是次」改由**厚度**表达。
//
// 厚度是一张**高度图**：每块把自己的 depth 按「以本块轮廓为中心、宽 seam 的软覆盖」涂进这片
// 玻璃，按子级声明顺序 source-over 折叠（后涂的覆盖先涂的，不累加），再按覆盖率归一。于是
//   · 单块：h 恒等于它自己的 depth —— 外轮廓处没有多余的坡，那一圈仍只由既有斜面负责；
//   · 相接等厚：覆盖率互补、h 恒定，交界不出沟；
//   · 相接异厚：一道宽约 seam 的单调台阶；
//   · 重叠：台阶落在**后声明块的轮廓**上 —— 厚盖薄是凸台阶，薄盖厚是凹槽。
// 台阶的坡面按解析梯度求出法线，用与外斜面同一套公式打光、并把背景轻微折一下 —— 这就是真实
// 熔接玻璃上那道细高光。厚度相同时梯度严格为零，等厚的组逐像素不变。
//
// 边缘打光作用在**融合后**的 SDF 上，所以高光自动沿着融合后的外轮廓流动，穿过交界时不断线。
// 成员形状走的是单面板同一套角解算（PuguiResolveQuad / PuguiSdPanel / PuguiPanelNormal），
// cut / notch / hexagon / rN 在融合组里照画 —— 内部台阶正是沿成员自己的轮廓走的。
Shader "UI/GlassGroup"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _BorderColor ("Border Color", Color) = (1,1,1,1)
        _GlowColor   ("Glow Color",   Color) = (1,1,1,1)
        _InnerGlowColor ("Inner Glow Color", Color) = (1,1,1,1)
        _BorderWidth ("Border Width",  Float) = 0
        _GlowSize    ("Glow Size",     Float) = 0
        _InnerGlowSize ("Inner Glow Size", Float) = 0
        _Weld        ("Weld Radius",   Float) = 8

        _GlassA ("frost / seam / dispersion / noise", Vector) = (0.5, 3, 0, 0.02)
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
            fixed4 _InnerGlowColor;
            float _BorderWidth;
            float _GlowSize;
            float _InnerGlowSize;
            float _Weld;
            float4 _GlassA;
            float4 _GlassB;

            // 逐成员数据。长度固定为 8：常量缓冲里的数组不能变长，C# 侧永远补满。
            float4 _WeldRects[PUGUI_MAX_WELD_MEMBERS];        // xy = 中心（组局部）, zw = 半尺寸
            // 逐角几何，与单面板 shader 的 _Radius / _CornerH / _CornerKind / _CornerFillet 同义、
            // 同为 CSS 顺序 TL,TR,BR,BL，且同样未经钳制 —— 钳制是角解算器的事，做两遍就会漂移。
            float4 _WeldCornerW[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldCornerH[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldCornerKind[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldCornerFillet[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldTintTop[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldTintBottom[PUGUI_MAX_WELD_MEMBERS];
            float4 _WeldDepths[PUGUI_MAX_WELD_MEMBERS];       // x = depth, y = shape 哨兵, z = hexW
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

            // 成员的距离场与解析外法线，一次算出两者。解析法线的理由（lightAngle 是画布空间概念、
            // 光栅 Y 轴朝向逐平台不同；导数指令在非均匀控制流里未定义）见 UI-PanelSDF.cginc。
            float MemberSd(int i, float2 p, out float2 n)
            {
                float4 rect = _WeldRects[i];
                float2 q = p - rect.xy;
                PuguiQuad quad = PuguiResolveQuad(q, rect.zw, _WeldCornerKind[i], _WeldCornerW[i],
                                                  _WeldCornerH[i], _WeldCornerFillet[i],
                                                  _WeldDepths[i].y, _WeldDepths[i].z);
                n = PuguiPanelNormal(q, rect.zw, quad);
                return PuguiSdPanel(q, rect.zw, quad);
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
                float s = 1.0 / k;

                // 分支外求导：分支条件逐像素成立，非均匀控制流里的 ddx/ddy 未定义。对**局部坐标**
                // 求导（不是对 d），拿到的就是「一屏幕像素等于多少画布单位」，与 SDF 的梯度是否
                // 恰好为 1 无关 —— 角解算在退化处并不保证这一点。取两轴的 RMS，与光栅 Y 轴朝向
                // 无关，跨平台一致。
                float2 dpdx = ddx(p);
                float2 dpdy = ddy(p);
                float unitsPerPixel = max(sqrt(0.5 * (dot(dpdx, dpdx) + dot(dpdy, dpdy))), 1e-5);
                // 0 是合法的 seam，意思是「本机能画的最锐」：兜到两个设备像素，台阶永远不会细到
                // 消失，也不会因为一个 0 除数炸掉梯度。
                float seam = max(_GlassA.y, 2.0 * unitsPerPixel);

                // 一趟循环把四件事一起折叠出来。角解算比圆角盒贵得多，所以每个成员的 SDF 与法线
                // 只求一次 —— 不再像以前那样为了省 indexable temp 而重算第二遍。
                //
                //  (1) 融合形状 d：polynomial smooth-min；
                //  (2) 外斜面用的逐像素 depth 与法线：按「离本块表面多近」的 softmax 权重混合。
                //      基准 dmin 边走边降，累加器随之按 exp((新-旧)/k) 缩放（在线 softmax），
                //      指数永不溢出，且不必先跑一遍求最小值；
                //  (3) 厚度高度图 h 与 tint：以本块轮廓为中心、宽 seam 的软覆盖 r，按声明顺序
                //      source-over 折叠，最后除以覆盖率 cov 归一。归一是关键 —— 单块时 h 恒等于
                //      它自己的 depth，外轮廓处不会凭空长出一道坡；
                //  (4) h 的解析梯度：∇r = 6u(1−u)·(−n/seam)（u 被 saturate 夹住的两端自动为 0），
                //      折叠式与 h 同构，最后按商法则合成 ∇h。
                float d = 1e6;
                float dmin = 1e6;
                float wsum = 0.0;
                float depth = 0.0;
                float2 nrm = 0.0;
                float accH = 0.0;
                float cov = 0.0;
                float4 accT = 0.0;
                float2 gradH = 0.0;
                float2 gradCov = 0.0;
                for (int j = 0; j < _WeldCount; j++)
                {
                    float2 nj;
                    float dj = MemberSd(j, p, nj);
                    float4 rect = _WeldRects[j];
                    float depthJ = _WeldDepths[j].x;
                    float tj = saturate((p.y - (rect.y - rect.w)) / max(2.0 * rect.w, 1e-4));
                    float4 tintJ = lerp(_WeldTintBottom[j], _WeldTintTop[j], tj);

                    d = (j == 0) ? dj : PuguiSmin(d, dj, k);

                    float newMin = min(dmin, dj);
                    float rescale = (j == 0) ? 0.0 : exp((newMin - dmin) * s);
                    wsum *= rescale;
                    depth *= rescale;
                    nrm *= rescale;
                    dmin = newMin;
                    float w = exp(-(dj - dmin) * s);
                    wsum += w;
                    depth += depthJ * w;
                    // 融合形状的法线 = 各块法线按同一组权重的混合。这正是 smin 对梯度做的事，
                    // 于是焊缝处法线平滑过渡、高光自然绕着融合后的外轮廓走。
                    nrm += nj * w;

                    float u = saturate(0.5 - dj / seam);
                    float r = u * u * (3.0 - 2.0 * u);
                    float2 gr = (6.0 * u * (1.0 - u)) * (-nj / seam);
                    gradH   = (1.0 - r) * gradH   + (depthJ - accH) * gr;
                    gradCov = (1.0 - r) * gradCov + (1.0 - cov) * gr;
                    accH = lerp(accH, depthJ, r);
                    accT = lerp(accT, tintJ, r);
                    cov  = lerp(cov, 1.0, r);
                }
                float inv = 1.0 / max(wsum, 1e-5);
                depth *= inv;
                float nlen = length(nrm);
                float2 fusedNormal = nlen < 1e-6 ? float2(0.0, 1.0) : nrm / nlen;

                float invCov = 1.0 / max(cov, 1e-4);
                float4 tint = accT * invCov;
                // 商法则。所有成员之外 cov→0，此处 inside 也是 0，台阶项整个被乘掉。
                float2 heightGrad = (gradH * cov - accH * gradCov) * invCov * invCov;

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

                // 台阶的坡面：法线朝下坡（从厚块指向薄块），强度就是坡度本身 —— 厚度相同时
                // heightGrad 严格为零，等厚的组一个像素都不变。
                float slope = length(heightGrad);
                float stepAmount = saturate(slope);
                float2 stepNormal = slope < 1e-5 ? float2(0.0, 1.0) : -heightGrad / slope;

                float4 base = float4(0.0, 0.0, 0.0, 0.0);
                if (_PUGUI_GlassBackdropAvailable > 0.5 && inside > 0.0)
                {
                    float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                    float2 n = fusedNormal;

                    float band = depth > 0.0 ? saturate(1.0 + d / depth) * inside : 0.0;
                    float bevel = band * band;

                    float2 offset = n * bevel * (depth / unitsPerPixel) * 0.5 / _ScreenParams.xy;
                    // 台阶也折射：真玻璃的厚度落差会把背景折一下，这一步正是「一道台阶」与
                    // 「画了条线」的区别。位移峰值约半个 seam，与外斜面同一约定；色散那三次
                    // 采样自然连它一起分开，不多花一次纹理采样。
                    offset += stepNormal * stepAmount * (seam / unitsPerPixel) * 0.5 / _ScreenParams.xy;

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

                    // 厚度台阶上的高光：与外斜面同一条公式，只是作用在高度图的坡面上。高出的
                    // 一方朝光的那一侧亮起来，背光侧留 0.35 的弱补光 —— 熔接玻璃上那道细线。
                    float ndlStep = dot(stepNormal, lightDir);
                    float specStep = pow(saturate(ndlStep), 4.0)
                                   + 0.35 * pow(saturate(-ndlStep), 4.0);
                    rgb += specStep * stepAmount * intensity * inside;

                    // 接触阴影：焊缝里侧压暗一点点，让两块的厚度差读得出来。没有它，融合处会平得
                    // 像同一块板，主次关系就丢了。
                    rgb *= 1.0 - crease * 0.45 * inside;

                    rgb += (IGNoise(uv * _ScreenParams.xy) - 0.5) * noise;

                    base = float4(rgb, inside);
                }

                tint.a *= inside;
                float4 col = PuguiOver(tint, base);

                // 内发光：外发光的镜像 —— 画在形状内侧、压在填充之上。
                // 排在外发光之前，让外发光的 under 合成看到「填充 + 内发光」这一个完整实心体
                // （两者除 AA 那一像素外并不相交，所以顺序只影响那一像素）。
                col = PuguiApplyInnerGlow(col, d, inside, _InnerGlowSize, _InnerGlowColor);

                // 外发光：仅在形状外侧衰减。
                col = PuguiApplyOuterGlow(col, d, inside, _GlowSize, _GlowColor);

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
