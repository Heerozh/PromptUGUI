// 融合玻璃组：把若干块玻璃当成一整片厚薄不均的连续玻璃画出来。
//
// 为什么不是「各画各的再拼」：两块玻璃相邻时，用描边或缝隙去分割都很难看。这里对成员的 SDF 做
// polynomial smooth-min，交界处自然长出圆角过渡；而「哪块是主、哪块是次」改由**厚度**表达。
//
// 厚度按子级声明顺序 source-over 折叠（后涂的覆盖先涂的，不累加）。台阶不是对折叠结果求导，而是
// **逐条轮廓**累加：每块在自己的轮廓上从身下的高度跨到自己的 depth，跨越发生在一条 seam 宽的斜坡
// 上，落在轮廓外（seam > 0）还是轮廓内（seam < 0）由符号选，两者是同一条三次曲线的镜像。于是
//   · 单块：身下没有材料（闸门为 0），外轮廓那一圈仍只由既有斜面负责；
//   · 相接等厚：厚度差为零，交界不出沟；
//   · 相接异厚：台阶在交界线上；
//   · 重叠：台阶贴着**后声明块的轮廓** —— 厚盖薄是凸台阶，薄盖厚是凹槽；
//   · 上层块的轮廓跑出下层材料之外：闸门跟着覆盖率在 seam 内淡出，台阶柔和收尾而不是被硬切
//     （那里上层块的边已经是整片玻璃的外轮廓，本来就该交给外斜面）。
// 剖面刻意单侧、最陡处在轮廓上：高光 = 坡度，于是它是「贴着上层块轮廓的一道亮边 + 渐隐的柔光」，
// 而不是横跨整个过渡带的一条均匀宽带（对称 smoothstep 的导数就是那样，宽度只随 seam 走、depth
// 只改亮度）。亮边的位置与符号无关（两条镜像剖面都在轮廓上最陡），符号只决定柔光落在哪一侧。
// 坡面按解析梯度求法线，用与外斜面同一套公式打光、并把背景轻微折一下 —— 这就是真实熔接玻璃上
// 那道细高光。厚度相同时梯度严格为零，等厚的组逐像素不变。
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
                // seam 的**符号**选边：正 = 斜坡落在上层块轮廓之外（柔光洒在它周围），
                // 负 = 落在轮廓之内（柔光留在块自己身上）。两者是同一条曲线的镜像，见下面的循环。
                // 0 合法，意思是「本机能画的最锐」：量值兜到两个设备像素，台阶永远不会细到消失，
                // 也不会因为一个 0 除数炸掉梯度。逐像素同值的分支，开销可忽略。
                float inward = _GlassA.y < 0.0 ? 1.0 : 0.0;
                float seam = max(abs(_GlassA.y), 2.0 * unitsPerPixel);

                // 一趟循环把四件事一起折叠出来。角解算比圆角盒贵得多，所以每个成员的 SDF 与法线
                // 只求一次 —— 不再像以前那样为了省 indexable temp 而重算第二遍。
                //
                //  (1) 融合形状 d：polynomial smooth-min；
                //  (2) 外斜面用的逐像素 depth 与法线：按「离本块表面多近」的 softmax 权重混合。
                //      基准 dmin 边走边降，累加器随之按 exp((新-旧)/k) 缩放（在线 softmax），
                //      指数永不溢出，且不必先跑一遍求最小值；
                //  (3) 厚度与 tint：软覆盖 c = 块内 1、轮廓外 seam 内 (1−t)³、再外 0，按声明顺序
                //      source-over 折叠；
                //  (4) 台阶梯度：**逐条轮廓累加**，不是对折叠出来的高度场求导。每条轮廓贡献
                //      「它与身下材料的厚度差 × 剖面导数」，并乘一个闸门 = 折叠到它之前的覆盖率
                //      （身下有多少材料可踩）。闸门只做乘法、不参与求导，这是关键：对高度场求导
                //      会被商法则把**下层块自己轮廓上的覆盖率梯度**带进来，在上层块覆盖率 < 1 的
                //      地方（向内剖面的整条斜坡带）漏成一条沿下层块边缘的假线，并在下层材料到头
                //      处硬切。乘法闸门两个毛病都没有：假线不存在，材料到头时台阶在 seam 内淡出。
                float d = 1e6;
                float dmin = 1e6;
                float wsum = 0.0;
                float depth = 0.0;
                float2 nrm = 0.0;
                float accH = 0.0;
                float cov = 0.0;
                float4 accT = 0.0;
                float2 stepGrad = 0.0;
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

                    // 本块这条轮廓的台阶。斜坡剖面把距离按符号翻一下 —— 向外与向内是同一条三次
                    // 曲线的镜像，两者都在**轮廓上**最陡、往远端三次方收掉，所以亮边永远贴着轮廓，
                    // 变的只是柔光落在哪一侧；seam 是那道柔光能伸多远。
                    //
                    // 强度 = 厚度差 × 闸门，而 (depthJ − accH/cov)·cov 就是 depthJ·cov − accH，
                    // 于是连除法都不用。折叠前取值：cov / accH 此刻正是「身下」的覆盖与高度。
                    // 单块（cov = 0）与等厚（depthJ·cov == accH）都严格得零，无需特判。
                    float dRamp = inward > 0.5 ? -dj : dj;
                    float skirt = 1.0 - saturate(dRamp / seam);
                    float onRamp = (dRamp > 0.0 && dRamp < seam) ? 1.0 : 0.0;
                    stepGrad += (depthJ * cov - accH)
                              * (onRamp * -3.0 * skirt * skirt / seam) * nj;

                    // 折叠用的软覆盖恒取**向外**那条：一块材料一直实到自己的轮廓、再往外软收。
                    // 这正是让邻块的边不会漏进台阶里的那一半原因（另一半是上面的乘法闸门）。
                    float tc = saturate(dj / seam);
                    float sc = 1.0 - tc;
                    float c = sc * sc * sc;
                    accH = lerp(accH, depthJ, c);
                    accT = lerp(accT, tintJ, c);
                    cov  = lerp(cov, 1.0, c);
                }
                float inv = 1.0 / max(wsum, 1e-5);
                depth *= inv;
                float nlen = length(nrm);
                float2 fusedNormal = nlen < 1e-6 ? float2(0.0, 1.0) : nrm / nlen;

                float4 tint = accT * (1.0 / max(cov, 1e-4));
                float2 heightGrad = stepGrad;

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

                // 台阶的坡面：法线朝下坡（从厚块指向薄块），强度是坡度经 Reinhard 压缩 ——
                // 越陡越亮，但永不封顶成一条平顶亮带（saturate 会），剖面始终是柔的。
                // 厚度相同时 heightGrad 严格为零，等厚的组一个像素都不变。
                float slope = length(heightGrad);
                float stepAmount = slope / (1.0 + slope);
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
