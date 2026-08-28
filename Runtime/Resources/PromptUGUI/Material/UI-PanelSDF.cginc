// 程序化面板的共享形状层：UI-ProceduralPanel 与 UI-GlassPanel 都 include 这份，
// 保证不透明面板和玻璃面板的圆角、切角、pill / hexagon 解算、抗锯齿宽度逐像素完全一致 ——
// 两块并排、只有填充方式不同的面板，边缘必须严丝合缝对齐。
#ifndef PROMPTUGUI_PANEL_SDF_INCLUDED
#define PROMPTUGUI_PANEL_SDF_INCLUDED

// 逐角处理方式，必须与 PromptUGUI.Parser.CornerKind 的数值一致。
// 判定一律取中点阈值：uniform 里是精确的 0/1/2，但浮点等值比较没有必要的脆弱。
#define PUGUI_KIND_ROUND       0.0
#define PUGUI_KIND_CUT         1.0
#define PUGUI_KIND_IS_ROUND(k) ((k) < 0.5)
#define PUGUI_KIND_IS_NOTCH(k) ((k) > 1.5)

// 整形哨兵，必须与 PromptUGUI.Parser.PanelShape 的数值一致。
#define PUGUI_SHAPE_IS_PILL(s)    ((s) > 0.5)
#define PUGUI_SHAPE_IS_HEXAGON(s) ((s) > 1.5)

// 本片元所属那个角的处理方式与两轴尺寸（画布单位）。
struct PuguiCorner
{
    float  kind;
    float2 size;   // x = 沿水平边的伸出量，y = 沿垂直边的伸出量；round 时两者相等
};

// 折叠帧里的角象限距离场。u = abs(p) - b，角在原点、内部为负。
// 折叠之后它同时就是矩形本身的 SDF —— 另外三个角的特征都在别的象限，够不着这里。
float PuguiSdQuadrant(float2 u)
{
    return min(max(u.x, u.y), 0.0) + length(max(u, 0.0));
}

// 折叠帧里的象限外法线（内部退化成「离哪条边最近就朝哪边」）。
float2 PuguiSdQuadrantNormal(float2 u)
{
    return (max(u.x, u.y) > 0.0)
        ? normalize(max(u, 0.0) + 1e-6)
        : ((u.x > u.y) ? float2(1.0, 0.0) : float2(0.0, 1.0));
}

// iq 的圆角，写在折叠帧里：q = u + r 与原来的 abs(p) - b + radius 逐字等价。
float PuguiSdRoundCorner(float2 u, float r)
{
    float2 q = u + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

// 斜切角：矩形与一条斜边半平面求交。斜边从 A = (-W, 0) 连到 B = (0, -H)。
float PuguiSdCutCorner(float2 u, float2 s)
{
    // 尺寸退化到 0 就什么也没切掉，顺带挡住下面 rsqrt 的零长度边。
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrant(u);

    // 斜边外法线 n = (H, W)/L，过 A 点，于是 d = dot(u, n) + W*H/L。
    float invL = rsqrt(s.x * s.x + s.y * s.y);
    float dLine = (u.x * s.y + u.y * s.x + s.x * s.y) * invL;
    float d = max(PuguiSdQuadrant(u), dLine);

    // 内部：两个精确半空间取 max 就是精确解（离最近那条边的距离）。
    if (d <= 0.0) return d;

    // 外部：斜边两个端点之外的楔形区里，max(投影, 投影) 会低估到顶点的距离（45° 切角最多
    // 8%）—— 直接后果是外发光在「斜边与直边相接」的那两个点上鼓出去一圈。改成量到斜边
    // **线段**，直边只在它确实还存在的那一侧参与。
    float2 w = u - float2(-s.x, 0.0);
    float2 e = float2(s.x, -s.y);
    float dOut = length(w - e * saturate(dot(w, e) / dot(e, e)));
    // 外部且 u.x <= -W 时 u.y 必然为正（否则就在形状内），u.y 就是到顶边的距离；右边同理。
    if (u.x <= -s.x) dOut = min(dOut, u.y);
    if (u.y <= -s.y) dOut = min(dOut, u.x);
    return dOut;
}

// 方形缺口：矩形挖掉角上一块 W×H。
float PuguiSdNotchCorner(float2 u, float2 s)
{
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrant(u);

    // 这个角剩下的部分是两个象限的并集：一个止于 x = -W，一个止于 y = -H。
    // 两个精确距离场取 min 就是精确的外部距离 —— 包括缺口造出来的那两个凸顶点。
    float2 v1 = u - float2(-s.x, 0.0);
    float2 v2 = u - float2(0.0, -s.y);
    float d = min(PuguiSdQuadrant(v1), PuguiSdQuadrant(v2));
    if (d >= 0.0) return d;

    // 内部：并集取 min 在缺口的凹顶点处最多浅 sqrt(2) 倍，内描边会正好在那个角上鼓一块。
    // 「矩形减去被挖掉的那块」在内部是精确的（两个场各自精确、相减处两条边就是真边界）。
    float2 half = s * 0.5;
    float2 q = abs(u + half) - half;
    float dRect = min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
    return max(PuguiSdQuadrant(u), -dRect);
}

float2 PuguiSdCutNormal(float2 u, float2 s)
{
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrantNormal(u);

    float invL = rsqrt(s.x * s.x + s.y * s.y);
    float dLine = (u.x * s.y + u.y * s.x + s.x * s.y) * invL;
    // 三条半平面里最靠近 0 的那条就是最近的特征 —— 内外都成立，因为 SDF 本身就是它们的 max。
    if (dLine >= max(u.x, u.y)) return float2(s.y, s.x) * invL;
    return (u.x > u.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
}

float2 PuguiSdNotchNormal(float2 u, float2 s)
{
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrantNormal(u);

    // 缺口引入的两条边都是轴对齐的，法线仍然只有两种；变的是这个片元归哪个象限。
    // 外部按更近的那个选（正确），内部按更深的那个选 —— 边缘打光只作用在边界附近的一条
    // 窄带里，内部深处选谁都照不到。
    float2 v1 = u - float2(-s.x, 0.0);
    float2 v2 = u - float2(0.0, -s.y);
    return (PuguiSdQuadrant(v1) < PuguiSdQuadrant(v2))
        ? PuguiSdQuadrantNormal(v1)
        : PuguiSdQuadrantNormal(v2);
}

// 挑出本片元所属的角，解算整形哨兵，再逐轴 clamp。
//
// pill / hexagon 在 GPU 上解算而不是在 C# 里：它们依赖 rect 尺寸，提前算掉会让同一 style
// 的不同尺寸面板拿到不同材质参数，白白丢掉材质共享（见 ProceduralMaterialCache）。
PuguiCorner PuguiResolveCorner(float2 p, float2 b, float4 kinds, float4 widths, float4 heights,
                               float shape, float hexW)
{
    // xyzw = top-left, top-right, bottom-right, bottom-left（CSS border-radius 顺序）。
    bool right = p.x > 0.0;
    bool top = p.y > 0.0;
    float2 kSide = right ? float2(kinds.y, kinds.z) : float2(kinds.x, kinds.w);
    float2 wSide = right ? float2(widths.y, widths.z) : float2(widths.x, widths.w);
    float2 hSide = right ? float2(heights.y, heights.z) : float2(heights.x, heights.w);

    PuguiCorner c;
    c.kind = top ? kSide.x : kSide.y;
    c.size = float2(top ? wSide.x : wSide.y, top ? hSide.x : hSide.y);

    if (PUGUI_SHAPE_IS_HEXAGON(shape))
    {
        // 左右两侧收成尖：每个角都是一次斜切，纵向伸出量恰好取半高，于是同一侧的两刀在
        // 中线上相交成一个点。尺寸一变自动跟随，作者不需要手算。
        c.kind = PUGUI_KIND_CUT;
        c.size = float2((hexW > 0.0) ? hexW : b.y, b.y);
    }
    else if (PUGUI_SHAPE_IS_PILL(shape))
    {
        c.kind = PUGUI_KIND_ROUND;
        float pillRadius = min(b.x, b.y);
        c.size = float2(pillRadius, pillRadius);
    }

    // round 只有一个半径，仍按短边 clamp（椭圆角不是这个属性的语义）；
    // cut / notch 两轴独立，各自 clamp 到半宽 / 半高，于是同一条边上的两个角永不穿越。
    float shortest = min(b.x, b.y);
    float radius = min(max(c.size.x, 0.0), shortest);
    c.size = PUGUI_KIND_IS_ROUND(c.kind)
        ? float2(radius, radius)
        : clamp(c.size, float2(0.0, 0.0), b);
    return c;
}

// 面板形状的 SDF。返回值：负=内部，正=外部，绝对值≈到边界的距离（画布单位）。
float PuguiSdPanel(float2 p, float2 b, PuguiCorner c)
{
    float2 u = abs(p) - b;
    if (PUGUI_KIND_IS_ROUND(c.kind)) return PuguiSdRoundCorner(u, c.size.x);
    if (PUGUI_KIND_IS_NOTCH(c.kind)) return PuguiSdNotchCorner(u, c.size);
    return PuguiSdCutCorner(u, c.size);
}

// 面板形状的**解析**外法线（画布空间，+Y = 界面正上方）。
//
// 刻意不用 ddx/ddy(d) 求梯度，两个理由都是正确性而非性能：
// 1) lightAngle 是画布空间的概念（0 = 正上方）。光栅空间 Y 轴朝向逐平台不同
//    （D3D/Metal 向下、GL/GLES/WebGL 向上），用屏幕导数当法线会让边缘高光和折射方向
//    在 GL 目标上整个上下翻转 —— 同一份 XML 在 WebGL 构建里长得不一样，且不报任何错。
// 2) 导数指令在非均匀控制流里是未定义行为，而调用点在逐像素的 `inside > 0` 分支内。
//
// 解析式顺带比中心差分更省：直接对形状求导，没有额外的 SDF 求值。
float2 PuguiPanelNormal(float2 p, float2 b, PuguiCorner c)
{
    float2 u = abs(p) - b;
    float2 g;
    if (PUGUI_KIND_IS_ROUND(c.kind)) g = PuguiSdQuadrantNormal(u + c.size.x);
    else if (PUGUI_KIND_IS_NOTCH(c.kind)) g = PuguiSdNotchNormal(u, c.size);
    else g = PuguiSdCutNormal(u, c.size);

    // abs() 把四个象限折到第一象限求解，这里再折回去。
    float2 s = float2(p.x >= 0.0 ? 1.0 : -1.0, p.y >= 0.0 ? 1.0 : -1.0);
    return normalize(s * g);
}

// ---- 以下两个只剩 weld 融合面在用 -------------------------------------------------------
// GlassGroupPanel 把成员形状打包成逐成员的 float4 半径数组再做 smooth-union，角部处理在那条
// 路径上是降级成同 W 圆角的（见 PUI-WELD-CORNER），所以它要的仍是纯圆角版本。

float PuguiSdRoundBox(float2 p, float2 b, float4 r)
{
    // 象限选角：右半区取 (TR, BR)，左半区取 (TL, BL)；再按上下二选一。
    float2 side = (p.x > 0.0) ? float2(r.y, r.z) : float2(r.x, r.w);
    float radius = (p.y > 0.0) ? side.x : side.y;
    return PuguiSdRoundCorner(abs(p) - b, radius);
}

float2 PuguiSdNormal(float2 p, float2 b, float4 r)
{
    float2 side = (p.x > 0.0) ? float2(r.y, r.z) : float2(r.x, r.w);
    float radius = (p.y > 0.0) ? side.x : side.y;
    float2 g = PuguiSdQuadrantNormal(abs(p) - b + radius);
    float2 s = float2(p.x >= 0.0 ? 1.0 : -1.0, p.y >= 0.0 ? 1.0 : -1.0);
    return normalize(s * g);
}

// 直 alpha 的 source-over 合成。
float4 PuguiOver(float4 src, float4 dst)
{
    float a = src.a + dst.a * (1.0 - src.a);
    float3 rgb = (src.rgb * src.a + dst.rgb * dst.a * (1.0 - src.a)) / max(a, 1e-5);
    return float4(rgb, a);
}

// ---- 两层发光 ----
//
// 三个面板 shader（不透明 / 玻璃 / 融合）逐字共用这两个函数，而不是各抄一份四行 ——
// 内外发光必须是**同一条曲线的镜像**：glow 与 innerGlow 等宽时，读起来要是跨越边缘的一整圈
// 对称光晕，而不是两种手感的光拼在一起。三份复制迟早会漂移成后者。
//
// size == 0 必须整段跳过。这是 uniform 分支，全体 fragment 走同一条路径，开销可忽略。

// 外发光：只在形状**外侧**（d > 0）衰减，画在已有内容的**下面**。
// 平方让边缘更快收住，更像光晕而不是色块。
float4 PuguiApplyOuterGlow(float4 col, float d, float inside, float size, float4 color)
{
    if (size <= 0.0) return col;
    float g = saturate(1.0 - d / size);
    color.a *= g * g * (1.0 - inside);
    return PuguiOver(col, color);
}

// 内发光：只在形状**内侧**（-size < d < 0）衰减，画在填充之**上**。
//
// 起点是形状边缘 d = 0，不是描边内沿（Photoshop Inner Glow 语义）：库里最常见的细半透明描边
// （white/0.4 之流）下，发光会延续到描边底下、边缘无缝；改成从描边内沿量，那个常见配置里
// 描边带就会比紧邻的发光暗，边缘冒出一条 1px 暗缝。粗不透明描边会盖掉最外几 px，作者把数值
// 调大即可 —— 用一个罕见配置的精度换一个常见配置的正确。
float4 PuguiApplyInnerGlow(float4 col, float d, float inside, float size, float4 color)
{
    if (size <= 0.0) return col;
    float g = saturate(1.0 + d / size);
    color.a *= g * g * inside;
    return PuguiOver(color, col);
}

// ---- 装饰原语（<Decor>）的形状层 ----
//
// 三个形状都在**规范朝向**里定义：bracket 抱住自己包围盒的左上角，tick 尖端朝下。
// 其余槽位（右上角 / 顶边 / 左右边…）不靠材质参数翻转，而是由 DecorPanel 在
// **顶点**里把局部坐标翻/转到规范朝向 —— 材质因此与槽位无关，四个角括号共用一份材质、
// 能合到同一个 draw call 里。参数进材质、朝向进顶点，与面板那套分工同源。

// 轴对齐矩形 SDF（iq）。c = 中心，h = 半尺寸。
float PuguiSdBoxAt(float2 p, float2 c, float2 h)
{
    float2 q = abs(p - c) - h;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
}

// L 形折线（角括号），抱住包围盒左上角。b = 包围盒半尺寸，t = 笔画宽。
// 两条臂各是一个矩形，取 min = 并集；交角处自然接成方头，不需要额外处理。
float PuguiSdBracket(float2 p, float2 b, float t)
{
    float w = clamp(t, 0.0, 2.0 * min(b.x, b.y));
    float half_w = 0.5 * w;
    // 横臂贴上沿铺满整宽，竖臂贴左沿铺满整高。
    float dh = PuguiSdBoxAt(p, float2(0.0, b.y - half_w), float2(b.x, half_w));
    float dv = PuguiSdBoxAt(p, float2(-b.x + half_w, 0.0), float2(half_w, b.y));
    return min(dh, dv);
}

// 等腰三角形（指示三角），底边贴上沿、尖端朝下。b = 包围盒半尺寸。
// 半平面交：底边 + 左右两条斜边，外区距离取到最近斜边线段的精确距离，
// 这样 glow 在尖端外侧不会被半平面交低估成一圈方晕。
float PuguiSdTick(float2 p, float2 b)
{
    float2 apex = float2(0.0, -b.y);
    float2 left = float2(-b.x, b.y);
    float2 right = float2(b.x, b.y);

    // 到三条边的有符号半平面距离（外法线朝外为正）。
    float2 el = apex - left;
    float dl = (p.x - left.x) * el.y - (p.y - left.y) * el.x;
    float2 er = right - apex;
    float dr = (p.x - apex.x) * er.y - (p.y - apex.y) * er.x;
    float2 nl = normalize(el);
    float2 nr = normalize(er);
    dl /= max(length(el), 1e-4);
    dr /= max(length(er), 1e-4);
    float dtop = p.y - b.y;

    float inside = max(max(dl, dr), dtop);

    // 内部（inside<0）半平面交即精确距离；外部再补上到顶点的精确项。
    float2 qa = p - apex;
    float2 ql = p - left;
    float2 qr = p - right;
    float dseg = min(min(length(ql - nl * clamp(dot(ql, nl), 0.0, length(el))),
                         length(qa - nr * clamp(dot(qa, nr), 0.0, length(er)))),
                     length(float2(clamp(p.x, -b.x, b.x), b.y) - p));
    return inside < 0.0 ? inside : dseg;
}

#endif // PROMPTUGUI_PANEL_SDF_INCLUDED
