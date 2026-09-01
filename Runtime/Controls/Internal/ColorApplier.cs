using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Single chokepoint for landing a resolved <see cref="ColorSpec"/> on a Graphic
    /// (spec §4.1). Solid → <c>graphic.color</c> (and disables any GradientTint);
    /// gradient → GradientTint vertex slot + <c>graphic.color = white</c> so the
    /// Graphic.color slot stays free for state modulates. GradientTint is lazy-added
    /// and toggled, never destroyed.
    /// </summary>
    internal static class ColorApplier
    {
        public static void Apply(Graphic g, ColorSpec spec)
        {
            if (spec.IsGradient)
            {
                var tint = g.GetComponent<GradientTint>() ?? g.gameObject.AddComponent<GradientTint>();
                tint.Set(spec);
                tint.enabled = true;
                g.color = Color.white;
            }
            else
            {
                var tint = g.GetComponent<GradientTint>();
                if (tint != null) tint.enabled = false;
                g.color = spec.Top;
            }
        }

        /// <summary>Read back the currently-applied value — gradient if a GradientTint is
        /// enabled, else the plain graphic colour. Used by StateTintReactor base capture, which
        /// re-applies what it reads with a modulate on top, so the whole spec (stop positions and
        /// hint curve included) has to survive the round trip.</summary>
        public static ColorSpec Peek(Graphic g)
        {
            var tint = g.GetComponent<GradientTint>();
            return tint != null && tint.enabled
                ? tint.Spec
                : ColorSpec.Solid(g.color);
        }
    }
}
