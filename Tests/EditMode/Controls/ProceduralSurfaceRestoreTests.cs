using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// M0 of the 2026-08-27 spec §5: what <c>ProceduralSurface.Restore()</c> is allowed to put back
    /// when the surface stands down.
    ///
    /// <para><b>The defect.</b> <c>Retire()</c> snapshots the host Image once, on the FIRST retire —
    /// and it runs from <c>Reconcile()</c>, which is <c>OnAfterApply</c>, i.e. AFTER every setter of
    /// that pass. So the snapshot is not "the control's built-in default", it is "whatever the
    /// author declared on the pass that first turned the surface on". <c>Restore()</c> then writes
    /// that snapshot back unconditionally, on a pass where the author's own <c>sprite=</c> /
    /// <c>color=</c> setters have ALREADY written the values the new skin wants — and clobbers
    /// them.</para>
    ///
    /// <para>Reachable whenever procedural mode is on for the FIRST apply pass: a persisted skin
    /// choice means <c>UI.Theme.Set("glass")</c> runs before <c>UI.Open</c>, and switching back to
    /// the pixel skin then loses its 9-slice and gets the glass alpha. Orthogonal to the lint change
    /// in §3 — today's <c>attr.glass=</c> spelling hits it just the same, which is why the variant
    /// fixtures below are the primary ones and the theme fixture is the shipped-sample shape.</para>
    ///
    /// <para>Two of these are GUARD tests (green today): the fix must not overshoot into "never
    /// restore anything", which would strand the control with the null sprite and zero alpha
    /// <c>Retire()</c> left behind.</para>
    /// </summary>
    public class ProceduralSurfaceRestoreTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Sprite MakeSprite()
        {
            var tex = new Texture2D(4, 4);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        }

        private static Btn LoadBtn(string attrs, string top = "")
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{top}<Screen name='S'>
  <Btn id='b' anchor='center' size='120x40' {attrs} text='OK'/>
</Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        private static UnityImage BgOf(Btn b) => b.GameObject.GetComponent<UnityImage>();

        // ===== §5.1 the alpha half =====

        /// <summary>
        /// RED. Opening while the surface is on snapshots the glass alpha (0x38 ≈ 0.22); leaving
        /// procedural mode then forces it back over the pixel skin's opaque colour, and the button
        /// comes back see-through. `_hasFill` already carries the signal this needs — the control's
        /// `color=` setter calls SetFill, and BeginPass clears it every pass.
        /// </summary>
        [Test]
        public void LeavingProceduralMode_KeepsTheAlphaThisPassDeclared()
        {
            UI.Variants.Set("mobile", true);
            var b = LoadBtn("color='#E8D2A8' color.mobile='#FFFFFF38' radius.mobile='10'");

            UI.Variants.Set("mobile", false);

            Assert.AreEqual(1f, BgOf(b).color.a, 0.01f,
                "the pass that turned the surface off declared color='#E8D2A8', which is opaque — "
                + "restoring the alpha captured under the glass skin makes the button translucent");
        }

        /// <summary>
        /// GUARD (green today). Retire() zeroes the alpha every pass it is on, so when NOTHING
        /// declares a colour on the way out there is nothing but the snapshot to come back to.
        /// </summary>
        [Test]
        public void LeavingProceduralMode_RestoresTheAlphaWhenNoColourIsDeclared()
        {
            var b = LoadBtn("radius.mobile='10'");
            var opaque = BgOf(b).color.a;

            UI.Variants.Set("mobile", true);
            UI.Variants.Set("mobile", false);

            Assert.AreEqual(opaque, BgOf(b).color.a, 0.001f,
                "no color= anywhere, so the Image must come back exactly as it went in — a fix that "
                + "simply stops restoring would leave it at Retire()'s alpha 0 and invisible");
        }

        // ===== §5.1 the sprite half =====

        /// <summary>
        /// RED. Same shape one layer over: the snapshot is taken after `sprite.mobile='none'` has
        /// already nulled the Image, so leaving procedural mode writes null over the sprite the
        /// pixel skin just asked for.
        /// </summary>
        [Test]
        public void LeavingProceduralMode_KeepsTheSpriteThisPassDeclared()
        {
            var wood = MakeSprite();
            UI.SpriteResolver = key => key == "s:wood" ? wood : null;

            UI.Variants.Set("mobile", true);
            var b = LoadBtn("sprite='s:wood' sprite.mobile='none' radius.mobile='10'");

            UI.Variants.Set("mobile", false);

            Assert.AreSame(wood, BgOf(b).sprite,
                "the pass that turned the surface off declared sprite='s:wood'; Restore must not "
                + "overwrite it with the null captured while the glass skin was drawing");
        }

        /// <summary>
        /// GUARD (green today). With no authored sprite the snapshot IS the built-in 9-slice, and
        /// restoring it is the whole reason Restore() exists — Retire() nulled it on the way in.
        /// </summary>
        [Test]
        public void LeavingProceduralMode_RestoresTheBuiltinSpriteWhenNoneIsDeclared()
        {
            var b = LoadBtn("radius.mobile='10'");
            var builtin = BgOf(b).sprite;
            Assert.IsNotNull(builtin, "<Btn> ships with a default 9-sliced sprite");

            UI.Variants.Set("mobile", true);
            Assert.IsNull(BgOf(b).sprite, "§7: no bitmap under the SDF face");

            UI.Variants.Set("mobile", false);

            Assert.AreSame(builtin, BgOf(b).sprite,
                "nobody declared a sprite on the way out, so the control's own default has to come "
                + "back — this is the case the snapshot is for");
        }

        // ===== the same round trip, driven by <Theme> instead of a variant =====

        /// <summary>
        /// RED, and the shape the shipped CommonControls sample takes after the §4 rewrite: the skin
        /// lives entirely in <c>&lt;Theme&gt;</c> packs, with the glass theme adding the shape
        /// attributes and the pixel theme simply not having them. Pinned separately from the variant
        /// fixtures because the two reach <c>ControlAttributeApplier</c> by different routes — a
        /// variant leaves a key that resolves to null, a theme drops the key outright.
        ///
        /// <para>Goes through the fake-files resolver rather than <c>LoadDocument(label, xml)</c>:
        /// only the async path runs <c>RegisterThemesAndAutoSet</c>, and without it the packs never
        /// reach <c>ThemeStore</c> and the whole fixture passes for the wrong reason. Same note
        /// <c>ThemeStyleSwitchTests</c> carries.</para>
        /// </summary>
        [Test]
        public void ThemeDrivenRoundTrip_KeepsWhatTheIncomingThemeDeclared()
        {
            var wood = MakeSprite();
            UI.SpriteResolver = key => key == "s:wood" ? wood : null;
            UI.SourceResolver = _ => AwaitableHelpers.Completed(
                @"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>
                    <Style name='btn' sprite='s:wood' color='#E8D2A8'/>
                    <Theme name='farm'><Style name='btn' sprite='s:wood' color='#E8D2A8'/></Theme>
                    <Theme name='glass'><Style name='btn' sprite='none' color='#FFFFFF38'
                           radius='10' glass='true'/></Theme>
                    <Screen name='S'>
                      <Btn id='b' class='btn' anchor='center' size='120x40' text='OK'/>
                    </Screen>
                  </PromptUGUI>");
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            // Before Open, so the FIRST apply pass is the one that retires the Image — the boot
            // order a persisted skin choice produces, and the only one that captures a snapshot the
            // other theme cannot live with.
            UI.Theme.Set("glass");
            var b = UI.Open("S").Get<Btn>("b");
            Assume.That(BgOf(b).sprite, Is.Null, "guard: the glass pack really did take the Image");

            UI.Theme.Set("farm");

            Assert.AreSame(wood, BgOf(b).sprite, "the farm pack declares the wood sprite");
            Assert.AreEqual(1f, BgOf(b).color.a, 0.01f, "…and an opaque colour");
        }
    }
}
