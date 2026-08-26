using System.Collections.Generic;

namespace PromptUGUI.IR
{
    public sealed class ElementNode
    {
        public string Tag { get; }
        public string Namespace { get; }
        public string Id { get; set; }
        public Dictionary<string, string> Attributes { get; }
        public string TextContent { get; set; }
        public List<ElementNode> Children { get; }

        /// <summary>
        /// Where the Template invocation that produced this node was written, preformatted as
        /// <c>file:line</c>; null on nodes that were not produced by one.
        ///
        /// <para><see cref="OriginSrc"/> plus <see cref="Line"/> say where the markup was DECLARED,
        /// which is where a fix goes. That is not enough on its own when one template is invoked ten
        /// times and a single instance is wrong — all ten findings would read identically. This says
        /// WHICH instance. It records the OUTERMOST invocation deliberately: nested ones are
        /// structural and identical across instances, so they cannot tell two apart.</para>
        /// </summary>
        public string InvokedAt { get; set; }

        /// <summary>
        /// Which attribute names the <c>class=</c> pack contributed at the last merge. Non-null iff
        /// the node's class has been resolved (an empty set is a real answer: every packed name was
        /// already spelled out inline).
        ///
        /// <para>This is what makes a theme switch re-computable: dropping exactly these names
        /// leaves inline attributes — and the common attributes a template invocation merged onto an
        /// instance root AFTER the style pack — untouched, so the new pack can be laid down over a
        /// clean slate without a separate snapshot of the author's own values.</para>
        /// </summary>
        public System.Collections.Generic.HashSet<string> StyleAttrNames { get; set; }

        /// <summary>
        /// 1-based line in <see cref="OriginSrc"/> where this node's opening tag starts; 0 when
        /// unknown (a synthesised node, or a caller that parsed without line info). Travels with
        /// <see cref="OriginSrc"/> through expansion so a lint finding can name a place, not a file.
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// The src this node's markup was written in, stamped by <c>UIDocumentParser.Parse(xml, src)</c>
        /// and carried through every expansion copy. Lint reports against it so a finding inside an
        /// imported Template body names the file the author has to open, not the entry document that
        /// merely invoked it. Null when the caller parsed without a src (the caller's own path is
        /// then the only sensible attribution).
        /// </summary>
        public string OriginSrc { get; set; }

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

        public ElementNode(string tag, string ns = null)
        {
            Tag = tag;
            Namespace = ns;
            Attributes = new Dictionary<string, string>();
            AttributesRaw = new Dictionary<string, string>();
            Children = new List<ElementNode>();
            VariantOverrides = new Dictionary<string, List<(string Variant, string Value)>>();
        }
    }
}
