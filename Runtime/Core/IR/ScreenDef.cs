using System.Collections.Generic;

namespace PromptUGUI.IR
{
    public sealed class ScreenDef
    {
        public string Name { get; }
        public ElementNode Root { get; }
        public List<VariantBlock> Variants { get; } = new();
        public CanvasMode CanvasMode { get; set; } = CanvasMode.Overlay;

        /// <summary>
        /// 与本 Screen 同 doc 的 Template 集合（已合并 commons / Imports）。
        /// 由 TemplateExpander.Expand 填充；用于 ScrollList 等运行时需要按 tag 实例化模板的控件。
        /// Key 格式与 UIDocument.Templates 一致：单段 "Name" 或 "ns.Name"。
        /// </summary>
        public Dictionary<string, TemplateDef> Templates { get; } = new();

        /// <summary>
        /// The document's global <c>&lt;Style&gt;</c> table, attached by
        /// <c>TemplateExpander.Expand</c> for the same reason <see cref="Templates"/> is: the runtime
        /// needs it after the LoadedDoc is gone. A theme switch folds its packs over this table and
        /// re-merges every <c>class=</c> node in the Screen.
        /// </summary>
        internal Dictionary<StyleKey, StyleDef> Styles { get; } = new();

        /// <summary>该 Screen 声明的 &lt;FocusCursor&gt; 节点（解析时从 Root.Children 抽出）；null = 未声明，
        /// 运行时回退全局默认光标（spec §5.2）。</summary>
        public ElementNode FocusCursor { get; set; }

        public ScreenDef(string name, ElementNode root)
        {
            Name = name;
            Root = root;
        }
    }
}
