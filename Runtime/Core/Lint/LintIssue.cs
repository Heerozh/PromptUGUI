namespace PromptUGUI.Lint
{
    public readonly struct LintIssue
    {
        public string Code { get; }
        public string Tag { get; }
        public string Id { get; }
        public string Message { get; }

        /// <summary>
        /// The src the offending node was written in (<see cref="IR.ElementNode.OriginSrc"/>), or
        /// null when unknown. Rules never set it — <see cref="IRWalker"/> stamps it centrally, so a
        /// new rule gets correct file attribution for free.
        /// </summary>
        public string Origin { get; }

        /// <summary>1-based line in <see cref="Origin"/>, or 0 when unknown.</summary>
        public int Line { get; }

        /// <summary>
        /// <c>file:line</c> of the Template invocation this node came from, or null. Distinguishes
        /// instances when one template is invoked many times; see <see cref="IR.ElementNode.InvokedAt"/>.
        /// </summary>
        public string Via { get; }

        public LintIssue(string code, string tag, string id, string message,
                         string origin = null, int line = 0, string via = null)
        {
            Code = code;
            Tag = tag;
            Id = id;
            Message = message;
            Origin = origin;
            Line = line;
            Via = via;
        }

        /// <summary>Same finding, attributed to a place in the source.</summary>
        public LintIssue WithSource(string origin, int line, string via = null) =>
            new LintIssue(Code, Tag, Id, Message, origin, line, via);
    }
}
