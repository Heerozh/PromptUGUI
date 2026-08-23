// 程序化面板的共享形状层：UI-ProceduralPanel 与 UI-GlassPanel 都 include 这份，
// 保证不透明面板和玻璃面板的圆角、pill 解算、抗锯齿宽度逐像素完全一致 ——
// 两块并排、只有填充方式不同的面板，边缘必须严丝合缝对齐。
#ifndef PROMPTUGUI_PANEL_SDF_INCLUDED
#define PROMPTUGUI_PANEL_SDF_INCLUDED

// iq 的圆角矩形 SDF，四角半径独立。p 以矩形中心为原点，b 为半尺寸。
// 返回值：负=内部，正=外部，绝对值≈到边界的距离（画布单位）。
float PuguiSdRoundBox(float2 p, float2 b, float4 r)
{
    // 象限选角：右半区取 (TR, BR)，左半区取 (TL, BL)；再按上下二选一。
    float2 side = (p.x > 0.0) ? float2(r.y, r.z) : float2(r.x, r.w);
    float radius = (p.y > 0.0) ? side.x : side.y;
    float2 q = abs(p) - b + radius;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
}

// pill 在 GPU 上解算而不是在 C# 里：它依赖 rect 尺寸，提前算掉会让同一 style
// 的不同尺寸面板拿到不同材质参数，白白丢掉材质共享（见 ProceduralMaterialCache）。
float4 PuguiResolveRadius(float4 radius, float pill, float2 halfSize)
{
    float shortest = min(halfSize.x, halfSize.y);
    float4 r = (pill > 0.5) ? shortest.xxxx : radius;
    return clamp(r, 0.0, shortest);
}

// 圆角矩形 SDF 的**解析**外法线（画布空间，+Y = 界面正上方）。
//
// 刻意不用 ddx/ddy(d) 求梯度，两个理由都是正确性而非性能：
// 1) lightAngle 是画布空间的概念（0 = 正上方）。光栅空间 Y 轴朝向逐平台不同
//    （D3D/Metal 向下、GL/GLES/WebGL 向上），用屏幕导数当法线会让边缘高光和折射方向
//    在 GL 目标上整个上下翻转 —— 同一份 XML 在 WebGL 构建里长得不一样，且不报任何错。
// 2) 导数指令在非均匀控制流里是未定义行为，而调用点在逐像素的 `inside > 0` 分支内。
//
// 解析式顺带比中心差分更省：直接对 PuguiSdRoundBox 求导，没有额外的 SDF 求值。
// 分支外区（max(q)>0）法线是 normalize(max(q,0))；内区退化成"离哪条边最近就朝哪边"。
float2 PuguiSdNormal(float2 p, float2 b, float4 r)
{
    float2 side = (p.x > 0.0) ? float2(r.y, r.z) : float2(r.x, r.w);
    float radius = (p.y > 0.0) ? side.x : side.y;
    float2 q = abs(p) - b + radius;

    float2 g = (max(q.x, q.y) > 0.0)
        ? normalize(max(q, 0.0) + 1e-6)
        : ((q.x > q.y) ? float2(1.0, 0.0) : float2(0.0, 1.0));

    // abs() 把四个象限折到第一象限求解，这里再折回去。
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
