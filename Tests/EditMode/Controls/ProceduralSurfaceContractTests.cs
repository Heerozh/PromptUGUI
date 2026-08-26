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
        // Lint codes are spelled out rather than referenced: M0 introduces no production code, so
        // there is nothing to reference yet. M4 swaps these for the constants.
        private const string SpriteConflictCode = "PUI-PROC-SPRITE-CONFLICT";
        private const string StateSpriteConflictCode = "PUI-PROC-STATE-SPRITE-CONFLICT";

        internal const string SurfaceName = "__Surface";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Btn Load(string btnAttrs)
        {
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
    }
}
