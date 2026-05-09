namespace PromptUGUI.IR
{
    public sealed class ImportRef
    {
        public string Src { get; }
        public string Namespace { get; }   // null = 无命名空间
        public ImportRef(string src, string ns)
        {
            Src = src;
            Namespace = ns;
        }
    }
}
