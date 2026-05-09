using System;

namespace PromptUGUI.Registry
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class BindAttribute : Attribute
    {
        public string ChildName { get; }
        public BindAttribute(string childName = null) { ChildName = childName; }
    }
}
