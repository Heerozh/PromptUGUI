using System.Collections.Generic;

namespace PromptUGUI.IR
{
    /// <summary>Top-level &lt;Theme name=... base=...&gt; block from a UIDocument.</summary>
    public sealed class ThemeBlock
    {
        public string Name;
        public string BaseName;     // null if no base=
        public List<ColorEntry> Colors = new List<ColorEntry>();
    }
}
