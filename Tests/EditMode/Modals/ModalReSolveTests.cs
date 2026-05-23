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
            var s = UI.Modal.TopScreen;
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf);

            UI.Variants.Set("mobile", true);   // triggers ReSolve
            Assert.IsFalse(s.Get<PromptUGUI.Controls.Btn>("cancel").GameObject.activeSelf,
                "ReSolve must not clobber Bind's SetActive(false)");
        }

        [Test]
        public void Bind_TextValue_survives_VariantStore_Changed_when_xml_has_inner_text()
        {
            // 用户自定义 MessageBox XML 时常常给 <Text id="text"> 留一个占位 inner text
            // 当作 "Message body" 默认值。Open 时 Bind 把 TextValue 改成实际消息,
            // 之后旋转屏幕触发 Variant 切换 → ReSolve 不应把它回退成 XML inner text。
            Files["test/Box1"] = MinimalMboxXml.Replace(
                "<Text id='text'  fontSize='14'/>",
                "<Text id='text'  fontSize='14'>Message body</Text>");

            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "Runtime message", Buttons = MsgBtn.OK });
            var s = UI.Modal.TopScreen;
            var textCtl = s.Get<PromptUGUI.Controls.Text>("text");
            Assert.AreEqual("Runtime message", textCtl.TmpComponent.text,
                "sanity: Bind 应该已经把 TextValue 改成 runtime 消息");

            UI.Variants.Set("portrait", true);   // 模拟屏幕旋转 → ReSolve

            Assert.AreEqual("Runtime message", textCtl.TmpComponent.text,
                "ReSolve 不能把 runtime 设置的 TextValue 回退成 XML 的 inner text");
        }
    }
}
