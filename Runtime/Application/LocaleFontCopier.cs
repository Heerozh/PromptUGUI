using System.Collections.Generic;

namespace PromptUGUI.Application
{
    /// <summary>
    /// Seeds every other locale's empty font/material slots from one source locale,
    /// so a many-locale <see cref="PromptUGUISettings"/> asset can be filled from a
    /// single configured language (driven by the Settings inspector's "Copy to All"
    /// button). Font and material are independent slots, matched by font-type;
    /// already-set values are never overwritten and the source locale is untouched.
    /// </summary>
    internal static class LocaleFontCopier
    {
        public static void CopyToEmptySlots(
            List<PromptUGUISettings.LocaleConfig> locales, string sourceLocale)
        {
            if (locales == null) return;

            PromptUGUISettings.LocaleConfig source = null;
            foreach (var lc in locales)
                if (lc != null && lc.locale == sourceLocale) { source = lc; break; }
            if (source?.fonts == null) return;

            foreach (var lc in locales)
            {
                if (lc == null || ReferenceEquals(lc, source) || lc.fonts == null) continue;
                foreach (var dst in lc.fonts)
                {
                    if (dst == null) continue;
                    var src = FindByType(source.fonts, dst.type);
                    if (src == null) continue;
                    if (dst.font == null) dst.font = src.font;
                    if (dst.material == null) dst.material = src.material;
                }
            }
        }

        private static PromptUGUISettings.FontEntry FindByType(
            List<PromptUGUISettings.FontEntry> fonts, string type)
        {
            foreach (var fe in fonts)
                if (fe != null && fe.type == type) return fe;
            return null;
        }
    }
}
