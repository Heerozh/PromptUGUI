namespace PromptUGUI.Lint
{
    public readonly struct LintIssue
    {
        public string Code { get; }
        public string Tag { get; }
        public string Id { get; }
        public string Message { get; }

        public LintIssue(string code, string tag, string id, string message)
        {
            Code = code;
            Tag = tag;
            Id = id;
            Message = message;
        }
    }
}
