using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnPressOffsetTests
    {
        private const int Normal = 0, Pressed = 2, Selected = 3, Disabled = 4;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Btn BuildBtn(string attrs, string body = "Hi")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {attrs}>{body}</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Btn>("b");
        }

        private static RectTransform Holder(Control c)
            => (RectTransform)c.GameObject.GetComponentInChildren<PressOffsetController>(true).transform;

        [Test]
        public void PressedOffset_ShiftsHolderDown_RevertsOnNormal()
        {
            var btn = BuildBtn("pressedOffset='0,-4'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var holder = Holder(btn);

            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "rest at zero");
            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -4), holder.anchoredPosition, "pressed -> down 4px");
            puiBtn.SimulateState(Normal);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "release -> zero");
        }

        [Test]
        public void NoOffset_CreatesNoHolder_LabelStaysDirectChild()
        {
            var btn = BuildBtn("");
            Assert.IsNull(btn.GameObject.GetComponentInChildren<PressOffsetController>(true),
                "plain Btn must not create an offset holder");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual(btn.GameObject.transform, label.transform.parent,
                "label is a direct child when no offset");
        }

        [Test]
        public void PressedOffset_ReparentsLabelIntoHolder()
        {
            var btn = BuildBtn("pressedOffset='0,-4'");
            var holder = Holder(btn);
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.AreEqual((Transform)holder, label.transform.parent, "label moved under the holder");
        }

        [Test]
        public void Disabled_StaysAtZero()
        {
            var btn = BuildBtn("pressedOffset='0,-4'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var holder = Holder(btn);
            puiBtn.SimulateState(Disabled);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "disabled has no offset");
        }

        [Test]
        public void SelectedFolds_NoOffsetForMomentaryBtn()
        {
            // Btn has no isOn; Selected folds to Normal, so SimulateState(Selected) -> zero.
            var btn = BuildBtn("pressedOffset='0,-4'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var holder = Holder(btn);
            puiBtn.SimulateState(Selected);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition);
        }

        [Test]
        public void VariantReSolve_NoDuplicateHolder_ReResolves()
        {
            var btn = BuildBtn("pressedOffset='0,-4' pressedOffset.dark='0,-8'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(1, btn.GameObject.GetComponentsInChildren<PressOffsetController>(true).Length,
                "one holder after initial apply");

            UI.Variants.Set("dark", true);   // VariantStore.Changed -> Screen ReSolve -> OnAfterApply re-runs

            Assert.AreEqual(1, btn.GameObject.GetComponentsInChildren<PressOffsetController>(true).Length,
                "Variant ReSolve must not add a second holder");
            var holder = Holder(btn);
            puiBtn.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -8), holder.anchoredPosition, "dark override applies after ReSolve");
        }
    }
}
