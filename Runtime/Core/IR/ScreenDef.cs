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

        public ScreenDef(string name, ElementNode root)
        {
            Name = name;
            Root = root;
        }
    }
}
