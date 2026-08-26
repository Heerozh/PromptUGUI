using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared by the three <c>itemTemplate=</c> consumers (<c>ScrollList</c> / <c>TabBar</c> /
    /// <c>Carousel</c>).
    /// </summary>
    internal static class ItemTemplateGuard
    {
        /// <summary>
        /// An itemTemplate is instantiated with no invocation, so a template's own
        /// <c>&lt;Param&gt;</c> defaults are the only arguments available. A REQUIRED param has no
        /// value at all: <c>TemplateExpander</c> therefore leaves such a body unexpanded, and
        /// instantiating it would apply <c>{{name}}</c> to a real attribute — which throws deep
        /// inside the bind, where R3 turns it into a console error about a colour token and the list
        /// just comes up empty. Say what is actually wrong, at Open, instead.
        /// </summary>
        public static void EnsureInstantiable(string tag, TemplateDef tpl)
        {
            List<string> required = null;
            foreach (var p in tpl.Params)
            {
                if (p.HasDefault) continue;
                (required ??= new List<string>()).Add(p.Name);
            }
            if (required == null) return;

            throw new ParseException(
                $"itemTemplate='{tag}': <Template name='{tpl.Name}'> has required <Param> " +
                $"{string.Join(", ", required)} with no default. An item template is instantiated " +
                "without an invocation, so there is nothing to supply them — give each a default=, " +
                "or point itemTemplate at a template that needs no arguments.");
        }
    }
}
