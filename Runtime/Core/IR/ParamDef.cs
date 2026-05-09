namespace PromptUGUI.IR
{
    public sealed class ParamDef
    {
        public string Name { get; }
        public string DefaultValue { get; }       // null = 必填
        public bool HasDefault => DefaultValue != null;

        public ParamDef(string name, string defaultValue)
        {
            Name = name;
            DefaultValue = defaultValue;
        }
    }
}
