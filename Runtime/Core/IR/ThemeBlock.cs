using System.Collections.Generic;

namespace PromptUGUI.IR
{
    /// <summary>Top-level &lt;Theme name=... base=...&gt; block from a UIDocument.</summary>
    public sealed class ThemeBlock
    {
        public string Name;
        public string BaseName;     // null if no base=
        public List<ColorEntry> Colors = new List<ColorEntry>();

        /// <summary>
        /// Theme-scoped <c>&lt;Style&gt;</c> packs, keyed by style name. Because <c>class=</c> is an
        /// attribute macro rather than a style engine, these give a theme the whole attribute surface
        /// — sprite, radius, font size, padding — not just colour tokens. Folded over the global
        /// <c>&lt;Style&gt;</c> of the same name (which acts as the implicit root of every theme
        /// chain), so a theme only spells out what differs. See the 2026-08-26 spec §3–§4.
        /// </summary>
        public Dictionary<string, StyleDef> Styles = new Dictionary<string, StyleDef>();
    }
}
