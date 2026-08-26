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

        public LintIssue(string code, string tag, string id, string message, string origin = null)
        {
            Code = code;
            Tag = tag;
            Id = id;
            Message = message;
            Origin = origin;
        }

        /// <summary>Same finding, attributed to <paramref name="origin"/>.</summary>
        public LintIssue WithOrigin(string origin) =>
            new LintIssue(Code, Tag, Id, Message, origin);
    }
}
