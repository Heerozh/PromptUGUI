using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>&lt;Markdown&gt; content comes from text= (or inline CDATA); child elements are ignored.</summary>
    public static class MarkdownRules
    {
        public const string NoChildrenCode = "PUI-MARKDOWN-NO-CHILDREN";

        public static IEnumerable<LintIssue> CheckMarkdown(ElementNode n)
        {
            if (n.Children.Count > 0)
                yield return new LintIssue(
                    NoChildrenCode, n.Tag, n.Id,
                    $"<Markdown id='{n.Id}'>: content comes from text= (or inline CDATA); " +
                    "child elements are ignored. Remove them.");
        }
    }
}
