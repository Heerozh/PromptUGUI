using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Says out loud that a gradient stop position landed somewhere it cannot be drawn
    /// (spec 2026-08-30 §6.2). A stop only exists per-fragment, in the procedural shader; a
    /// vertex-coloured Graphic has nothing but corner vertices to place it on, so the hardware
    /// interpolates straight through and the ramp comes out full-height whatever the author wrote.
    ///
    /// <para>Called from the paths that KNOW they are on the vertex side — the sprite-backed
    /// primitives, the TMP label appliers, and <see cref="ProceduralSurface.Reconcile"/> once it has
    /// decided the surface stays off. Deliberately NOT from <see cref="ColorApplier"/>: a control
    /// like <c>Btn</c> hands the same spec to both the Image and the surface before the mode is
    /// known, so warning there would fire on every correct procedural button.</para>
    ///
    /// <para>The static counterpart is <c>PUI-GRADIENT-STOP-NO-SURFACE</c>, which reaches the wider
    /// set of colour attributes (a Toggle's checkmark, a Dropdown's popup) — those have no
    /// per-attribute chokepoint worth threading a warning through.</para>
    /// </summary>
    internal static class GradientStopWarning
    {
        internal static void IfMoved(in ColorSpec spec, Object context, string what)
        {
            if (!spec.HasStops) return;
            Debug.LogWarning(
                $"PromptUGUI: {what} carries a gradient stop position, but it paints a " +
                "vertex-coloured graphic — only a procedural surface can place a stop, so the " +
                "gradient spans the full height instead. Give the node a procedural shape " +
                "(radius / glass / borderWidth), or drop the position. [PUI-GRADIENT-STOP-NO-SURFACE]",
                context);
        }
    }
}
