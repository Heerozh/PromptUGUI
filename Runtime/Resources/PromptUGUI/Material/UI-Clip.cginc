#ifndef PUGUI_UI_CLIP_INCLUDED
#define PUGUI_UI_CLIP_INCLUDED

// RectMask2D clipping the way Unity's UI/Default does it (since 2020.1; the canvas shader-graph pass
// spells the same lines): a coverage ramp at the clip rect's edge rather than UnityGet2DClipping's hard
// step at the pixel centre — which drew one column further than UI/Default under the same mask, a
// 1px seam where a glow met the mask edge — and RectMask2D.softness, which the canvas renderer hands
// to every material as _UIMaskSoftnessX / _UIMaskSoftnessY.
//
// Vertex stage: OUT.mask = PuguiClipMask(v.vertex.xy, OUT.vertex, _ClipRect);
// Fragment stage, under UNITY_UI_CLIP_RECT: col.a *= PuguiClipCoverage(IN.mask, _ClipRect);

float _UIMaskSoftnessX;
float _UIMaskSoftnessY;

// xy = the vertex against the clip rect's centre (doubled), zw = the ramp's slope: one over the softness
// plus four screen pixels, both in canvas units.
float4 PuguiClipMask(float2 canvasXY, float4 positionCS, float4 clipRect)
{
    float2 pixelSize = positionCS.w;
    pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
    float4 r = clamp(clipRect, -2e10, 2e10);
    return float4(canvasXY * 2 - r.xy - r.zw,
                  0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
}

// How much of this fragment the clip rect keeps, 0..1.
half PuguiClipCoverage(float4 mask, float4 clipRect)
{
    half2 m = saturate((clipRect.zw - clipRect.xy - abs(mask.xy)) * mask.zw);
    return m.x * m.y;
}

#endif
