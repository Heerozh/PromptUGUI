using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxStaticTests : ModalTestFixture
    {
        [Test]
        public void Open_default_overload_returns_MsgBtn_OK()
        {
            var task = MessageBox.Open("hello", MsgBtn.OK);
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_custom_labels_overload_returns_mapped_key()
        {
            var task = MessageBox.Open("hello",
                new[] { ("Retry", MsgBtn.OK), ("Skip", MsgBtn.Cancel) });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(MsgBtn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_default_overload_no_buttons_arg_defaults_to_OK()
        {
            var task = MessageBox.Open("hello");
            var s = UI.Get("test/Box1");
            Assert.IsTrue(s.Get<PromptUGUI.Controls.Btn>("ok").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);
            s.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
        }
    }
}
