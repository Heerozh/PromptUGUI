using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// M0 of <c>docs~/superpowers/specs/2026-08-26-theme-driven-style-design.md</c> §8.
    ///
    /// <para><b>The property under test</b>: flipping an attribute A→B must leave the control in the
    /// same state as building it with B in the first place. <c>ControlAttributeApplier</c> re-applies
    /// every declared attribute on each <c>ReSolve</c>, but a setter that only ever ADDS state leaves
    /// the previous value's side effects behind when the value changes — so "re-applied" is not the
    /// same as "reverted". Each test therefore compares a **flipped** control against a **freshly
    /// built** reference rather than against a hardcoded expectation.</para>
    ///
    /// <para>Today this is reachable with a plain <c>.variant</c> flip. Under theme-driven styles
    /// (spec §5) every theme switch walks the same path, which is why these are pinned before that
    /// feature lands.</para>
    ///
    /// <para><b>The <c>[Ignore]</c>d tests assert the DESIRED behaviour and currently FAIL.</b> The
    /// fixes are a separate PR (spec §14 M0) — un-ignore each one as its fix lands. They are ignored
    /// rather than left red so a full-suite run stays a meaningful regression signal.</para>
    ///
    /// <para>Deliberately NOT here: the <c>mask</c> family. <c>PUI-MASK-VARIANT</c> /
    /// <c>PUI-PROG-MASK-VARIANT</c> already declare per-variant mask switching unsupported in v1 for
    /// exactly this reason, so its irreversibility is a documented non-feature, not a defect — see
    /// the characterization test at the bottom.</para>
    /// </summary>
    public class AttributeReversibilityTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string VariantName = "alt";

        private static void StubSprites()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;
        }

        private static PromptScreen OpenScreen(string label, string screenName, string body)
        {
            UI.LoadDocument(label,
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='{screenName}'>{body}</Screen></PromptUGUI>");
            return UI.Open(screenName);
        }

        private static Tab OpenTab(string label, string screenName, string tabAttrs)
            => OpenScreen(label, screenName,
                    $"<TabBar id='bar'><Tab id='t' {tabAttrs}/></TabBar>")
               .Get<Tab>("bar/t");

        private static Selectable.Transition TransitionOf(Tab tab)
            => tab.GameObject.GetComponent<Selectable>().transition;

        private static Progress OpenProgress(string label, string screenName, string progressAttrs)
            => OpenScreen(label, screenName, $"<Progress id='p' {progressAttrs}/>").Get<Progress>("p");

        private static bool ChildActive(Progress p, string path)
            => p.GameObject.transform.Find(path).gameObject.activeSelf;

        // ------------------------------------------------------------------
        // Tab.selectedSprite — Tab.cs:277 sets transition=None when a sprite is
        // supplied (the swapped sprite IS the selection feedback, so uGUI's ColorTint
        // must not double-tint it). Clearing the sprite restores neither.
        // ------------------------------------------------------------------

        [Test]
        [Ignore("Red: spec 2026-08-26 §8 — Tab.selectedSprite does not restore Selectable.transition. Un-ignore with the fix.")]
        public void TabSelectedSprite_FlippedToNone_RestoresTransition_LikeFreshBuild()
        {
            StubSprites();

            var reference = OpenTab("ref", "R", "selectedSprite='none'");
            var expected = TransitionOf(reference);
            Assume.That(expected, Is.EqualTo(Selectable.Transition.ColorTint),
                "guard: a Tab built without a selected sprite keeps uGUI's ColorTint");

            var tab = OpenTab("t", "S", $"selectedSprite='ui:sel' selectedSprite.{VariantName}='none'");
            Assume.That(TransitionOf(tab), Is.EqualTo(Selectable.Transition.None),
                "guard: the authored selected sprite takes the Tab off ColorTint");

            UI.Variants.Set(VariantName, true);

            Assert.AreEqual(expected, TransitionOf(tab),
                "clearing selectedSprite must put the Tab back on ColorTint — otherwise the tab is "
                + "left with no hover/press feedback at all, and no attribute the author can write brings it back");
        }

        // ------------------------------------------------------------------
        // Progress.bg / Progress.frame — Progress.cs:94 / :118 do SetActive(true)
        // and early-return on an empty value, so the layer can be switched on but
        // never off. Note PUI-PROG-MASK-VARIANT's message currently advertises
        // `bg` as variant-safe (ProgressAttributeRules.cs:72) — see the lint test below.
        // ------------------------------------------------------------------

        [Test]
        [Ignore("Red: spec 2026-08-26 §8 — Progress.bg cannot be turned off once set. Un-ignore with the fix.")]
        public void ProgressBg_FlippedToEmpty_HidesLayer_LikeFreshBuild()
        {
            StubSprites();

            var reference = OpenProgress("ref", "R", "bg=''");
            var expected = ChildActive(reference, "MaskWrapper/Bg");
            Assume.That(expected, Is.False, "guard: a Progress built with no bg keeps its Bg layer disabled");

            var p = OpenProgress("t", "S", $"bg='ui:track' bg.{VariantName}=''");
            Assume.That(ChildActive(p, "MaskWrapper/Bg"), Is.True, "guard: the authored bg enables the layer");

            UI.Variants.Set(VariantName, true);

            Assert.AreEqual(expected, ChildActive(p, "MaskWrapper/Bg"),
                "clearing bg must hide the Bg layer — it currently stays visible with a stale sprite");
        }

        [Test]
        [Ignore("Red: spec 2026-08-26 §8 — Progress.frame cannot be turned off once set. Un-ignore with the fix.")]
        public void ProgressFrame_FlippedToEmpty_HidesLayer_LikeFreshBuild()
        {
            StubSprites();

            var reference = OpenProgress("ref", "R", "frame=''");
            var expected = ChildActive(reference, "Frame");
            Assume.That(expected, Is.False, "guard: a Progress built with no frame keeps its Frame layer disabled");

            var p = OpenProgress("t", "S", $"frame='ui:fr' frame.{VariantName}=''");
            Assume.That(ChildActive(p, "Frame"), Is.True, "guard: the authored frame enables the layer");

            UI.Variants.Set(VariantName, true);

            Assert.AreEqual(expected, ChildActive(p, "Frame"),
                "clearing frame must hide the Frame layer — it currently stays visible with a stale sprite");
        }

        // ------------------------------------------------------------------
        // Characterization (GREEN, on purpose): the mask family is irreversible by
        // DESIGN, not by accident — PUI-MASK-VARIANT (MaskAttributeRules.cs) rejects
        // per-variant mask switching outright because it "requires AddComponent /
        // Destroy which has performance / lifetime issues".
        //
        // This test exists so that whoever eventually makes mask reversible sees it
        // fail and knows the lint rule must be retired in the same change, rather than
        // silently leaving authors barred from a feature that now works.
        // ------------------------------------------------------------------

        [Test]
        public void FrameMask_FlippedToNone_KeepsRectMask2D_DocumentedNonFeature()
        {
            var screen = OpenScreen("t", "S",
                $"<Frame id='f' mask='rect' mask.{VariantName}='none'/>");
            var frame = screen.Get<Frame>("f");
            Assume.That(frame.GameObject.GetComponent<RectMask2D>(), Is.Not.Null,
                "guard: mask='rect' installs the clipper");

            UI.Variants.Set(VariantName, true);

            Assert.IsNotNull(frame.GameObject.GetComponent<RectMask2D>(),
                "mask is add-only at runtime; this is why PUI-MASK-VARIANT rejects the markup that "
                + "reaches here. If this assertion starts failing, retire that lint rule in the same change.");
        }
    }
}
