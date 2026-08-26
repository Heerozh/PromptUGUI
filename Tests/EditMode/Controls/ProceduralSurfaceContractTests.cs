using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// M0 of the procedural-surface spec: the contract, written down as something that runs, before
    /// any of it works. Most of these are RED and stay red until M1 attaches a surface to
    /// <c>&lt;Btn&gt;</c>; the handful marked GUARD pass today and exist so M1 cannot break them.
    ///
    /// <para>Everything here is black-box — GameObject hierarchy and observable behaviour — on
    /// purpose. M0 must not pin down an API that M1 has not designed yet, and the hierarchy IS the
    /// contract: a lazily added child called <c>__Surface</c> (the naming follows the shipped
    /// <c>__FocusCursor</c>), stretched, at sibling index 0, drawn under the control's own content.
    /// The same shape <c>GlassGroupPanel.Attach</c> already ships for weld.</para>
    ///
    /// <para><b>Procedural mode is a resolved-value question, not a written-text one.</b> A
    /// variant-only attribute (<c>radius.mobile="8"</c> with no base <c>radius</c>) is legal — see
    /// <c>VariantResolverTests.Variant_only_attr_returns_override_when_active</c> — so the mode
    /// genuinely flips at runtime, and every consequence of it (which Graphic is retired, where
    /// <c>targetGraphic</c> points) has to be recomputed rather than latched on first apply. That is
    /// spec §8's rule, and it is the same defect shape as <c>Btn.ReconcileTransition</c> and
    /// <c>StateTintReactor</c>'s base colour.</para>
    /// </summary>
    public class ProceduralSurfaceContractTests
    {
        private const string SpriteConflictCode = ProceduralSurfaceRules.SpriteConflictCode;
        private const string StateSpriteConflictCode = ProceduralSurfaceRules.StateSpriteConflictCode;

        internal const string SurfaceName = "__Surface";

        // Selectable.SelectionState is protected, so the ordinals are spelled out — same as
        // BtnStateTests.
        private const int NormalState = 0;
        private const int Highlighted = 1;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        private static Btn Load(string btnAttrs)
        {
            UI.UnloadAll();   // some tests build two screens to compare them
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' anchor='center' size='120x40' {btnAttrs} text='OK'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Btn>("b");
        }

        private static Transform SurfaceOf(Btn b) => b.GameObject.transform.Find(SurfaceName);

        private static ProceduralPanel PanelOf(Btn b)
        {
            var s = SurfaceOf(b);
            return s == null ? null : s.GetComponent<ProceduralPanel>();
        }

        private static System.Collections.Generic.List<LintIssue> Walk(string body, string top = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{top}
  <Screen name='S'>
{body}
  </Screen>
</PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool Has(System.Collections.Generic.List<LintIssue> issues, string code)
            => issues.Any(i => i.Code == code);

        // ===== §5 attach / don't attach =====

        /// <summary>GUARD. The zero-cost promise: nothing changes for a control that never asks.</summary>
        [Test]
        public void NoProceduralAttrs_NoSurfaceAtAll()
        {
            var b = Load("");

            Assert.IsNull(SurfaceOf(b), "a plain <Btn> must be byte-for-byte what it is today");
            Assert.IsEmpty(b.GameObject.GetComponentsInChildren<ProceduralPanel>(true),
                "no ProceduralPanel anywhere in the subtree");
        }

        [Test]
        public void AProceduralAttr_AttachesASurface()
        {
            var b = Load("radius='8'");

            Assert.IsNotNull(SurfaceOf(b), $"expected a lazily attached '{SurfaceName}' child");
            Assert.IsNotNull(PanelOf(b), "the surface draws with a ProceduralPanel");
        }

        [TestCase("radius='8'")]
        [TestCase("borderWidth='2' borderColor='#fff'")]
        [TestCase("glow='6'")]
        [TestCase("glass='true'")]
        public void AnyPanelAttachingAttr_AttachesASurface(string attrs)
        {
            Assert.IsNotNull(SurfaceOf(Load(attrs)), $"<Btn {attrs}> should get a surface");
        }

        /// <summary>§13.6 — decided by copying what <c>GlassWeld</c> already does.</summary>
        [Test]
        public void Surface_DrawsUnderTheControlsOwnContent()
        {
            var b = Load("radius='8'");
            var surface = SurfaceOf(b);

            Assert.AreEqual(0, surface.GetSiblingIndex(),
                "the background must sit below the Label and below any author child");
            Assert.IsNotNull(b.GameObject.transform.Find("Label"), "guard: the auto-label still exists");
        }

        [Test]
        public void Surface_StretchesOverTheControl()
        {
            var rt = (RectTransform)SurfaceOf(Load("radius='8'"));

            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(Vector2.zero, rt.offsetMin);
            Assert.AreEqual(Vector2.zero, rt.offsetMax);
        }

        [Test]
        public void Surface_IsClickThrough()
        {
            var b = Load("radius='8'");

            Assert.IsFalse(PanelOf(b).raycastTarget,
                "the control itself is the hit target; a background must not swallow the click");
        }

        // ===== §7 sprite and procedural are mutually exclusive on one surface =====

        [Test]
        public void EnteringProceduralMode_RetiresTheBuiltinSprite()
        {
            var b = Load("radius='8'");
            var bg = b.GameObject.GetComponent<UnityImage>();

            Assert.IsNotNull(bg, "the Image component stays — destroying it is what §4 rules out");
            Assert.IsNull(bg.sprite,
                "<Btn> ships with a default 9-sliced sprite; leaving it under the SDF is the mess "
                + "§7 exists to prevent. Clearing a DEFAULT is not a conflict, it is housekeeping");
        }

        [Test]
        public void Color_FeedsTheSurfacesFill_NotTheRetiredImage()
        {
            var b = Load("radius='8' color='#3366ff'");
            var panel = PanelOf(b);

            Assert.IsTrue(panel.CurrentParams.FillTop.r > 0.1f || panel.CurrentParams.FillTop.b > 0.5f,
                "§7: color is the fill in both modes; in procedural mode it goes to Panel.SetFill");
            Assert.IsTrue(panel.IsPanelVisible,
                "a control that asked for a shape must actually draw one — <Btn radius='8'> with no "
                + "explicit colour still has the control's default colour to fill with");
        }

        // ===== §8 state visuals ride on whichever Graphic is showing =====

        [Test]
        public void InProceduralMode_TargetGraphicIsTheSurface()
        {
            var b = Load("radius='8'");
            var selectable = b.GameObject.GetComponent<PuiButton>();

            Assert.AreSame(PanelOf(b), selectable.targetGraphic,
                "*Color / *Modulate drive Graphic.color, so they must drive the visible one");
        }

        /// <summary>
        /// The reversibility case, and the reason §8 insists the migration is computed. A
        /// variant-only <c>radius.mobile</c> means procedural mode is genuinely off when 'mobile' is
        /// off — the surface has to stand down and hand the control back to its Image.
        /// </summary>
        [Test]
        public void VariantOnlyProceduralAttr_ModeFollowsTheVariant()
        {
            var b = Load("radius.mobile='8'");
            var selectable = b.GameObject.GetComponent<PuiButton>();
            var bg = b.GameObject.GetComponent<UnityImage>();

            Assume.That(UI.Variants.IsActive("mobile"), Is.False, "guard: starts off");
            Assert.AreSame(bg, selectable.targetGraphic, "mode off → the Image is what shows");

            UI.Variants.Set("mobile", true);
            Assert.IsNotNull(SurfaceOf(b), "mode on → a surface appears");
            Assert.AreSame(PanelOf(b), selectable.targetGraphic);

            UI.Variants.Set("mobile", false);
            Assert.AreSame(bg, selectable.targetGraphic,
                "mode off again → targetGraphic must come back, or the control is left driving a "
                + "hidden layer and never changes colour again");
        }

        [Test]
        public void VariantRoundTrip_NeverRebuildsAndNeverStacks()
        {
            var b = Load("radius.mobile='8'");
            var go = b.GameObject;

            UI.Variants.Set("mobile", true);
            var surface = SurfaceOf(b).gameObject;
            UI.Variants.Set("mobile", false);
            UI.Variants.Set("mobile", true);

            Assert.AreSame(go, b.GameObject, "references and R3 subscriptions must survive");
            Assert.AreSame(surface, SurfaceOf(b).gameObject,
                "Strategy C: instantiate once, toggle after — never Destroy while the Screen is open");
            Assert.AreEqual(1, go.GetComponentsInChildren<ProceduralPanel>(true).Length);
        }

        [Test]
        public void ModeOff_HidesTheSurface_RatherThanDestroyingIt()
        {
            var b = Load("radius.mobile='8'");
            UI.Variants.Set("mobile", true);
            var surface = SurfaceOf(b).gameObject;

            UI.Variants.Set("mobile", false);

            Assert.IsNotNull(surface, "still there…");
            Assert.IsFalse(surface.activeSelf, "…just not drawing");
        }

        [Test]
        public void HoverColor_ReachesTheSurface()
        {
            var b = Load("radius='8' color='#3366ff' hoverColor='#ff0000'");

            Assert.AreSame(PanelOf(b),
                b.GameObject.GetComponent<PuiButton>().targetGraphic,
                "state colours are a targetGraphic feature; nothing else needs to change");
        }

        // ===== §13.5 Disabled must not swap the SDF material away =====

        /// <summary>
        /// The one interaction in this design that breaks something that works today.
        /// <c>DisabledGrayscaleController</c> greys a subtree by assigning
        /// <c>graphic.material = UI-Grayscale</c>. On a <c>ProceduralPanel</c> that replaces the SDF
        /// material outright — shape, border, glow and glass all vanish — and it is doubly broken,
        /// because <c>FlushParams</c> writes the cached material back on the next parameter change
        /// and erases the greying in turn.
        /// </summary>
        [Test]
        public void DisabledRoundTrip_KeepsTheSdfMaterial()
        {
            var b = Load("radius='8' color='#3366ff' interactable='false'");
            var panel = PanelOf(b);

            StringAssert.Contains("ProceduralPanel", panel.materialForRendering.shader.name,
                "greying must happen inside the panel's own material (glass already has a "
                + "'saturation' uniform for exactly this), never by swapping the material out");
            Assert.IsTrue(panel.IsPanelVisible, "a disabled button is greyed, not erased");
        }

        // ===== §7 / §8 lint =====

        [Test]
        public void ProceduralAttrOnAControl_StopsBeingReportedAsIgnored()
        {
            // Inverse of the M-lint rule that ships today. When M1 lands, that control has to come
            // out of PureContainerVisualAttrRules — ProceduralAttrNamesTests fails until it does.
            Assert.IsFalse(
                Has(Walk("<Btn id='b' radius='8'/>"), PureContainerVisualAttrRules.VisualAttrCode),
                "radius works on <Btn> now; reporting it as silently ignored is the false positive "
                + "this milestone creates if the rule is not narrowed");
        }

        [Test]
        public void ProceduralPlusSprite_IsAConflict()
        {
            Assert.IsTrue(Has(Walk("<Btn id='b' radius='8' sprite='ui:card'/>"), SpriteConflictCode),
                "a bitmap on top of an SDF face is a mess, and Image.type's sliced/tiled inference "
                + "is meaningless there");
        }

        [TestCase("sprite='none'")]
        [TestCase("sprite=''")]
        public void ProceduralPlusClearedSprite_IsNotAConflict(string spriteAttr)
        {
            Assert.IsFalse(Has(Walk($"<Btn id='b' radius='8' {spriteAttr}/>"), SpriteConflictCode),
                "'clear the bitmap' agrees with entering procedural mode, and skin packs are full "
                + "of this spelling");
        }

        [Test]
        public void ProceduralPlusColor_IsNotAConflict()
        {
            Assert.IsFalse(Has(Walk("<Btn id='b' radius='8' color='#333'/>"), SpriteConflictCode));
        }

        [TestCase("pressedSprite")]
        [TestCase("disabledSprite")]
        public void ProceduralPlusStateSprite_IsAConflict(string attr)
        {
            Assert.IsTrue(
                Has(Walk($"<Btn id='b' radius='8' {attr}='ui:card'/>"), StateSpriteConflictCode),
                "state sprites are Image.overrideSprite swaps and have no meaning on an SDF face");
        }

        [Test]
        public void ProceduralFromAClass_IsJudgedTheSameAsInline()
        {
            // Skins carry the shape in a <Style>; every rule above has to see through class=.
            Assert.IsTrue(
                Has(Walk("<Btn id='b' class='glassy' sprite='ui:card'/>",
                         "<Style name='glassy' radius='8' glass='true'/>"),
                    SpriteConflictCode));
        }

        // ===== §8: a state sprite that cannot show must not take the control off ColorTint =====

        /// <summary>
        /// The composition §8 asks for, and the one that bites hardest if it is missed.
        ///
        /// <para>An authored <c>pressedSprite</c> switches uGUI's built-in ColorTint off, so the
        /// swapped art is not additionally darkened. But in procedural mode the Image it swaps is
        /// retired and invisible — so the swap shows nothing, ColorTint is gone, and the button ends
        /// up with <b>no press feedback at all</b> and no attribute the author could write to get it
        /// back. Exactly the irreversible-latch shape <c>Btn.ReconcileTransition</c> was written to
        /// avoid; the surface has to be part of the same computation.</para>
        /// </summary>
        [Test]
        public void ProceduralMode_StateSprite_DoesNotStripColorTint()
        {
            UI.SpriteResolver = _ => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var b = Load("radius='8' pressedSprite='ui:pressed'");

            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint,
                b.GameObject.GetComponent<PuiButton>().transition,
                "the sprite swap is invisible here, so giving up ColorTint for it leaves the button "
                + "with no feedback whatsoever");
        }

        /// <summary>GUARD: outside procedural mode the old behaviour is exactly as it was.</summary>
        [Test]
        public void WithoutASurface_StateSprite_StillStripsColorTint()
        {
            UI.SpriteResolver = _ => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var b = Load("pressedSprite='ui:pressed'");

            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.None,
                b.GameObject.GetComponent<PuiButton>().transition,
                "an Image-backed Btn still hands feedback to the sprite swap");
        }

        /// <summary>
        /// …and it is COMPUTED, not latched: a Variant that turns the surface off has to hand
        /// feedback back to the sprite swap, and turning it on again has to take it back.
        /// </summary>
        [Test]
        public void StateSpriteAndSurface_ComposeBothWays()
        {
            UI.SpriteResolver = _ => Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var b = Load("radius.mobile='8' pressedSprite='ui:pressed'");
            var btn = b.GameObject.GetComponent<PuiButton>();

            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.None, btn.transition,
                "mode off → the sprite swap owns feedback");

            UI.Variants.Set("mobile", true);
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint, btn.transition,
                "mode on → the swap is invisible, so ColorTint comes back");

            UI.Variants.Set("mobile", false);
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.None, btn.transition,
                "…and back again");
        }

        // ===== state colours on a procedural surface land in two different places =====
        //
        // A panel keeps its authored look in its MATERIAL and treats Graphic.color as a multiplier
        // (`col *= IN.color` in the shader) — the split that lets panels sharing a style share one
        // material. An Image has no such split: there, Graphic.color IS the fill. So the reactor's
        // "premultiply the modulate into the base and write the product to .color" is right for an
        // Image and wrong here, twice over:
        //   • the base would be applied as the fill AND as the tint, so color="#3366ff" renders as
        //     its own square;
        //   • an "absolute" hoverColor written to a multiplier channel is not absolute — it darkens
        //     whatever is underneath, which on glass tints the blurred backdrop instead of the pane.

        private static ProceduralPanel TargetPanel(Btn b) =>
            b.GameObject.GetComponent<PuiButton>().targetGraphic as ProceduralPanel;

        [Test]
        public void ProceduralMode_AuthoredColour_LandsInTheFillOnly()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius='8' color='#3366ff' hoverColor='#ff0000'");
            var panel = TargetPanel(b);

            Assert.AreEqual(new Color32(0x33, 0x66, 0xff, 0xff), (Color32)panel.CurrentParams.FillTop);
            Assert.AreEqual(Color.white, panel.color,
                "the vertex tint must stay identity, or the authored colour is applied twice and the "
                + "button renders as its own square");
        }

        [Test]
        public void ProceduralMode_HoverColour_IsAbsolute()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius='8' color='#3366ff' hoverColor='#ff0000'");
            var panel = TargetPanel(b);

            b.GameObject.GetComponent<PuiButton>().SimulateState(
                Highlighted);

            Assert.AreEqual(new Color32(0xff, 0x00, 0x00, 0xff), (Color32)panel.CurrentParams.FillTop,
                "hoverColor is documented as ABSOLUTE — it must BE the colour, not multiply into it");
            Assert.AreEqual(Color.white, panel.color);
        }

        [Test]
        public void ProceduralMode_HoverModulate_StaysAMultiplier()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius='8' color='#3366ff' hoverModulate='#808080'");
            var panel = TargetPanel(b);

            b.GameObject.GetComponent<PuiButton>().SimulateState(
                Highlighted);

            Assert.AreEqual(new Color32(0x33, 0x66, 0xff, 0xff), (Color32)panel.CurrentParams.FillTop,
                "a modulate must not touch the fill…");
            Assert.AreEqual(new Color32(0x80, 0x80, 0x80, 0xff), (Color32)panel.color,
                "…it is exactly what the vertex channel is for");
        }

        [Test]
        public void ProceduralMode_StateColours_RevertExactly()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius='8' color='#3366ff' hoverColor='#ff0000'");
            var panel = TargetPanel(b);
            var btn = b.GameObject.GetComponent<PuiButton>();

            btn.SimulateState(Highlighted);
            btn.SimulateState(NormalState);

            Assert.AreEqual(new Color32(0x33, 0x66, 0xff, 0xff), (Color32)panel.CurrentParams.FillTop);
            Assert.AreEqual(Color.white, panel.color);
        }

        /// <summary>
        /// The case that prompted all of this: on glass, the state colour has to move the pane's own
        /// tint. Written to the multiplier channel it would instead darken the blurred backdrop —
        /// which reads as "hover does something vaguely wrong" rather than "hover changes the colour".
        /// </summary>
        [Test]
        public void GlassSurface_HoverColour_MovesTheGlassTint()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius='8' glass='true' color='white/0.22' hoverColor='#ffcc00/0.5'");
            var panel = TargetPanel(b);

            b.GameObject.GetComponent<PuiButton>().SimulateState(
                Highlighted);

            var fill = panel.CurrentParams.FillTop;
            Assert.AreEqual(1f, fill.r, 0.01f);
            Assert.AreEqual(0.8f, fill.g, 0.02f);
            Assert.AreEqual(0f, fill.b, 0.01f);
            Assert.AreEqual(0.5f, fill.a, 0.01f);
            Assert.IsTrue(panel.CurrentParams.Glass, "…and it is still glass");
        }

        // ===== the graphic that stands down must stop being driven =====

        /// <summary>
        /// Only reproducible by going THROUGH the other skin, which is why it shipped.
        ///
        /// <para><c>StateTintInstaller</c> installs a reactor on the control's <c>targetGraphic</c>.
        /// When a procedural surface takes over, targetGraphic moves to the panel and a second
        /// reactor appears there — but the first one, still sitting on the now-retired Image and
        /// still subscribed to the state stream, keeps writing. The next hover repaints the Image at
        /// FULL ALPHA in the previous skin's colour, and it draws as a hard rectangle behind the
        /// rounded surface. Open straight into the second skin and there is nothing to go stale, so
        /// every test that did so passed.</para>
        /// </summary>
        [Test]
        public void WhenTheSurfaceTakesOver_TheRetiredImageStopsBeingDriven()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius.mobile='8' color='#E8D2A8' hoverColor='#F5E6C8'");
            var bg = b.GameObject.GetComponent<UnityImage>();
            var btn = b.GameObject.GetComponent<PuiButton>();

            // Mode off: the Image IS the visible layer, and the reactor rightly drives it.
            btn.SimulateState(Highlighted);
            Assume.That(bg.color.a, Is.EqualTo(1f).Within(0.001f), "guard: the Image is live here");
            btn.SimulateState(NormalState);

            UI.Variants.Set("mobile", true);      // the surface takes over
            btn.SimulateState(Highlighted);

            Assert.AreEqual(0f, bg.color.a, 0.001f,
                "the retired Image must stay retired — a stale reactor repainting it puts the old "
                + "skin's colour back as a hard rectangle behind the rounded surface");
        }

        /// <summary>…and it has to come back when the surface stands down again.</summary>
        [Test]
        public void WhenTheSurfaceStandsDown_TheImageIsDrivenAgain()
        {
            StateTintReactor.TestForceInstant = true;
            var b = Load("radius.mobile='8' color='#E8D2A8' hoverColor='#F5E6C8'");
            var bg = b.GameObject.GetComponent<UnityImage>();
            var btn = b.GameObject.GetComponent<PuiButton>();

            UI.Variants.Set("mobile", true);
            UI.Variants.Set("mobile", false);
            btn.SimulateState(Highlighted);

            Assert.AreEqual(new Color32(0xF5, 0xE6, 0xC8, 0xff), (Color32)bg.color,
                "detaching must be reversible, or the round trip loses hover entirely");
        }

        // ===== the surface is the Image's stand-in, so it lives where the Image lives =====

        /// <summary>
        /// <c>StateOffsetInstaller</c> sweeps a control's direct children into a content holder so
        /// one shift moves them all on press. The bg Image escapes that sweep for free — it is a
        /// COMPONENT, not a child. Its procedural stand-in is a child, so without an exemption it
        /// gets swept in, and two things break at once: it would shift with the press (the sprite
        /// skin's background never does), and it lands at the END of the holder, painting over the
        /// label.
        ///
        /// <para>Only reproducible when the surface appears AFTER the holder already exists — a
        /// theme switch. Open straight into the procedural skin and the surface is swept in first,
        /// landing at index 0, which looks correct. Hence the variant flip here.</para>
        /// </summary>
        [Test]
        public void Surface_StaysOutsideTheContentHolder_EvenWhenItAppearsLater()
        {
            var b = Load("radius.mobile='8' pressedOffset='0,-1'");
            var holder = b.GameObject.transform.Find("_offsetHolder");
            Assume.That(holder, Is.Not.Null, "guard: pressedOffset built the holder up front");

            UI.Variants.Set("mobile", true);

            var surface = b.GameObject.transform.Find(SurfaceName);
            Assert.IsNotNull(surface,
                "the surface must be a direct child of the control, like the Image it replaces — "
                + "not swept into the content holder");
            Assert.AreEqual(0, surface.GetSiblingIndex(), "…and drawn behind everything");
            Assert.Less(surface.GetSiblingIndex(), holder.GetSiblingIndex(),
                "the label lives in the holder; a surface above it would paint over the text");
        }

        [Test]
        public void Surface_LayerOrderIsTheSame_WhicheverWayYouGotThere()
        {
            // Straight into the procedural skin…
            var direct = Load("radius='8' pressedOffset='0,-1'");
            var directIndex = direct.GameObject.transform.Find(SurfaceName).GetSiblingIndex();
            var directParent = direct.GameObject.transform.Find(SurfaceName).parent.name;

            // …versus arriving via a variant flip.
            var flipped = Load("radius.mobile='8' pressedOffset='0,-1'");
            UI.Variants.Set("mobile", true);
            var flippedNode = flipped.GameObject.transform.Find(SurfaceName);

            Assert.IsNotNull(flippedNode);
            Assert.AreEqual(directParent, flippedNode.parent.name);
            Assert.AreEqual(directIndex, flippedNode.GetSiblingIndex());
        }
    }
}
