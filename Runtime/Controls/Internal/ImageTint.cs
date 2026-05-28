using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Switches a <see cref="UnityImage"/> between Unity's default multiply tint
    /// (material = null → Graphic falls back to UI/Default) and PromptUGUI's
    /// Linear Light tint material. The material asset is shared process-wide and
    /// lazy-loaded from Resources on first use; only the material setter is ever
    /// touched (never the getter), so no per-Image material instance is created.
    /// </summary>
    internal static class ImageTint
    {
        private const string LinearLightTintResourcePath = "PromptUGUI/Material/UI-LinearLightTint";
        private static Material _linearLightTint;

        public static void Apply(UnityImage img, string mode)
        {
            if (img == null) return;
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
                    Debug.LogWarning(
                        $"PromptUGUI: tint=\"{mode}\" is not a recognized value " +
                        "(expected: multiply, linear). Falling back to multiply.");
                    img.material = null;
                    break;
            }
        }
    }
}
