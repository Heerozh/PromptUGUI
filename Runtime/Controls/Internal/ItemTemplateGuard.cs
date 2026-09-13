using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared by every consumer that instantiates a template WITHOUT an invocation: the three
    /// <c>itemTemplate=</c> hosts (<c>ScrollList</c> / <c>TabBar</c> / <c>Carousel</c>) and
    /// <c>Screen.Instantiate</c> — all through <c>TemplateFactoryResolver</c>.
    /// </summary>
    internal static class ItemTemplateGuard
    {
        /// <summary>
        /// A template instantiated with no invocation has only its own <c>&lt;Param&gt;</c>
        /// defaults for arguments. A REQUIRED param has no value at all: <c>TemplateExpander</c>
        /// therefore leaves such a body unexpanded, and instantiating it would apply
        /// <c>{{name}}</c> to a real attribute — which throws deep inside the bind, where R3 turns
        /// it into a console error about a colour token and the list just comes up empty. Say what
        /// is actually wrong, at the call that names the template, instead.
        /// </summary>
        /// <param name="context">
        /// Names the call site in the message: <c>itemTemplate='Card'</c> for the hosts,
        /// <c>Instantiate("Card")</c> for the C# API.
        /// </param>
        public static void EnsureInstantiable(string context, TemplateDef tpl)
        {
            List<string> required = null;
            foreach (var p in tpl.Params)
            {
                if (p.HasDefault) continue;
                (required ??= new List<string>()).Add(p.Name);
            }
            if (required == null) return;

            throw new ParseException(
                $"{context}: <Template name='{tpl.Name}'> has required <Param> " +
                $"{string.Join(", ", required)} with no default. A template instantiated this way " +
                "has no invocation to supply them — give each a default=, or use a template that " +
                "needs no arguments.");
        }
    }
}
