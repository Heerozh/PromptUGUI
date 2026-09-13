using System;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// The one place a name written for <c>itemTemplate=</c> — or passed to
    /// <c>Screen.Instantiate</c> — turns into a subtree factory.
    /// Resolution order: a <c>&lt;Template&gt;</c> visible to the owner Screen's document (own file,
    /// Imports as <c>ns.Name</c>, commons) first, then a registered Control tag. The four callers
    /// (ScrollList / Carousel / TabGroupCore / Screen.Instantiate) only differ in what they throw
    /// when neither matches, which is why "not found" is a <c>false</c> return rather than an exception.
    /// </summary>
    internal static class TemplateFactoryResolver
    {
        /// <param name="owner">
        /// The Screen whose document the templates are looked up in, and which the produced subtree
        /// is registered on (ReSolve / scale). May be null — a control not inside an open Screen —
        /// in which case only the Control branch can match and the subtree is not registered anywhere.
        /// </param>
        /// <param name="name">Template name (<c>Card</c> / <c>ui.Card</c>) or registered Control tag.</param>
        /// <param name="context">
        /// Prefix for the required-<c>&lt;Param&gt;</c> error, e.g. <c>itemTemplate='Card'</c> or
        /// <c>Instantiate("Card")</c>. Only ever appears in that message.
        /// </param>
        internal static bool TryResolve(Screen owner, string name, string context,
                                        out Func<RectTransform, IControl> factory)
        {
            factory = null;
            if (string.IsNullOrEmpty(name)) return false;

            if (owner != null && owner.Def.Templates.TryGetValue(name, out var tpl))
            {
                ItemTemplateGuard.EnsureInstantiable(context, tpl);
                factory = parent => UI.GetInstantiator().InstantiateNode(tpl.Body, parent, owner);
                return true;
            }

            if (UI.Registry.Has(name))
            {
                factory = parent => UI.GetInstantiator().InstantiateNode(new ElementNode(name), parent, owner);
                return true;
            }

            return false;
        }
    }
}
