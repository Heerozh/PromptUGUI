using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// A free-positioned element's <c>margin</c> only offsets it from the edge(s) its
    /// <c>anchor</c> actually consumes (spec §6.2 / <c>MarginResolver</c>):
    /// <list type="bullet">
    /// <item>a stretched axis consumes BOTH of its margin slots (they pull the size in);</item>
    /// <item>a point anchor (top/bottom/left/right) consumes ONLY the slot on its own side;</item>
    /// <item>a centered axis consumes NEITHER slot.</item>
    /// </list>
    /// A non-zero value in a slot the anchor doesn't consume is silently dropped — e.g. the common
    /// <c>margin="60,_,_,_"</c> (top=60) under a <c>bottom</c> anchor does nothing. This rule
    /// surfaces that at lint time.
    ///
    /// <para>CLI-only: dispatched from <see cref="IRWalker"/>, intentionally NOT from
    /// <c>ScreenInstantiator</c>. The failure mode is silent no-op (author confusion), not visible
    /// breakage, so a runtime <c>Debug.LogWarning</c> would be noise — same call the
    /// <see cref="PureContainerVisualAttrRules"/> rule makes.</para>
    ///
    /// <para>Scope (kept narrow to avoid false positives):</para>
    /// <list type="bullet">
    /// <item>Only the explicit <c>anchor=</c> form is checked. With <c>anchor</c> omitted the default
    /// preset is control-specific (Frame: per-axis stretch/top-left; others: top-left) and that logic
    /// lives in the Unity-side <c>Controls/</c> layer the lint Core can't reach.</item>
    /// <item>Only the 4-component (per-side) <c>margin</c> form is checked. 1- / 2-component
    /// shorthands are symmetric "inset" values that always land on the consumed side(s), so flagging
    /// them would be a false positive.</item>
    /// <item>Base attributes only — <c>anchor</c>/<c>margin</c> in a Variant override are not
    /// cross-resolved (consistent with the CLI's "no Variant resolution" scope).</item>
    /// </list>
    /// </summary>
    public static class MarginAnchorRules
    {
        public const string InertSideCode = "PUI-MARGIN-INERT-SIDE";

        public static IEnumerable<LintIssue> Check(ElementNode n)
        {
            // Explicit anchor + 4-component margin only — see class doc for why.
            if (!n.Attributes.TryGetValue("anchor", out var anchorStr) || string.IsNullOrEmpty(anchorStr))
                yield break;
            if (!n.Attributes.TryGetValue("margin", out var marginStr) || string.IsNullOrEmpty(marginStr))
                yield break;
            if (!TryParseAnchor(anchorStr, out var preset))
                yield break; // invalid anchor — reported by the runtime parse path, not here.

            var parts = marginStr.Split(',');
            if (parts.Length != 4)
                yield break; // symmetric shorthand always lands on the consumed side.

            // Slot order is top,right,bottom,left (see MarginResolver.Parse).
            // A stretched axis consumes both its slots; a point anchor only its own side.
            // A fractional axis (width="46%" / width="clamp(min, N%, max)") behaves like stretch for
            // margin purposes — the child spans an anchor sub-range and both margins inset into it
            // (Control.ApplyCommon routes it through MarginResolver's stretch branch) — so it too
            // consumes both slots.
            var fracX = IsFractionalAxis(n, "width");
            var fracY = IsFractionalAxis(n, "height");
            var consumedTop = preset.StretchY || fracY || preset.V == AnchorVertical.Top;
            var consumedRight = preset.StretchX || fracX || preset.H == AnchorHorizontal.Right;
            var consumedBottom = preset.StretchY || fracY || preset.V == AnchorVertical.Bottom;
            var consumedLeft = preset.StretchX || fracX || preset.H == AnchorHorizontal.Left;

            if (!consumedTop && IsNonZero(parts[0]))
                yield return Inert(n, anchorStr, "top", parts[0], preset, vertical: true);
            if (!consumedRight && IsNonZero(parts[1]))
                yield return Inert(n, anchorStr, "right", parts[1], preset, vertical: false);
            if (!consumedBottom && IsNonZero(parts[2]))
                yield return Inert(n, anchorStr, "bottom", parts[2], preset, vertical: true);
            if (!consumedLeft && IsNonZero(parts[3]))
                yield return Inert(n, anchorStr, "left", parts[3], preset, vertical: false);
        }

        // Base attribute only (same scope as anchor / margin above). '%' and 'clamp(' are the two
        // value forms that put an axis on the fractional path; both are pure string checks so the
        // CLI needs no SizeSpec.
        private static bool IsFractionalAxis(ElementNode n, string attr) =>
            n.Attributes.TryGetValue(attr, out var v) && !string.IsNullOrEmpty(v)
            && (v.TrimEnd().EndsWith("%", System.StringComparison.Ordinal) || ClampRules.IsClampValue(v));

        private static bool TryParseAnchor(string s, out AnchorPreset p)
        {
            try { p = AnchorPreset.Parse(s); return true; }
            catch (System.ArgumentException) { p = default; return false; }
        }

        private static bool IsNonZero(string raw)
        {
            var v = raw.Trim();
            if (v.Length == 0 || v == "_") return false;
            // Malformed components throw at runtime in MarginResolver; not this rule's concern.
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
            return f != 0f;
        }

        private static LintIssue Inert(
            ElementNode n, string anchorStr, string side, string rawValue, AnchorPreset preset, bool vertical)
        {
            var axisName = vertical ? "vertical" : "horizontal";
            var anchorWord = (vertical ? preset.V.ToString() : preset.H.ToString()).ToLowerInvariant();
            var consumedSide = vertical
                ? (preset.V == AnchorVertical.Top ? "top" : preset.V == AnchorVertical.Bottom ? "bottom" : null)
                : (preset.H == AnchorHorizontal.Left ? "left" : preset.H == AnchorHorizontal.Right ? "right" : null);

            var message = consumedSide != null
                ? $"<{n.Tag} id='{n.Id}'>: the '{side}' margin ({rawValue.Trim()}) has no effect under " +
                  $"anchor='{anchorStr}'. A '{anchorWord}' {axisName} anchor only reads the '{consumedSide}' " +
                  "margin (slots are top,right,bottom,left). Put the offset in the " +
                  $"'{consumedSide}' slot, or change the anchor."
                : $"<{n.Tag} id='{n.Id}'>: the '{side}' margin ({rawValue.Trim()}) has no effect under " +
                  $"anchor='{anchorStr}'. A {axisName}-centered anchor reads neither margin on that axis " +
                  "(slots are top,right,bottom,left); margin can't offset a centered axis — use a " +
                  "'stretch' anchor on that axis, or position via pivot.";

            return new LintIssue(InertSideCode, n.Tag, n.Id, message);
        }
    }
}
