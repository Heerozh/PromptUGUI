using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class SubmitWakeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // ── TryWakeOnSubmit 三分支（纯逻辑，无控件） ──────────────────────────
        [Test]
        public void TryWake_NavDisabled_ReturnsFalse_NoFlip()
        {
            Assert.IsFalse(UI.Navigation.IsEnabled);
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            Assert.IsFalse(UI.Navigation.TryWakeOnSubmit());
            Assert.AreEqual(UI.Navigation.NavMode.Pointer, UI.Navigation.Mode);
        }

        [Test]
        public void TryWake_Directional_ReturnsFalse_NoFlip()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            Assert.IsFalse(UI.Navigation.TryWakeOnSubmit());
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
        }

        [Test]
        public void TryWake_PointerEnabled_ReturnsTrue_FlipsToDirectional()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            Assert.IsTrue(UI.Navigation.TryWakeOnSubmit());
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
        }

        // ── PuiButton.OnSubmit 吞掉分支（EditMode 干净：提前 return，不调 base 协程） ──
        [Test]
        public void PuiButton_Submit_InPointerMode_SwallowsClick_AndWakes()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var pui = btn.GameObject.GetComponent<PuiButton>();
            bool clicked = false;
            btn.OnClick.Subscribe(_ => clicked = true).AddTo(screen);

            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            var es = Object.FindAnyObjectByType<EventSystem>();
            pui.OnSubmit(new BaseEventData(es));

            Assert.IsFalse(clicked, "Submit while cursor hidden (Pointer) must NOT click");
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode,
                "first Submit wakes the cursor instead of acting");
        }

        // ── PuiToggle.OnSubmit 两条分支（EditMode 干净：Toggle.OnSubmit 无协程） ──
        private static UnityEngine.UI.Toggle BuildToggle()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Toggle id='t'>On</Toggle></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            return screen.Get<Toggle>("t").GameObject.GetComponent<UnityEngine.UI.Toggle>();
        }

        [Test]
        public void PuiToggle_Submit_InPointerMode_DoesNotToggle_AndWakes()
        {
            UI.UseGamepadNavigation();
            var tog = BuildToggle();
            bool initial = tog.isOn;
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            var es = Object.FindAnyObjectByType<EventSystem>();
            tog.OnSubmit(new BaseEventData(es));
            Assert.AreEqual(initial, tog.isOn, "Submit while hidden must not flip isOn");
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
        }

        [Test]
        public void PuiToggle_Submit_InDirectionalMode_TogglesNormally()
        {
            UI.UseGamepadNavigation();
            var tog = BuildToggle();
            bool initial = tog.isOn;
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            var es = Object.FindAnyObjectByType<EventSystem>();
            tog.OnSubmit(new BaseEventData(es));
            Assert.AreNotEqual(initial, tog.isOn, "Submit while cursor visible toggles normally");
        }
    }
}
