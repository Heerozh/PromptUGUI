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

        // An attribute self-heals iff the runtime unconditionally re-applies it when its variant clears.
        // DERIVED from the shared PromptUGUI.IR.CommonAttributes set (the ApplyCommon path) — a new common
        // attr added there is picked up here automatically, no edit needed — with two intrinsic exceptions:
        //   - `hidden` IS a common attr but is applied conditionally (`if (hidden.HasValue)` in Control.cs),
        //     so a null-resolving hidden is SKIPPED, not reset → it does NOT self-heal.
        //   - `scale` is NOT a common attr (handled by Screen.ApplyScales, not ApplyCommon) but DOES
        //     self-heal (localScale reset to identity when unresolved).
        // These two are the only points where "is a common attr" ≠ "self-heals"; both are exercised by
        // VariantBaseRulesTests so a future runtime change that moves an attr across the line trips a test.
        private static bool SelfHeals(string attr)
            => attr == "scale"
               || (attr != "hidden" && CommonAttributes.Contains(attr));

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

            // A control whose procedural surface toggles WHOLESALE with the variant reverts on its
            // own, so the base-less form is the correct way to write it — and the idiom a skin wants:
            // `radius.glass="10"` shapes the control under one variant and leaves its sprite alone
            // under the others. ProceduralSurface recomputes the mode every pass (procedural-surface
            // spec §8), and turning it off hides the surface and restores the Image with the sprite
            // and alpha it had.
            //
            // "Wholesale" is the load-bearing word, and why this is a NODE-level question rather than
            // a per-attribute one. With a base `radius=` present the mode never turns off, so a
            // base-less `glass.mobile` beside it really would stick — that node is reported as usual.
            // <Frame> is deliberately not included: its panel takes parameters directly with no
            // per-pass reconcile, so nothing there self-heals.
            var proceduralSelfHeals = ProceduralSurfaceRules.AppliesTo(n.Tag)
                                      && !DeclaresBaseProcedural(n);

            foreach (var kv in n.VariantOverrides)
            {
                var attr = kv.Key;
                if (HasBase(n, attr)) continue;
                if (SelfHeals(attr)) continue;
                if (proceduralSelfHeals && IsProcedural(attr)) continue;
                // One attribute per inner surface, so base-less always means "this surface toggles
                // wholesale" — no node-level condition needed.
                if (ProceduralSurfaceRules.AppliesTo(n.Tag) && IsInnerLayerRadius(attr)) continue;
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

        private static bool IsInnerLayerRadius(string attr)
        {
            foreach (var name in ProceduralAttrNames.InnerLayerRadius)
                if (name == attr) return true;
            return false;
        }

        private static bool IsProcedural(string attr)
        {
            foreach (var name in ProceduralAttrNames.NeedsPanel)
                if (name == attr) return true;
            return attr == "color";
        }

        /// <summary>Any procedural attribute with a base value pins the mode on.</summary>
        private static bool DeclaresBaseProcedural(ElementNode n)
        {
            foreach (var name in ProceduralAttrNames.NeedsPanel)
            {
                if (name == "weld") continue;
                if (n.Attributes.ContainsKey(name)) return true;
            }
            return false;
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
