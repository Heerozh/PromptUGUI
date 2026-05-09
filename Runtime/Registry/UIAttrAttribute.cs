using System;

namespace PromptUGUI.Registry
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UIAttrAttribute : Attribute
    {
        public string Name { get; }
        /// <summary>
        /// Optional XSD pattern (regex) for value validation. Uses XSD pattern syntax —
        /// implicitly anchored to the entire value, so do NOT include `^` or `$`
        /// (they're treated as literal characters). Prefer ASCII char classes
        /// (`[A-Za-z0-9_-]`) over `\w` to match runtime parser behavior.
        /// </summary>
        public string Pattern { get; set; }

        public UIAttrAttribute(string name = null) { Name = name; }
    }
}
