using System.Collections.Generic;

namespace PromptUGUI.IR
{
    public sealed class TemplateDef
    {
        public string Name { get; }
        public List<ParamDef> Params { get; } = new();
        public ElementNode Body { get; set; }    // 必须有且仅有一个根元素
        public string OriginSrc { get; set; }   // 仅 commons reload 时使用；其他场景 null

        /// <summary>
        /// True only on the per-Screen runtime copies whose <see cref="Body"/> went through
        /// expansion (<c>TemplateExpander.BuildRuntimeTemplates</c>). A body kept RAW — because a
        /// required <c>&lt;Param&gt;</c> left nothing to substitute — still carries
        /// <c>{{placeholder}}</c> text, so anything that resolves attribute values against it (a
        /// theme re-merge looking up <c>class=</c>) must leave it alone.
        /// </summary>
        internal bool BodyExpanded { get; set; }

        public TemplateDef(string name) { Name = name; }
    }
}
