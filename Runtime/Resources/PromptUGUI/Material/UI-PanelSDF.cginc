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

#endif // PROMPTUGUI_PANEL_SDF_INCLUDED
