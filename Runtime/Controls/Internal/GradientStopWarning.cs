using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Says out loud that a shaped gradient — a stop position, a hint, or a third colour (spec
    /// 2026-09-17) — landed somewhere it cannot be drawn (spec 2026-08-30 §6.2). Since 2026-09-01
    /// (VGS) that is only TMP text: a procedural surface draws stops per fragment and every other
    /// Graphic gets them by slicing its mesh, but TMP paints a gradient per glyph, and four glyph
    /// corners hold a two-colour ramp in any direction and nothing more.
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
            UILog.Warn(context,
                $"PromptUGUI: {what} carries a gradient stop position, hint or third colour, but it paints " +
                "TMP text — a gradient there is placed per glyph, so a stop has nowhere to live and only " +
                "the two end colours are drawn. Drop the shaping, or put the shaped ramp on a graphic " +
                "behind the text. [PUI-GRADIENT-STOP-NO-SURFACE]");
        }
    }
}
