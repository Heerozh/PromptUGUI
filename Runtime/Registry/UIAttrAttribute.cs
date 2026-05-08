using System;

namespace PromptUGUI.Registry {
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UIAttrAttribute : Attribute {
        public string Name { get; }
        /// <summary>Optional XSD pattern (regex) for value validation.</summary>
        public string Pattern { get; set; }

        public UIAttrAttribute(string name = null) { Name = name; }
    }
}
