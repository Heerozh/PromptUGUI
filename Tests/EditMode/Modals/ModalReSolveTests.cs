using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalReSolveTests : ModalTestFixture
    {
        [Test]
        public void Bind_SetActive_false_survives_VariantStore_Changed()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "x", Buttons = MsgBtn.OK });
            var s = UI.Get("test/Box1");
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);

            UI.Variants.Set("mobile", true);   // triggers ReSolve
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf,
                "ReSolve must not clobber Bind's SetActive(false)");
        }
    }
}
