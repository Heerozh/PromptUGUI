using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>&lt;Animation reveal&gt;</c> and <c>reverse-on=</c> (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §2.3). String-level detection only —
    /// <c>AnimationSpec</c> lives outside the CLI compile set, and every rule here is about the
    /// shape of the declaration rather than its parsed value.
    /// </summary>
    public static class AnimationRules
    {
        public const string SingleChildCode = "PUI-REVEAL-SINGLE-CHILD";
        public const string SizeConflictCode = "PUI-REVEAL-SIZE-CONFLICT";
        public const string ScaleCode = "PUI-REVEAL-SCALE";
        public const string ChildStretchCode = "PUI-REVEAL-CHILD-STRETCH";
        public const string ReverseLoopCode = "PUI-REVERSE-LOOP";
        public const string ReverseTextCode = "PUI-REVERSE-TEXT";
        public const string ReverseOnTagCode = "PUI-REVERSE-ON-TAG";

        /// <summary>The axis a reveal owns, or null when the node declares no reveal.</summary>
        private static string RevealAxis(ElementNode n)
        {
            if (!n.Attributes.TryGetValue("reveal", out var value)) return null;
            var v = value?.Trim();
            return v == "y" ? "height" : v == "x" ? "width" : null;
        }

        public static bool HasReveal(ElementNode n) => n.Attributes.ContainsKey("reveal");

        /// <summary>Everything checked on an <c>&lt;Animation&gt;</c> node itself.</summary>
        public static IEnumerable<LintIssue> CheckAnimation(ElementNode n)
        {
            var hasReverseOn = n.Attributes.ContainsKey("reverse-on")
                               || n.VariantOverrides.ContainsKey("reverse-on");

            if (hasReverseOn)
            {
                if (n.Attributes.ContainsKey("loop"))
                    yield return new LintIssue(
                        ReverseLoopCode, n.Tag, n.Id,
                        $"<Animation id='{n.Id}'>: reverse-on= and loop= cannot be combined — a looping " +
                        "motion has no resting end state to reverse from.");

                if (n.Attributes.ContainsKey("count") || n.Attributes.ContainsKey("char-color"))
                    yield return new LintIssue(
                        ReverseTextCode, n.Tag, n.Id,
                        $"<Animation id='{n.Id}'>: reverse-on= and count= / char-color= cannot be combined — " +
                        "a number counting backwards has no stable current value to reverse from.");
            }

            var axis = RevealAxis(n);
            if (!HasReveal(n)) yield break;

            if (n.Attributes.ContainsKey("scale") || n.VariantOverrides.ContainsKey("scale"))
                yield return new LintIssue(
                    ScaleCode, n.Tag, n.Id,
                    $"<Animation id='{n.Id}'>: reveal= and scale=\"...\" on the same node — the revealed axis " +
                    "is owned by the layout pass, which would drop the box-preserving inflation that scale " +
                    "applies. Fix: move scale to the revealed child.");

            if (n.Children.Count != 1)
                yield return new LintIssue(
                    SingleChildCode, n.Tag, n.Id,
                    $"<Animation id='{n.Id}'>: reveal= needs exactly one child to measure and clip; " +
                    $"found {n.Children.Count}. Fix: wrap the content in a single <VStack> / <Frame>.");

            if (axis != null)
            {
                if (Declares(n, axis) || Declares(n, "size"))
                    yield return new LintIssue(
                        SizeConflictCode, n.Tag, n.Id,
                        $"<Animation id='{n.Id}'>: reveal=\"{n.Attributes["reveal"]}\" already owns the " +
                        $"{axis}, so writing {axis}= / size= on the same node is a contradiction — the reveal " +
                        "overwrites it on every pass. Fix: drop it, or use reveal-from / reveal-to to set the " +
                        "endpoints.");

                if (n.Children.Count == 1)
                {
                    var child = n.Children[0];
                    if (child.Attributes.TryGetValue("anchor", out var anchor)
                        && StretchesOn(anchor, axis))
                        yield return new LintIssue(
                            ChildStretchCode, child.Tag, child.Id,
                            $"<{child.Tag} id='{child.Id}'>: anchor=\"{anchor}\" stretches on the axis its " +
                            "<Animation reveal> is animating — the child would follow the box while the box " +
                            "measures the child. Fix: give the child a fixed size on that axis (or hug it).");
                }
            }
        }

        /// <summary><c>reverse-on=</c> anywhere it cannot do anything.</summary>
        public static IEnumerable<LintIssue> CheckReverseOnTag(ElementNode n)
        {
            if (n.Tag == "Animation") yield break;
            if (!n.Attributes.ContainsKey("reverse-on") && !n.VariantOverrides.ContainsKey("reverse-on"))
                yield break;

            yield return new LintIssue(
                ReverseOnTagCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: reverse-on= is only supported on <Animation> — a <{n.Tag}> has no " +
                "motion to play backwards. Fix: for a second event stream in C#, add another <Trigger>.");
        }

        private static bool Declares(ElementNode n, string attr)
            => n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr);

        /// <summary>Whether an anchor preset stretches on the given axis ("width" / "height").</summary>
        private static bool StretchesOn(string anchor, string axis)
        {
            var a = anchor?.Trim();
            if (string.IsNullOrEmpty(a)) return false;
            if (a == "stretch" || a == "fill") return true;
            var dash = a.IndexOf('-');
            if (dash < 0) return false;
            var vertical = a.Substring(0, dash);
            var horizontal = a.Substring(dash + 1);
            return axis == "height"
                ? string.Equals(vertical, "stretch", StringComparison.Ordinal)
                : string.Equals(horizontal, "stretch", StringComparison.Ordinal);
        }
    }
}
