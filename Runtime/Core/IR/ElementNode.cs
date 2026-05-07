using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class ElementNode {
        public string Tag { get; }
        public string Id { get; set; }
        public Dictionary<string, string> Attributes { get; }
        public string TextContent { get; set; }
        public List<ElementNode> Children { get; }

        public ElementNode(string tag) {
            Tag = tag;
            Attributes = new Dictionary<string, string>();
            Children = new List<ElementNode>();
        }
    }
}
