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
        /// <summary>Sentinel empty set passed for template bodies (no screen id context).</summary>
        private static readonly HashSet<string> s_emptyIds = new HashSet<string>();

        public static IEnumerable<LintIssue> Walk(UIDocument doc)
        {
            // Rules that reason about a node's configuration (rather than one written value) have to
            // see attributes arriving through class= as well — see StyleAttributeView.
            var styles = new StyleAttributeView(doc.Styles);

            foreach (var screen in doc.Screens)
            {
                // Collect all ids in this Screen once (pre-expansion, best-effort) for
                // PUI-NAV-UNKNOWN-TARGET. Add-directive children are part of the same Screen scope.
                var screenIds = CollectScreenIds(screen);

                foreach (var issue in WalkNode(screen.Root, inTemplateBody: false, hasStateSourceAncestor: false, parentIsLayoutGroup: false, isTemplateBodyRoot: false, screenIds: screenIds, styles: styles))
                    yield return issue;

                foreach (var variant in screen.Variants)
                    foreach (var add in variant.Adds)
                        foreach (var addChild in add.Children)
                            foreach (var issue in WalkNode(addChild, inTemplateBody: false, hasStateSourceAncestor: false, parentIsLayoutGroup: false, isTemplateBodyRoot: false, screenIds: screenIds, styles: styles))
                                yield return issue;
            }

            // <Style> is not an ElementNode, so its attribute values never reach WalkNode —
            // they get their own pass so a bad radius in a style is caught where it's written.
            foreach (var style in doc.Styles.Values)
                foreach (var issue in StyleRules.CheckStyle(style))
                    yield return issue.WithSource(style.OriginSrc, 0);

            foreach (var template in doc.Templates.Values)
            {
                // Template bodies live in declaration-space, not in any actual TabBar
                // hierarchy. The PARENT check is meaningless here: TabBar.CollectStaticTabs
                // walks Template-expanded wrappers recursively, so a Frame>Tab pattern
                // inside a Template body is intentional structure, not a misuse.
                // The body node IS the template's instance root (isTemplateBodyRoot) — a template
                // invocation may merge CommonAttrs onto it, which PUI-VARIANT-NO-BASE must account for.
                // s_emptyIds: template bodies don't belong to a specific Screen — skip nav-target check.
                if (template.Body != null)
                    foreach (var issue in WalkNode(template.Body, inTemplateBody: true, hasStateSourceAncestor: false, parentIsLayoutGroup: false, isTemplateBodyRoot: true, screenIds: s_emptyIds, styles: styles))
                        yield return issue;
            }
        }

        private static HashSet<string> CollectScreenIds(ScreenDef screen)
        {
            var ids = new HashSet<string>();
            CollectIds(screen.Root, ids);
            foreach (var variant in screen.Variants)
                foreach (var add in variant.Adds)
                    foreach (var child in add.Children)
                        CollectIds(child, ids);
            return ids;
        }

        private static void CollectIds(ElementNode node, HashSet<string> ids)
        {
            if (!string.IsNullOrEmpty(node.Id))
                ids.Add(node.Id);
            foreach (var child in node.Children)
                CollectIds(child, ids);
        }

        /// <summary>
        /// Attributes every finding to the file its node was written in. Doing it here means a rule
        /// never has to think about it, and a finding raised inside an imported Template body names
        /// that library rather than the entry document that merely invoked it.
        ///
        /// <para>Issues that already carry an origin come from the recursive call below — a child
        /// stamps itself, so this must not overwrite them.</para>
        /// </summary>
        private static IEnumerable<LintIssue> WalkNode(ElementNode node, bool inTemplateBody, bool hasStateSourceAncestor, bool parentIsLayoutGroup, bool isTemplateBodyRoot, HashSet<string> screenIds, StyleAttributeView styles)
        {
            foreach (var issue in WalkNodeCore(node, inTemplateBody, hasStateSourceAncestor, parentIsLayoutGroup, isTemplateBodyRoot, screenIds, styles))
                yield return issue.Origin == null ? issue.WithSource(node.OriginSrc, node.Line, node.InvokedAt) : issue;
        }

        private static IEnumerable<LintIssue> WalkNodeCore(ElementNode node, bool inTemplateBody, bool hasStateSourceAncestor, bool parentIsLayoutGroup, bool isTemplateBodyRoot, HashSet<string> screenIds, StyleAttributeView styles)
        {
            // Per-tag self-checks (mirror of ScreenInstantiator dispatch; CLI errors).
            // Self-relative — about the node itself, unlike parent-relative LayoutGroupChildRules.
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    yield return issue;
            else if (node.Tag == "Image")
            {
                foreach (var issue in MaskAttributeRules.CheckImage(node, styles))
                    yield return issue;
                foreach (var issue in ImageFitRules.CheckVariant(node))
                    yield return issue;
                foreach (var issue in ImageFitRules.CheckGeometry(node))
                    yield return issue;
            }
            else if (node.Tag == "RawImage")
                foreach (var issue in MaskAttributeRules.CheckRawImage(node))
                    yield return issue;
            else if (node.Tag == "Progress")
                foreach (var issue in ProgressAttributeRules.CheckProgress(node, styles))
                    yield return issue;
            else if (node.Tag == "TabBar")
                foreach (var issue in TabRules.CheckTabBar(node))
                    yield return issue;
            else if (node.Tag == "Carousel")
                foreach (var issue in CarouselRules.CheckCarousel(node))
                    yield return issue;
            else if (node.Tag == "Markdown")
                foreach (var issue in MarkdownRules.CheckMarkdown(node))
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
            // Exception: a flow="false" child is back in free-positioning semantics — margin is
            // meaningful again, so the inert-side check applies after all.
            if (!parentIsLayoutGroup || LayoutGroupChildRules.MightBeOutOfFlow(node))
                foreach (var issue in MarginAnchorRules.Check(node))
                    yield return issue;

            // Static color literal validation: hex values starting with '#' must parse.
            foreach (var issue in ColorLiteralRules.Check(node))
                yield return issue;

            // class="" plus the value grammar of the procedural visual attrs (radius / borderWidth /
            // glow / the glass parameters) — pure syntax, so the CLI can reject it without a Unity
            // instance.
            foreach (var issue in StyleRules.Check(node))
                yield return issue;

            // CLI-only: glass parameters on a node that never enters glass mode, and weld groups
            // whose members or parameter placement can't work. All silent at runtime.
            foreach (var issue in GlassRules.Check(node, styles))
                yield return issue;
            foreach (var issue in GlassRules.CheckWeldGroup(node, styles))
                yield return issue;

            // Universal: nav*/focus on a non-Selectable tag. Also wired in ScreenInstantiator.
            foreach (var issue in NavTargetRules.CheckNav(node))
                yield return issue;

            // CLI-only: navUp/navDown/navLeft/navRight value not found in the same Screen's id set.
            // Skipped in Template bodies (no screen-id context).
            if (!inTemplateBody)
                foreach (var issue in NavTargetRules.CheckNavTarget(node, screenIds))
                    yield return issue;

            // CLI-only: a control-specific attr with a `.variant` override but no base value won't
            // revert when the variant deactivates (set-only). Self-relative — about the node's own attrs.
            // Gated on built-in tags (rule decides) + the template-instance-root signal (invocation may
            // supply CommonAttr bases). See VariantBaseRules for the verified self-heal whitelist.
            foreach (var issue in VariantBaseRules.Check(node, isTemplateBodyRoot))
                yield return issue;

            // Static reject: a gradient value on a *Modulate multiplier (solid-only, spec §6).
            foreach (var issue in GradientModulateRules.Check(node))
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
            var isLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar" or "Carousel";
            var isTabBar = node.Tag == "TabBar";
            foreach (var child in node.Children)
            {
                if (isLayoutGroup)
                    foreach (var issue in LayoutGroupChildRules.CheckChild(child))
                        yield return issue.WithSource(child.OriginSrc, child.Line, child.InvokedAt);
                else
                    // 'flow' under a non-layout-group parent is inert — flag it. Dispatched
                    // from the child loop only (parent tag truly known): template-body roots,
                    // <Add> roots and screen roots are exempt automatically — their real
                    // parent is resolved at invocation/runtime and may well be an HStack.
                    foreach (var issue in LayoutGroupChildRules.CheckNonLayoutChild(child))
                        yield return issue.WithSource(child.OriginSrc, child.Line, child.InvokedAt);
                if (node.Tag == "Carousel")
                    foreach (var issue in CarouselRules.CheckCard(node, child))
                        yield return issue.WithSource(child.OriginSrc, child.Line, child.InvokedAt);
                // CLI-only: a direct <Grid> child's own size/width/height is overridden by cellSize.
                // Grid-specific (V/HStack children's size IS meaningful), so gated on the parent tag here.
                if (node.Tag == "Grid")
                    foreach (var issue in LayoutGroupChildRules.CheckGridChild(child))
                        yield return issue.WithSource(child.OriginSrc, child.Line, child.InvokedAt);
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
                        "Mutual exclusion and shared visuals will not apply.",
                        child.OriginSrc, child.Line, child.InvokedAt);
                foreach (var issue in WalkNode(child, inTemplateBody, childHasStateSourceAncestor, parentIsLayoutGroup: isLayoutGroup, isTemplateBodyRoot: false, screenIds: screenIds, styles: styles))
                    yield return issue;
            }
        }
    }
}
