using System.Runtime.CompilerServices;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// <c>Debug.LogWarning</c> / <c>LogError</c> for anything that is ABOUT a node the author wrote.
    /// Appends a second line saying where — <c>  at &lt;Icon id='bell'&gt; screens/home:12 (via …)</c>,
    /// the same string <see cref="SourceLocation"/> gives the UIXmlLint CLI — and hands Unity the
    /// GameObject as context so a click on the Console entry pings it in the Hierarchy. The
    /// collapsed Console shows exactly two lines, so the place is readable without expanding.
    ///
    /// <para>Three ways to say which node, by how much the caller knows:</para>
    /// <list type="bullet">
    /// <item>the <see cref="Control"/> itself — its <see cref="Control.SourceNode"/>;</item>
    /// <item>any Component / GameObject inside a control's tree (an FxImage, a TMP label): the
    /// nearest owning control is found by walking up the transforms through the registry
    /// <see cref="Register"/> fills. Components never learn about nodes, so a new one gets
    /// attribution for free;</item>
    /// <item>nothing at all: the node whose attributes <see cref="ControlAttributeApplier"/> is
    /// applying right now (<see cref="UI.ResolveSprite"/> is a static every <c>sprite=</c> setter
    /// calls, with no node in hand).</item>
    /// </list>
    /// </summary>
    internal static class UILog
    {
        // ── the outlets ──────────────────────────────────────────────────────────────────────

        public static void Warn(Control control, string message)
            => Debug.LogWarning(message + At(NodeOf(control)), control?.GameObject);

        public static void Error(Control control, string message)
            => Debug.LogError(message + At(NodeOf(control)), control?.GameObject);

        public static void Warn(Object context, string message)
            => Debug.LogWarning(message + At(NodeOf(context)), context);

        public static void Error(Object context, string message)
            => Debug.LogError(message + At(NodeOf(context)), context);

        /// <summary>No control in hand: the node being applied right now is the only context.</summary>
        public static void Error(string message) => Debug.LogError(message + At(Applying));

        /// <summary>
        /// A lint rule's finding, raised by <see cref="ScreenInstantiator"/> for a node it is about
        /// to build. Stamped here the way <see cref="IRWalker"/> stamps it for the CLI (a rule that
        /// already named a place keeps it). No GameObject to ping yet — the checks run first.
        /// </summary>
        public static void Warn(ElementNode node, LintIssue issue)
            => Debug.LogWarning(issue.Message + At(Stamp(node, issue)));

        // ── the at-line ──────────────────────────────────────────────────────────────────────

        /// <summary><c>\n  at &lt;Tag id='x'&gt; origin:line (via …)</c>; empty for null.</summary>
        public static string At(ElementNode node)
            => node == null ? "" : "\n  at " + SourceLocation.Describe(node);

        public static string At(LintIssue issue) => "\n  at " + SourceLocation.Describe(issue);

        public static LintIssue Stamp(ElementNode node, LintIssue issue)
            => issue.Origin == null ? issue.WithSource(node.OriginSrc, node.Line, node.InvokedAt) : issue;

        // ── which node ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The node <see cref="ControlAttributeApplier"/> is applying attributes from, for as long
        /// as it is. Main thread only, like everything else here.
        /// </summary>
        public static ElementNode Applying { get; set; }

        // Weak on the GameObject: an entry lives exactly as long as the control's own reference
        // to its GameObject does, so nothing has to unregister on Dispose or scene teardown.
        private static readonly ConditionalWeakTable<GameObject, Control> s_owners = new();

        public static void Register(Control control)
        {
            var go = control.GameObject;
            if (go == null) return;
            s_owners.Remove(go);
            s_owners.Add(go, control);
        }

        private static ElementNode NodeOf(Control control)
        {
            for (var c = control; c != null; c = c.Parent)
                if (c.SourceNode != null) return c.SourceNode;
            // Not in the description tree (a default Scrollbar the host built itself): the nearest
            // control above it in the transform hierarchy is the one the author wrote.
            var go = control?.GameObject;
            var above = go != null ? go.transform.parent : null;
            return above != null ? NodeOf(OwnerOf(above)) : Applying;
        }

        private static ElementNode NodeOf(Object context)
        {
            var owner = OwnerOf(context);
            return owner != null ? NodeOf(owner) : Applying;
        }

        private static Control OwnerOf(Object context)
        {
            if (context == null) return null; // Unity's ==: a destroyed object counts as none
            var t = context is Component c ? c.transform : context is GameObject g ? g.transform : null;
            for (; t != null; t = t.parent)
                if (s_owners.TryGetValue(t.gameObject, out var owner)) return owner;
            return null;
        }
    }
}
