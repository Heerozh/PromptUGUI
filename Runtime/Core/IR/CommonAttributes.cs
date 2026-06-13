using System.Collections.Generic;

namespace PromptUGUI.IR
{
    /// <summary>
    /// The attributes <c>ControlAttributeApplier</c> routes through the generic <c>ApplyCommon</c> path
    /// (layout geometry + visibility), as opposed to a control's own <c>[UIAttr]</c> setter. This is the
    /// SINGLE SOURCE OF TRUTH for "is this a common attribute", shared by:
    ///   - the runtime dispatch — <c>ControlAttributeApplier.IsCommonAttribute</c> delegates to <see cref="All"/>;
    ///   - the <c>PUI-VARIANT-NO-BASE</c> lint — its self-heal set is DERIVED from <see cref="All"/>
    ///     (every common attr self-heals EXCEPT <c>hidden</c>, plus <c>scale</c> which lives on a separate path).
    /// Living in Core/IR means the lint CLI (which compiles only Parser/IR/Lint, never Application) can read
    /// it directly — no mirror, no drift. Adding a new common attr here updates both consumers at once.
    ///
    /// <para>NOT to be confused with <c>TemplateExpander.CommonAttrs</c> — that is a different set (the
    /// attributes a template invocation may merge onto the instance root: it adds <c>padding</c>/<c>spacing</c>
    /// and omits <c>flow</c>).</para>
    /// </summary>
    public static class CommonAttributes
    {
        public static readonly HashSet<string> All = new()
        {
            "anchor", "size", "width", "height", "margin", "pivot", "hidden", "interactable", "flow",
        };

        public static bool Contains(string name) => All.Contains(name);
    }
}
