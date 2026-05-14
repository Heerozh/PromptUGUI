using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxStaticTests : ModalTestFixture
    {
        [Test]
        public void Open_default_overload_returns_Btn_OK()
        {
            var task = MessageBox.Open("hello", Btn.OK);
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_custom_labels_overload_returns_mapped_key()
        {
            var task = MessageBox.Open("hello",
                new[] { ("Retry", Btn.OK), ("Skip", Btn.Cancel) });
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("cancel").SimulateClick();
            Assert.AreEqual(Btn.Cancel, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_default_overload_no_buttons_arg_defaults_to_OK()
        {
            var task = MessageBox.Open("hello");
            var s = UI.Get("test/Box1");
            Assert.IsTrue(s.Get<PromptUGUI.Controls.Btn>("ok").GameObject.activeSelf);
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);
            s.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(Btn.OK, task.GetAwaiter().GetResult());
        }
    }
}
