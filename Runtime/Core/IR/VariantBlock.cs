using System.Collections.Generic;

namespace PromptUGUI.IR {
    /// <summary>
    /// `<Variant when="X">...</Variant>` 块。当变体 X 激活时，依次执行内部的 Adds。
    /// </summary>
    public sealed class VariantBlock {
        public string When { get; }
        public List<AddDirective> Adds { get; } = new();

        public VariantBlock(string when) { When = when; }
    }
}
