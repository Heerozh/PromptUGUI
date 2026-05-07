using System.Collections.Generic;

namespace PromptUGUI.IR {
    public sealed class TemplateDef {
        public string Name { get; }
        public List<ParamDef> Params { get; } = new();
        public ElementNode Body { get; set; }    // 必须有且仅有一个根元素

        public TemplateDef(string name) { Name = name; }
    }
}
