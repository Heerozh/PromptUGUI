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
                foreach (var issue in WalkNode(screen.Root))
                    yield return issue;

                foreach (var variant in screen.Variants)
                    foreach (var add in variant.Adds)
                        foreach (var addChild in add.Children)
                            foreach (var issue in WalkNode(addChild))
                                yield return issue;
            }

            foreach (var template in doc.Templates.Values)
            {
                if (template.Body != null)
                    foreach (var issue in WalkNode(template.Body))
                        yield return issue;
            }
        }

        private static IEnumerable<LintIssue> WalkNode(ElementNode node)
        {
            // Self-checks (tag-specific). Mask rules are about the node itself,
            // not its parent (unlike LayoutGroupChildRules which is parent-relative).
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    yield return issue;
            else if (node.Tag == "Image")
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    yield return issue;

            var isLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var child in node.Children)
            {
                if (isLayoutGroup)
                    foreach (var issue in LayoutGroupChildRules.CheckChild(child))
                        yield return issue;
                foreach (var issue in WalkNode(child))
                    yield return issue;
            }
        }
    }
}
