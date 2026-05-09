using System.Collections.Generic;

namespace PromptUGUI.IR
{
    public sealed class UIDocument
    {
        public int Version { get; set; } = 1;
        public List<ScreenDef> Screens { get; } = new();
        public Dictionary<string, TemplateDef> Templates { get; } = new();
        public List<ImportRef> Imports { get; } = new();
    }
}
