using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// PUI-VARIANT-NO-BASE: a control-specific attribute carrying a <c>attr.&lt;variant&gt;</c> override
    /// but no base <c>attr=</c> does not revert when that variant deactivates. <c>ControlAttributeApplier</c>'s
    /// per-attribute loop does <c>if (v == null) continue;</c> — once the active variant clears and there is
    /// no base, the attribute resolves to <c>null</c> and the setter is simply never called, so the
    /// last-applied value sticks (e.g. a window resized portrait→landscape stays in the portrait-only
    /// layout). Pairing the override with a base value gives the deactivated state something to revert to.
    ///
    /// <para>CLI-only: a write-time authoring concern. The defect manifests on a later variant switch, not
    /// at <c>UI.Open()</c>, so a per-Open runtime warning would be the wrong moment (and the registry-blind
    /// runtime gains nothing the author can't see statically). It also can only reason about built-in tags
    /// (see the <c>BuiltinTags</c> gate below) — a custom control's setters are invisible pre-expansion.</para>
    ///
    /// <para>The self-heal whitelist is verified against the runtime, NOT the authoring SKILL.
    /// <c>ControlAttributeApplier.ApplyCommon</c> recomputes anchor/size/width/height/margin/pivot every
    /// pass (null → control default) and coerces interactable/flow to their true-defaults;
    /// <c>Screen.ApplyScales</c> resets <c>localScale</c> to identity when <c>scale</c> resolves to null.
    /// <c>hidden</c> is DELIBERATELY EXCLUDED: <c>ApplyCommon</c> applies it via
    /// <c>if (hidden.HasValue) Hidden = …</c> (Control.cs), so a null-resolving <c>hidden</c> is skipped,
    /// not reset — it behaves exactly like a control-specific setter and must carry a base.</para>
    /// </summary>
    public static class VariantBaseRules
    {
        public const string NoBaseCode = "PUI-VARIANT-NO-BASE";

        // Attrs whose base-less variant override cleanly reverts (the runtime's generic reset path) —
        // see the class doc for the per-attribute justification (verified against ApplyCommon / ApplyScales).
        private static readonly HashSet<string> SelfHealing = new()
        {
            "anchor", "size", "width", "height", "margin", "pivot", "interactable", "flow", "scale",
        };

        // Never reach a control setter (resolved at parse / expansion / instantiation time), so a
        // ".variant" form is meaningless rather than a revert bug.
        private static readonly HashSet<string> NotSetters = new()
        {
            "tr", "ctx", "id", "if",
        };

        // Owned by PUI-MASK-VARIANT in ALL cases (switching mask mode is unsupported, base or not);
        // its "pick one config / split screens" advice supersedes this rule's "add a base".
        private static readonly HashSet<string> MaskFamily = new()
        {
            "mask", "showMask", "maskPadding",
        };

        // Mirror of <c>PromptUGUI.Template.TemplateExpander.CommonAttrs</c> — the attributes a template
        // invocation is allowed to merge onto the instance ROOT as a base. The lint layer can't compile
        // Core/Template, so it mirrors the set (same idiom as <c>BuiltinTags</c>); kept honest by
        // VariantBaseRulesTests.InvocationMergeableMirror_MatchesTemplateExpander_NoDrift. On a template
        // body root these may be supplied by the caller, so a base-less override there is not provably stuck.
        internal static readonly HashSet<string> InvocationMergeableOntoTemplateRoot = new()
        {
            "anchor", "size", "width", "height", "margin", "pivot",
            "padding", "spacing",
            "hidden", "interactable",
        };

        public static IEnumerable<LintIssue> Check(ElementNode n, bool isTemplateBodyRoot = false)
        {
            // Only built-in controls have setters this rule can reason about. Non-builtin tags are template
            // invocations (param semantics, not setter semantics), custom controls the CLI can't introspect,
            // or the synthetic "__screen_root__" — all out of scope.
            if (!BuiltinTags.IsBuiltin(n.Tag))
                yield break;

            foreach (var kv in n.VariantOverrides)
            {
                var attr = kv.Key;
                if (HasBase(n, attr)) continue;
                if (SelfHealing.Contains(attr)) continue;
                if (NotSetters.Contains(attr)) continue;
                if (MaskFamily.Contains(attr)) continue;
                if (isTemplateBodyRoot && InvocationMergeableOntoTemplateRoot.Contains(attr)) continue;
                // `type`: PUI-IMAGE-FIT-VARIANT owns the cover/contain values (its "not supported in v1"
                // advice is correct there; "add a base" would be wrong). Non-fit type values (sliced /
                // tiled / simple) have no AspectRatioFitter lifetime issue and DO revert with a base.
                if (attr == "type" && HasFitValue(kv.Value)) continue;

                var sampleVariant = kv.Value.Count > 0 ? kv.Value[0].Variant : "variant";
                yield return new LintIssue(
                    NoBaseCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: '{attr}.{sampleVariant}' is a variant override with no base '{attr}='. " +
                    $"Control-specific attributes are set-only when a variant clears — with no base, '{attr}' stays " +
                    "stuck at its last-applied value after the variant deactivates (e.g. a window resized " +
                    $"portrait→landscape). Add a base '{attr}=...' giving the value to revert to.");
            }
        }

        private static bool HasBase(ElementNode n, string attr)
        {
            if (n.Attributes.ContainsKey(attr)) return true;
            // Text shorthand: element text content is the base for the "text" attribute, re-applied
            // unconditionally by ControlAttributeApplier's TextContent block.
            if (attr == "text" && !string.IsNullOrEmpty(n.TextContent)) return true;
            return false;
        }

        private static bool HasFitValue(List<(string Variant, string Value)> overrides)
        {
            foreach (var o in overrides)
                if (o.Value == "cover" || o.Value == "contain")
                    return true;
            return false;
        }
    }
}
