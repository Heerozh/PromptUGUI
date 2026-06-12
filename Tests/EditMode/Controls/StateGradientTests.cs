using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Task 8 — gradient state colours. State ABSOLUTES (hover/pressed/selected/disabled Color)
    /// may be gradients and land through <see cref="ColorApplier"/> (enabling/disabling the bg
    /// <see cref="GradientTint"/>); *Modulate stays solid-only and a gradient value throws.
    /// </summary>
    public class StateGradientTests
    {
        // PuiButton SelectionState ordinals (mirror of the protected enum; PuiButton casts internally).
        private const int Highlighted = 1; // -> Hover
        private const int Pressed = 2;     // -> Pressed
        private const int Normal = 0;

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
                $"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static Btn BuildBtn(string attrs)
        {
            var s = Open($"<Btn id='b' {attrs}>x</Btn>");
            return s.Get<Btn>("b");
        }

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        private static void AssertColorApprox(Color actual, Color expected, float within, string msg)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(within), msg + " (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(within), msg + " (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(within), msg + " (b)");
        }

        // 1. Gradient hoverColor over a solid base — enters Hover -> gradient tint; back to Normal -> base restored.
        [Test]
        public void GradientHoverColor_EnablesGradientTintOnHover_RestoresSolidBaseOnNormal()
        {
            var btn = BuildBtn("color='#888888' hoverColor='#ffffff,#000000'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var pui = btn.GameObject.GetComponent<PuiButton>();
            var tint = bg.GetComponent<GradientTint>();

            // At rest (Normal): solid base, no enabled gradient.
            Assert.IsTrue(tint == null || !tint.enabled, "no gradient at rest (solid base)");

            pui.SimulateState(Highlighted); // -> Hover
            tint = bg.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint added on hover");
            Assert.IsTrue(tint.enabled, "GradientTint enabled on hover");
            Assert.AreEqual(Color.white, tint.Top, "hover gradient top white");
            Assert.AreEqual(Color.black, tint.Bottom, "hover gradient bottom black");

            pui.SimulateState(Normal); // -> Normal
            Assert.IsFalse(tint.enabled, "GradientTint disabled back at Normal");
            AssertColorApprox(bg.color, Hex("#888888"), 0.01f, "solid base #888888 restored");
        }

        // 2. Gradient BASE + solid hoverColor — the Peek-base-capture test.
        [Test]
        public void GradientBase_SolidHoverColor_RoundTripsTintAcrossStates()
        {
            var btn = BuildBtn("color='#ffffff,#000000' hoverColor='#ff0000'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var pui = btn.GameObject.GetComponent<PuiButton>();
            var tint = bg.GetComponent<GradientTint>();

            // At rest (Normal): the BASE gradient must be live (Peek captured it as the reactor base).
            Assert.IsNotNull(tint, "base gradient tint present at rest");
            Assert.IsTrue(tint.enabled, "base gradient enabled at rest");
            Assert.AreEqual(Color.white, tint.Top, "base gradient top white");
            Assert.AreEqual(Color.black, tint.Bottom, "base gradient bottom black");

            pui.SimulateState(Highlighted); // -> Hover (solid red)
            Assert.IsFalse(tint.enabled, "solid hover disables base gradient");
            AssertColorApprox(bg.color, Color.red, 0.01f, "solid hover red applied");

            pui.SimulateState(Normal); // -> Normal: base gradient restored
            Assert.IsTrue(tint.enabled, "base gradient re-enabled at Normal");
            Assert.AreEqual(Color.white, tint.Top, "base gradient top restored");
            Assert.AreEqual(Color.black, tint.Bottom, "base gradient bottom restored");
        }

        // 3. Gradient base × solid pressedModulate — both stops are halved.
        [Test]
        public void GradientBase_SolidPressedModulate_MultipliesBothStops()
        {
            var btn = BuildBtn("color='#ffffff,#000000' pressedModulate='#808080'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var pui = btn.GameObject.GetComponent<PuiButton>();
            var tint = bg.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "base gradient present");

            pui.SimulateState(Pressed); // -> Pressed: gradient × 0.5
            Assert.IsTrue(tint.enabled, "gradient stays enabled under solid modulate");
            var half = 0x80 / 255f; // ~0.5019608
            // white × 0.5 = (half,half,half); black × 0.5 = (0,0,0). ±3 bytes ≈ 0.012 tolerance.
            AssertColorApprox(tint.Top, new Color(half, half, half, 1f), 3f / 255f, "top = white × grey");
            AssertColorApprox(tint.Bottom, new Color(0f, 0f, 0f, 1f), 3f / 255f, "bottom = black × grey");
        }

        // 4. Gradient selectedColor on a Tab (selection base) — active tab shows the gradient at rest.
        [Test]
        public void GradientSelectedColor_OnActiveTab_ShowsGradientAtRest()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' isOn='true' selectedColor='#ffffff,#000000'/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var a = s.Get<Tab>("bar/a");
            var bg = a.GameObject.GetComponent<UnityImage>();

            var tint = bg.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "selected tab gradient present");
            Assert.IsTrue(tint.enabled, "selected tab gradient enabled at rest");
            Assert.AreEqual(Color.white, tint.Top, "selected gradient top white");
            Assert.AreEqual(Color.black, tint.Bottom, "selected gradient bottom black");
        }

        // 5. A *Modulate with a gradient value throws (modulates are solid-only).
        [Test]
        public void GradientModulate_Throws_On_Open()
        {
            // hoverModulate routes through UI.Theme.Resolve, which rejects gradients;
            // ControlAttributeApplier wraps the System.Exception as a ParseException.
            var ex = Assert.Throws<ParseException>(
                () => Open("<Btn id='b' hoverModulate='#ffffff,#000000'>x</Btn>"));
            StringAssert.Contains("does not support gradient", ex.Message);
        }

        // 6. ReSolve (Variant toggle) must not lose an active gradient state.
        [Test]
        public void ReSolve_KeepsGradientHoverState()
        {
            var btn = BuildBtn("color='#888888' hoverColor='#ffffff,#000000'");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var pui = btn.GameObject.GetComponent<PuiButton>();

            pui.SimulateState(Highlighted); // -> Hover (gradient enabled)
            var tint = bg.GetComponent<GradientTint>();
            Assert.IsNotNull(tint);
            Assert.IsTrue(tint.enabled, "precondition: gradient hover enabled before ReSolve");

            try
            {
                // Toggling any variant fires Variants.Changed -> Screen.ReSolve, which re-runs
                // ControlAttributeApplier.Apply -> Btn.OnAfterApply -> reactor re-Configure (repaint).
                UI.Variants.Set("mobile", true);

                Assert.IsTrue(tint.enabled,
                    "gradient hover state must survive ReSolve (re-Configure repaint)");
                Assert.AreEqual(Color.white, tint.Top, "gradient top kept after ReSolve");
                Assert.AreEqual(Color.black, tint.Bottom, "gradient bottom kept after ReSolve");
            }
            finally
            {
                UI.Variants.Set("mobile", false);
            }
        }
    }
}
