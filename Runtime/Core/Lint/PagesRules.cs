using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for <c>&lt;Pages&gt;</c> (spec 2026-09-17-pages-design §4.5). Every direct child is
    /// a page, exactly one of them active at a time, so the structure has to hold: a page needs an
    /// id to be selectable, <c>selected</c> has to name one, and a page must not declare
    /// <c>hidden</c> — the container owns its pages' activeSelf, and a declared <c>hidden</c> is
    /// replayed by every ReSolve on top of it.
    ///
    /// <para>Consumed by <see cref="IRWalker"/> (CLI) and <c>ScreenInstantiator</c> (runtime
    /// warnings). At runtime the control itself reports <see cref="ChildIdCode"/> and
    /// <see cref="SelectedCode"/> together with what it does about them (the page stays inactive /
    /// the first page wins), so the instantiator skips those two. <see cref="CheckAddTargets"/>
    /// needs the Screen's Variant blocks and is CLI-only.</para>
    /// </summary>
    public static class PagesRules
    {
        public const string Tag = "Pages";
        public const string ChildIdCode = "PUI-PAGES-CHILD-ID";
        public const string SelectedCode = "PUI-PAGES-SELECTED";
        public const string ChildHiddenCode = "PUI-PAGES-CHILD-HIDDEN";
        public const string EmptyCode = "PUI-PAGES-EMPTY";
        public const string AddTargetCode = "PUI-PAGES-ADD-TARGET";

        public static IEnumerable<LintIssue> CheckPages(ElementNode n) => CheckPages(n, StyleAttributeView.Empty);

        /// <summary>The self-checks on one <c>&lt;Pages&gt;</c> node: its pages and its <c>selected</c>.</summary>
        public static IEnumerable<LintIssue> CheckPages(ElementNode n, StyleAttributeView styles)
        {
            if (n == null || n.Tag != Tag) yield break;
            styles ??= StyleAttributeView.Empty;

            if (n.Children.Count == 0)
            {
                yield return new LintIssue(
                    EmptyCode, n.Tag, n.Id,
                    $"<Pages id='{n.Id}'> has no pages: every direct child is a page, and there is none.");
                yield break;
            }

            var ids = new List<string>();
            foreach (var c in n.Children)
            {
                if (string.IsNullOrEmpty(c.Id))
                    yield return new LintIssue(
                        ChildIdCode, c.Tag, c.Id,
                        $"<Pages id='{n.Id}'>: direct child <{c.Tag}> has no id, so it can never be selected " +
                        "and stays inactive. Every page needs an id.")
                        .WithSource(c.OriginSrc, c.Line, c.InvokedAt);
                else
                    ids.Add(c.Id);

                // Through a class too (expanded pass); an unresolvable class keeps this quiet.
                if (!styles.IsUncertain(c) && styles.Declares(c, "hidden"))
                    yield return new LintIssue(
                        ChildHiddenCode, c.Tag, c.Id,
                        $"<Pages id='{n.Id}'>: page <{c.Tag} id='{c.Id}'> declares hidden. The container owns its " +
                        "pages' visibility, and a declared hidden is replayed by every ReSolve on top of it — " +
                        "drop it and pick the page with selected= on the <Pages> (selected.portrait= for a variant).")
                        .WithSource(c.OriginSrc, c.Line, c.InvokedAt);
            }

            var pages = string.Join(", ", ids);
            if (n.Attributes.TryGetValue("selected", out var baseValue) && !ids.Contains(baseValue))
                yield return new LintIssue(
                    SelectedCode, n.Tag, n.Id,
                    $"<Pages id='{n.Id}'>: selected='{baseValue}' names no page (pages: {pages}).");
            if (n.VariantOverrides.TryGetValue("selected", out var overrides))
                foreach (var (variant, value) in overrides)
                    if (!ids.Contains(value))
                        yield return new LintIssue(
                            SelectedCode, n.Tag, n.Id,
                            $"<Pages id='{n.Id}'>: selected.{variant}='{value}' names no page (pages: {pages}).");
        }

        /// <summary>
        /// Screen-wide: a Variant <c>&lt;Add&gt;</c> whose target is a <c>&lt;Pages&gt;</c>. Pages are
        /// static in v1 — the block and the container would both drive the added page's activeSelf.
        /// </summary>
        public static IEnumerable<LintIssue> CheckAddTargets(ScreenDef screen)
        {
            if (screen == null) yield break;
            var pagesIds = new HashSet<string>();
            CollectPagesIds(screen.Root, pagesIds);
            if (pagesIds.Count == 0) yield break;

            foreach (var variant in screen.Variants)
                foreach (var add in variant.Adds)
                {
                    var target = TargetId(add.IntoPath);
                    if (target == null || !pagesIds.Contains(target)) continue;
                    var issue = new LintIssue(
                        AddTargetCode, Tag, target,
                        $"<Add into='{add.IntoPath}'> targets <Pages id='{target}'>: pages added by a Variant block " +
                        "are not supported (the block and the container would both drive the page's activeSelf). " +
                        "Declare every page statically and vary selected= (selected.mobile=\"…\") instead.");
                    var first = add.Children.Count > 0 ? add.Children[0] : null;
                    yield return first != null ? issue.WithSource(first.OriginSrc, first.Line, first.InvokedAt) : issue;
                }
        }

        private static void CollectPagesIds(ElementNode n, HashSet<string> ids)
        {
            if (n == null) return;
            if (n.Tag == Tag && !string.IsNullOrEmpty(n.Id)) ids.Add(n.Id);
            foreach (var c in n.Children) CollectPagesIds(c, ids);
        }

        // "#id" / "#id/path/to/inner" → the last segment; "@root" and anything else → null.
        private static string TargetId(string intoPath)
        {
            if (string.IsNullOrEmpty(intoPath) || intoPath[0] != '#') return null;
            var path = intoPath.Substring(1);
            var slash = path.LastIndexOf('/');
            var last = slash >= 0 ? path.Substring(slash + 1) : path;
            return last.Length == 0 ? null : last;
        }
    }
}
