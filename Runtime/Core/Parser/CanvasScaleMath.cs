using System;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// The CanvasScaler factor a <c>&lt;Screen&gt;</c> ends up with — physical pixels per canvas
    /// unit — for the two <c>scale-mode</c>s. Pure C# so the runtime (<c>Screen.ApplyCanvasScaler</c>)
    /// and the font-floor lint (<c>FontFloorRules</c>, which has to know the factor on a device it is
    /// not running on) share one formula.
    /// </summary>
    public static class CanvasScaleMath
    {
        /// <summary>
        /// <c>scale-mode="auto"</c> with a <c>reference</c>: CanvasScaler's <c>Expand</c> match mode,
        /// <c>min(screen / reference)</c> per axis — the tighter axis drives the scale, so the whole
        /// reference rect always fits.
        /// </summary>
        public static float ExpandFactor(float screenW, float screenH, float refW, float refH)
            => Math.Min(screenW / refW, screenH / refH);

        /// <summary>
        /// <c>scale-mode="pixel"</c>: an integer factor (or 1/2^n below 1x) so one design pixel maps
        /// to whole device pixels.
        /// <code>
        /// raw = min(screenW/designW, screenH/designH)  (fit-inside)
        /// raw >= 1  -> floor(raw)                       (integer 1, 2, 3, ...)
        /// raw &lt;  1  -> 1 / 2^ceil(log2(1/raw))          (snap to 0.5, 0.25, 0.125, ...)
        /// </code>
        /// Degenerate input (any axis &lt;= 0) -> 1 (safe fallback).
        ///
        /// <para><paramref name="powerOfTwoOnly"/> collapses the whole ladder to powers of two
        /// (...0.25, 0.5, 1, 2, 4, 8...): the result is the largest power of two &lt;= raw, i.e.
        /// 2^floor(log2(raw)). For raw &lt; 1 that equals the default 1/2^ceil(log2(1/raw)) (since
        /// -ceil(-x) == floor(x)), so only the magnify segment (raw &gt;= 1) changes: 3x -> 2x,
        /// 5x -> 4x, ... still fit-inside (rounds down, never overflows).</para>
        ///
        /// <para><paramref name="minPixelScale"/> &gt; 0 then floors the result
        /// (<c>UI.MinPixelScale</c>); 0 = no floor.</para>
        /// </summary>
        public static float PixelFactor(float screenW, float screenH, float designW, float designH,
                                        bool powerOfTwoOnly = false, float minPixelScale = 0f)
        {
            var factor = Ladder(screenW, screenH, designW, designH, powerOfTwoOnly);
            if (minPixelScale > 0f && factor < minPixelScale)
                factor = minPixelScale;
            return factor;
        }

        private static float Ladder(float screenW, float screenH, float designW, float designH,
                                    bool powerOfTwoOnly)
        {
            if (screenW <= 0f || screenH <= 0f || designW <= 0f || designH <= 0f)
                return 1f;
            var raw = Math.Min(screenW / designW, screenH / designH);
            if (powerOfTwoOnly)
                // +epsilon absorbs float error in Log so exact powers (raw == 4, 8, ...)
                // don't drop a rung when log2 lands just under the integer exponent.
                return (float)Math.Pow(2.0, Math.Floor((float)Math.Log(raw, 2.0) + 1e-5f));
            if (raw >= 1f) return (float)Math.Floor(raw);
            var n = (int)Math.Ceiling((float)Math.Log(1f / raw, 2.0));
            return 1f / (float)Math.Pow(2.0, n);
        }
    }
}
