using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Walks a parsed <see cref="UIDocument"/> and applies all lint rules to each node.
    /// Used by the <c>UIXmlLint</c> CLI; pure C# so it can be unit-tested without Unity.
    /// </summary>
    public static class IRWalker
    {
        public static IEnumerable<LintIssue> Walk(UIDocument doc)
        {
            foreach (var screen in doc.Screens)
            {
                foreach (var issue in WalkNode(screen.Root, inTemplateBody: false, hasStateSourceAncestor: false, parentIsLayoutGroup: false))
                    yield return issue;

                foreach (var variant in screen.Variants)
                    foreach (var add in variant.Adds)
                        foreach (var addChild in add.Children)
                            foreach (var issue in WalkNode(addChild, inTemplateBody: false, hasStateSourceAncestor: false, parentIsLayoutGroup: false))
                                yield return issue;
            }

            foreach (var template in doc.Templates.Values)
            {
                // Template bodies live in declaration-space, not in any actual TabBar
                // hierarchy. The PARENT check is meaningless here: TabBar.CollectStaticTabs
                // walks Template-expanded wrappers recursively, so a Frame>Tab pattern
                // inside a Template body is intentional structure, not a misuse.
                if (template.Body != null)
                    foreach (var issue in WalkNode(template.Body, inTemplateBody: true, hasStateSourceAncestor: false, parentIsLayoutGroup: false))
                        yield return issue;
            }
        }

        private static IEnumerable<LintIssue> WalkNode(ElementNode node, bool inTemplateBody, bool hasStateSourceAncestor, bool parentIsLayoutGroup)
        {
            // Per-tag self-checks (mirror of ScreenInstantiator dispatch; CLI errors).
            // Self-relative — about the node itself, unlike parent-relative LayoutGroupChildRules.
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    yield return issue;
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    yield return issue;
            else if (node.Tag == "Progress")
                foreach (var issue in ProgressAttributeRules.CheckProgress(node))
                    yield return issue;
            else if (node.Tag == "TabBar")
                foreach (var issue in TabRules.CheckTabBar(node))
                    yield return issue;

            // CLI-only: pure containers carry no Graphic; sprite/color silently dropped.
            // Intentionally NOT dispatched from ScreenInstantiator — see rule's XML docs.
            if (PureContainerVisualAttrRules.AppliesTo(node.Tag))
                foreach (var issue in PureContainerVisualAttrRules.Check(node))
                    yield return issue;

            // CLI-only: a margin slot set on a side this anchor doesn't consume is silently
            // dropped (spec §6.2). Self-relative — about the node's own anchor + margin.
            // Skipped under a layout group: margin is wholly ignored there and PUI-LAYOUT-MARGIN
            // already owns that message, so an inert-side error would be a redundant second hit.
            if (!parentIsLayoutGroup)
                foreach (var issue in MarginAnchorRules.Check(node))
                    yield return issue;

            // Static color literal validation: hex values starting with '#' must parse.
            foreach (var issue in ColorLiteralRules.Check(node))
                yield return issue;

            // Ancestor-aware (like PUI-TAB-PARENT, but upward): a bare state-* trigger /
            // animation / show resolves to its nearest clickable (<Btn>/<Tab>/<Toggle>) ancestor
            // at runtime. With no such ancestor it hard-throws (TriggerSourceResolver.FindStateSource);
            // surface it statically here. Exempt Template bodies + instance roots — the clickable
            // ancestor may be supplied only at invocation. @id forms defer to runtime ScopedIds resolution.
            if (!inTemplateBody && !node.IsTemplateInstanceRoot)
                foreach (var issue in StateTriggerRules.CheckStateSource(node, hasStateSourceAncestor))
                    yield return issue;

            var childHasStateSourceAncestor = hasStateSourceAncestor || StateTriggerRules.IsStateSourceTag(node.Tag);
            var isLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar";
            var isTabBar = node.Tag == "TabBar";
            foreach (var child in node.Children)
            {
                if (isLayoutGroup)
                    foreach (var issue in LayoutGroupChildRules.CheckChild(child))
                        yield return issue;
                // Exempt Template-instance roots and bodies: <Tab> wrapped in a
                // Template (e.g. <Template name='FileTab'><Frame><Tab/>...) is
                // intentional — TabBar.CollectStaticTabs walks wrappers via FindTabIn.
                if (child.Tag == "Tab"
                    && !isTabBar
                    && !node.IsTemplateInstanceRoot
                    && !inTemplateBody)
                    yield return new LintIssue(
                        TabRules.TabParentCode, child.Tag, child.Id,
                        $"<Tab id='{child.Id}'>: must be a direct child of <TabBar>; current parent is <{node.Tag}>. " +
                        "Mutual exclusion and shared visuals will not apply.");
                foreach (var issue in WalkNode(child, inTemplateBody, childHasStateSourceAncestor, parentIsLayoutGroup: isLayoutGroup))
                    yield return issue;
            }
        }
    }
}
