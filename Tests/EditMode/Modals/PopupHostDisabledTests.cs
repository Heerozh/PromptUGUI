using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.Modals
{
    /// <summary>
    /// Spec 2026-10-19 §1.2-5: a popup whose host is switched off from above (code hiding an ancestor)
    /// used to stay on the chain — invisible, yet still the chain's top, so the next Escape closed it
    /// instead of the dialog it was in. The chain now prunes such entries before it answers.
    /// </summary>
    public class PopupHostDisabledTests
    {
        private const string Menu = @"
    <TabMenu id='m' transition='0'>
      <Tab id='a' text='World' isOn='true'/>
      <Tab id='g' text='Guild'/>
    </TabMenu>";

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

        private const string HomeXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='home'>
  <Frame id='holder' anchor='top-left' size='200x44'>" + Menu + @"
  </Frame>
  <TabMenu id='other' anchor='top-right' transition='0'>
    <Tab id='x' text='X' isOn='true'/>
  </TabMenu>
</Screen></PromptUGUI>";

        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "test/MenuBox";
            var files = new Dictionary<string, string> { ["test/MenuBox"] = MenuBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            if (_ownEs != null) Object.DestroyImmediate(_ownEs);
        }

        [Test]
        public void A_menu_hidden_with_its_ancestor_no_longer_eats_Escape()
        {
            _ = MessageBox.Open("x", MsgBtn.OK | MsgBtn.Cancel);
            var dialog = UI.Modal.TopScreen;
            var menu = dialog.Get<TabMenu>("m");
            menu.Expand();
            Assume.That(menu.IsExpanded, "precondition");

            dialog.Get<Frame>("menuHost").Hidden = true;
            dialog.RootGameObject.GetComponent<ModalEscapeListener>().FireForTests();

            Assert.IsFalse(UI.Modal.IsAnyOpen, "the Escape reached the dialog, not the menu nobody can see");
            Assert.IsFalse(menu.IsExpanded);
        }

        [Test]
        public void Opening_another_popup_prunes_the_hidden_one()
        {
            UI.LoadDocument("t", HomeXml);
            var home = UI.Open("home");
            var menu = home.Get<TabMenu>("m");
            menu.Expand();

            home.Get<Frame>("holder").Hidden = true;
            home.Get<TabMenu>("other").Expand();

            Assert.IsFalse(menu.IsExpanded, "pruned, so its OnCollapsed has fired");
            Assert.AreEqual(1, PopupChain.Count);
        }

        [Test]
        public void A_selection_inside_the_hidden_panel_is_cleared()
        {
            UI.LoadDocument("t", HomeXml);
            var home = UI.Open("home");
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                _ownEs = new GameObject("EventSystem", typeof(EventSystem));
            UI.UseGamepadNavigation();
            var es = Object.FindAnyObjectByType<EventSystem>();
            var menu = home.Get<TabMenu>("m");
            menu.Expand();
            Assume.That(es.currentSelectedGameObject, Is.Not.Null, "precondition: the cage landed on a row");

            home.Get<Frame>("holder").Hidden = true;
            _ = PopupChain.Top;   // any question to the chain prunes first

            Assert.IsNull(es.currentSelectedGameObject, "a selection in a panel that went away is dropped");
        }
    }
}
