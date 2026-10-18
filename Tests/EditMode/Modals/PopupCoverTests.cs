using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.Modals
{
    /// <summary>
    /// A layer that appears over an open popup closes it (spec 2026-10-18 §4.5): an ad-hoc dialog,
    /// a routed Page or Modal, the Loading overlay — not a Toast, not a tutorial. Otherwise the hidden
    /// popup keeps the next Escape, and one inside a lower dialog draws over the new one.
    /// </summary>
    public class PopupCoverTests
    {
        private const string Menu = @"
    <TabMenu id='m' transition='0'>
      <Tab id='a' text='World' isOn='true'/>
      <Tab id='g' text='Guild'/>
    </TabMenu>";

        private const string HomeXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='home'>
  <Btn id='elsewhere'>O</Btn>" + Menu + @"
</Screen></PromptUGUI>";

        private const string ShopXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='shop'>
  <Btn id='buy'>Buy</Btn>
</Screen></PromptUGUI>";

        private const string SettingsXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='settings'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Btn id='done' anchor='center'>Done</Btn>
</Screen></PromptUGUI>";

        // A MessageBox layout with a menu in it.
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

        private const string LoadingXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='test/Loading'>
  <Image id='backdrop' anchor='stretch' color='#000000C0'/>
  <Text id='text' anchor='center' fontSize='16'/>
</Screen></PromptUGUI>";

        private const string ToastXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='test/Toast'>
  <Text id='text' anchor='center' align='center' fontSize='40' color='white'/>
</Screen></PromptUGUI>";

        private Dictionary<string, int> _store;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            Loading.XmlSrc = "test/Loading";
            UI.Toast.XmlSrc = "test/Toast";
            _store = new Dictionary<string, int>();
            UI.Tutorial.UseProgressStore(
                id => _store.TryGetValue(id, out var v) ? v : 0,
                (id, n) => _store[id] = n);
            var files = new Dictionary<string, string>
            {
                ["home"] = HomeXml,
                ["shop"] = ShopXml,
                ["settings"] = SettingsXml,
                ["test/MenuBox"] = MenuBoxXml,
                ["test/Loading"] = LoadingXml,
                ["test/Toast"] = ToastXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            // Mutable statics other fixtures rely on; put the built-ins back.
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            Loading.XmlSrc = "PromptUGUI/Modals/Loading.ui";
            UI.Toast.XmlSrc = "PromptUGUI/Toast.ui";
        }

        private static TabMenu OpenHomeMenu()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            var menu = UI.Get("home").Get<TabMenu>("m");
            menu.Expand();
            Assert.IsTrue(menu.IsExpanded, "precondition");
            return menu;
        }

        private static EventSystem Es() => EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        // §1.2-3
        [Test]
        public void Opening_a_modal_by_code_collapses_an_open_menu()
        {
            var menu = OpenHomeMenu();

            _ = MessageBox.Open("hi");

            Assert.IsTrue(UI.Modal.IsAnyOpen, "precondition: the dialog materialised synchronously");
            Assert.IsFalse(menu.IsExpanded, "a dialog over the page closes the menu under it");
        }

        [Test]
        public void Closing_that_modal_returns_the_focus_to_the_menu_handle()
        {
            UI.UseGamepadNavigation();
            var menu = OpenHomeMenu();
            Assert.AreSame(menu.GetAt(0).GameObject, Es().currentSelectedGameObject, "precondition: focus is on a row");

            _ = MessageBox.Open("hi");
            UI.Modal.TopScreen.Get<Btn>("ok").SimulateClick();

            Assert.IsFalse(UI.Modal.IsAnyOpen);
            Assert.AreSame(menu.GameObject, Es().currentSelectedGameObject,
                "the dialog remembered the handle, not a row of a panel that is gone");
        }

        [Test]
        public void A_routed_page_over_the_page_collapses_its_menu()
        {
            var menu = OpenHomeMenu();

            UI.Router.Open("shop").GetAwaiter().GetResult();

            Assert.IsFalse(menu.IsExpanded);
        }

        [Test]
        public void A_routed_modal_over_the_page_collapses_its_menu()
        {
            var menu = OpenHomeMenu();

            UI.Router.Open("settings").GetAwaiter().GetResult();

            Assert.IsFalse(menu.IsExpanded);
        }

        [Test]
        public void The_loading_overlay_collapses_a_menu_on_a_page()
        {
            var menu = OpenHomeMenu();

            var handle = Loading.Open("…");

            Assert.IsFalse(handle.IsClosed, "precondition");
            Assert.IsFalse(menu.IsExpanded);
        }

        [Test]
        public void A_dialog_over_a_dialog_collapses_the_lower_dialogs_menu()
        {
            MessageBox.XmlSrc = "test/MenuBox";
            _ = MessageBox.Open("bottom", MsgBtn.OK);
            var menu = UI.Modal.TopScreen.Get<TabMenu>("m");
            menu.Expand();

            _ = MessageBox.Open("top", MsgBtn.OK);

            Assert.IsFalse(menu.IsExpanded, "or it would draw over the new dialog");
        }

        [Test]
        public void A_menu_inside_the_dialog_that_opens_is_not_touched()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            MessageBox.XmlSrc = "test/MenuBox";
            _ = MessageBox.Open("x", MsgBtn.OK);
            var menu = UI.Modal.TopScreen.Get<TabMenu>("m");

            menu.Expand();

            Assert.IsTrue(menu.IsExpanded);
        }

        [Test]
        public void A_toast_leaves_the_menu_open()
        {
            var menu = OpenHomeMenu();

            UI.Toast.Show("saved");

            Assert.IsTrue(menu.IsExpanded, "a toast draws over everything but covers nothing");
        }

        [Test]
        public void A_tutorial_step_leaves_the_menu_open()
        {
            var menu = OpenHomeMenu();

            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            UI.Tutorial.TickForTests(0.016f);

            Assert.IsTrue(menu.IsExpanded, "a tutorial may be pointing into the menu");
            _ = run;   // left pending; teardown clears it
        }
    }
}
