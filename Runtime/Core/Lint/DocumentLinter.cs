using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Runs <see cref="IRWalker"/> over BOTH the document as written and the document as expanded,
    /// deduplicated. Neither view is a superset of the other:
    ///
    /// <list type="bullet">
    /// <item><b>raw</b> — reaches code the expander drops: subtrees behind <c>if="false"</c>,
    /// templates nobody invokes. Author mistakes there are still author mistakes.</item>
    /// <item><b>expanded</b> — the tree <c>ScreenInstantiator</c> actually builds. Reaches what the
    /// raw view structurally cannot: a template invocation's real parent/child context, attributes
    /// arriving through <c>class=</c>, and the resolved configuration a rule needs before it will
    /// speak at all (<see cref="GlassRules"/>, <see cref="PureContainerVisualAttrRules"/>,
    /// <c>PUI-TAB-PARENT</c>).</item>
    /// </list>
    ///
    /// <para>Lives here rather than in the CLI's <c>Program.cs</c> so it is reachable from
    /// <c>PromptUGUI.Tests.EditMode</c> — the CLI has no test assembly of its own, and this is the
    /// layer where a miscount silently turns a failing lint into exit code 0.</para>
    ///
    /// <para>See the 2026-08-26 theme-driven-style spec §9.</para>
    /// </summary>
    public static class DocumentLinter
    {
        /// <summary>Expansion itself failed — an unknown template / style name, or an Import cycle.</summary>
        public const string ExpansionCode = "PUI-EXPAND";

        /// <summary>
        /// <paramref name="imports"/> resolves an <c>&lt;Import src&gt;</c> to its parsed document.
        /// Pass <c>null</c> when the caller cannot produce the closure — a project may serve its
        /// commons from Addressables or a custom resolver with no filesystem shape at all, and
        /// failing a today-clean document over that would cost more than the missed coverage. The
        /// expanded pass is then skipped and the raw rules still apply, exactly as before.
        /// </summary>
        public static IEnumerable<LintIssue> Walk(
            UIDocument doc, string entrySrc = "<entry>", Func<string, UIDocument> imports = null)
        {
            if (doc == null) yield break;

            var seen = new HashSet<string>();

            foreach (var issue in IRWalker.Walk(doc))
                if (seen.Add(KeyOf(issue)))
                    yield return issue;

            if (imports == null && doc.Imports.Count > 0)
                yield break;

            // Expansion throws instead of collecting, and C# forbids `yield return` inside a catch,
            // so stage the outcome first and emit it below.
            LoadedDoc loaded = null;
            LintIssue? failure = null;
            try
            {
                loaded = DocumentAssembler.Assemble(
                    entrySrc,
                    s => s == entrySrc ? doc : imports?.Invoke(s),
                    allowScreens: true);
            }
            catch (Exception ex) when (ex is TemplateException || ex is ParseException)
            {
                failure = new LintIssue(ExpansionCode, null, null, ex.Message);
            }
            if (failure.HasValue)
            {
                yield return failure.Value;
                yield break;
            }

            // Theme rules run BEFORE expansion is attempted: the most common thing they diagnose —
            // a theme style with no global baseline — is itself what makes expansion throw, and
            // reporting only "unknown style 'card'" would send the author looking in the wrong place.
            var themes = ThemesByName(loaded);

            foreach (var issue in ThemeStyleRules.CheckBaselines(loaded.Styles, themes))
                if (seen.Add(KeyOf(issue)))
                    yield return issue;

            foreach (var issue in ThemeStyleRules.CheckShape(loaded.Styles, themes))
                if (seen.Add(KeyOf(issue)))
                    yield return issue;

            foreach (var issue in CheckInvocationsAcross(loaded, themes))
                if (seen.Add(KeyOf(issue)))
                    yield return issue;

            UIDocument expanded = null;
            try
            {
                expanded = TemplateExpander.Expand(loaded);
            }
            catch (Exception ex) when (ex is TemplateException || ex is ParseException)
            {
                // Reaching UI.Open() to learn that a template or style name does not resolve is the
                // slowest feedback loop there is; surfacing it at write time is the point of a CLI.
                failure = new LintIssue(ExpansionCode, null, null, ex.Message);
            }
            if (failure.HasValue)
            {
                yield return failure.Value;
                yield break;
            }

            foreach (var issue in IRWalker.Walk(expanded))
                if (seen.Add(KeyOf(issue)))
                    yield return issue;

            // Rules that reason about a node's resolved configuration (GlassRules above all) can only
            // answer for ONE skin at a time. Re-derive the tree under each theme and walk again;
            // dedup means a theme that changes nothing relevant costs nothing but the walk. No flag
            // to remember: the themes a document can reach are already declared in it.
            foreach (var themeName in SortedKeys(themes))
            {
                ThemeStyleApplierForLint(expanded, loaded.Styles, themes, themeName);
                foreach (var issue in IRWalker.Walk(expanded))
                    if (seen.Add(KeyOf(issue)))
                        yield return issue;
            }
        }

        private static Dictionary<string, ThemeBlock> ThemesByName(LoadedDoc loaded)
        {
            var themes = new Dictionary<string, ThemeBlock>();
            foreach (var (theme, _) in loaded.Themes)
                themes[theme.Name] = theme;   // a cross-file duplicate already threw in Assemble
            return themes;
        }

        private static IEnumerable<LintIssue> CheckInvocationsAcross(
            LoadedDoc loaded, Dictionary<string, ThemeBlock> themes)
        {
            var themeStyleNames = new HashSet<string>();
            foreach (var theme in themes.Values)
                foreach (var name in theme.Styles.Keys)
                    themeStyleNames.Add(name);
            if (themeStyleNames.Count == 0) yield break;

            var tags = new List<string>();
            foreach (var key in loaded.Templates.Keys) tags.Add(key.ToString());

            // Invocations only exist BEFORE expansion, so this walks the assembled-but-raw screens.
            foreach (var screen in loaded.Screens)
            {
                foreach (var issue in ThemeStyleRules.CheckInvocations(screen.Root, tags, themeStyleNames))
                    yield return issue;
                foreach (var block in screen.Variants)
                    foreach (var add in block.Adds)
                        foreach (var child in add.Children)
                            foreach (var issue in ThemeStyleRules.CheckInvocations(child, tags, themeStyleNames))
                                yield return issue;
            }
            foreach (var tpl in loaded.Templates.Values)
                foreach (var issue in ThemeStyleRules.CheckInvocations(tpl.Body, tags, themeStyleNames))
                    yield return issue;
        }

        private static void ThemeStyleApplierForLint(
            UIDocument expanded,
            IReadOnlyDictionary<StyleKey, StyleDef> globalStyles,
            IReadOnlyDictionary<string, ThemeBlock> themes,
            string themeName)
        {
            var effective = ThemeStyleResolver.Resolve(globalStyles, themes, themeName);
            foreach (var screen in expanded.Screens)
                ThemeStyleApplier.Apply(screen, effective);
        }

        private static List<string> SortedKeys(Dictionary<string, ThemeBlock> themes)
        {
            var names = new List<string>(themes.Keys);
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

        // Identical message => same finding, whichever pass produced it. Code/Tag/Id alone would
        // collapse two genuinely different problems on one node.
        private static string KeyOf(LintIssue i) => $"{i.Code}|{i.Tag}|{i.Id}|{i.Message}";
    }
}
