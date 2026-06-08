using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using R3;
using TMPro;
using PBtn = PromptUGUI.Controls.Btn;
using PInputField = PromptUGUI.Controls.InputField;
using PText = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.Modals
{
    // The post-bind `Configure` hook hands callers the live IScreen so they can reach any
    // control without subclassing — disable the OK Btn, wire validation, restyle, etc. It is
    // declared once on the base ModalRequest and invoked centrally in ModalEntry.RunBind, so
    // MessageBox / InputBox / custom requests all get it; Loading mirrors it in its own pump.
    public class ModalConfigureHookTests
    {
        private const string MboxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Box1'>
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
  </Screen>
</PromptUGUI>";

        private const string InputXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/InputBox1'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x220'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Text id='message' fontSize='14'/>
        <InputField id='field' height='44'/>
        <Btn id='ok'>OK</Btn>
        <Btn id='cancel'>Cancel</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>
            {
                ["test/Box1"] = MboxXml,
                ["test/InputBox1"] = InputXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/Box1";
            InputBox.XmlSrc = "test/InputBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static string Label(string id)
            => UI.Modal.TopScreen.Get<PBtn>(id).GameObject
                .GetComponentInChildren<TMP_Text>().text;

        private static TMP_InputField Field()
            => UI.Modal.TopScreen.Get<PInputField>("field").GameObject
                .GetComponent<TMP_InputField>();

        // --- MessageBox: hook fires, and fires AFTER Bind (overrides a Bind-set custom label) ---
        [Test]
        public void MessageBox_configure_runs_after_bind()
        {
            MessageBox.Open("hi", new[] { ("Retry", MsgBtn.OK) },
                configure: s => s.Get<PBtn>("ok").Text = "HOOK");
            Assert.AreEqual("HOOK", Label("ok"),
                "Configure must run after Bind so it overrides the custom 'Retry' label");
        }

        // --- InputBox static helper threads `configure` through to the request ---
        [Test]
        public void InputBox_configure_can_disable_ok()
        {
            InputBox.Open("Name?", configure: s => s.Get<PBtn>("ok").Interactable = false);
            Assert.IsFalse(UI.Modal.TopScreen.Get<PBtn>("ok").Interactable);
        }

        // --- The fix: Enter (OnSubmit) is gated by ok.Interactable ---
        [Test]
        public void InputBox_enter_is_gated_when_ok_disabled()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest
            {
                Title = "Name?",
                Configure = s => s.Get<PBtn>("ok").Interactable = false,
            });

            Field().onSubmit.Invoke("typed");
            Assert.IsTrue(UI.Modal.IsAnyOpen, "Enter must not submit while OK is disabled");

            // Re-enable → Enter now closes with the typed text.
            UI.Modal.TopScreen.Get<PBtn>("ok").Interactable = true;
            Field().onSubmit.Invoke("typed2");
            Assert.AreEqual("typed2", task.GetAwaiter().GetResult());
        }

        // --- Loading overlay (separate subsystem) also honors configure ---
        [Test]
        public void Loading_configure_receives_screen_and_mutates()
        {
            Loading.XmlSrc = "PromptUGUI/Modals/Loading.ui";   // builtin, via Resources
            IScreen captured = null;
            var handle = Loading.Open("orig", configure: s =>
            {
                captured = s;
                s.Get<PText>("text").TextValue = "HOOK";
            });

            Assert.IsNotNull(captured, "Configure must receive the live Loading screen");
            Assert.AreEqual("HOOK",
                LoadingOverlay.ActiveScreens.First().Get<PText>("text").TmpComponent.text);
            handle.Close();
        }

        // --- Custom ModalRequest<T> subclasses inherit the hook for free ---
        [Test]
        public void Custom_request_configure_invoked_with_screen()
        {
            var ran = false;
            UI.Modal.OpenAsync(new ProbeRequest
            {
                Configure = s => ran = s.Get<PBtn>("ok") != null,
            });
            Assert.IsTrue(ran, "Base-class Configure must fire for arbitrary ModalRequest subclasses");
        }

        private sealed class ProbeRequest : ModalRequest<string>
        {
            public override string XmlSrc => "test/InputBox1";

            public override void Bind(IScreen screen, Action<string> close)
                => screen.Get<PBtn>("ok").OnClick.Subscribe(_ => close("ok")).AddTo(screen);
        }
    }
}
