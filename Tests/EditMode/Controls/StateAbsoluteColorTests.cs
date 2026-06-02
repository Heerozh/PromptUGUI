using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateAbsoluteColorTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        // Two-tab bar so we can drive 'a' to Normal (select 'b') then to Selected (select 'a').
        private static (Tab a, Tab b) TwoTabs(string aAttrs, string aBody = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' {aAttrs}>{aBody}</Tab><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            return (s.Get<Tab>("bar/a"), s.Get<Tab>("bar/b"));
        }

        private static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        [Test]
        public void SelectedColor_IsAbsolute_NotMultiplied()
        {
            var (a, b) = TwoTabs("color='#202020' selectedColor='#076DD7'");
            var bg = a.GameObject.GetComponent<UnityImage>();
            b.IsOn = true;            // a -> Normal (bg == #202020)
            a.IsOn = true;            // a -> Selected
            var expected = Hex("#076DD7");          // absolute, NOT #202020 × #076DD7
            Assert.That(bg.color.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(bg.color.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(bg.color.b, Is.EqualTo(expected.b).Within(0.001f));
        }

        [Test]
        public void SelectedColor_DoesNotFanOutToDescendants()
        {
            var (a, b) = TwoTabs("selectedColor='#ff0000'", "<Text id='lbl' color='#00ff00'>x</Text>");
            var label = a.Get<Text>("lbl").GameObject.GetComponent<TMP_Text>();
            var bg = a.GameObject.GetComponent<UnityImage>();
            b.IsOn = true;
            a.IsOn = true;            // a -> Selected
            Assert.That(bg.color.r, Is.EqualTo(1f).Within(0.001f), "bg becomes absolute red");
            Assert.That(label.color.g, Is.EqualTo(1f).Within(0.001f), "label keeps its own green (absolute does not fan out)");
            Assert.That(label.color.r, Is.EqualTo(0f).Within(0.001f), "label not painted red");
        }

        [Test]
        public void AbsoluteAndModulate_Compose()
        {
            var (a, b) = TwoTabs("color='#202020' selectedColor='#ffffff' selectedModulate='#808080'");
            var bg = a.GameObject.GetComponent<UnityImage>();
            b.IsOn = true;
            a.IsOn = true;            // a -> Selected: (#ffffff) × (#808080) ≈ 0.5 grey
            var half = 0.5019608f;    // 0x80 / 255
            Assert.That(bg.color.r, Is.EqualTo(half).Within(0.001f));
        }

        [Test]
        public void StateWithoutAnyAttr_FallsBackToColorBase()
        {
            // selectedColor present, but Pressed has neither attr → Pressed bg == color base.
            var (a, b) = TwoTabs("color='#202020' selectedColor='#076DD7'");
            var bg = a.GameObject.GetComponent<UnityImage>();
            var pt = a.GameObject.GetComponent<PuiToggle>();
            pt.SimulateState((int)InteractState.Pressed);
            var baseC = Hex("#202020");
            Assert.That(bg.color.r, Is.EqualTo(baseC.r).Within(0.001f));
            Assert.That(bg.color.g, Is.EqualTo(baseC.g).Within(0.001f));
        }
    }
}
