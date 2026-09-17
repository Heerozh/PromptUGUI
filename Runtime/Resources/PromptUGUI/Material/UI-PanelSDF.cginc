// 程序化面板的共享形状层：UI-ProceduralPanel / UI-GlassPanel / UI-GlassGroup 都 include 这份，
// 保证不透明面板、玻璃面板和融合玻璃组的圆角、切角、pill / hexagon 解算、抗锯齿宽度逐像素完全
// 一致 —— 两块并排、只有填充方式不同的面板，边缘必须严丝合缝对齐；融合组里一块玻璃的斜边，也得
// 和它单独画时是同一条。
#ifndef PROMPTUGUI_PANEL_SDF_INCLUDED
#define PROMPTUGUI_PANEL_SDF_INCLUDED

// 逐角处理方式，必须与 PromptUGUI.Parser.CornerKind 的数值一致。
// 判定一律取中点阈值：uniform 里是精确的 0/1/2，但浮点等值比较没有必要的脆弱。
#define PUGUI_KIND_ROUND       0.0
#define PUGUI_KIND_CUT         1.0
#define PUGUI_KIND_IS_ROUND(k) ((k) < 0.5)
#define PUGUI_KIND_IS_CUT(k)   ((k) > 0.5 && (k) < 1.5)
#define PUGUI_KIND_IS_NOTCH(k) ((k) > 1.5)

// 整形哨兵，必须与 PromptUGUI.Parser.PanelShape 的数值一致。
#define PUGUI_SHAPE_IS_PILL(s)    ((s) > 0.5)
#define PUGUI_SHAPE_IS_HEXAGON(s) ((s) > 1.5)

// 一个角的原始数据：uniform 取出、整形哨兵已解算、尺寸未钳制。
struct PuguiRawCorner
{
    float  kind;
    float2 size;     // round：(半径, 半径)；cut / notch：(W, H)
    float  fillet;
};

// 本片元所属象限解算后的全部几何（spec 2026-08-29 §6.2 / 第二部分 §15）。
struct PuguiQuad
{
    float  kind;      // 自己的角：round / cut / notch（方角 = round 且 roundR 为 0）
    float  r;         // 本象限的收缩半径（fillet），0 = 无。> 0 时下面的尺寸都在收缩帧（盒子 b − r）里
    float2 bp;        // 收缩后的半尺寸 b − r
    float  roundR;    // round：半径
    float2 size;      // cut：斜边截距；notch：缺口尺寸
    float  vSpill;    // 竖向邻居的斜边越过中线进了本象限
    float2 vNear;     // 它在本象限里落地的顶点（竖边上；越过角时在顶边上）
    float2 vFar;      // 它在邻居那端的顶点
    float  hSpill;    // 横向邻居的，镜像
    float2 hNear;
    float2 hFar;
    float  legacy;    // 1 = 无 fillet 无溢出：走第一部分之前的函数原样
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
    // 别叫 half:它在 HLSL 里是类型名,Unity 在桌面 Vulkan / Metal 上还会 #define half float,
    // 移动端则 #define half min16float —— 那几个平台上这行会直接编译失败(GLCore/D3D 下 FXC 恰好放过)。
    float2 hs = s * 0.5;
    float2 q = abs(u + hs) - hs;
    float dRect = min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
    return max(PuguiSdQuadrant(u), -dRect);
}

float2 PuguiSdCutNormal(float2 u, float2 s)
{
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrantNormal(u);

    float invL = rsqrt(s.x * s.x + s.y * s.y);
    float dLine = (u.x * s.y + u.y * s.x + s.x * s.y) * invL;
    float dEdge = max(u.x, u.y);

    // 内侧：三条半平面里最靠近 0 的那条就是最近的特征，SDF 本身就是它们的 max。
    if (max(dEdge, dLine) <= 0.0)
    {
        if (dLine >= dEdge) return float2(s.y, s.x) * invL;
        return (u.x > u.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
    }

    // 外侧：与 PuguiSdCutCorner 同一组候选，谁的距离赢就取谁的方向。斜边线段两端的楔形区里最近点
    // 是端点，方向从端点指向片元 —— 倒圆之后这片区域正是圆弧带，玻璃打光靠它连续旋转而不是硬切。
    float2 w = u - float2(-s.x, 0.0);
    float2 e = float2(s.x, -s.y);
    float2 toSeg = w - e * saturate(dot(w, e) / dot(e, e));
    float best = length(toSeg);
    float2 n = normalize(toSeg + 1e-6);
    if (u.x <= -s.x && u.y < best) { best = u.y; n = float2(0.0, 1.0); }
    if (u.y <= -s.y && u.x < best) { n = float2(1.0, 0.0); }
    return n;
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

// ---- 倒圆缺口（spec 2026-08-29 §5.4）------------------------------------------------------------
//
// opening 只圆凸顶点：收缩把凹角撑成弧，膨胀又把它缩回尖角。所以凹角走另一条路 —— 缺口腔本身换成
// 内角倒圆的圆角矩形。这里已在收缩帧（u 是 u + r）：缺口仍是 W×H（两壁与盒子各内移 r，相对位置
// 不变），凹弧半径是 2r（凹弧被收缩放大一个 r，膨胀时缩回 r）。r == 0 走上面未倒圆的
// PuguiSdNotchCorner —— 两者数学等价但不保证逐位相等，旧路径必须原样保留。
float PuguiSdNotchCornerFilleted(float2 u, float2 s, float r)
{
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrant(u);

    // 缺口让位（PuguiResolveQuad 已把 s 钳进收缩盒），凹弧还得装进缺口里。
    float rc = min(2.0 * r, min(s.x, s.y));

    // 缺口腔 = 以内角为原点的无限象限 {t >= 0}，原点处倒圆 rc。
    float2 t = u + s;
    float dBite = PuguiSdRoundCorner(-t, rc);
    float dBox = PuguiSdQuadrant(u);

    // 盒内：材料（负）或腔内（正）。两个场各自精确、相减处两条壁与凹弧就是真边界；壁的无限延长段
    // 都在盒外，盒内的点到它们的垂足永远落在真壁上。
    if (dBox < 0.0) return max(dBox, -dBite);

    // 盒外：口沿两个凸顶点，沿用未倒圆版本已验证的双象限并集。凹弧只朝腔内，盒外的点永远离口沿
    // 顶点更近，它不需要参与。
    float2 v1 = u - float2(-s.x, 0.0);
    float2 v2 = u - float2(0.0, -s.y);
    return min(PuguiSdQuadrant(v1), PuguiSdQuadrant(v2));
}

float2 PuguiSdNotchFilletedNormal(float2 u, float2 s, float r)
{
    if (min(s.x, s.y) <= 0.0) return PuguiSdQuadrantNormal(u);

    float rc = min(2.0 * r, min(s.x, s.y));
    float2 t = u + s;
    float dBite = PuguiSdRoundCorner(-t, rc);
    float dBox = PuguiSdQuadrant(u);

    // max(dBox, -dBite) 的梯度：谁赢取谁。-dBite 对 u 的梯度 = 圆角象限场在 -t 处的梯度，
    // 两次取反抵消，就是 PuguiSdQuadrantNormal(-t + rc)：朝腔内 —— 材料在壁上、在凹弧上的外法线。
    if (dBox < 0.0)
        return (dBox >= -dBite) ? PuguiSdQuadrantNormal(u) : PuguiSdQuadrantNormal(-t + rc);

    return PuguiSdNotchNormal(u, s);
}

// ---- 象限解算（spec 2026-08-29 §6.2 / 第二部分 §14–§15）------------------------------------------
//
// 一个象限的几何由**三个角**联立决定：自己的角，加上两条边上的邻居 —— 邻居的 cut 越过中线时，
// 它的斜边就延伸进本象限（溢出）。钳制与收缩都是 uniforms + b 的确定函数，源象限与接收象限各自
// 算出同一条线，折叠帧在中线两侧仍然严丝合缝。

PuguiRawCorner PuguiPickCorner(float4 kinds, float4 widths, float4 heights, float4 fillets,
                               bool right, bool top, float2 b, float shape, float hexW)
{
    // xyzw = top-left, top-right, bottom-right, bottom-left（CSS border-radius 顺序）。
    float2 kSide = right ? float2(kinds.y, kinds.z) : float2(kinds.x, kinds.w);
    float2 wSide = right ? float2(widths.y, widths.z) : float2(widths.x, widths.w);
    float2 hSide = right ? float2(heights.y, heights.z) : float2(heights.x, heights.w);
    float2 fSide = right ? float2(fillets.y, fillets.z) : float2(fillets.x, fillets.w);

    PuguiRawCorner c;
    c.kind = top ? kSide.x : kSide.y;
    c.size = float2(top ? wSide.x : wSide.y, top ? hSide.x : hSide.y);
    c.fillet = top ? fSide.x : fSide.y;

    // pill / hexagon 在 GPU 上解算而不是在 C# 里：它们依赖 rect 尺寸，提前算掉会让同一 style
    // 的不同尺寸面板拿到不同材质参数，白白丢掉材质共享（见 ProceduralMaterialCache）。
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
        c.fillet = 0.0;
    }

    float shortest = min(b.x, b.y);
    if (PUGUI_KIND_IS_ROUND(c.kind))
    {
        // round 只有一个半径，按短边 clamp（椭圆角不是这个属性的语义）；圆角没有顶点可倒。
        float radius = min(max(c.size.x, 0.0), shortest);
        c.size = float2(radius, radius);
        c.fillet = 0.0;
    }
    else
    {
        c.size = max(c.size, float2(0.0, 0.0));
        if (min(c.size.x, c.size.y) <= 0.0)
        {
            // 尺寸为 0 的处理方式什么也没切：就是方角（与 CornerSpec.IsSquare 一致），fillet 一并作废。
            c.kind = PUGUI_KIND_ROUND;
            c.size = float2(0.0, 0.0);
            c.fillet = 0.0;
        }
    }
    return c;
}

// 一个角沿一条边的占用长度（§14.1）：round = 半径，cut / notch = 沿该边的伸出量，方角 = 0。
// 越线的 cut 在这里永远不是度量对象 —— 两边都越线时各退回半边。
float PuguiReach(PuguiRawCorner c, bool alongVertical, float2 b)
{
    if (PUGUI_KIND_IS_ROUND(c.kind)) return c.size.x;
    return min(alongVertical ? c.size.y : c.size.x, alongVertical ? b.y : b.x);
}

// §14.1 钳制。vn = 与 c 共用竖边的角，hn = 共用顶边的角。
//
// round / notch 仍是半边规则。cut 的伸出量可以越过中线，上限是「整边 − 邻居的占用长度」；两个角
// 都要越线时各退回半边（hexagon 的尖端就是这个情形）；邻居是 notch 时也退回半边（notch 象限不
// 接收溢出）。
float2 PuguiClampSize(PuguiRawCorner c, PuguiRawCorner vn, PuguiRawCorner hn, float2 b)
{
    if (!PUGUI_KIND_IS_CUT(c.kind)) return min(c.size, b);
    float2 s = min(c.size, 2.0 * b);
    if (s.y > b.y)
    {
        bool blocked = PUGUI_KIND_IS_NOTCH(vn.kind)
                    || (PUGUI_KIND_IS_CUT(vn.kind) && min(vn.size.y, 2.0 * b.y) > b.y);
        s.y = blocked ? b.y : min(s.y, 2.0 * b.y - PuguiReach(vn, true, b));
    }
    if (s.x > b.x)
    {
        bool blocked = PUGUI_KIND_IS_NOTCH(hn.kind)
                    || (PUGUI_KIND_IS_CUT(hn.kind) && min(hn.size.x, 2.0 * b.x) > b.x);
        s.x = blocked ? b.x : min(s.x, 2.0 * b.x - PuguiReach(hn, false, b));
    }
    return s;
}

// 斜边平行内移 r 后截距的缩放系数：k = 1 − r·(W+H−L)/(W·H)。k <= 0 表示斜边被两个圆弧吃光，
// 角退化成半径 r 的圆角 —— cut … rN 到普通圆角是一条连续谱（§5.2）。
float PuguiErodedK(float2 s, float r)
{
    if (min(s.x, s.y) <= 0.0) return 0.0;
    float L = length(s);
    return max(1.0 - r * (s.x + s.y - L) / (s.x * s.y), 0.0);
}

// 接收方沿一条边留给收缩后斜边的截距上限（§14.4）。n / sn = 接收方的角与它钳后的尺寸；
// sp = spiller 收缩后的截距（T' 规则要用它的斜率）。
float PuguiSpillRoom(PuguiRawCorner n, float2 sn, float r, float2 bp, bool alongVertical, float2 sp)
{
    float edge2 = 2.0 * (alongVertical ? bp.y : bp.x);
    if (PUGUI_KIND_IS_CUT(n.kind))
    {
        // 不越过邻居斜边的端点。
        float2 snp = sn * PuguiErodedK(sn, r);
        return edge2 - (alongVertical ? snp.y : snp.x);
    }
    float rp = PUGUI_KIND_IS_ROUND(n.kind) ? max(n.size.x - r, 0.0) : 0.0;
    if (rp > 0.0) return edge2 - rp;                       // 不碰弧：不做线–弧求交
    // 方角：允许越过角落到另一条边上（梯形的顶角），但落点不得越过对面的中线。
    float other = alongVertical ? bp.x : bp.y;
    float along = alongVertical ? sp.y : sp.x;
    float across = alongVertical ? sp.x : sp.y;
    return edge2 + other * along / max(across, 1e-4);
}

// 收缩后斜边的截距（spiller 自己的折叠帧），含 §5.3 / §14.4 的 pull-back：收缩后的顶点放不下时
// 按比例把斜边平行外移 —— 斜率不变、圆弧仍与两边相切。
// s：钳后的基础尺寸；spillV / spillH：基础尺寸是否越过中线；vn / hn：两条边上的邻居与它们钳后
// 的尺寸；r：本次收缩半径。
float2 PuguiErodeCut(float2 s, float r, float2 b, bool spillV, bool spillH,
                     PuguiRawCorner vn, float2 svn, PuguiRawCorner hn, float2 shn)
{
    if (min(s.x, s.y) <= 0.0) return float2(0.0, 0.0);
    if (r <= 0.0) return s;
    float2 sp = s * PuguiErodedK(s, r);
    if (min(sp.x, sp.y) <= 0.0) return float2(0.0, 0.0);
    float2 bp = b - r;
    float roomY = spillV ? PuguiSpillRoom(vn, svn, r, bp, true, sp) : bp.y;
    float roomX = spillH ? PuguiSpillRoom(hn, shn, r, bp, false, sp) : bp.x;
    float m = min(1.0, min(roomY / sp.y, roomX / sp.x));
    return sp * max(m, 0.0);
}

// 溢入线落进接收象限的折叠帧。sv = spiller 收缩后的截距（它自己的帧）。近端在本象限的竖边上，
// 越过角时落在顶边上；远端在邻居那端的顶点。横向版本是它的镜像。
void PuguiSpillSegmentV(float2 sv, float2 bp, out float2 nearPt, out float2 farPt)
{
    float edge2 = 2.0 * bp.y;
    nearPt = (sv.y <= edge2)
        ? float2(0.0, -(edge2 - sv.y))
        : float2(-sv.x * (sv.y - edge2) / sv.y, 0.0);
    farPt = float2(-sv.x, -edge2);
}

void PuguiSpillSegmentH(float2 sh, float2 bp, out float2 nearPt, out float2 farPt)
{
    float edge2 = 2.0 * bp.x;
    nearPt = (sh.x <= edge2)
        ? float2(-(edge2 - sh.x), 0.0)
        : float2(0.0, -sh.y * (sh.x - edge2) / sh.x);
    farPt = float2(-edge2, -sh.y);
}

PuguiQuad PuguiResolveQuad(float2 p, float2 b, float4 kinds, float4 widths, float4 heights,
                           float4 fillets, float shape, float hexW)
{
    bool right = p.x > 0.0;
    bool top = p.y > 0.0;
    PuguiRawCorner o  = PuguiPickCorner(kinds, widths, heights, fillets, right,  top,  b, shape, hexW);
    PuguiRawCorner vn = PuguiPickCorner(kinds, widths, heights, fillets, right,  !top, b, shape, hexW);
    PuguiRawCorner hn = PuguiPickCorner(kinds, widths, heights, fillets, !right, top,  b, shape, hexW);
    PuguiRawCorner dg = PuguiPickCorner(kinds, widths, heights, fillets, !right, !top, b, shape, hexW);

    float2 so  = PuguiClampSize(o,  vn, hn, b);
    float2 svn = PuguiClampSize(vn, o,  dg, b);
    float2 shn = PuguiClampSize(hn, dg, o,  b);
    float2 sdg = PuguiClampSize(dg, hn, vn, b);

    bool oNotch = PUGUI_KIND_IS_NOTCH(o.kind);
    bool vIn = !oNotch && PUGUI_KIND_IS_CUT(vn.kind) && svn.y > b.y;
    bool hIn = !oNotch && PUGUI_KIND_IS_CUT(hn.kind) && shn.x > b.x;

    // 本象限的收缩半径：自己的 fillet 与溢入线所属角的 fillet 取大（§14.3）—— 一个象限只能做一次
    // opening，「没有顶点比 r 尖」是唯一自洽的合并规则。
    float shortest = min(b.x, b.y);
    float r = PUGUI_KIND_IS_ROUND(o.kind) ? 0.0 : o.fillet;
    if (vIn) r = max(r, vn.fillet);
    if (hIn) r = max(r, hn.fillet);
    r = min(max(r, 0.0), shortest);
    // notch 到 min(W,H)/2 时两壁恰好被口沿圆弧吃光、凹弧仍是 r，轮廓是光滑的 S 形；再大会在腔底
    // 出一个 cusp —— 静默钳住比那个难看的退化好。
    if (oNotch) r = min(r, 0.5 * min(so.x, so.y));

    PuguiQuad q;
    q.kind = o.kind;
    q.r = r;
    q.bp = b - r;
    q.legacy = (r <= 0.0 && !vIn && !hIn) ? 1.0 : 0.0;
    q.roundR = PUGUI_KIND_IS_ROUND(o.kind) ? max(o.size.x - r, 0.0) : 0.0;
    if (oNotch)
        q.size = (r > 0.0) ? min(so, q.bp) : so;     // 收缩帧里缺口仍是 W×H；口沿圆弧不越中线，缺口让位
    else if (PUGUI_KIND_IS_CUT(o.kind))
        q.size = PuguiErodeCut(so, r, b, so.y > b.y, so.x > b.x, vn, svn, hn, shn);
    else
        q.size = float2(0.0, 0.0);

    q.vSpill = vIn ? 1.0 : 0.0;
    q.vNear = float2(0.0, 0.0);
    q.vFar = float2(0.0, 0.0);
    if (vIn)
    {
        float2 sv = PuguiErodeCut(svn, r, b, true, svn.x > b.x, o, so, dg, sdg);
        PuguiSpillSegmentV(sv, q.bp, q.vNear, q.vFar);
    }
    q.hSpill = hIn ? 1.0 : 0.0;
    q.hNear = float2(0.0, 0.0);
    q.hFar = float2(0.0, 0.0);
    if (hIn)
    {
        float2 sh = PuguiErodeCut(shn, r, b, shn.y > b.y, true, dg, sdg, o, so);
        PuguiSpillSegmentH(sh, q.bp, q.hNear, q.hFar);
    }
    return q;
}

// ---- 特征表距离场 ----------------------------------------------------------------------------

// 到线段 a–c 的距离与最近点。
float PuguiSegDist(float2 u, float2 a, float2 c, out float2 closest)
{
    float2 e = c - a;
    float t = saturate(dot(u - a, e) / max(dot(e, e), 1e-8));
    closest = a + e * t;
    return length(u - closest);
}

// 过 a、c 两点的半平面，外法线朝离开 interior 的一侧；返回有符号距离。
float PuguiHalfPlane(float2 u, float2 a, float2 c, float2 interior, out float2 n)
{
    float2 e = c - a;
    n = normalize(float2(e.y, -e.x) + 1e-6);
    if (dot(interior - a, n) > 0.0) n = -n;
    return dot(u - a, n);
}

// 象限特征表的距离场（第二部分 §14.2）：内侧 = 半平面 max（凸集之交，精确），外侧 = 到最近特征
// （射线 / 线段 / 圆弧）的距离（凸形状，精确）。n 是对应的外法线：内侧取赢的那个半平面，外侧取
// 「最近点指向片元」的方向 —— 圆弧带上法线的连续旋转由此而来。只处理 round / cut / 方角，
// notch 不进这里。u 已在收缩帧。
float PuguiSdQuadFeatures(float2 u, PuguiQuad q, out float2 n)
{
    const float BIG = 1e5;
    bool isRound = PUGUI_KIND_IS_ROUND(q.kind);
    float R = isRound ? q.roundR : 0.0;
    bool hasCut = !isRound && min(q.size.x, q.size.y) > 0.0;
    bool vIn = q.vSpill > 0.5;
    bool hIn = q.hSpill > 0.5;
    bool vOnTop = vIn && q.vNear.y >= 0.0;     // 竖向溢入线越过了角落在顶边上：竖边整条没了
    bool hOnSide = hIn && q.hNear.x >= 0.0;    // 横向溢入线越过了角落在竖边上：顶边整条没了
    float2 interior = -q.bp;                    // 收缩盒的中心，永远在形状内

    // ---- 内侧 ----
    float d = isRound ? PuguiSdRoundCorner(u, R) : PuguiSdQuadrant(u);
    n = isRound ? PuguiSdQuadrantNormal(u + R) : PuguiSdQuadrantNormal(u);
    if (hasCut)
    {
        float invL = rsqrt(dot(q.size, q.size));
        float dl = (u.x * q.size.y + u.y * q.size.x + q.size.x * q.size.y) * invL;
        if (dl > d) { d = dl; n = float2(q.size.y, q.size.x) * invL; }
    }
    if (vIn)
    {
        float2 nv;
        float dv = PuguiHalfPlane(u, q.vNear, q.vFar, interior, nv);
        if (dv > d) { d = dv; n = nv; }
    }
    if (hIn)
    {
        float2 nh;
        float dh = PuguiHalfPlane(u, q.hNear, q.hFar, interior, nh);
        if (dh > d) { d = dh; n = nh; }
    }
    if (d <= 0.0) return d;

    // ---- 外侧 ----
    float best = BIG;
    float2 closest = float2(0.0, 0.0);
    float2 c;
    float dist;

    if (!hOnSide)
    {
        // 顶边：从中线方向（或横向溢入点）到自己角特征的那端（或竖向溢入越过角后的落点）。
        float xStart = hasCut ? -q.size.x : -R;
        if (vOnTop) xStart = q.vNear.x;
        float xEnd = hIn ? min(q.hNear.x, xStart) : -BIG;
        c = float2(clamp(u.x, xEnd, xStart), 0.0);
        dist = length(u - c);
        if (dist < best) { best = dist; closest = c; }
    }
    if (!vOnTop)
    {
        float yStart = hasCut ? -q.size.y : -R;
        if (hOnSide) yStart = q.hNear.y;
        float yEnd = vIn ? min(q.vNear.y, yStart) : -BIG;
        c = float2(0.0, clamp(u.y, yEnd, yStart));
        dist = length(u - c);
        if (dist < best) { best = dist; closest = c; }
    }
    if (hasCut)
    {
        dist = PuguiSegDist(u, float2(-q.size.x, 0.0), float2(0.0, -q.size.y), c);
        if (dist < best) { best = dist; closest = c; }
    }
    if (isRound && R > 0.0 && u.x > -R && u.y > -R)
    {
        // 圆弧扇区；弧的两个端点就是两条边段的起点，已在上面覆盖。
        float2 v = u + R;
        float len = max(length(v), 1e-6);
        dist = len - R;
        if (dist < best) { best = dist; closest = u - v * (dist / len); }
    }
    if (vIn)
    {
        dist = PuguiSegDist(u, q.vNear, q.vFar, c);
        if (dist < best) { best = dist; closest = c; }
    }
    if (hIn)
    {
        dist = PuguiSegDist(u, q.hNear, q.hFar, c);
        if (dist < best) { best = dist; closest = c; }
    }
    n = normalize(u - closest + 1e-6);
    return best;
}

// 面板形状的 SDF。返回值：负=内部，正=外部，绝对值≈到边界的距离（画布单位）。
float PuguiSdPanel(float2 p, float2 b, PuguiQuad q)
{
    float2 u = abs(p) - b;
    if (PUGUI_KIND_IS_NOTCH(q.kind))
    {
        if (q.r <= 0.0) return PuguiSdNotchCorner(u, q.size);
        return PuguiSdNotchCornerFilleted(u + q.r, q.size, q.r) - q.r;
    }
    if (q.legacy > 0.5)
    {
        // 无 fillet 无溢出：第一部分之前的函数原样 —— 逐像素不变的承诺靠这一分支不碰。
        if (PUGUI_KIND_IS_ROUND(q.kind)) return PuguiSdRoundCorner(u, q.roundR);
        return PuguiSdCutCorner(u, q.size);
    }
    // 倒圆 = 形态学 opening：形状先向内收 r（盒子 b − r，斜边已换算到收缩帧），再 d − r 向外放。
    // 直边回到原位，每个凸顶点变成与两边相切的 r 圆弧。
    float2 shrunk = u + q.r;
    if (q.vSpill < 0.5 && q.hSpill < 0.5 && q.roundR <= 0.0 && min(q.size.x, q.size.y) <= 0.0)
        return PuguiSdQuadrant(shrunk) - q.r;      // 斜边被圆弧吃光：与 round 同一串指令（§5.2）
    float2 n;
    return PuguiSdQuadFeatures(shrunk, q, n) - q.r;
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
float2 PuguiPanelNormal(float2 p, float2 b, PuguiQuad q)
{
    float2 u = abs(p) - b;
    float2 g;
    if (PUGUI_KIND_IS_NOTCH(q.kind))
    {
        g = (q.r <= 0.0) ? PuguiSdNotchNormal(u, q.size)
                         : PuguiSdNotchFilletedNormal(u + q.r, q.size, q.r);
    }
    else if (q.legacy > 0.5)
    {
        g = PUGUI_KIND_IS_ROUND(q.kind) ? PuguiSdQuadrantNormal(u + q.roundR)
                                        : PuguiSdCutNormal(u, q.size);
    }
    else
    {
        // 收缩帧：膨胀不改变法线方向，收缩形状的法线就是最终形状的法线。
        float2 shrunk = u + q.r;
        if (q.vSpill < 0.5 && q.hSpill < 0.5 && q.roundR <= 0.0 && min(q.size.x, q.size.y) <= 0.0)
            g = PuguiSdQuadrantNormal(shrunk);
        else
            PuguiSdQuadFeatures(shrunk, q, g);
    }

    // abs() 把四个象限折到第一象限求解，这里再折回去。
    float2 s = float2(p.x >= 0.0 ? 1.0 : -1.0, p.y >= 0.0 ? 1.0 : -1.0);
    return normalize(s * g);
}

// ───── 进度裁切（spec 2026-09-18 §5.2）─────
// 形状 ∩ 半平面：<Progress> 的程序化 fill 不收缩 rect，value 变成这里的一条切线。切割进了 d，
// 于是描边 / 两层发光 / 雾 / 玻璃折射全部沿切完的形状走 —— 推进边是直的，光晕包住它而不是被刀切。
// 参数走顶点通道（uv1.zw），与局部坐标 / 半尺寸同一条理由：逐面板不同、逐样式相同，材质照常共享，
// value 动画只脏顶点。
//
// code：0 = 不切；±1 = 沿 x（+1 从左往右填）；±2 = 沿 y（+2 从下往上填）。四个顶点写同一个值，
// 插值后逐位不变，所以下面按常量处理。offset e 是切线到 rect 中心的有符号距离（局部像素），
// 由 C# 按 (2·value − 1) · b_axis 算好。
float2 PuguiCutNormal(float code)
{
    float s = code >= 0.0 ? 1.0 : -1.0;
    return abs(code) < 1.5 ? float2(s, 0.0) : float2(0.0, s);
}

// 用 select 而不是分支：d 随后要过 fwidth，导数指令不能落在（编译器看来）非均匀的控制流之后。
// 不切时切割项取一个远小于任何 d 的常数，max 逐位不改 d —— 非 Progress 的面板与从前完全一样。
float PuguiCutTerm(float2 p, float code, float e)
{
    return abs(code) > 0.5 ? dot(p, PuguiCutNormal(code)) - e : -1e6;
}

float PuguiSdCut(float d, float2 p, float code, float e)
{
    return max(d, PuguiCutTerm(p, code, e));
}

// 交集里切割项赢的区域，外法线就是切线的法线 —— 玻璃的折射带与高光才会沿推进边走。
float2 PuguiPanelNormalCut(float2 p, float2 b, PuguiQuad q, float dShape, float code, float e)
{
    float2 n = PuguiPanelNormal(p, b, q);
    return PuguiCutTerm(p, code, e) > dShape ? PuguiCutNormal(code) : n;
}

// 直 alpha 的 source-over 合成。
float4 PuguiOver(float4 src, float4 dst)
{
    float a = src.a + dst.a * (1.0 - src.a);
    float3 rgb = (src.rgb * src.a + dst.rgb * dst.a * (1.0 - src.a)) / max(a, 1e-5);
    return float4(rgb, a);
}

// ---- 曝光（intensity）----
//
// 把 k 份同样的光 screen 叠在一起：f(x) = 1 - (1 - x)^k。k = 1 逐位恒等；暗端 ≈ k·x（光晕尾巴
// 保色相、只是更亮更宽）；亮端各通道依次饱和到 1 —— 「核心发白、光晕保色」这条曲线正是人眼把
// 一块颜色读成「光」的原因（spec 2026-09-12 §5.1）。输入输出都在 0..1，不需要 HDR 目标。
//
// 作用在**预乘色**上。作用在直色上时，通道为 1 的颜色（#00f 的 B）在任何能量下都直接饱和，
// 光晕尾巴整片纯色；预乘后尾巴处 p → 0，行为正确。alpha 走同一条曲线：光 k 倍亮就 k 倍宽。
//
// 过曝串扰：逐通道曲线对零通道无能为力（#f00 / #00f 永远不发白）。能量 k·max(p) 越过 1 的
// 像素向自身最亮通道靠拢（去饱和），能量 4 时到满、系数 0.5 —— 纯色最多白到一半，霓虹的观感。
// 阈值与系数按设计稿标定，是常数不是参数。尾巴处能量 < 1，串扰为 0。
//
// 三个 shader（不透明面板 / <Decor> / FxImage）逐字共用，理由同两层发光：一份曲线喂所有面。
float4 PuguiExpose(float4 col, float k)
{
    // uniform 分支：k = 1 走原路，既有面板逐位不变。
    if (k <= 1.0) return col;
    float3 p  = col.rgb * col.a;
    float3 fp = 1.0 - pow(max(1.0 - p, 0.0), k);
    float  fa = 1.0 - pow(max(1.0 - col.a, 0.0), k);
    float  energy = k * max(p.r, max(p.g, p.b));
    float  h = saturate((energy - 1.0) / 3.0);
    float  target = max(fp.r, max(fp.g, fp.b));
    fp = lerp(fp, target.xxx, 0.5 * h);
    return float4(fp / max(fa, 1e-5), fa);
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

// ---- 线性渐变（spec 2026-09-17）----
//
// 四个面板 shader（不透明 / 玻璃 / 玻璃焊接组 / <Decor>）共用，理由同上面两层发光：方向、色标
// 位置与曲线必须处处逐字一致，否则同一份 color= 在不同表面上渐变到不同的地方 —— 而且现在
// 填充、描边、外发光、内发光四个色槽都走这一条函数，同一个 token 写在 color 与 borderColor 上，
// 转换点落在同一排像素（LG-D5）。C# 侧的孪生是 ColorSpec.Evaluate / DirectionFor / LineLengthFor。
//
// 一条 ramp 占 7 个 uniform：4 个色标色、色标位置 (P0..P3)、曲线 (E0, E1, E2, 色标数)、方向
// (xy = 单位向量[角度模式]; z = 1 表示「角」模式; w = CSS 顺序的角编号)。未用的色标槽由 C# 填成
// 最后一个色标，所以第四槽永远可读、不必判个数。
//
// 方向遵循 CSS：0deg 向上、90deg 向右、180deg 向下（默认 —— 与方向出现之前逐位相同）。
// 「角」（to bottom right）是 CSS 的魔法角：渐变线垂直于**相邻两角**的连线，因此不论宽高比，
// 命名的那个角恰在 0%、对角恰在 100%、另外两角恰在 50% 线上。它依赖盒子尺寸，所以 C# 只传
// 角编号，向量在这里按 b 现算。
//
// 渐变线长度 L = |w·dir.x| + |h·dir.y|（CSS 定义：让 0% / 100% 恰好触到盒子的两个角），
// s = (dot(p, dir) + L/2) / L。默认方向下 L = h、s = (b.y − p.y) / (2 b.y)，即色标出现之前那条 ramp。
//
// 色标之间每段一条幂曲线：指数来自 CSS 的 color hint（`A, 70%, B`）。色标是**切** ramp ——
// 在色标处斜率从 0 突然跳到满值，颜色连续但导数不连续，人眼把这种断点读成一条实际不存在的
// 分界线（马赫带）；提示改成**弯**它：整段一条幂曲线，处处光滑。指数在 C# 侧由
// ColorParser.StopCurveExponent 算好（log0.5/log t），这里只负责代入；== 1.0 时整段跳过。
// 相邻色标位置相等是合法的硬边：1e-4 的下限把它收成一个像素以内的跳变。

struct PuguiRamp
{
    float4 c0, c1, c2, c3;
    float4 stops;     // P0..P3
    float4 curves;    // E0, E1, E2, count
    float4 dir;       // xy = 单位方向（角度模式）; z = 角模式标志; w = 角编号（CSS 顺序）
};

PuguiRamp PuguiMakeRamp(float4 c0, float4 c1, float4 c2, float4 c3,
                        float4 stops, float4 curves, float4 dir)
{
    PuguiRamp r;
    r.c0 = c0; r.c1 = c1; r.c2 = c2; r.c3 = c3;
    r.stops = stops;
    r.curves = curves;
    r.dir = dir;
    return r;
}

// 每个 shader 用这两个宏声明并组装一组 ramp uniform：PUGUI_RAMP_UNIFORMS(_Fill) 展开成
// _Fill0.._Fill3 / _FillStops / _FillCurves / _FillDir，PUGUI_RAMP(_Fill) 把它们打包成 PuguiRamp。
// 名字与 C# 的 GradientUniforms 一一对应。
#define PUGUI_RAMP_UNIFORMS(prefix) \
    fixed4 prefix##0; fixed4 prefix##1; fixed4 prefix##2; fixed4 prefix##3; \
    float4 prefix##Stops; float4 prefix##Curves; float4 prefix##Dir;

#define PUGUI_RAMP(prefix) \
    PuguiMakeRamp(prefix##0, prefix##1, prefix##2, prefix##3, prefix##Stops, prefix##Curves, prefix##Dir)

// 「角」模式的方向：b = 半尺寸。CSS 顺序 0 左上、1 右上、2 右下、3 左下。
// to bottom right 的相邻角是右上与左下，连线 (w, h)，垂直且指向右下象限的是 (h, −w)；其余按符号。
float2 PuguiCornerDir(float2 b, float corner)
{
    float sx = (corner == 1.0 || corner == 2.0) ? 1.0 : -1.0;   // right?
    float sy = (corner < 1.5) ? 1.0 : -1.0;                     // top?
    float2 v = float2(b.y * sx, b.x * sy);
    float len = length(v);
    return len > 1e-6 ? v / len : float2(0.0, -1.0);
}

float4 PuguiRampSegment(float4 a, float4 c, float pa, float pb, float e, float s)
{
    float u = saturate((s - pa) / max(pb - pa, 1e-4));
    if (e != 1.0) u = pow(u, e);
    return lerp(a, c, u);
}

// p = 相对 rect 中心的局部坐标（y 向上），b = rect 半尺寸。
float4 PuguiGradient(float2 p, float2 b, PuguiRamp r)
{
    float count = r.curves.w;
    // 纯色：uniform 分支，全体 fragment 同一条路径，且让既有纯色面板逐位不变。
    if (count <= 1.0) return r.c0;

    float2 dir = r.dir.z > 0.5 ? PuguiCornerDir(b, r.dir.w) : r.dir.xy;
    float L = abs(2.0 * b.x * dir.x) + abs(2.0 * b.y * dir.y);
    float s = saturate((dot(p, dir) + 0.5 * L) / max(L, 1e-4));

    if (s <= r.stops.x) return r.c0;
    if (count > 2.5 && s > r.stops.y)
    {
        if (count > 3.5 && s > r.stops.z)
            return PuguiRampSegment(r.c2, r.c3, r.stops.z, r.stops.w, r.curves.z, s);
        return PuguiRampSegment(r.c1, r.c2, r.stops.y, r.stops.z, r.curves.y, s);
    }
    return PuguiRampSegment(r.c0, r.c1, r.stops.x, r.stops.y, r.curves.x, s);
}


// ---- 噪声雾（haze，spec 2026-09-17）----
//
// 参考图里按钮底边渗进来的几块「云雾状亮斑」：一个**低频**噪声场决定雾的 alpha，颜色来自 hazeColor
// 那条 ramp（它同时就是方向遮罩：`to top, A, A/0` = 从底边渗入）。玻璃的 noise 是逐像素的高频颗粒，
// 频段与用途都不同，不是同一个东西。
//
// 采样坐标是 **Canvas 空间**（worldPosition.xy，与 _ClipRect 同一坐标系），不是 rect 局部：同参数的
// 相邻按钮落在噪声场的不同区域、自然长得不一样，而材质照旧共享；跨面板连续；同一位置同一时刻永远
// 同一张图。代价是面板平移时雾像窗外的云一样滑过（§5.3）。
//
// 三 octave value noise（lacunarity 2、gain 0.5），quintic 插值（比 cubic 少一圈格子感）。格点坐标
// 先对 256 取正模再 hash，噪声以 256 格为周期平铺，于是 hazeDrift 的时间偏移永远不需要回绕。
// 第二、三个 octave 的采样域各转一个常角（0.7 / 1.4 rad）并错开原点：value noise 的极值落在格点上，
// 三层同轴叠加会露出一张方格（实施时的第一版就是那样），转开后叠成没有轴向的云团。
// 三个 octave 各沿一个不共线的常向量、以 1 / 1.5 / 2 倍速漂移：图样在流动中**形变**而不是整体平移，
// 细节「沸腾」、整体缓慢。角度、方向与倍速都是常数不是参数。
//
// 覆盖率映射（spec §5.1 / H-D7）：fbm 的均值 0.50、标准差 0.15，落到雾浓度要过一张「色阶」——
// 黑场 b、白场 t，中间一条曲线。hazeDensity = d 把三者一起推：黑场 0.55 → 0、白场 0.85 → 1、曲线
// 从 Hermite S 形淡成直线。d = 0 只取分布的高尾（28% 像素沾雾，几团光、大片空白）；d = 1 时 u 恰好
// 等于 n、曲线是直线，雾就是原始噪声场（100% 沾雾，一片平雾）；默认 0.5 是参考图的薄雾 + 亮云团
// （87% 沾雾、26% 半亮以上）。/alpha 管整体浓淡、d 管覆盖多少，两者正交。

#define PUGUI_HAZE_PERIOD 256.0

// Dave Hoskins hash12：无 sin，移动端 half 精度下不塌。
float PuguiHash12(float2 c)
{
    float3 p3 = frac(float3(c.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// 可平铺 value noise，quintic 插值。
float PuguiValueNoise(float2 q)
{
    float2 i = floor(q);
    float2 f = q - i;
    i -= PUGUI_HAZE_PERIOD * floor(i / PUGUI_HAZE_PERIOD);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    // 右邻 / 上邻格点在周期边界上要回绕到 0，否则平铺处有一条缝：i 取过模，i + 1 仍可能等于 256。
    float2 i1 = i + 1.0;
    i1 -= PUGUI_HAZE_PERIOD * floor(i1 / PUGUI_HAZE_PERIOD);
    float a = PuguiHash12(i);
    float b = PuguiHash12(float2(i1.x, i.y));
    float c = PuguiHash12(float2(i.x, i1.y));
    float d = PuguiHash12(i1);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 三 octave fBm → 覆盖率。pos 为 Canvas 空间坐标，size 为斑块特征尺寸（px），drift 为 px/s，
// t 为未缩放秒（全局 _PuguiUnscaledTime，HazeClock 每帧写），density 为 hazeDensity（0..1）。
// 旋转 0.7 rad / 1.4 rad：mul(R, q) = (c·x − s·y, s·x + c·y)。
static const float2x2 PUGUI_HAZE_ROT1 = float2x2(0.7648, -0.6442, 0.6442, 0.7648);
static const float2x2 PUGUI_HAZE_ROT2 = float2x2(0.1700, -0.9854, 0.9854, 0.1700);

float PuguiHazeWeight(float2 pos, float size, float drift, float t, float density)
{
    float2 q = pos / max(size, 1e-3);
    float  v = drift * t / max(size, 1e-3);          // 格 / 秒 × 秒
    float2 o0 = q + normalize(float2(1.0, 0.35)) * v;
    float2 o1 = mul(PUGUI_HAZE_ROT1, q * 2.0) + float2(13.1, 7.3)  + normalize(float2(-0.6, 0.80)) * v * 1.5;
    float2 o2 = mul(PUGUI_HAZE_ROT2, q * 4.0) + float2(26.2, 14.6) + normalize(float2(0.3, -1.00)) * v * 2.0;
    float  n = 0.5 * PuguiValueNoise(o0) + 0.25 * PuguiValueNoise(o1) + 0.125 * PuguiValueNoise(o2);
    n /= 0.875;
    // 色阶：黑场 / 白场随 density 走，u 是线性归一；最后一行决定保留多少 Hermite 平滑。
    // density = 0 时逐字是 smoothstep(0.55, 0.85, n)，= 1 时逐字是 n。
    float b = 0.55 * (1.0 - density);
    float w = 0.85 + 0.15 * density;
    float u = saturate((n - b) / (w - b));
    return lerp(u * u * (3.0 - 2.0 * u), u, density);
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
