namespace PromptUGUI.Application
{
    public enum ScaleMode
    {
        /// <summary>
        /// Existing behavior: when <c>&lt;Screen reference="..."&gt;</c> is set, CanvasScaler runs in
        /// ScaleWithScreenSize (continuous fractional scaling). When unset, falls back to
        /// ConstantPixelSize with scaleFactor=1 (XML numbers = device pixels).
        /// </summary>
        Auto = 0,

        /// <summary>
        /// ConstantPixelSize + integer scaleFactor (with 1/2^n snap below 1x). Requires
        /// <c>&lt;Screen reference="WxH"&gt;</c> as the design resolution. Use for pixel-art / iso-grid
        /// projects where 1 design pixel must map to exactly N physical pixels.
        /// </summary>
        Pixel = 1,
    }
}
