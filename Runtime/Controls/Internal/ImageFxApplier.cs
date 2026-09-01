using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared landing point for <c>blur</c> / <c>glow</c> / <c>glowColor</c> on <c>&lt;Image&gt;</c>
    /// and <c>&lt;Icon&gt;</c> (spec 2026-09-02 §5.4) — the same shape as
    /// <see cref="RotateFlipApplier"/> and <see cref="ImageTint"/>: the controls' setters stay
    /// one-liners and the parsing rules live in one place.
    ///
    /// <para>Every entry point tolerates a graphic that is not an <see cref="FxImage"/>. That is a
    /// node whose Image came from a prefab rather than from <c>OnAttached</c>; the attribute is
    /// dropped with one warning rather than throwing, because the rest of the screen is fine.</para>
    /// </summary>
    internal static class ImageFxApplier
    {
        private static bool _warnedPlainImage;

        /// <summary>Re-arms the warn-once diagnostic; called from <c>UI.ResetForTests</c>.</summary>
        internal static void ResetDiagnostics() => _warnedPlainImage = false;

        public static void SetBlur(Graphic graphic, string tag, string value)
        {
            if (!TryFx(graphic, tag, "blur", out var fx)) return;
            fx.Blur = ProceduralValueParser.Pixels(value, "blur");
        }

        public static void SetGlow(Graphic graphic, string tag, string value)
        {
            if (!TryFx(graphic, tag, "glow", out var fx)) return;
            fx.Glow = ProceduralValueParser.Pixels(value, "glow");
        }

        public static void SetGlowColor(Graphic graphic, string tag, string value)
        {
            if (!TryFx(graphic, tag, "glowColor", out var fx)) return;

            // Variant can only change a value, never remove the attribute, so an empty string is the
            // author's way back to "the sprite's own colour" — the same convention as glow="".
            if (string.IsNullOrWhiteSpace(value))
            {
                fx.ClearGlowColor();
                return;
            }

            // "self" / "self/0.5": the sprite's own blurred colour, with the usual /alpha suffix as
            // its strength — the one knob the unwritten default has no way to turn. Intercepted
            // before the theme sees it: it is not a colour token, and no theme may redefine it.
            var trimmed = value.Trim();
            if (trimmed == SelfKeyword || trimmed.StartsWith(SelfKeyword + "/", System.StringComparison.Ordinal))
            {
                if (!ColorParser.TrySplitAlpha(trimmed, out _, out var alpha, out var error))
                    throw new ParseException($"glowColor=\"{value}\": {error}");
                fx.SetGlowSelf(alpha ?? 1f);
                return;
            }

            fx.SetGlowColor(UI.Theme.Resolve(value));
        }

        /// <summary>The <c>glowColor</c> spelling of "the sprite's own colour" (spec §3).</summary>
        internal const string SelfKeyword = "self";

        /// <summary>Pushes the material through once every attribute has been applied, so a freshly
        /// opened Screen renders correctly without waiting for a canvas rebuild — and re-checks the
        /// sprite / type the radii depend on.</summary>
        public static void Flush(Graphic graphic)
        {
            if (graphic is FxImage fx)
            {
                fx.RefreshFxState();
                fx.FlushParams();
            }
        }

        /// <summary>Warns once when the author asked for an fx the node's Image cannot provide.</summary>
        public static void WarnIfFxOnNonSimple(Graphic graphic, string tag)
        {
            if (graphic is not FxImage fx) return;
            if (fx.Pad <= 0f || fx.sprite == null) return;
            if (fx.type == UnityEngine.UI.Image.Type.Simple) return;

            Debug.LogWarning(
                $"PromptUGUI: <{tag}> asks for blur / glow, but its sprite carries a 9-slice border " +
                $"so the Image is drawn as {fx.type} — the effect only applies to type=\"simple\" " +
                "and is skipped. Write type=\"simple\" if the borders are not wanted here.");
        }

        private static bool TryFx(Graphic graphic, string tag, string attr, out FxImage fx)
        {
            fx = graphic as FxImage;
            if (fx != null) return true;
            if (graphic == null) return false;

            if (!_warnedPlainImage)
            {
                _warnedPlainImage = true;
                Debug.LogWarning(
                    $"PromptUGUI: <{tag} {attr}=…> needs PromptUGUI's own Image component, but this " +
                    "node already carries a plain UnityEngine.UI.Image (from a prefab?). The " +
                    "attribute is ignored — remove the Image from the prefab and let PromptUGUI add " +
                    "its own, or drop the attribute.");
            }
            return false;
        }
    }
}
