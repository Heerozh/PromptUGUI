using System.Collections.Generic;

namespace PromptUGUI.IR
{
    public sealed class ScreenDef
    {
        public string Name { get; }
        public ElementNode Root { get; }
        public List<VariantBlock> Variants { get; } = new();
        public CanvasMode CanvasMode { get; set; } = CanvasMode.Overlay;

        public ScreenDef(string name, ElementNode root)
        {
            Name = name;
            Root = root;
        }
    }
}
