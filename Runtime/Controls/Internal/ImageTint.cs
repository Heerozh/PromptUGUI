using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Switches a <see cref="Graphic"/> (Image / RawImage / …) between Unity's default
    /// multiply tint (material = null → Graphic falls back to UI/Default) and PromptUGUI's
    /// Linear Light tint material. The material asset is shared process-wide and
    /// lazy-loaded from Resources on first use; only the material setter is ever
    /// touched (never the getter), so no per-graphic material instance is created.
    /// </summary>
    internal static class ImageTint
    {
        private const string LinearLightTintResourcePath = "PromptUGUI/Material/UI-LinearLightTint";
        private static Material _linearLightTint;

        public static void Apply(Graphic img, string mode)
        {
            if (img == null) return;

            // On an FxImage the material slot is already spoken for — blur, glow and the disabled
            // grey live in it — so the linear tint becomes one more parameter of that shader instead
            // of a material to swap in (spec 2026-09-02 §2). Flushed eagerly: a setter re-driven from
            // C# or a Variant must show immediately, without waiting for a canvas rebuild.
            if (img is FxImage fx)
            {
                switch (mode)
                {
                    case null:
                    case "":
                    case "multiply":
                        fx.TintLinear = false;
                        break;
                    case "linear":
                        fx.TintLinear = true;
                        break;
                    default:
                        WarnUnknown(mode);
                        fx.TintLinear = false;
                        break;
                }
                fx.FlushParams();
                return;
            }

            switch (mode)
            {
                case null:
                case "":
                case "multiply":
                    img.material = null;
                    break;
                case "linear":
                    img.material = _linearLightTint ??=
                        Resources.Load<Material>(LinearLightTintResourcePath);
                    break;
                default:
                    WarnUnknown(mode);
                    img.material = null;
                    break;
            }
        }

        private static void WarnUnknown(string mode) =>
            Debug.LogWarning(
                $"PromptUGUI: tint=\"{mode}\" is not a recognized value " +
                "(expected: multiply, linear). Falling back to multiply.");
    }
}
