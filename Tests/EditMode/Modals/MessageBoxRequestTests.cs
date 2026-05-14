using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxRequestTests : ModalTestFixture
    {
        [Test]
        public void Bind_only_OK_hides_other_buttons()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK });
            var s = UI.Get("test/Box1");

            Assert.IsTrue(s.Get<PromptUGUI.Controls.Btn>("ok").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("yes").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("no").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("close").GameObject.activeSelf);
        }

        [Test]
        public void Click_OK_returns_Btn_OK()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Click_Cancel_returns_Btn_Cancel_when_OK_Cancel_combo()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = "hi",
                Buttons = Btn.OK | Btn.Cancel,
            });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Title_null_hides_title_node()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = Btn.OK, Title = null });
            var s = UI.Get("test/Box1");
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Text>("title").GameObject.activeSelf);
        }

        [Test]
        public void Title_present_shows_title_node_with_text()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = "body",
                Buttons = Btn.OK,
                Title = "Heading",
            });
            var s = UI.Get("test/Box1");
            var title = s.Get<PromptUGUI.Controls.Text>("title");
            Assert.IsTrue(title.GameObject.activeSelf);
            Assert.AreEqual("Heading", title.TmpComponent.text);
        }

        [Test]
        public void Click_with_custom_labels_returns_mapped_flag()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = "hi",
                Buttons = Btn.OK | Btn.Cancel,
                CustomLabels = new[] { ("Retry", Btn.OK), ("Skip", Btn.Cancel) },
            });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TryEscape_only_OK_returns_false()
        {
            var req = new MessageBoxRequest { Buttons = Btn.OK };
            Assert.IsFalse(req.TryEscape(out _));
        }

        [Test]
        public void TryEscape_priority_Cancel_over_No_over_Close()
        {
            var c = new MessageBoxRequest { Buttons = Btn.Cancel | Btn.No | Btn.Close };
            Assert.IsTrue(c.TryEscape(out var r1));
            Assert.AreEqual(Btn.Cancel, r1);

            var n = new MessageBoxRequest { Buttons = Btn.No | Btn.Close };
            Assert.IsTrue(n.TryEscape(out var r2));
            Assert.AreEqual(Btn.No, r2);

            var x = new MessageBoxRequest { Buttons = Btn.Close };
            Assert.IsTrue(x.TryEscape(out var r3));
            Assert.AreEqual(Btn.Close, r3);
        }

        [Test]
        public void Escape_via_listener_returns_negative_button()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = "x",
                Buttons = Btn.OK | Btn.Cancel,
            });
            var listener = UI.Get("test/Box1")
                .RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "Pump must attach ModalEscapeListener to the modal Screen root");
            listener.FireForTests();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Escape_via_listener_with_only_OK_does_not_resolve()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = "x",
                Buttons = Btn.OK,
            });
            var listener = UI.Get("test/Box1")
                .RootGameObject.GetComponent<ModalEscapeListener>();
            listener.FireForTests();
            Assert.IsTrue(UI.Modal.IsAnyOpen, "ESC on OK-only should be a no-op");
            Assert.IsFalse(task.GetAwaiter().IsCompleted);
            UI.Modal.CloseAll();
        }
    }
}
