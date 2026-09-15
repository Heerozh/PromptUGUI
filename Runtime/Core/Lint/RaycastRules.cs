using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>raycastTarget</c> (spec 2026-09-15 §5). Pointer hit-testing is declared, not painted:
    /// only an interactive control's own hit layer and a node marked <c>raycastTarget="true"</c>
    /// are ever hit, so the two things that can go wrong are the attribute landing on a tag that
    /// drops it (<see cref="TagCode"/>), and a panel that draws but never said whether it blocks
    /// what is behind it (<see cref="UndecidedCode"/>).
    ///
    /// <para><see cref="CheckUndecided"/> is a screen-level walk, not a per-node rule: it asks the
    /// OUTERMOST drawn surface on each path down from the root, and stops at the first answer —
    /// inside a <c>raycastTarget="true"</c> node (or an interactive control) every surface is
    /// already covered, and once an undecided one is reported, the ones inside it are not
    /// interesting until it is settled. CLI-only, like <see cref="PureContainerVisualAttrRules"/>:
    /// a click-through panel is not broken, it is undecided, and a Console warning on every open
    /// would be noise.</para>
    /// </summary>
    public static class RaycastRules
    {
        public const string Attr = "raycastTarget";
        public const string TagCode = "PUI-RAYCAST-TAG";
        public const string UndecidedCode = "PUI-RAYCAST-UNDECIDED";

        /// <summary>The tags that expose the attribute (Frame / Image / RawImage / Text).</summary>
        public static readonly HashSet<string> ExposingTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "Frame", "Image", "RawImage", "Text",
        };

        // No Graphic at all: nothing the raycaster could return.
        private static readonly HashSet<string> LayoutOnlyTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "VStack", "HStack", "Grid", "SafeArea", "Show", "Trigger", "Animation",
        };

        // Hard-wired click-through: their Graphics never take the pointer (PB-D16, Decor, Icon).
        private static readonly HashSet<string> AlwaysThroughTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "Icon", "Decor", "Progress",
        };

        // Their own hit layer is always on; there is nothing to configure and everything inside
        // them is already under a catcher.
        private static readonly HashSet<string> InteractiveTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "Btn", "Toggle", "Tab", "TabBar", "TabMenu", "Collapsible", "Slider", "Scrollbar",
            "Dropdown", "InputField", "ScrollList", "Carousel", "Markdown",
        };

        // ===== PUI-RAYCAST-TAG =====

        public static IEnumerable<LintIssue> CheckTag(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || ExposingTags.Contains(n.Tag)) yield break;
            if (!BuiltinTags.IsBuiltin(n.Tag)) yield break;   // a template invocation: root unknown here
            if (!styles.Declares(n, Attr)) yield break;

            string why;
            if (LayoutOnlyTags.Contains(n.Tag))
                why = "it has no Graphic — nothing the raycaster could return. Put it on the <Frame> / <Image> that draws.";
            else if (AlwaysThroughTags.Contains(n.Tag))
                why = "it is always click-through. Wrap it in a <Frame raycastTarget=\"true\"> to catch the pointer there.";
            else
                why = "it catches the pointer by definition; its hit layer is not configurable.";

            yield return new LintIssue(
                TagCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: raycastTarget= is only supported on <Frame> / <Image> / <RawImage> / <Text> — " +
                $"on <{n.Tag}> {why}");
        }

        // ===== PUI-RAYCAST-UNDECIDED =====

        /// <summary>
        /// Walks down from <paramref name="root"/> (a Screen root, or an Add-block child, whose host
        /// is not known statically and is therefore treated as outermost) and reports the outermost
        /// drawn surfaces that carry no <c>raycastTarget</c>. Issues are stamped with the node's
        /// own source here, since this does not go through <c>IRWalker.WalkNode</c>.
        /// </summary>
        public static IEnumerable<LintIssue> CheckUndecided(ElementNode root, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            var found = new List<LintIssue>();
            if (root == null) return found;
            // The parser's stand-in for <Screen> itself is not a control; start at what it holds.
            if (root.Tag == ScreenRootTag)
                foreach (var child in root.Children) Visit(child, styles, found);
            else
                Visit(root, styles, found);
            return found;
        }

        private const string ScreenRootTag = "__screen_root__";

        private static void Visit(ElementNode n, StyleAttributeView styles, List<LintIssue> found)
        {
            if (n == null) return;

            // A class this document cannot resolve may carry the decision — stay quiet, whole subtree.
            if (styles.IsUncertain(n)) return;

            if (styles.Declares(n, Attr))
            {
                // "true" (in the base, or in any variant) covers what is inside; "false" is a
                // decoration, so what is inside still has to be looked at.
                if (DeclaresTrue(n, styles)) return;
            }
            else if (InteractiveTags.Contains(n.Tag) || AlwaysThroughTags.Contains(n.Tag))
            {
                // Interactive: covered by its own hit layer. Always-through: has no attribute to
                // write, so there is nothing to decide.
                return;
            }
            else if (!BuiltinTags.IsBuiltin(n.Tag))
            {
                // A template invocation seen before expansion: its root is unknown here, and the
                // expanded pass judges the real node in place.
                return;
            }
            else if (IsDrawnSurface(n, styles))
            {
                found.Add(new LintIssue(
                    UndecidedCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'> is the outermost drawn surface here and does not say whether it " +
                    "catches the pointer. Write raycastTarget=\"true\" (a panel that blocks what is behind it) " +
                    "or raycastTarget=\"false\" (a decoration; clicks pass through).")
                    .WithSource(n.OriginSrc, n.Line, n.InvokedAt));
                return;
            }

            foreach (var child in n.Children)
                Visit(child, styles, found);
        }

        private static bool DeclaresTrue(ElementNode n, StyleAttributeView styles)
        {
            styles.Resolve(n, Attr, out var baseValue, out var variants);
            if (IsTrue(baseValue)) return true;
            foreach (var (_, value) in variants)
                if (IsTrue(value)) return true;
            return false;
        }

        private static bool IsTrue(string value)
            => value != null && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// An <c>&lt;Image&gt;</c> / <c>&lt;RawImage&gt;</c> always draws a quad; a <c>&lt;Frame&gt;</c>
        /// draws once any procedural visual attribute (<see cref="ProceduralAttrNames.All"/>, weld
        /// included) reaches it, inline or through a class.
        /// </summary>
        private static bool IsDrawnSurface(ElementNode n, StyleAttributeView styles)
        {
            if (n.Tag == "Image" || n.Tag == "RawImage") return true;
            if (n.Tag != "Frame") return false;
            foreach (var attr in ProceduralAttrNames.All)
                if (styles.Declares(n, attr)) return true;
            return false;
        }
    }
}
