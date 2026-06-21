using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.UI;
using PuiImage = PromptUGUI.Controls.Image;
using PuiText = PromptUGUI.Controls.Text;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnStateTests
    {
        // Mirror of the (protected) UnityEngine.UI.Selectable.SelectionState ordinals.
        // The test assembly cannot name the protected nested type, so PuiButton's
        // test hooks take the ordinal int and cast internally.
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;
        private const int Selected = 3;
        private const int Disabled = 4;

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

        private static Btn BuildBtn(string extraAttrs = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {extraAttrs}>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            return screen.Get<Btn>("b");
        }

        // Builds a Btn from a full inner-XML body (children + attrs on the Btn itself).
        private static Btn BuildBtnXml(string btnAttrs, string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {btnAttrs}>{body}</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            return screen.Get<Btn>("b");
        }

        [Test]
        public void Map_TranslatesSelectionStatesToInteractState()
        {
            Assert.AreEqual(InteractState.Normal, StateBroadcaster.MapTransient(Normal));
            Assert.AreEqual(InteractState.Hover, StateBroadcaster.MapTransient(Highlighted));
            Assert.AreEqual(InteractState.Pressed, StateBroadcaster.MapTransient(Pressed));
            Assert.AreEqual(InteractState.Disabled, StateBroadcaster.MapTransient(Disabled));
            // Momentary button must not keep a sticky highlight after a touch tap.
            Assert.AreEqual(InteractState.Normal, StateBroadcaster.MapTransient(Selected));
        }

        [Test]
        public void OnState_EmitsCurrentValueImmediatelyAsNormal()
        {
            var btn = BuildBtn();
            InteractState seen = (InteractState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(InteractState.Normal, seen);
        }

        [Test]
        public void OnState_EmitsSequenceFollowingSimulatedStates()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.IsNotNull(puiBtn, "Btn should host a PuiButton");

            var seen = new List<InteractState>();
            using var _ = btn.OnState.Subscribe(s => seen.Add(s));

            puiBtn.SimulateState(Highlighted); // -> Hover
            puiBtn.SimulateState(Pressed);     // -> Pressed
            puiBtn.SimulateState(Selected);    // -> Normal (Selected folds to Normal)
            puiBtn.SimulateState(Highlighted); // -> Hover again (proves stream still live)

            // First emission is the replayed Normal initial value, then each *changed* state.
            // ReactiveProperty is distinct-until-changed, so Selected->Normal emits a single
            // Normal (the prior value was Pressed) and a redundant Normal->Normal would be
            // suppressed — see the dedicated dedup test below.
            CollectionAssert.AreEqual(
                new[]
                {
                    InteractState.Normal,  // replayed initial value
                    InteractState.Hover,
                    InteractState.Pressed,
                    InteractState.Normal,  // Selected -> Normal
                    InteractState.Hover,
                },
                seen);
        }

        [Test]
        public void OnState_IsDistinctUntilChanged_SelectedAfterNormalEmitsNothing()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            var seen = new List<InteractState>();
            using var _ = btn.OnState.Subscribe(s => seen.Add(s));

            // Already Normal (initial). Selected folds to Normal, so no change -> no emission.
            puiBtn.SimulateState(Selected);
            puiBtn.SimulateState(Normal);

            CollectionAssert.AreEqual(new[] { InteractState.Normal }, seen);
        }

        [Test]
        public void InteractableFalse_DrivesButtonAndEmitsDisabled()
        {
            // `interactable` is a common attr: it flows through ApplyCommon -> Control.Interactable
            // (CanvasGroup) and, via Btn.OnAfterApply, is bridged to Button.interactable. Setting
            // Button.interactable = false synchronously runs DoStateTransition(Disabled).
            var btn = BuildBtn("interactable='false'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.IsNotNull(puiBtn, "Btn should host a PuiButton");

            Assert.IsFalse(puiBtn.interactable, "Button.interactable should mirror interactable='false'");

            InteractState seen = (InteractState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(InteractState.Disabled, seen);
        }

        [Test]
        public void InteractableOmitted_StaysNormalAndButtonInteractable()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            Assert.IsTrue(puiBtn.interactable, "default <Btn> Button.interactable should be true");

            InteractState seen = (InteractState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(InteractState.Normal, seen);
        }

        [Test]
        public void RuntimeInteractableFalse_DrivesButtonAndEmitsDisabled()
        {
            // Setting Btn.Interactable from code (e.g. a modal Configure hook) must bridge to
            // Button.interactable too — not just the CanvasGroup — so the button greys out and
            // OnState emits Disabled, matching the interactable='false' XML path. Before the
            // Btn override this only touched CanvasGroup, leaving the button un-greyed.
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.IsTrue(puiBtn.interactable, "precondition: starts interactable");

            btn.Interactable = false;

            Assert.IsFalse(puiBtn.interactable,
                "runtime Interactable=false should drive Button.interactable");
            InteractState seen = (InteractState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(InteractState.Disabled, seen);
        }

        [Test]
        public void RuntimeInteractableTrue_ReEnablesButton()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            btn.Interactable = false;
            btn.Interactable = true;

            Assert.IsTrue(puiBtn.interactable,
                "runtime Interactable=true should re-enable Button.interactable");
            InteractState seen = (InteractState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(InteractState.Normal, seen);
        }

        [Test]
        public void InteractableFalseXml_KeepsBlocksRaycasts()
        {
            // A disabled Btn must still ABSORB the pointer, never become click-through. Otherwise the
            // click falls through to whatever sits behind it — e.g. a modal's full-screen backdrop whose
            // OnPointerDown cancels — which is the "clicking a disabled CenteredSlideBox button closes the
            // window" bug. `interactable='false'` greys + suppresses onClick via CanvasGroup.interactable;
            // it must NOT also drop CanvasGroup.blocksRaycasts (standard Unity disabled-Selectable still
            // eats the raycast).
            var btn = BuildBtn("interactable='false'");
            var cg = btn.GameObject.GetComponent<CanvasGroup>();
            Assert.IsNotNull(cg, "disabling routes through the CanvasGroup-backed common attr");
            Assert.IsFalse(cg.interactable, "interactable='false' should disable the CanvasGroup");
            Assert.IsTrue(cg.blocksRaycasts,
                "disabled Btn must keep blocking raycasts so the click can't leak to a backdrop behind it");
        }

        [Test]
        public void RuntimeInteractableFalse_KeepsBlocksRaycasts()
        {
            // Same contract as the XML path, but disabled from code (e.g. a modal Configure hook gating
            // a button). blocksRaycasts must stay true so the disabled button still swallows the click.
            var btn = BuildBtn();
            btn.Interactable = false;

            var cg = btn.GameObject.GetComponent<CanvasGroup>();
            Assert.IsFalse(cg.interactable, "runtime Interactable=false should disable the CanvasGroup");
            Assert.IsTrue(cg.blocksRaycasts,
                "runtime-disabled Btn must keep blocking raycasts (no click-through to backdrop)");
        }

        [Test]
        public void PlainBtn_BackCompat_TargetGraphicIsBgAndTransitionIsColorTint()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(bg, puiBtn.targetGraphic);
            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition);
        }

        // ---- Phase 2: state-driven tint fan-out (StateTintReactor) ----

        // Force the reactor's fade to 0 so the target colour is applied synchronously
        // (no frame loop in EditMode). PRODUCTION default stays 0.1f.
        private static void UseInstantTint() => StateTintReactor.TestForceInstant = true;

        [Test]
        public void PressedModulate_InstallsReactorOnBgAndDescendantGraphics()
        {
            var btn = BuildBtnXml("pressedModulate='#808080'", "<Image id='img'/><Text id='t'>x</Text>");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg.GetComponent<StateTintReactor>(), "bg should host a reactor");

            var img = btn.Get<PuiImage>("img");
            Assert.IsNotNull(img.GameObject.GetComponent<StateTintReactor>(), "Image graphic should host a reactor");

            var txt = btn.Get<PuiText>("t");
            Assert.IsNotNull(txt.GameObject.GetComponent<StateTintReactor>(), "Text graphic should host a reactor");
        }

        [Test]
        public void PressedModulate_TintsThenRestoresOnStateChange()
        {
            UseInstantTint();
            var btn = BuildBtnXml("pressedModulate='#808080'", "<Image id='img'/>");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var img = btn.Get<PuiImage>("img").GameObject.GetComponent<UnityImage>();

            var bgBase = bg.color;
            var imgBase = img.color;
            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f); // #808080

            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            puiBtn.SimulateState(Pressed);

            AssertColorsEqual(bgBase * half, bg.color);
            AssertColorsEqual(imgBase * half, img.color);

            puiBtn.SimulateState(Normal);
            AssertColorsEqual(bgBase, bg.color);
            AssertColorsEqual(imgBase, img.color);
        }

        [Test]
        public void PressedModulate_VariantReSolve_KeepsSingleReactorAndReConfiguresMultiplier()
        {
            UseInstantTint();
            // pressedModulate has an inline Variant override: light=#808080, dark=#404040.
            var btn = BuildBtnXml("pressedModulate='#808080' pressedModulate.dark='#404040'", "<Image id='img'/>");
            var bg = btn.GameObject.GetComponent<UnityImage>();

            // Base (authored) colour + the single reactor installed by the first apply.
            var bgBase = bg.color;
            Assert.AreEqual(1, bg.GetComponents<StateTintReactor>().Length,
                "bg should host exactly one reactor after the initial apply");

            // Toggle the 'dark' variant: VariantStore.Changed fires → open Screen ReSolves →
            // Btn.OnAfterApply re-runs StateTintInstaller.Install with the dark-resolved multiplier.
            UI.Variants.Set("dark", true);

            // (a) No duplicate reactor — re-apply reuses the existing one via GetComponent ?? Add.
            Assert.AreEqual(1, bg.GetComponents<StateTintReactor>().Length,
                "Variant ReSolve must NOT add a second reactor");

            // (b) Pressed now multiplies by the dark override (#404040), and the base colour
            // is still the original authored colour (reactor never re-captured a tinted value).
            var dark = new Color(0.2509804f, 0.2509804f, 0.2509804f, 1f); // #404040
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            puiBtn.SimulateState(Pressed);
            AssertColorsEqual(bgBase * dark, bg.color);

            // Returning to Normal restores the untinted base, proving base wasn't promoted.
            puiBtn.SimulateState(Normal);
            AssertColorsEqual(bgBase, bg.color);
        }

        [Test]
        public void StateReactFalse_ChildKeepsColorAndHasNoReactor()
        {
            UseInstantTint();
            var btn = BuildBtnXml("pressedModulate='#808080'",
                "<Image id='keep' color='#FF0000' stateReact='false'/>");
            var keep = btn.Get<PuiImage>("keep").GameObject.GetComponent<UnityImage>();

            Assert.IsNull(keep.GetComponent<StateTintReactor>(),
                "stateReact='false' child must not get a reactor");

            var before = keep.color;
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            puiBtn.SimulateState(Pressed);
            AssertColorsEqual(before, keep.color); // unchanged across state
            puiBtn.SimulateState(Normal);
            AssertColorsEqual(before, keep.color);
        }

        [Test]
        public void NoStateColor_KeepsColorTintAndHasNoReactors()
        {
            var btn = BuildBtnXml("", "<Image id='img'/><Text id='t'>x</Text>");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition);

            var reactors = btn.GameObject.GetComponentsInChildren<StateTintReactor>(includeInactive: true);
            Assert.AreEqual(0, reactors.Length, "plain Btn must install zero reactors");
        }

        [Test]
        public void StateColor_SwitchesTransitionToNone()
        {
            var btn = BuildBtn("pressedModulate='#808080'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(Selectable.Transition.None, puiBtn.transition);
        }

        [Test]
        public void NestedStateSource_IsFanOutBoundary()
        {
            UseInstantTint();
            // Outer Btn with pressedModulate; an inner <Btn> (another IStateSource) must NOT receive the
            // outer's reactor on its own bg — the inner owns its subtree.
            var outer = BuildBtnXml("pressedModulate='#808080'", "<Btn id='inner'>x</Btn>");
            var inner = outer.Get<Btn>("inner");
            var innerBg = inner.GameObject.GetComponent<UnityImage>();
            Assert.IsNull(innerBg.GetComponent<StateTintReactor>(),
                "nested state source must be a fan-out boundary (no reactor from the outer Btn)");
        }

        [Test]
        public void PressedSprite_SwapsBgOverrideOnPressed_RevertsOnNormal()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;

            var btn = BuildBtn("pressedSprite='ui:pressed'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite; // built-in 9-slice default, must stay untouched
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            // Image.overrideSprite's getter returns m_OverrideSprite ?? sprite, so "no override
            // in effect" is observable as the getter falling back to the authored base sprite —
            // which is exactly the visible result we care about (no reflection needed).
            Assert.AreEqual(authored, bg.overrideSprite, "no override before press (falls back to base sprite)");

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(stub, bg.overrideSprite, "Pressed shows pressedSprite via overrideSprite");
            Assert.AreEqual(authored, bg.sprite, "authored sprite is untouched during press");

            puiBtn.SimulateState(Normal);
            Assert.AreEqual(authored, bg.overrideSprite, "release clears the override → getter falls back to base sprite");
            Assert.AreEqual(authored, bg.sprite, "authored sprite still untouched after release");
        }

        // User scenario: transparent normal (sprite="") + a bordered pressedSprite swapped in on press.
        // overrideSprite shares the Image's single `type` field, so the pressed image must render 9-sliced.
        [Test]
        public void PressedSprite_With9SliceBorder_OnTransparentNormal_RendersSliced()
        {
            var tex = new Texture2D(16, 16);
            var bordered = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
            UI.SpriteResolver = _ => bordered;

            var btn = BuildBtn("sprite='' pressedSprite='ui:pressed'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(bordered, bg.overrideSprite, "Pressed shows the bordered pressedSprite");
            Assert.AreEqual(UnityImage.Type.Sliced, bg.type, "bordered pressedSprite renders 9-sliced");
        }

        // Sibling of the above: when the bordered pressedSprite is hint-registered (.pxl tiled:true),
        // the authored-override branch of ApplyStateSprite derives Tiled (hint beats border).
        [Test]
        public void PressedSprite_TiledHint_RendersTiled()
        {
            var tex = new Texture2D(16, 16);
            var bordered = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
            PromptUGUI.Application.Internal.SpriteRenderHints.Register(bordered);
            UI.SpriteResolver = _ => bordered;

            var btn = BuildBtn("sprite='' pressedSprite='ui:pressed'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(bordered, bg.overrideSprite);
            Assert.AreEqual(UnityImage.Type.Tiled, bg.type, "hint-registered pressedSprite renders Tiled");
        }

        [Test]
        public void PressedSprite_DisablesDefaultColorTint()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;

            var btn = BuildBtn("pressedSprite='ui:pressed'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(Selectable.Transition.None, puiBtn.transition,
                "a pressedSprite must switch the Btn off uGUI's built-in ColorTint");
        }

        [Test]
        public void PressedSprite_ComposesWithPressedModulate()
        {
            UseInstantTint();
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;

            var btn = BuildBtnXml("pressedSprite='ui:pressed' pressedModulate='#808080'", "<Image id='img'/>");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var bgBase = bg.color;
            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f); // #808080
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(stub, bg.overrideSprite, "sprite swaps on press");
            AssertColorsEqual(bgBase * half, bg.color);  // and the tint reactor still multiplies
        }

        [Test]
        public void PressedSprite_VariantOverride_ReResolves()
        {
            var a = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var b = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:b" ? b : a;

            var btn = BuildBtn("pressedSprite='ui:a' pressedSprite.dark='ui:b'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(a, bg.overrideSprite, "light variant uses 'ui:a'");

            puiBtn.SimulateState(Normal);
            UI.Variants.Set("dark", true); // ReSolve re-invokes the setter with the 'dark' override

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(b, bg.overrideSprite, "dark variant uses 'ui:b' after ReSolve");
        }

        [Test]
        public void PressedSprite_None_NoSwapAndKeepsColorTint()
        {
            var btn = BuildBtn("pressedSprite='none'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite;
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition,
                "pressedSprite='none' must not disable the default ColorTint");

            puiBtn.SimulateState(Pressed);
            // none => no override in effect; the getter falls back to the base sprite.
            Assert.AreEqual(authored, bg.overrideSprite, "pressedSprite='none' => no swap on press");
        }

        [Test]
        public void DisabledSprite_SwapsBgOverrideWhenDisabled_RevertsWhenEnabled()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;

            var btn = BuildBtn("disabledSprite='ui:disabled'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite;
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            Assert.AreEqual(authored, bg.overrideSprite, "no override while interactable");

            puiBtn.SimulateState(Disabled);
            Assert.AreEqual(stub, bg.overrideSprite, "Disabled shows disabledSprite via overrideSprite");
            Assert.AreEqual(authored, bg.sprite, "authored sprite untouched while disabled");

            puiBtn.SimulateState(Normal);
            Assert.AreEqual(authored, bg.overrideSprite, "back to base sprite when re-enabled");
        }

        [Test]
        public void DisabledSprite_DisablesDefaultColorTint()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;

            var btn = BuildBtn("disabledSprite='ui:disabled'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(Selectable.Transition.None, puiBtn.transition,
                "a disabledSprite must switch the Btn off uGUI's built-in ColorTint (no double-darken)");
        }

        // Disabled and Pressed are mutually exclusive states; the resolver prioritises Disabled.
        [Test]
        public void DisabledAndPressedSprite_EachStateShowsItsOwnSprite()
        {
            var pressed = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var disabled = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:disabled" ? disabled : pressed;

            var btn = BuildBtn("pressedSprite='ui:pressed' disabledSprite='ui:disabled'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(pressed, bg.overrideSprite, "Pressed shows pressedSprite");

            puiBtn.SimulateState(Disabled);
            Assert.AreEqual(disabled, bg.overrideSprite, "Disabled shows disabledSprite");
        }

        // ---- Default pressed-state fallback (built-in pugui_9slice_pressed skin) ----

        [Test]
        public void DefaultBtn_PressedFallsBackToBuiltinPressedSprite()
        {
            var btn = BuildBtn("");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            puiBtn.SimulateState(Pressed);
            Assert.AreEqual("pugui_9slice_pressed", bg.overrideSprite.name);
            puiBtn.SimulateState(Normal);
            Assert.AreEqual(bg.sprite, bg.overrideSprite, "release 后回落 base sprite");
        }

        [Test]
        public void DefaultBtn_PressedFallback_KeepsColorTintTransition()
        {
            var btn = BuildBtn("");
            Assert.AreEqual(Selectable.Transition.ColorTint,
                btn.GameObject.GetComponent<PuiButton>().transition,
                "默认兜底不得触发 transition=None（hover 反馈保留）");
        }

        [Test]
        public void AuthoredSprite_SuppressesDefaultPressedFallback()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = _ => stub;
            var btn = BuildBtn("sprite='ui:custom'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            btn.GameObject.GetComponent<PuiButton>().SimulateState(Pressed);
            Assert.AreEqual(bg.sprite, bg.overrideSprite, "自定皮肤按钮没有内置按下图");
        }

        [Test]
        public void PressedSpriteEmpty_SuppressesDefaultPressedFallback()
        {
            var btn = BuildBtn("pressedSprite=''");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            btn.GameObject.GetComponent<PuiButton>().SimulateState(Pressed);
            Assert.AreEqual(bg.sprite, bg.overrideSprite, "显式 ''/none = 关闭换图，包括默认兜底");
        }

        [Test]
        public void TransparentSprite_SuppressesDefaultPressedFallback()
        {
            var btn = BuildBtn("sprite=''");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            btn.GameObject.GetComponent<PuiButton>().SimulateState(Pressed);
            Assert.AreEqual(bg.sprite, bg.overrideSprite, "透明按钮（sprite=''）按下不得冒出内置按下皮");
        }

        [Test]
        public void DisabledSprite_None_NoSwapAndKeepsColorTint()
        {
            var btn = BuildBtn("disabledSprite='none'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var authored = bg.sprite;
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition,
                "disabledSprite='none' must not disable the default ColorTint");

            puiBtn.SimulateState(Disabled);
            Assert.AreEqual(authored, bg.overrideSprite, "disabledSprite='none' => no swap when disabled");
        }

        private static void AssertColorsEqual(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), "b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), "a");
        }
    }
}
