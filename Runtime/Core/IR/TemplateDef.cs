using System.Collections.Generic;

namespace PromptUGUI.IR
{
    public sealed class TemplateDef
    {
        public string Name { get; }
        public List<ParamDef> Params { get; } = new();
        public ElementNode Body { get; set; }    // 必须有且仅有一个根元素
        public string OriginSrc { get; set; }   // 仅 commons reload 时使用；其他场景 null

        public TemplateDef(string name) { Name = name; }
    }
}
