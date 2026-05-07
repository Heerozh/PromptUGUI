using System;

namespace PromptUGUI.Registry {
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UIAttrAttribute : Attribute {
        public string Name { get; }
        public UIAttrAttribute(string name = null) { Name = name; }
    }
}
