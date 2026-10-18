using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Tests.PlayMode.Modals
{
    /// <summary>
    /// A <c>&lt;Dropdown&gt;</c> list open inside a dialog, and a real Escape key (spec 2026-10-18
    /// §1.2-4). TMP closes its list through EventSystem Cancel (<c>Show()</c> selects the current
    /// item, whose <c>OnCancel</c> hides the list), which the dialog's own Escape listener never hears
    /// about — so the same press used to close the dialog too. Real keys, because the bug is two
    /// listeners on two input paths hearing one press; <c>FireForTests()</c> would only drive one.
    /// </summary>
    public class DropdownEscapePlayTests
#if ENABLE_INPUT_SYSTEM
        : InputTestFixture
#endif
    {
        private const string BoxWithDropdown = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/DropBox'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x300'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Text id='text'  fontSize='14'/>
        <Dropdown id='dd' width='200' height='40'/>
        <Btn  id='ok'>OK</Btn>
        <Btn  id='cancel'>Cancel</Btn>
        <Btn  id='yes'>Yes</Btn>
        <Btn  id='no'>No</Btn>
        <Btn  id='close'>Close</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

#if ENABLE_INPUT_SYSTEM
        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            DestroyAllEventSystems();   // a navigation test's leftover EventSystem would take `current`
            new GameObject("EventSystem", typeof(EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            var files = new Dictionary<string, string> { ["test/DropBox"] = BoxWithDropdown };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/DropBox";
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            DestroyAllEventSystems();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            base.TearDown();
        }

        private static void DestroyAllEventSystems()
        {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }
#endif

        [UnityTest]
        public IEnumerator Escape_on_an_expanded_dropdown_inside_a_modal_closes_only_the_dropdown()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = InputSystem.AddDevice<Keyboard>();
            _ = MessageBox.Open("pick one", MsgBtn.OK | MsgBtn.Cancel);
            yield return null;
            yield return null;
            Assert.IsTrue(UI.Modal.IsAnyOpen, "precondition: the dialog is up");

            var tmp = UI.Modal.TopScreen.Get<Dropdown>("dd").GameObject.GetComponent<TMP_Dropdown>();
            tmp.options.Add(new TMP_Dropdown.OptionData("a"));
            tmp.options.Add(new TMP_Dropdown.OptionData("b"));
            tmp.Show();
            yield return null;
            Assert.IsTrue(tmp.IsExpanded, "precondition: the list is open");

            Press(kb.escapeKey);
            yield return null;
            Release(kb.escapeKey);
            yield return new WaitForSeconds(0.4f);   // TMP fades the list out before destroying it

            Assert.IsFalse(tmp.IsExpanded, "Escape closes the open list");
            Assert.IsTrue(UI.Modal.IsAnyOpen, "…and the dialog behind it survives that press");
#else
            yield break;
#endif
        }

        // With no list open the press goes on to the dialog — which closes inside the listener's own
        // Input System callback, so the listener must not dispose its action map right there.
        [UnityTest]
        public IEnumerator Escape_with_no_list_open_closes_the_modal()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = InputSystem.AddDevice<Keyboard>();
            _ = MessageBox.Open("bye", MsgBtn.OK | MsgBtn.Cancel);
            yield return null;
            Assert.IsTrue(UI.Modal.IsAnyOpen, "precondition: the dialog is up");

            Press(kb.escapeKey);
            yield return null;
            Release(kb.escapeKey);
            yield return null;

            Assert.IsFalse(UI.Modal.IsAnyOpen);
#else
            yield break;
#endif
        }

        private const string PageWithDropdown = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='P'>
  <Dropdown id='dd' anchor='top-left' width='200' height='40'/>
</Screen></PromptUGUI>";

        [UnityTest]
        public IEnumerator A_modal_opened_by_code_hides_an_expanded_dropdown_below_it()
        {
#if ENABLE_INPUT_SYSTEM
            UI.LoadDocument("p", PageWithDropdown);
            var tmp = UI.Open("P").Get<Dropdown>("dd").GameObject.GetComponent<TMP_Dropdown>();
            tmp.options.Add(new TMP_Dropdown.OptionData("a"));
            tmp.options.Add(new TMP_Dropdown.OptionData("b"));
            yield return null;
            tmp.Show();
            yield return null;
            Assert.IsTrue(tmp.IsExpanded, "precondition: the list is open");

            _ = MessageBox.Open("hi", MsgBtn.OK);
            yield return null;
            Assert.AreEqual(0, PromptUGUI.Controls.Internal.PuiDropdown.ExpandedCount,
                "the list sorts above everything, so a dialog over its page hides it");
            yield return new WaitForSeconds(0.4f);

            Assert.IsFalse(tmp.IsExpanded);
            Assert.IsTrue(UI.Modal.IsAnyOpen);
#else
            yield break;
#endif
        }
    }
}
