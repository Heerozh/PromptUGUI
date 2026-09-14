using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Turns "where was this node written" into text — the ONE spelling of it, so a Console line
    /// and a UIXmlLint line can be grepped with the same string. The CLI prints
    /// <c>origin:line: [CODE] msg (via origin:line)</c>; the runtime appends
    /// <c>  at &lt;Tag id='x'&gt; origin:line (via …)</c> to a warning / error (see
    /// <c>PromptUGUI.Application.UILog</c>). Both read <see cref="ElementNode.OriginSrc"/> /
    /// <see cref="ElementNode.Line"/> / <see cref="ElementNode.InvokedAt"/>, which the parser stamps
    /// and expansion carries — so the place named is where the markup was DECLARED (a template body
    /// inside an imported library), which is where the fix goes; the invocation is context.
    ///
    /// <para>The origin is the src key the caller handed the SourceResolver (<c>UI.LoadDocument</c>'s
    /// label, an <c>&lt;Import src&gt;</c>), not a filesystem path: the runtime never sees one.
    /// It is the name the author already knows the file by.</para>
    /// </summary>
    public static class SourceLocation
    {
        /// <summary>
        /// <c>origin:line</c>; <c>origin</c> alone when the line is unknown; null when the origin
        /// is — a line without a file is not a place anyone can open.
        /// </summary>
        public static string Format(string origin, int line)
        {
            if (string.IsNullOrEmpty(origin)) return null;
            return line > 0 ? origin + ":" + line : origin;
        }

        /// <summary>
        /// <c> (via origin:line)</c> when the template invocation names a different place than
        /// <paramref name="where"/>; empty otherwise. The declaration stays primary — the
        /// invocation only earns its space when it adds information.
        /// </summary>
        public static string Via(string where, string via)
            => string.IsNullOrEmpty(via) || via == where ? "" : " (via " + via + ")";

        /// <summary><c>origin:line (via …)</c>, or null when the node carries no origin.</summary>
        public static string Of(ElementNode node)
        {
            var where = Format(node.OriginSrc, node.Line);
            return where == null ? null : where + Via(where, node.InvokedAt);
        }

        /// <summary>
        /// <c>&lt;Tag id='x'&gt; origin:line (via …)</c>. Just <c>&lt;Tag id='x'&gt;</c> for a node
        /// nobody wrote (Markdown output, a caller that parsed without a src): the tag is still
        /// worth saying, a made-up place is not.
        /// </summary>
        public static string Describe(ElementNode node)
            => Describe(node.Tag, node.Id, node.OriginSrc, node.Line, node.InvokedAt);

        /// <summary>Same shape for a finding that has been stamped (see <see cref="IRWalker"/>).</summary>
        public static string Describe(LintIssue issue)
            => Describe(issue.Tag, issue.Id, issue.Origin, issue.Line, issue.Via);

        private static string Describe(string tag, string id, string origin, int line, string via)
        {
            var head = string.IsNullOrEmpty(id) ? "<" + tag + ">" : "<" + tag + " id='" + id + "'>";
            var where = Format(origin, line);
            return where == null ? head : head + " " + where + Via(where, via);
        }
    }
}
