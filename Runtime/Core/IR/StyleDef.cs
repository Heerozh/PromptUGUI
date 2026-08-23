using System.Collections.Generic;

namespace PromptUGUI.IR
{
    /// <summary>
    /// A named attribute pack declared by a top-level <c>&lt;Style name="..."&gt;</c> and pulled onto
    /// nodes with <c>class="..."</c>. Deliberately NOT a style engine: no selectors, no cascade, no
    /// inheritance — just "these attribute names/values, merged at expansion time". That is why it
    /// works on every control and every attribute (including <c>.variant</c> suffixes) without the
    /// controls knowing styles exist, and why nothing about it survives into the runtime tree.
    /// </summary>
    public sealed class StyleDef
    {
        public string Name { get; }

        /// <summary>Base attribute values, mirroring <see cref="ElementNode.Attributes"/>.</summary>
        public Dictionary<string, string> Attributes { get; } = new();

        /// <summary>
        /// <c>attr.variant</c> overrides, mirroring <see cref="ElementNode.VariantOverrides"/>.
        /// Merged verbatim onto the target node, so the runtime <c>VariantResolver</c> resolves them
        /// exactly as if the author had written them inline.
        /// </summary>
        public Dictionary<string, List<(string Variant, string Value)>> VariantOverrides { get; } = new();

        /// <summary>Only set for commons-pool entries, so a commons reload can swap by origin. Mirrors <see cref="TemplateDef.OriginSrc"/>.</summary>
        public string OriginSrc { get; set; }

        public StyleDef(string name) { Name = name; }

        /// <summary>
        /// Every attribute name this style declares, in either form. The merge treats a name as
        /// atomic — declaring <c>radius.mobile</c> alone still masks a lower-precedence
        /// <c>radius</c> — so callers need the union, not just the base keys.
        /// </summary>
        public IEnumerable<string> DeclaredNames
        {
            get
            {
                foreach (var k in Attributes.Keys) yield return k;
                foreach (var k in VariantOverrides.Keys)
                    if (!Attributes.ContainsKey(k)) yield return k;
            }
        }
    }
}
