namespace PromptUGUI.Application
{
    public enum ScaleMode
    {
        // Existing behavior: when <Screen reference="..."> is set, CanvasScaler runs in
        // ScaleWithScreenSize (continuous fractional scaling). When unset, falls back to
        // ConstantPixelSize with scaleFactor=1 (XML numbers = device pixels).
        Auto = 0,

        // ConstantPixelSize + integer scaleFactor (with 1/2^n snap below 1x). Requires
        // <Screen reference="WxH"> as the design resolution. Use for pixel-art / iso-grid
        // projects where 1 design pixel must map to exactly N physical pixels.
        Pixel = 1,
    }
}
