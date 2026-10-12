using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for <c>&lt;Cooldown&gt;</c> (spec 2026-10-13 §6), shared by <c>IRWalker</c> (the
    /// UIXmlLint CLI) and <c>ScreenInstantiator</c> (runtime warnings) — single source of truth, like
    /// <see cref="ProgressAttributeRules"/>. <see cref="CheckSources"/> is the screen-wide half: a
    /// bare <c>ready</c> / <c>cooling</c> trigger resolves to the NEAREST Cooldown, which no per-node
    /// rule can see.
    /// </summary>
    public static class CooldownRules
    {
        public const string Tag = "Cooldown";

        public const string ValueRangeCode = "PUI-CD-VALUE-RANGE";
        public const string EnumCode = "PUI-CD-ENUM";
        public const string ChildrenCode = "PUI-CD-CHILDREN";
        /// <summary>cap="round" on something that is not a circular ring: drawn with flat ends.</summary>
        public const string RoundCapCode = "PUI-CD-ROUND-CAP";
        /// <summary>thickness / cap on a bitmap fill: ignored — the bitmap is swept as drawn.</summary>
        public const string BitmapShapeCode = "PUI-CD-BITMAP-SHAPE";
        public const string GlassCode = "PUI-CD-GLASS";
        public const string TenthsCode = "PUI-CD-TENTHS";
        public const string NoSourceCode = "PUI-CD-NO-SOURCE";
        public const string AmbiguousCode = "PUI-CD-AMBIGUOUS";

        /// <summary>
        /// The codes <c>ScreenInstantiator</c> repeats as runtime warnings. The rest the control
        /// reports itself: an unknown keyword or a bad <c>tenthsBelow</c> is a parse error from the
        /// setter, a refused glass and a round cap that falls back are warned by the control and its
        /// panel — each once, with the same code — and the source rules are the trigger's own throw.
        /// </summary>
        public static readonly HashSet<string> RuntimeCodes = new HashSet<string>
        {
            ValueRangeCode, ChildrenCode, BitmapShapeCode,
        };

        private static readonly (string Attr, string[] Values)[] Keywords =
        {
            ("origin", new[] { "top", "right", "bottom", "left" }),
            ("draw", new[] { "remaining", "elapsed" }),
            ("cap", new[] { "flat", "round" }),
            ("time", new[] { "scaled", "unscaled" }),
        };

        public static IEnumerable<LintIssue> Check(ElementNode n) => Check(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            if (n == null || n.Tag != Tag) yield break;
            styles ??= StyleAttributeView.Empty;

            // value: literals only — a bound {{…}} parses as non-numeric and is skipped.
            foreach (var raw in Written(n, "value"))
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    && (v < 0f || v > 1f))
                {
                    yield return new LintIssue(
                        ValueRangeCode, n.Tag, n.Id,
                        $"<Cooldown id='{n.Id}'>: value='{raw}' is outside [0..1] (the remaining fraction) " +
                        "and will be clamped.");
                    break;
                }

            foreach (var (attr, values) in Keywords)
                foreach (var raw in Written(n, attr))
                {
                    var v = raw.Trim();
                    if (v.Length == 0 || v.Contains("{{") || System.Array.IndexOf(values, v) >= 0) continue;
                    yield return new LintIssue(
                        EnumCode, n.Tag, n.Id,
                        $"<Cooldown id='{n.Id}'>: {attr}='{raw}' is invalid. Valid: {string.Join(", ", values)}.");
                    break;
                }

            foreach (var raw in Written(n, "tenthsBelow"))
            {
                if (raw.Contains("{{")) continue;
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t >= 0f)
                    continue;
                yield return new LintIssue(
                    TenthsCode, n.Tag, n.Id,
                    $"<Cooldown id='{n.Id}'>: tenthsBelow='{raw}' must be a number of seconds ≥ 0 " +
                    "(0 = never show tenths).");
                break;
            }

            if (n.Children.Count > 0)
                yield return new LintIssue(
                    ChildrenCode, n.Tag, n.Id,
                    $"<Cooldown id='{n.Id}'>: Cooldown is a leaf and takes no child elements. Put it BESIDE " +
                    "what it covers — e.g. inside the <Btn>, after the icon.");

            // The rest reason about the node's configuration, so they have to see class= too; a class
            // this document cannot resolve makes the answer unknowable.
            if (styles.IsUncertain(n)) yield break;

            if (AnyTrue(styles, n, "glass"))
                yield return new LintIssue(
                    GlassCode, n.Tag, n.Id,
                    $"<Cooldown id='{n.Id}'>: glass is not supported — glass samples the scene behind the " +
                    "canvas, not the icon under the veil. The fill is drawn opaque.");

            styles.Resolve(n, "fill", out var fill, out var fillVariants);
            var bitmapFill = IsBitmap(fill);
            foreach (var (_, value) in fillVariants) bitmapFill |= IsBitmap(value);
            if (bitmapFill && (styles.Declares(n, "thickness") || styles.Declares(n, "cap")))
                yield return new LintIssue(
                    BitmapShapeCode, n.Tag, n.Id,
                    $"<Cooldown id='{n.Id}'>: thickness / cap shape the procedural fill, and fill='{fill}' is a " +
                    "bitmap — Image.Filled sweeps the bitmap as drawn, so they are ignored. Use a ring sprite, " +
                    "or drop fill= and give fillColor=.");
            else if (AnyEquals(styles, n, "cap", "round"))
            {
                var why = RoundCapProblem(n, styles);
                if (why != null)
                    yield return new LintIssue(
                        RoundCapCode, n.Tag, n.Id,
                        $"<Cooldown id='{n.Id}'>: cap='round' {why} Round ends are drawn only on a circular " +
                        "ring — thickness= with radius='pill' on a square rect; anything else gets flat ends.");
            }
        }

        private static string RoundCapProblem(ElementNode n, StyleAttributeView styles)
        {
            styles.Resolve(n, "thickness", out var thickness, out var thicknessVariants);
            var ring = PositiveNumber(thickness);
            foreach (var (_, value) in thicknessVariants) ring |= PositiveNumber(value);
            if (!ring) return "has no ring to cap (no thickness=).";

            styles.Resolve(n, "radius", out var radius, out var radiusVariants);
            if (Circular(radius, n)) return null;
            foreach (var (_, value) in radiusVariants)
                if (Circular(value, n)) return null;
            return $"is on a shape that is not a circle (radius='{radius}').";
        }

        /// <summary><c>pill</c>, or a number reaching half of a square <c>size</c> written on the node.</summary>
        private static bool Circular(string radius, ElementNode n)
        {
            if (radius == null) return false;
            radius = radius.Trim();
            if (radius == "pill" || radius.Contains("{{")) return true;
            if (!float.TryParse(radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) return false;
            if (!n.Attributes.TryGetValue("size", out var size)) return false;
            var x = size.IndexOf('x');
            if (x <= 0) return false;
            return float.TryParse(size.Substring(0, x), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                   && float.TryParse(size.Substring(x + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                   && w == h && r >= w * 0.5f;
        }

        // ── the nearest-source rule (§5.8) ─────────────────────────────────────────────────

        private static readonly HashSet<string> TriggerTags = new HashSet<string> { "Trigger", "Animation", "Show" };

        /// <summary>
        /// Every bare <c>ready</c> / <c>cooling</c> trigger under <paramref name="root"/> (a Screen
        /// root), resolved the way the runtime does: its own subtree, then each enclosing node's
        /// subtree, stopping at the first level that holds any <c>&lt;Cooldown&gt;</c>. A level that
        /// holds a Template invocation (seen before expansion) cannot be counted, so the rule stops
        /// there in silence; the expanded pass sees the real nodes. A Screen with <c>&lt;Add&gt;</c>
        /// blocks may grow a Cooldown at runtime, so "none at all" is not reported there. Issues are
        /// stamped with the trigger's own source, as this does not go through the per-node walk.
        /// </summary>
        public static IEnumerable<LintIssue> CheckSources(ElementNode root, bool screenHasAdds)
        {
            var found = new List<LintIssue>();
            if (root == null) return found;
            Visit(root, new List<ElementNode>(), screenHasAdds, found);
            return found;
        }

        private static void Visit(ElementNode n, List<ElementNode> ancestors, bool screenHasAdds, List<LintIssue> found)
        {
            if (TriggerTags.Contains(n.Tag) && n.Namespace == null && !n.IsTemplateInstanceRoot)
                foreach (var attr in new[] { "on", "reverse-on" })
                    if (n.Attributes.TryGetValue(attr, out var on) && (on == "ready" || on == "cooling"))
                    {
                        var issue = Resolve(n, attr, on, ancestors, screenHasAdds);
                        if (issue.HasValue) found.Add(issue.Value.WithSource(n.OriginSrc, n.Line, n.InvokedAt));
                    }

            ancestors.Add(n);
            foreach (var child in n.Children) Visit(child, ancestors, screenHasAdds, found);
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        private static LintIssue? Resolve(ElementNode trigger, string attr, string on,
                                          List<ElementNode> ancestors, bool screenHasAdds)
        {
            for (var i = ancestors.Count; i >= 0; i--)
            {
                var scope = i == ancestors.Count ? trigger : ancestors[i];
                var count = 0;
                var uncertain = false;
                Count(scope, ref count, ref uncertain);
                if (count > 1)
                    return new LintIssue(
                        AmbiguousCode, trigger.Tag, trigger.Id,
                        $"<{trigger.Tag} {attr}=\"{on}\">: ambiguous — the nearest level holds {count} " +
                        $"<Cooldown>s. Use {attr}=\"{on}@<id>\" to pick one.");
                if (uncertain) return null;
                if (count == 1) return null;
            }
            if (screenHasAdds) return null;
            return new LintIssue(
                NoSourceCode, trigger.Tag, trigger.Id,
                $"<{trigger.Tag} {attr}=\"{on}\">: no <Cooldown> in its subtree or around it. {on} is the " +
                $"state of the nearest <Cooldown> — add one, or point at it with {attr}=\"{on}@<id>\".");
        }

        /// <summary>Counts the Cooldowns below <paramref name="node"/>; a Template invocation makes the count uncertain.</summary>
        private static void Count(ElementNode node, ref int count, ref bool uncertain)
        {
            foreach (var child in node.Children)
            {
                if (child.Namespace == null && child.Tag == Tag)
                {
                    count++;
                    continue;
                }
                if (child.Namespace != null || !BuiltinTags.IsBuiltin(child.Tag)) uncertain = true;
                Count(child, ref count, ref uncertain);
            }
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────

        /// <summary>The node's own base value and every variant override of <paramref name="attr"/>.</summary>
        private static IEnumerable<string> Written(ElementNode n, string attr)
        {
            if (n.Attributes.TryGetValue(attr, out var v) && v != null) yield return v;
            if (n.VariantOverrides.TryGetValue(attr, out var overrides))
                foreach (var (_, value) in overrides)
                    if (value != null) yield return value;
        }

        private static bool AnyTrue(StyleAttributeView styles, ElementNode n, string attr) =>
            AnyEquals(styles, n, attr, "true");

        private static bool AnyEquals(StyleAttributeView styles, ElementNode n, string attr, string expected)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            if (baseValue != null && baseValue.Trim() == expected) return true;
            foreach (var (_, value) in variants)
                if (value != null && value.Trim() == expected) return true;
            return false;
        }

        private static bool IsBitmap(string fill) =>
            !string.IsNullOrWhiteSpace(fill) && fill != "none" && !fill.Contains("{{");

        private static bool PositiveNumber(string value) =>
            value != null
            && (value.Contains("{{")
                || (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0f));
    }
}
