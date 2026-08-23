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

// 直 alpha 的 source-over 合成。
float4 PuguiOver(float4 src, float4 dst)
{
    float a = src.a + dst.a * (1.0 - src.a);
    float3 rgb = (src.rgb * src.a + dst.rgb * dst.a * (1.0 - src.a)) / max(a, 1e-5);
    return float4(rgb, a);
}

#endif // PROMPTUGUI_PANEL_SDF_INCLUDED
