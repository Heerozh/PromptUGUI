using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.TestTools;
using PBtn = PromptUGUI.Controls.Btn;

namespace PromptUGUI.Tests.PlayMode.Modals
{
    /// <summary>
    /// A modal whose XML declares an exit (spec 2026-09-16-close-transition-design §5.6): the
    /// result resolves at the click as before, the dialog fades out as a ghost, and a queued
    /// modal is promoted at once.
    /// </summary>
    public class ModalCloseTransitionPlayTests
    {
        private const string FadeBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/FadeBox'>
    <Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.25s' easing='linear'>
      <Frame anchor='stretch'>
        <Image id='backdrop' anchor='stretch' color='#0000007F'/>
        <Frame id='dialog' anchor='center' size='400x200'>
          <VStack anchor='stretch' margin='16' spacing='8'>
            <Text id='title' fontSize='20'/>
            <Text id='text'  fontSize='14'/>
            <Btn  id='ok'>OK</Btn>
            <Btn  id='cancel'>Cancel</Btn>
            <Btn  id='yes'>Yes</Btn>
            <Btn  id='no'>No</Btn>
            <Btn  id='close'>Close</Btn>
          </VStack>
        </Frame>
      </Frame>
    </Animation>
  </Screen>
</PromptUGUI>";

        private string _savedSrc;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _savedSrc = MessageBox.XmlSrc;
            var files = new Dictionary<string, string> { ["test/FadeBox"] = FadeBoxXml };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/FadeBox";
        }

        [TearDown]
        public void TearDown()
        {
            MessageBox.XmlSrc = _savedSrc;
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator The_result_resolves_at_the_click_and_the_dialog_fades_out_behind_it()
        {
            var task = MessageBox.Open("first", MsgBtn.OK);
            for (int i = 0; i < 10 && UI.Modal.TopScreen == null; i++) yield return null;
            var screen = UI.Modal.TopScreen;
            yield return new WaitForSeconds(0.3f);

            screen.Get<PBtn>("ok").SimulateClick();
            Assert.IsTrue(task.GetAwaiter().IsCompleted, "the caller is not made to wait for the fade");
            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen, "popped from the stack at once");
            Assert.IsTrue(screen.IsClosing, "but the dialog is still fading out");
            Assert.IsNotNull(screen.RootGameObject);

            yield return new WaitForSeconds(0.35f);
            yield return null;
            yield return null;
            Assert.IsNull(screen.RootGameObject);
        }

        [UnityTest]
        public IEnumerator A_queued_modal_is_promoted_while_the_previous_one_fades()
        {
            // Queued: the second waits for the first to close (Popup would stack them).
            var first = MessageBox.Open("first", MsgBtn.OK, mode: ModalMode.Queued);
            var second = MessageBox.Open("second", MsgBtn.OK, mode: ModalMode.Queued);
            for (int i = 0; i < 10 && UI.Modal.TopScreen == null; i++) yield return null;
            var firstScreen = UI.Modal.TopScreen;
            yield return new WaitForSeconds(0.3f);
            Assume.That(UI.Modal.TopScreen, Is.SameAs(firstScreen), "guard: the second is still waiting");

            firstScreen.Get<PBtn>("ok").SimulateClick();
            Assert.IsTrue(first.GetAwaiter().IsCompleted, "the first result resolves at the click");
            for (int i = 0; i < 10 && UI.Modal.TopScreen == null; i++) yield return null;
            var secondScreen = UI.Modal.TopScreen;
            Assert.IsNotNull(secondScreen, "the next dialog opens without waiting for the fade");
            Assert.AreNotSame(firstScreen, secondScreen);
            Assert.IsTrue(firstScreen.IsClosing, "overlap: the first is still on its way out");

            yield return new WaitForSeconds(0.35f);
            yield return null;
            yield return null;
            Assert.IsNull(firstScreen.RootGameObject);
            Assert.IsNotNull(secondScreen.RootGameObject);
            secondScreen.Get<PBtn>("ok").SimulateClick();
            Assert.IsTrue(second.GetAwaiter().IsCompleted, "the second result resolves at its own click");
        }
    }
}
