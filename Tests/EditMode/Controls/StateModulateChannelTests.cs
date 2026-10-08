using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PuiImage = PromptUGUI.Controls.Image;
using PuiText = PromptUGUI.Controls.Text;
using ToggleControl = PromptUGUI.Controls.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>*Modulate</c> is a multiplier, so it lives on the graphic's <c>CanvasRenderer</c> colour —
    /// the same layer uGUI's own ColorTint drives — and never in <c>Graphic.color</c>, which belongs
    /// to whoever set the graphic's colour: its <c>color=</c>, a Variant, a theme, or code.
    ///
    /// <para>The fan-out used to write <c>captured base × multiplier</c> into <c>Graphic.color</c>,
    /// with the base peeked once when the reactor was installed. Every later write to a descendant's
    /// colour was then undone by the next hover or press: a <c>Text.Color</c> set from C#, a
    /// <c>color.alt=</c> Variant, a theme token. The bg had the same defect for a runtime
    /// <c>Btn.Color</c>, and the default disabled grey for a label (it captured <c>tmp.color</c> and
    /// wrote it back on enable).</para>
    /// </summary>
    public class StateModulateChannelTests
    {
        // Selectable.SelectionState ordinals (the test assembly cannot name the protected type).
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;
        private const int Disabled = 4;

        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);    // #808080
        private static readonly Color Quarter = new Color(0.2509804f, 0.2509804f, 0.2509804f, 1f); // #404040

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = true;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                + $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        /// <summary>What reaches the screen: the graphic's own colour times the CanvasRenderer's.</summary>
        private static Color Shown(Graphic g) => g.color * g.canvasRenderer.GetColor();

        /// <summary>Alpha after every CanvasGroup above the graphic has had its say.</summary>
        private static float ShownAlpha(Graphic g)
        {
            var a = g.color.a * g.canvasRenderer.GetAlpha();
            for (var t = g.transform; t != null; t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group == null) continue;
                a *= group.alpha;
                if (group.ignoreParentGroups) break;
            }
            return a;
        }

        private static void AssertColor(Color expected, Color actual, string because)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.002f), because + " (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.002f), because + " (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.002f), because + " (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.002f), because + " (a)");
        }

        private static PuiButton PuiOf(IControl control)
            => ((Control)control).GameObject.GetComponent<PuiButton>();

        // ── the reported case: a colour written from code inside a modulated control ──────────

        [Test]
        public void RuntimeTextColour_InsideAModulatedBtn_SurvivesHoverAndPress()
        {
            var btn = Open("<Btn id='b' width='80' height='30' hoverModulate='#808080' pressedModulate='#404040'>"
                           + "<Text id='t'>x</Text></Btn>").Get<Btn>("b");
            var text = btn.Get<PuiText>("t");
            var tmp = text.GameObject.GetComponent<TMP_Text>();
            var pui = PuiOf(btn);

            text.Color = "#FF0000";

            pui.SimulateState(Highlighted);
            AssertColor(Color.red * Half, Shown(tmp), "hovered: the colour code set, dimmed by hoverModulate");
            pui.SimulateState(Pressed);
            AssertColor(Color.red * Quarter, Shown(tmp), "pressed: dimmed by pressedModulate");
            pui.SimulateState(Normal);
            AssertColor(Color.red, Shown(tmp), "at rest: still the colour code set, not the one captured at build");
            AssertColor(Color.red, tmp.color, "the multiplier never lands in the text's own colour");
        }

        [Test]
        public void RuntimeTextColour_WrittenMidHover_IsDimmedAtOnce_AndKeptOnRelease()
        {
            var btn = Open("<Btn id='b' width='80' height='30' hoverModulate='#808080'><Text id='t'>x</Text></Btn>")
                .Get<Btn>("b");
            var text = btn.Get<PuiText>("t");
            var tmp = text.GameObject.GetComponent<TMP_Text>();
            var pui = PuiOf(btn);

            pui.SimulateState(Highlighted);
            text.Color = "#00FF00";
            AssertColor(Color.green * Half, Shown(tmp), "written while hovered: shown dimmed straight away");

            pui.SimulateState(Normal);
            AssertColor(Color.green, Shown(tmp), "released: the written colour, undimmed");
        }

        [Test]
        public void DescendantColour_FollowsAVariant_PastAStateChange()
        {
            UI.Variants.Set("alt", false);
            var btn = Open("<Btn id='b' width='80' height='30' hoverModulate='#808080'>"
                           + "<Text id='t' color='#FF0000' color.alt='#0000FF'>x</Text></Btn>").Get<Btn>("b");
            var tmp = btn.Get<PuiText>("t").GameObject.GetComponent<TMP_Text>();
            var pui = PuiOf(btn);

            UI.Variants.Set("alt", true);
            pui.SimulateState(Highlighted);
            pui.SimulateState(Normal);

            AssertColor(Color.blue, Shown(tmp), "the Variant's colour, not the one the reactor peeked at build");
        }

        [Test]
        public void DescendantColour_FollowsATheme_PastAStateChange()
        {
            const string xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
  <Theme name='a'><Color name='accent' value='#FF0000'/></Theme>
  <Theme name='b'><Color name='accent' value='#0000FF'/></Theme>
  <Screen name='S'>
    <Btn id='b' width='80' height='30' pressedModulate='#808080'><Image id='i' size='10x10' color='accent'/></Btn>
  </Screen>
</PromptUGUI>";
            UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);
            UI.LoadDocumentAsync("d").GetAwaiter().GetResult();
            UI.Theme.Set("a");
            var btn = UI.Open("S").Get<Btn>("b");
            var img = btn.Get<PuiImage>("i").GameObject.GetComponent<UnityEngine.UI.Image>();
            var pui = PuiOf(btn);

            UI.Theme.Set("b");
            pui.SimulateState(Pressed);
            AssertColor(Color.blue * Half, Shown(img), "pressed: the new theme's accent, dimmed");
            pui.SimulateState(Normal);
            AssertColor(Color.blue, Shown(img), "released: the new theme's accent, not the first theme's");
        }

        [Test]
        public void RuntimeBtnTextColour_SurvivesAPress()
        {
            var btn = Open("<Btn id='b' width='80' height='30' pressedModulate='#808080'>Buy</Btn>").Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            var pui = PuiOf(btn);

            btn.TextColor = "#00FF00";
            pui.SimulateState(Pressed);
            pui.SimulateState(Normal);

            AssertColor(Color.green, Shown(label), "textColor set from code is the label's colour from then on");
        }

        [Test]
        public void RuntimeCheckmarkColour_SurvivesAHover()
        {
            var toggle = Open("<Toggle id='g' isOn='true' hoverModulate='#808080'>x</Toggle>").Get<ToggleControl>("g");
            var check = toggle.GameObject.transform.Find("Background/Checkmark").GetComponent<Graphic>();
            var pui = toggle.GameObject.GetComponent<PuiToggle>();

            toggle.CheckmarkColor = "#FF0000";
            pui.SimulateState(Highlighted);
            pui.SimulateState(Normal);

            AssertColor(Color.red, Shown(check), "checkmarkColor set from code survives the hover");
        }

        // ── the channel itself ────────────────────────────────────────────────────────────

        [Test]
        public void TheMultiplier_LandsOnTheCanvasRenderer_NotInTheGraphicsColour()
        {
            var btn = Open("<Btn id='b' width='80' height='30' color='#FFFFFF' pressedModulate='#808080'>"
                           + "<Image id='i' size='10x10' color='#FF0000'/></Btn>").Get<Btn>("b");
            var img = btn.Get<PuiImage>("i").GameObject.GetComponent<UnityEngine.UI.Image>();
            var bg = PuiOf(btn).targetGraphic;
            var pui = PuiOf(btn);

            pui.SimulateState(Pressed);
            AssertColor(Color.red, img.color, "a descendant keeps its own colour");
            AssertColor(Half, img.canvasRenderer.GetColor(), "the multiplier sits on its CanvasRenderer");
            AssertColor(Color.white, bg.color, "the bg keeps its color= too");
            AssertColor(Half, bg.canvasRenderer.GetColor(), "and is dimmed the same way");

            pui.SimulateState(Normal);
            AssertColor(Color.white, img.canvasRenderer.GetColor(), "released: identity multiplier");
            AssertColor(Color.white, bg.canvasRenderer.GetColor(), "released: identity multiplier on the bg");
        }

        [Test]
        public void TheMultiplier_ReachesTmpSubMeshes()
        {
            // Glyphs from a fallback font (any CJK label) live in a TMP_SubMeshUI with its own
            // CanvasRenderer. TMP copies the parent's CanvasRenderer colour into it only when the
            // mesh is regenerated, which a multiplier change does not do.
            var btn = Open("<Btn id='b' width='80' height='30' pressedModulate='#808080'>Hi</Btn>").Get<Btn>("b");
            var label = (TextMeshProUGUI)btn.GameObject.GetComponentInChildren<TMP_Text>();
            var shared = new Material(label.fontSharedMaterial) { name = "fallback (shared)" };
            try
            {
                var sub = TMP_SubMeshUI.AddSubTextObject(label, new MaterialReference(1, label.font, null, shared, 0f));
                var pui = PuiOf(btn);

                pui.SimulateState(Pressed);
                AssertColor(Half, sub.canvasRenderer.GetColor(), "pressed: the fallback glyphs dim with the rest");
                pui.SimulateState(Normal);
                AssertColor(Color.white, sub.canvasRenderer.GetColor(), "released: back to identity");
            }
            finally
            {
                Object.DestroyImmediate(shared);
            }
        }

        [Test]
        public void ANestedSelectablesTintTarget_IsLeftToIt()
        {
            // A Slider inside a modulated Btn tints its own handle through that same CanvasRenderer
            // colour (uGUI ColorTint). Two writers on one channel overwrite each other every
            // transition, so the fan-out leaves that graphic to the Slider.
            var btn = Open("<Btn id='b' width='200' height='40' pressedModulate='#808080'>"
                           + "<Slider id='s' width='150' height='20'/></Btn>").Get<Btn>("b");
            var slider = btn.Get<IControl>("s").GameObject.GetComponent<UnityEngine.UI.Slider>();
            Assume.That(slider.transition, Is.EqualTo(Selectable.Transition.ColorTint), "guard: the Slider tints its handle");
            Assume.That(slider.targetGraphic, Is.Not.Null, "guard: the Slider has a tint target");

            Assert.IsNull(slider.targetGraphic.GetComponent<StateTintReactor>(),
                "the Slider's tint target belongs to the Slider");
        }

        // ── Toggle: the check's visibility must not share the multiplier's channel ──────────

        [Test]
        public void ToggleCheck_StaysHiddenUnderAHoverModulate_WhileUnchecked()
        {
            var toggle = Open("<Toggle id='g' hoverModulate='#808080'>x</Toggle>").Get<ToggleControl>("g");
            var check = toggle.GameObject.transform.Find("Background/Checkmark").GetComponent<Graphic>();
            var pui = toggle.GameObject.GetComponent<PuiToggle>();

            pui.SimulateState(Highlighted);
            Assert.That(ShownAlpha(check), Is.EqualTo(0f).Within(0.001f),
                "hovering an unchecked Toggle must not reveal its check");

            toggle.IsOn = true;
            Assert.That(ShownAlpha(check), Is.EqualTo(1f).Within(0.001f), "checked: the check shows");
            AssertColor(check.color * Half, Shown(check), "…dimmed by the hover multiplier");

            toggle.IsOn = false;
            Assert.That(ShownAlpha(check), Is.EqualTo(0f).Within(0.001f), "unchecked again: hidden again");
        }

        [Test]
        public void ToggleCheck_AuthoredOn_ShowsFromTheStart()
        {
            var on = Open("<Toggle id='g' isOn='true'>x</Toggle>").Get<ToggleControl>("g");
            var check = on.GameObject.transform.Find("Background/Checkmark").GetComponent<Graphic>();
            Assert.That(ShownAlpha(check), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void ToggleCheck_AuthoredOff_IsHiddenFromTheStart()
        {
            var off = Open("<Toggle id='g'>x</Toggle>").Get<ToggleControl>("g");
            var check = off.GameObject.transform.Find("Background/Checkmark").GetComponent<Graphic>();
            Assert.That(ShownAlpha(check), Is.EqualTo(0f).Within(0.001f));
        }

        // ── the control's own bg: a runtime color= is the new base ──────────────────────────

        [Test]
        public void RuntimeBtnColour_IsTheBaseItReturnsTo_PastAHover()
        {
            var btn = Open("<Btn id='b' width='80' height='30' color='#888888' hoverColor='#FF0000'>x</Btn>")
                .Get<Btn>("b");
            var pui = PuiOf(btn);
            var bg = pui.targetGraphic;

            btn.Color = "#00FF00";
            AssertColor(Color.green, Shown(bg), "written at rest: shown at once");
            pui.SimulateState(Highlighted);
            AssertColor(Color.red, Shown(bg), "hovered: hoverColor");
            pui.SimulateState(Normal);
            AssertColor(Color.green, Shown(bg), "released: the base code set, not the one declared at build");
        }

        [Test]
        public void RuntimeBtnColour_WrittenMidHover_KeepsTheHoverColourUntilRelease()
        {
            var btn = Open("<Btn id='b' width='80' height='30' color='#888888' hoverColor='#FF0000'>x</Btn>")
                .Get<Btn>("b");
            var pui = PuiOf(btn);
            var bg = pui.targetGraphic;

            pui.SimulateState(Highlighted);
            btn.Color = "#00FF00";
            AssertColor(Color.red, Shown(bg), "still hovered: hoverColor wins over the new base");
            pui.SimulateState(Normal);
            AssertColor(Color.green, Shown(bg), "released: the new base");
        }

        [Test]
        public void RuntimeTabColour_IsTheUnselectedBase_PastASelection()
        {
            var screen = Open("<TabBar id='tb' width='200' height='30'>"
                              + "<Tab id='t' width='stretch' color='#888888' selectedColor='#FF0000'/>"
                              + "<Tab id='o' width='stretch' isOn='true'/></TabBar>");
            var tab = screen.Get<Tab>("t");
            var bg = tab.GameObject.GetComponent<PuiToggle>().targetGraphic;

            tab.Color = "#00FF00";
            tab.IsOn = true;
            AssertColor(Color.red, Shown(bg), "selected: selectedColor");
            screen.Get<Tab>("o").IsOn = true;
            AssertColor(Color.green, Shown(bg), "deselected: the base code set");
        }

        [Test]
        public void RuntimeToggleColour_IsTheBaseItReturnsTo_PastAHover()
        {
            var toggle = Open("<Toggle id='g' color='#888888' hoverColor='#FF0000'>x</Toggle>").Get<ToggleControl>("g");
            var pui = toggle.GameObject.GetComponent<PuiToggle>();
            var bg = pui.targetGraphic;

            toggle.Color = "#00FF00";
            pui.SimulateState(Highlighted);
            pui.SimulateState(Normal);
            AssertColor(Color.green, Shown(bg), "released: the base code set");
        }

        [Test]
        public void RuntimeCollapsibleHeaderColour_IsTheBaseItReturnsTo_PastAHover()
        {
            var c = Open("<Collapsible id='c' text='T' transition='0' headerColor='#888888' hoverColor='#FF0000'>"
                         + "<Btn id='r' height='30'/></Collapsible>").Get<Collapsible>("c");
            var pui = c.GameObject.transform.Find("Header").GetComponent<PuiButton>();
            var bg = pui.targetGraphic;

            c.HeaderColor = "#00FF00";
            pui.SimulateState(Highlighted);
            pui.SimulateState(Normal);
            AssertColor(Color.green, Shown(bg), "released: the header colour code set");
        }

        // ── the default disabled grey: on the glyphs' vertices, never in the text's colour ──

        private static Color32 FirstVertex(TMP_Text tmp)
        {
            tmp.ForceMeshUpdate();
            Assume.That(tmp.textInfo.characterCount, Is.GreaterThan(0), "guard: the text generated glyphs");
            return tmp.textInfo.meshInfo[0].colors32[0];
        }

        [Test]
        public void DefaultGrey_LeavesTheTextColourAlone_AndComesOffOnEnable()
        {
            var btn = Open("<Btn id='b' width='80' height='30'><Text id='t'>Hi</Text></Btn>").Get<Btn>("b");
            Assume.That(btn.GameObject.GetComponent<DisabledGrayscaleController>(), Is.Not.Null,
                "guard: no disabled* authored, so the default grey is installed");
            var text = btn.Get<PuiText>("t");
            var tmp = text.GameObject.GetComponent<TMP_Text>();
            var pui = PuiOf(btn);

            text.Color = "#FF0000";
            var lit = FirstVertex(tmp);

            pui.SimulateState(Disabled);
            var greyed = FirstVertex(tmp);
            Assert.AreEqual(DisabledGrayscaleController.Desaturate(lit), greyed, "disabled: the glyphs go grey");
            Assert.AreNotEqual(lit, greyed, "guard: grey differs from red");
            AssertColor(Color.red, tmp.color, "…without the grey being written into the colour the text owns");

            pui.SimulateState(Normal);
            AssertColor(Color.red, tmp.color, "re-enabled: still the colour code set");
            Assert.AreEqual(lit, FirstVertex(tmp), "and the glyphs are lit again");
        }

        [Test]
        public void DefaultGrey_AColourWrittenWhileDisabled_ShowsGrey_ThenLitOnEnable()
        {
            var btn = Open("<Btn id='b' width='80' height='30'><Text id='t'>Hi</Text></Btn>").Get<Btn>("b");
            var text = btn.Get<PuiText>("t");
            var tmp = text.GameObject.GetComponent<TMP_Text>();
            var pui = PuiOf(btn);

            pui.SimulateState(Disabled);
            text.Color = "#00FF00";
            var greyed = FirstVertex(tmp);

            pui.SimulateState(Normal);
            var lit = FirstVertex(tmp);
            AssertColor(Color.green, tmp.color, "the colour written while disabled is the text's colour");
            Assert.AreEqual(DisabledGrayscaleController.Desaturate(lit), greyed,
                "while still disabled it showed grey, not green");
        }
    }
}
