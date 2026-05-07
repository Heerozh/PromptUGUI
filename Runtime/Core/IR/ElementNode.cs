using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class ElementNode {
        public string Tag { get; }
        public string Id { get; set; }
        public Dictionary<string, string> Attributes { get; }
        public string TextContent { get; set; }
        public List<ElementNode> Children { get; }

        /// <summary>
        /// True 表示此节点是某个模板调用展开后产生的"实例根"。
        /// 它内部声明的 id 形成一个独立作用域，由 Control.ScopedIds 持有。
        /// 仅由 TemplateExpander 设置；parser 始终为 false。
        /// </summary>
        public bool IsTemplateInstanceRoot { get; set; }

        public ElementNode(string tag) {
            Tag = tag;
            Attributes = new Dictionary<string, string>();
            Children = new List<ElementNode>();
        }
    }
}
