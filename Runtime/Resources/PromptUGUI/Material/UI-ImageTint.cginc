#ifndef PROMPTUGUI_UI_IMAGE_TINT_INCLUDED
#define PROMPTUGUI_UI_IMAGE_TINT_INCLUDED

// tint="linear" 的 Linear Light 混合 —— UI-LinearLightTint.shader（RawImage / 控件底图那条路）
// 与 UI-ImageFx.shader（<Image> / <Icon>）逐字共用同一份实现。
//
// 两处必须是同一条公式而不是两份抄写：同一个 tint= 在不同标签上得出不同颜色，作者是查不出来的。
//
// 在 gamma（sRGB 显示）空间做，让美术眼中的 128 灰真的等于中性。tint 两种 color space 下都已是
// gamma 空间，不做转换；只有 linear 模式下的 sprite（被采样器 linear 化）需要转回 gamma。
inline half4 PuguiLinearLight(half4 sprite, half4 tint)
{
    half3 tintG = tint.rgb;
    #ifdef UNITY_COLORSPACE_GAMMA
    half3 spriteG = sprite.rgb;
    #else
    half3 spriteG = half3(
        LinearToGammaSpaceExact(sprite.r),
        LinearToGammaSpaceExact(sprite.g),
        LinearToGammaSpaceExact(sprite.b));
    #endif

    half3 rgbG = saturate(tintG + 2.0 * spriteG - 1.0);

    #ifdef UNITY_COLORSPACE_GAMMA
    half3 rgb = rgbG;
    #else
    half3 rgb = half3(
        GammaToLinearSpaceExact(rgbG.r),
        GammaToLinearSpaceExact(rgbG.g),
        GammaToLinearSpaceExact(rgbG.b));
    #endif

    return half4(rgb, sprite.a * tint.a);
}

#endif // PROMPTUGUI_UI_IMAGE_TINT_INCLUDED
