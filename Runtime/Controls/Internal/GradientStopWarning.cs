using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Says out loud that a gradient stop position landed somewhere it cannot be drawn
    /// (spec 2026-08-30 §6.2). Since 2026-09-01 (VGS) that is only TMP text: a procedural surface
    /// draws stops per fragment and every other Graphic gets them by slicing its mesh, but TMP
    /// paints a gradient per glyph, and four glyph corners have nowhere to put one.
    ///
    /// <para>Called from the two TMP paths only — <c>&lt;Text color&gt;</c> and
    /// <see cref="LabelColorApplier"/>. Deliberately NOT from <see cref="ColorApplier"/>, which now
    /// honours stops on every graphic it touches.</para>
    ///
    /// <para>The static counterpart is <c>PUI-GRADIENT-STOP-NO-SURFACE</c>, which reaches the label
    /// colour attributes a control exposes (<c>textColor</c>, a Dropdown's <c>itemTextColor</c>) —
    /// those have no per-attribute chokepoint worth threading a warning through.</para>
    /// </summary>
    internal static class GradientStopWarning
    {
        internal static void IfMoved(in ColorSpec spec, Object context, string what)
        {
            if (!spec.HasStops) return;
            Debug.LogWarning(
                $"PromptUGUI: {what} carries a gradient stop position, but it paints TMP text — " +
                "a gradient there is placed per glyph, so a stop has nowhere to live and the ramp " +
                "spans the full height instead. Drop the position, or put the shaped ramp on a " +
                "graphic behind the text. [PUI-GRADIENT-STOP-NO-SURFACE]",
                context);
        }
    }
}
