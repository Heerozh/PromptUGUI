using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class ElementNode {
        public string Tag { get; }
        public string Namespace { get; }
        public string Id { get; set; }
        public Dictionary<string, string> Attributes { get; }
        public string TextContent { get; set; }
        public List<ElementNode> Children { get; }

        /// <summary>
        /// Pre-substitution textContent. Filled by parser AND TemplateExpander preserves it through
        /// to expanded nodes (parser fills raw, expander leaves it alone but updates TextArgs).
        /// Only Text/Btn-shaped controls consume this; other tags ignore.
        /// </summary>
        public string TextContentRaw { get; set; }

        /// <summary>
        /// Template instantiation arguments captured by TemplateExpander when the node was produced
        /// from a Template expansion. Empty / null on parser-produced nodes.
        /// Used at runtime so TrResolver can re-substitute on the translated msgstr.
        /// </summary>
        public Dictionary<string, string> TextArgs { get; set; }

        /// <summary>
        /// Pre-substitution attribute values, populated for attributes whose VALUE contained {{...}}
        /// (e.g. <code>text="Gold: {{n}}"</code>). Other attributes can be retrieved from Attributes
        /// directly. Used at runtime for the same reason as TextContentRaw.
        /// </summary>
        public Dictionary<string, string> AttributesRaw { get; set; }

        /// <summary>
        /// True 表示此节点是某个模板调用展开后产生的"实例根"。
        /// 它内部声明的 id 形成一个独立作用域，由 Control.ScopedIds 持有。
        /// 仅由 TemplateExpander 设置；parser 始终为 false。
        /// </summary>
        public bool IsTemplateInstanceRoot { get; set; }

        /// <summary>
        /// Variant 属性覆盖：原属性名（无后缀）→ 一个有序列表 [(variantName, value), ...]。
        /// 列表顺序就是 XML 中 `attr.varName="..."` 出现的声明顺序；多个后缀可共存。
        /// 仅 parser 写入；instantiator/resolver 只读。
        /// 同一 attrName 在 Attributes 与 VariantOverrides 中可同时存在；前者为基础值，
        /// 后者按 last-active-wins 选取覆盖（spec §8.3）。
        /// </summary>
        public Dictionary<string, List<(string Variant, string Value)>> VariantOverrides { get; }

        public ElementNode(string tag, string ns = null) {
            Tag = tag;
            Namespace = ns;
            Attributes = new Dictionary<string, string>();
            AttributesRaw = new Dictionary<string, string>();
            Children = new List<ElementNode>();
            VariantOverrides = new Dictionary<string, List<(string Variant, string Value)>>();
        }
    }
}
