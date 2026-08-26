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
            UIDocument expanded = null;
            LintIssue? expansionFailure = null;
            try
            {
                var loaded = DocumentAssembler.Assemble(
                    entrySrc,
                    s => s == entrySrc ? doc : imports?.Invoke(s),
                    allowScreens: true);
                expanded = TemplateExpander.Expand(loaded);
            }
            catch (Exception ex) when (ex is TemplateException || ex is ParseException)
            {
                // Reaching UI.Open() to learn that a template or style name does not resolve is the
                // slowest feedback loop there is; surfacing it at write time is the point of a CLI.
                expansionFailure = new LintIssue(ExpansionCode, null, null, ex.Message);
            }

            if (expansionFailure.HasValue)
            {
                yield return expansionFailure.Value;
                yield break;
            }

            foreach (var issue in IRWalker.Walk(expanded))
                if (seen.Add(KeyOf(issue)))
                    yield return issue;
        }

        // Identical message => same finding, whichever pass produced it. Code/Tag/Id alone would
        // collapse two genuinely different problems on one node.
        private static string KeyOf(LintIssue i) => $"{i.Code}|{i.Tag}|{i.Id}|{i.Message}";
    }
}
