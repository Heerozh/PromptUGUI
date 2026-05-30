using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ShowTests
    {
        // Mirror of the (protected) UnityEngine.UI.Selectable.SelectionState ordinals.
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuiButton Pui(Screen screen, string btnPath)
            => screen.Get<Btn>(btnPath).GameObject.GetComponent<PuiButton>();

        // ---- 1: mutual exclusion + Normal fallback (no hover block) ----

        [Test]
        public void Normal_and_pressed_shows_are_mutually_exclusive_with_normal_fallback()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Btn id='b'>" +
                "  <Show id='sn' on='state-normal'><Image id='n'/></Show>" +
                "  <Show id='sp' on='state-pressed'><Image id='p'/></Show>" +
                "</Btn>" +
                $"{Footer}");
            var screen = UI.Open("S");

            var sn = screen.Get<Show>("b/sn");
            var sp = screen.Get<Show>("b/sp");
            var pui = Pui(screen, "b");

            // At open: Normal -> normal block active, pressed block inactive.
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active at open");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show inactive at open");

            pui.SimulateState(Pressed);
            Assert.IsFalse(sn.GameObject.activeSelf, "normal Show hidden when pressed");
            Assert.IsTrue(sp.GameObject.activeSelf, "pressed Show shown when pressed");

            // Hover with no hover block -> Normal fallback (no hover claim).
            pui.SimulateState(Highlighted);
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show is fallback for unclaimed Hover");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show hidden at hover");
        }

        // ---- 2: explicit hover block overrides Normal fallback ----

        [Test]
        public void Explicit_hover_show_overrides_normal_fallback()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Btn id='b'>" +
                "  <Show id='sn' on='state-normal'><Image id='n'/></Show>" +
                "  <Show id='sh' on='state-hover'><Image id='h'/></Show>" +
                "  <Show id='sp' on='state-pressed'><Image id='p'/></Show>" +
                "</Btn>" +
                $"{Footer}");
            var screen = UI.Open("S");

            var sn = screen.Get<Show>("b/sn");
            var sh = screen.Get<Show>("b/sh");
            var sp = screen.Get<Show>("b/sp");
            var pui = Pui(screen, "b");

            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active at open");
            Assert.IsFalse(sh.GameObject.activeSelf);
            Assert.IsFalse(sp.GameObject.activeSelf);

            pui.SimulateState(Highlighted);
            Assert.IsFalse(sn.GameObject.activeSelf, "normal hidden when explicit hover claimed");
            Assert.IsTrue(sh.GameObject.activeSelf, "hover Show shown at hover");
            Assert.IsFalse(sp.GameObject.activeSelf);
        }

        // ---- 3: Strategy C — hidden Show is not destroyed ----

        [Test]
        public void Hidden_show_gameobject_still_exists()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Btn id='b'>" +
                "  <Show id='sn' on='state-normal'><Image id='n'/></Show>" +
                "  <Show id='sp' on='state-pressed'><Image id='p'/></Show>" +
                "</Btn>" +
                $"{Footer}");
            var screen = UI.Open("S");

            var sp = screen.Get<Show>("b/sp");
            // At open the pressed Show is hidden — but the GameObject must still exist.
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show hidden at open");
            Assert.IsNotNull(sp.GameObject, "hidden Show GameObject must not be destroyed");
            Assert.IsTrue(sp.GameObject != null, "hidden Show GameObject must not be destroyed (Unity null)");
        }

        // ---- 4: non-state on= throws the spec'd message ----

        [Test]
        public void Show_with_non_state_on_throws()
        {
            Assert.That(() =>
            {
                UI.LoadDocument("t", $"{Header}" +
                    "<Btn id='b'><Show id='s' on='click'><Image/></Show></Btn>" +
                    $"{Footer}");
                UI.Open("S");
            }, Throws.InstanceOf<Exception>().With.Message.Contains(
                "<Show> only accepts state-* events"));
        }

        // ---- 5: initial visibility correct with no manual state drive ----

        [Test]
        public void Initial_visibility_correct_without_manual_drive()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Btn id='b'>" +
                "  <Show id='sn' on='state-normal'><Image id='n'/></Show>" +
                "  <Show id='sp' on='state-pressed'><Image id='p'/></Show>" +
                "</Btn>" +
                $"{Footer}");
            var screen = UI.Open("S");

            // Pure registration-time evaluation — no SimulateState called.
            Assert.IsTrue(screen.Get<Show>("b/sn").GameObject.activeSelf);
            Assert.IsFalse(screen.Get<Show>("b/sp").GameObject.activeSelf);
        }
    }
}
