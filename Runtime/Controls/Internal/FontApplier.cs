using PromptUGUI.Application;
using TMPro;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Applies the locale's font + optional material preset for a logical font type
    /// to a TMP component. Centralizes the order rule: assign the font first, then the
    /// material (assigning a new font resets TMP's material, so material must come last),
    /// and always set the material explicitly so switching back to a preset-less type
    /// restores the font's default material.
    /// </summary>
    internal static class FontApplier
    {
        public static void Apply(TMP_Text tmp, string type)
        {
            if (tmp == null) return;
            var settings = PromptUGUISettings.Instance;
            if (settings == null) return;
            var res = settings.ResolveFontEntry(UI.Locale.Current, type);
            if (res.Font == null) return;
            tmp.font = res.Font;
            tmp.fontSharedMaterial = res.Material != null ? res.Material : res.Font.material;
        }
    }
}
