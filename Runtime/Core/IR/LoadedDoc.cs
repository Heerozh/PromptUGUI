using System.Collections.Generic;

namespace PromptUGUI.IR
{
    /// <summary>
    /// One entry document with its whole <c>&lt;Import&gt;</c> closure merged in: every Screen,
    /// Template, Style and Theme reachable from it, keyed so namespaced references resolve.
    /// Produced by <c>DocumentLoader</c> (which owns the async source fetching) and consumed by
    /// <see cref="Template.TemplateExpander"/>.
    ///
    /// <para>Lives in Core/IR rather than next to its producer so the expansion layer stays free of
    /// <c>PromptUGUI.Application</c> — that is what lets the UIXmlLint CLI compile
    /// <c>Core/Template</c> and lint the EXPANDED tree, the same one ScreenInstantiator sees.
    /// See the 2026-08-26 theme-driven-style spec §9.</para>
    /// </summary>
    internal sealed class LoadedDoc
    {
        public string EntrySrc;
        public HashSet<string> AllSrcs = new();
        public List<ScreenDef> Screens = new();
        public Dictionary<TemplateKey, TemplateDef> Templates = new();
        public Dictionary<StyleKey, StyleDef> Styles = new();
        // Each entry carries the original src so cross-doc duplicate detection
        // can name both conflicting files in the error message.
        public List<(ThemeBlock Theme, string Src)> Themes = new();
    }
}
