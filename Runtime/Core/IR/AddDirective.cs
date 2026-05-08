using System.Collections.Generic;

namespace PromptUGUI.IR {
    /// <summary>
    /// `<Variant when="..."><Add into="#id|@root" at="start|end|N">...</Add></Variant>`
    /// 中的单条 Add 指令。在 Variant 激活时把 Children 实例化到 IntoPath 指向的父节点。
    /// </summary>
    public sealed class AddDirective {
        public string IntoPath { get; set; }      // "#id" / "#id/path/to/inner" / "@root"
        public string At { get; set; } = "end";   // "start" / "end" / 整数字符串
        public List<ElementNode> Children { get; } = new();
    }
}
