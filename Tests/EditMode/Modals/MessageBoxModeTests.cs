using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxModeTests : ModalTestFixture
    {
        [Test]
        public void Open_default_mode_is_popup_stacks()
        {
            var t1 = MessageBox.Open("a", MsgBtn.OK);
            var t2 = MessageBox.Open("b", MsgBtn.OK);          // 默认 Popup
            Assert.AreEqual(2, UI.Modal.QueuedCount, "默认 Popup 两个都在栈上");

            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t2.GetAwaiter().GetResult());
            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t1.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_queued_mode_waits()
        {
            var t1 = MessageBox.Open("a", MsgBtn.OK);
            var t2 = MessageBox.Open("b", MsgBtn.OK, mode: ModalMode.Queued);
            Assert.IsNull(UI.Get("test/Box1"));               // sanity: 不再按名拿 modal

            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t1.GetAwaiter().GetResult());
            // 现在 t2 应已显示
            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t2.GetAwaiter().GetResult());
        }
    }
}
