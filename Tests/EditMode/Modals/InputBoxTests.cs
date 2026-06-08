using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using TMPro;
using PBtn = PromptUGUI.Controls.Btn;
using PInputField = PromptUGUI.Controls.InputField;
using PText = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.Modals
{
    public class InputBoxTests
    {
        private const string InputBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
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

        // Same dialog but WITHOUT the message node — covers an override XML that drops it.
        private const string InputBoxNoMessageXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/InputBoxNoMsg'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
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
                ["test/InputBox1"] = InputBoxXml,
                ["test/InputBoxNoMsg"] = InputBoxNoMessageXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            InputBox.XmlSrc = "test/InputBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static PInputField Field()
            => UI.Modal.TopScreen.Get<PInputField>("field");

        private static string Label(string id)
            => UI.Modal.TopScreen.Get<PBtn>(id).GameObject
                .GetComponentInChildren<TMP_Text>().text;

        [Test]
        public void Click_OK_returns_current_field_text()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            Field().TextValue = "Link";
            UI.Modal.TopScreen.Get<PBtn>("ok").SimulateClick();
            Assert.AreEqual("Link", task.GetAwaiter().GetResult());
        }

        [Test]
        public void Click_Cancel_returns_null()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            UI.Modal.TopScreen.Get<PBtn>("cancel").SimulateClick();
            Assert.IsNull(task.GetAwaiter().GetResult());
        }

        [Test]
        public void Submit_resolves_with_submitted_text()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            Field().GameObject.GetComponent<TMP_InputField>().onSubmit.Invoke("typed");
            Assert.AreEqual("typed", task.GetAwaiter().GetResult());
        }

        [Test]
        public void Empty_input_then_OK_returns_empty_string_not_null()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            UI.Modal.TopScreen.Get<PBtn>("ok").SimulateClick();
            var result = task.GetAwaiter().GetResult();
            Assert.IsNotNull(result);
            Assert.AreEqual("", result);
        }

        [Test]
        public void Initial_prefills_field()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Initial = "preset" });
            Assert.AreEqual("preset",
                Field().GameObject.GetComponent<TMP_InputField>().text);
        }

        [Test]
        public void ContentType_password_applied_to_field()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Pw", ContentType = "password" });
            Assert.AreEqual(TMP_InputField.ContentType.Password,
                Field().GameObject.GetComponent<TMP_InputField>().contentType);
        }

        [Test]
        public void Custom_ok_cancel_labels_override_default()
        {
            UI.Modal.OpenAsync(new InputBoxRequest
            {
                Title = "Name?",
                OkLabel = "Save",
                CancelLabel = "Later",
            });
            Assert.AreEqual("Save", Label("ok"));
            Assert.AreEqual("Later", Label("cancel"));
        }

        [Test]
        public void Null_message_hides_message_node()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Message = null });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("message").GameObject.activeSelf);
        }

        [Test]
        public void Message_present_shows_message_node_with_text()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Message = "details" });
            var msg = UI.Modal.TopScreen.Get<PText>("message");
            Assert.IsTrue(msg.GameObject.activeSelf);
            Assert.AreEqual("details", msg.TmpComponent.text);
        }

        [Test]
        public void Missing_message_node_in_xml_is_tolerated()
        {
            InputBox.XmlSrc = "test/InputBoxNoMsg";
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Message = "ignored" });
            // Bind must not throw even though there is no 'message' id.
            Field().TextValue = "ok";
            UI.Modal.TopScreen.Get<PBtn>("ok").SimulateClick();
            Assert.AreEqual("ok", task.GetAwaiter().GetResult());
        }

        [Test]
        public void TryEscape_returns_null_and_true()
        {
            var req = new InputBoxRequest { Title = "Name?" };
            Assert.IsTrue(req.TryEscape(out var r));
            Assert.IsNull(r);
        }

        // ESC must route through the real pump → ModalEscapeListener → TryEscape → close,
        // not just the unit-level TryEscape. Mirrors MessageBoxRequestTests.Escape_via_listener_*.
        [Test]
        public void Escape_via_listener_returns_null_and_closes()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            var listener = UI.Modal.TopScreen
                .RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "Pump must attach ModalEscapeListener to the modal Screen root");
            listener.FireForTests();
            Assert.IsNull(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Null_title_hides_title_node()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = null });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
        }

        [Test]
        public void Placeholder_applied_to_field()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Placeholder = "type here" });
            var ph = Field().GameObject.transform.Find("Text Area/Placeholder")
                .GetComponent<TMP_Text>();
            Assert.AreEqual("type here", ph.text);
        }
    }
}
