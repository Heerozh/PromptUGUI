using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Modals
{
    /// <summary>
    /// Who answers an Escape (spec 2026-10-18 §4.4): one decision per press, in a fixed order —
    /// a blocking tutorial step, then the open popup, then the top dialog, then a routed Modal.
    /// </summary>
    public class EscapeChainTests
    {
        private Dictionary<string, int> _store;

        private const string Menu = @"
    <TabMenu id='m' transition='0'>
      <Tab id='a' text='World' isOn='true'/>
      <Tab id='g' text='Guild'/>
    </TabMenu>";

        private const string HomeXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='home'>
  <Btn id='b' size='100x40'>x</Btn>" + Menu + @"
</Screen></PromptUGUI>";

        private const string SettingsXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='settings'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='panel' anchor='center' size='300x200'>" + Menu + @"
  </Frame>
</Screen></PromptUGUI>";

        // A MessageBox layout (title / text / every button) with a menu in it.
        private const string MenuBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='test/MenuBox'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='dialog' anchor='center' size='400x300'>
    <VStack anchor='stretch' margin='16' spacing='8'>
      <Text id='title' fontSize='20'/>
      <Text id='text'  fontSize='14'/>
      <Btn  id='ok'>OK</Btn>
      <Btn  id='cancel'>Cancel</Btn>
      <Btn  id='yes'>Yes</Btn>
      <Btn  id='no'>No</Btn>
      <Btn  id='close'>Close</Btn>
    </VStack>
    <Frame id='menuHost' anchor='top-left' size='200x44'>" + Menu + @"
    </Frame>
  </Frame>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "test/MenuBox";
            _store = new Dictionary<string, int>();
            UI.Tutorial.UseProgressStore(
                id => _store.TryGetValue(id, out var v) ? v : 0,
                (id, n) => _store[id] = n);
            var files = new Dictionary<string, string>
            {
                ["home"] = HomeXml,
                ["settings"] = SettingsXml,
                ["test/MenuBox"] = MenuBoxXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            // A mutable static other fixtures rely on; put the built-in back.
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
        }

        private static ModalEscapeListener ListenerOn(GameObject go) => go.GetComponent<ModalEscapeListener>();

        private static ModalEscapeListener MenuListener(TabMenu m) =>
            ListenerOn(m.RectTransform.Find("Popup").gameObject);

        private static bool Press(EscapeKey key = EscapeKey.Escape) =>
            EscapeChain.Dispatch(key, EscapeChain.NextTestPress());

        private static TabMenu OpenDialogWithMenu(MsgBtn buttons = MsgBtn.OK | MsgBtn.Cancel)
        {
            _ = MessageBox.Open("x", buttons);
            var menu = UI.Modal.TopScreen.Get<TabMenu>("m");
            menu.Expand();
            Assert.IsTrue(menu.IsExpanded, "precondition");
            return menu;
        }

        // ── UI.EscapeConsumedThisFrame: what game code reads before acting on its own back key ──────

        [Test]
        public void A_press_that_closes_a_menu_is_reported_consumed()
        {
            var menu = OpenDialogWithMenu();
            Assert.IsFalse(UI.EscapeConsumedThisFrame, "nothing pressed yet");

            MenuListener(menu).FireForTests();

            Assert.IsFalse(menu.IsExpanded, "precondition: the press closed the menu");
            Assert.IsTrue(UI.EscapeConsumedThisFrame);
        }

        [Test]
        public void A_press_a_routed_modal_answers_is_reported_consumed()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();

            Assert.IsTrue(Press());
            Assert.IsTrue(UI.EscapeConsumedThisFrame);
        }

        [Test]
        public void A_press_nothing_took_is_not()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();

            Assert.IsFalse(Press(EscapeKey.CancelButton), "precondition: no popup, B closes no dialog");
            Assert.IsFalse(UI.EscapeConsumedThisFrame);
        }

        [Test]
        public void A_new_play_session_forgets_the_last_consumed_press()
        {
            var menu = OpenDialogWithMenu();
            MenuListener(menu).FireForTests();
            Assume.That(UI.EscapeConsumedThisFrame);

            EscapeChain.ResetForPlaySession();

            Assert.IsFalse(UI.EscapeConsumedThisFrame, "frame numbers start over in the next session");
        }

        // ── Reproductions of spec §1.2 ────────────────────────────────────────────────────

        // §1.2-1: the routed Modal's own listener never asked the menu, so one press closed both.
        [Test]
        public void Routed_modal_with_an_open_menu_closes_only_the_menu()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            var settings = UI.Get("settings");
            var menu = settings.Get<TabMenu>("m");
            menu.Expand();

            ListenerOn(settings.RootGameObject).FireForTests();

            Assert.IsFalse(menu.IsExpanded, "the menu is closer to the user, so it answers first");
            CollectionAssert.AreEqual(new[] { "home", "settings" }, UI.Router.Chain.ToList(),
                "…and the routed Modal behind it survives that press");
        }

        // §1.2-2: a blocking tutorial step swallows Escape — the menu it may be pointing into included.
        [Test]
        public void Blocking_tutorial_leaves_an_open_menu_open()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            var menu = UI.Get("home").Get<TabMenu>("m");
            menu.Expand();
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.IsBlockingInput, "precondition: a Block step is up");

            MenuListener(menu).FireForTests();

            Assert.IsTrue(menu.IsExpanded, "Escape does nothing during a Block step");
            _ = run;   // left pending; teardown clears it
        }

        // ── The order ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Nothing_open_is_not_consumed()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.IsFalse(Press());
        }

        [Test]
        public void An_open_menu_takes_the_press_before_an_ad_hoc_modal()
        {
            var menu = OpenDialogWithMenu();

            ListenerOn(UI.Modal.TopScreen.RootGameObject).FireForTests();
            Assert.IsFalse(menu.IsExpanded);
            Assert.IsTrue(UI.Modal.IsAnyOpen, "the dialog survives the press that closed its menu");

            ListenerOn(UI.Modal.TopScreen.RootGameObject).FireForTests();
            Assert.IsFalse(UI.Modal.IsAnyOpen, "the next press closes the dialog");
        }

        [Test]
        public void Cancel_button_closes_a_menu_but_never_a_modal()
        {
            var menu = OpenDialogWithMenu();

            Assert.IsTrue(Press(EscapeKey.CancelButton));
            Assert.IsFalse(menu.IsExpanded);

            Assert.IsFalse(Press(EscapeKey.CancelButton), "B is not a modal key");
            Assert.IsTrue(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void The_same_press_heard_twice_acts_once()
        {
            var menu = OpenDialogWithMenu();
            var press = EscapeChain.NextTestPress();

            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.Escape, press));
            Assert.IsFalse(menu.IsExpanded);

            menu.Expand();
            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.Escape, press), "same answer for the second listener…");
            Assert.IsTrue(menu.IsExpanded, "…and no second action");
            Assert.IsTrue(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void FireForTests_presses_are_distinct()
        {
            _ = MessageBox.Open("bottom", MsgBtn.OK | MsgBtn.Cancel);
            _ = MessageBox.Open("top", MsgBtn.OK | MsgBtn.Cancel);

            ListenerOn(UI.Modal.TopScreen.RootGameObject).FireForTests();
            ListenerOn(UI.Modal.TopScreen.RootGameObject).FireForTests();

            Assert.IsFalse(UI.Modal.IsAnyOpen, "two calls are two presses, even inside one frame");
        }

        [Test]
        public void Ad_hoc_modal_answers_before_a_routed_modal()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            _ = MessageBox.Open("x", MsgBtn.OK | MsgBtn.Cancel);

            Assert.IsTrue(Press());

            Assert.IsFalse(UI.Modal.IsAnyOpen);
            CollectionAssert.AreEqual(new[] { "home", "settings" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Refused_escape_on_an_ok_only_box_is_still_consumed()
        {
            _ = MessageBox.Open("x", MsgBtn.OK);

            Assert.IsTrue(Press(), "the dialog owns the press even when it refuses to close");
            Assert.IsTrue(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void A_popup_below_the_top_modal_does_not_take_the_press()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            var pageMenu = UI.Get("home").Get<TabMenu>("m");
            _ = MessageBox.Open("x", MsgBtn.OK | MsgBtn.Cancel);
            pageMenu.Expand();                              // by code, under the dialog

            Assert.IsTrue(Press());

            Assert.IsFalse(UI.Modal.IsAnyOpen, "the dialog the user sees takes the press");
            Assert.IsTrue(pageMenu.IsExpanded);
        }

        [Test]
        public void Routed_modal_on_top_goes_back()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();

            Assert.IsTrue(Press());

            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void A_rejected_Back_is_consumed_and_does_not_throw()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            System.Func<string, bool> rejectAll = _ => false;
            UI.Router.AddGuard(rejectAll);
            try
            {
                bool consumed = false;
                Assert.DoesNotThrow(() => consumed = Press());
                Assert.IsTrue(consumed);
                CollectionAssert.AreEqual(new[] { "home", "settings" }, UI.Router.Chain.ToList());
            }
            finally { UI.Router.RemoveGuard(rejectAll); }
        }

        [Test]
        public void A_routed_page_on_top_does_nothing()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();

            Assert.IsFalse(Press());

            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void A_popup_press_is_remembered_for_the_frame()
        {
            OpenDialogWithMenu();
            Assert.AreNotEqual(Time.frameCount, EscapeChain.PopupConsumedFrame);

            Press();

            Assert.AreEqual(Time.frameCount, EscapeChain.PopupConsumedFrame,
                "a text field reading this frame must not also act on the press");
            EscapeChain.ForgetPopupConsumedFrameForTests();
            Assert.AreNotEqual(Time.frameCount, EscapeChain.PopupConsumedFrame);
        }
    }
}
