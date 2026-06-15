using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PBtn = PromptUGUI.Controls.Btn;

namespace PromptUGUI.Tests.Modals
{
    // 直连真实默认皮肤(Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml)的回归测试。
    // 其余 CenteredSlideBox 测试都把 XmlSrc override 成测试 key、并让 <Screen name> 与之对齐，
    // 因此从不验证「默认 XML 的 <Screen name> 是否等于默认 XmlSrc」。这条 bug(name 写成短名
    // "CenteredSlideBox" → OpenModalScreen 按完整路径查 _docs 落空 → "not loaded")正落在盲区里。
    public class CenteredSlideBoxDefaultSkinTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // 显式恢复默认 XmlSrc:它是 static 可变属性,别的模态测试类会把它改成测试 key。
            CenteredSlideBox.XmlSrc = "PromptUGUI/Modals/CenteredSlideBox.ui";
            // 不设 SourceResolver:内置模态(PromptUGUI/ 前缀)走 Resources.Load,不经 resolver。
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Open_With_Default_Skin_Loads_And_Confirms()
        {
            var items = new[] { "1", "2", "3" };
            var task = CenteredSlideBox.Open(items, (card, s) => { }, title: "123");

            Assert.IsTrue(UI.Modal.IsAnyOpen,
                "默认皮肤应正常打开;若 <Screen name> 与默认 XmlSrc 不一致会 'not loaded' → 此处 false");
            Assert.IsNotNull(UI.Modal.TopScreen.Get<Carousel>("cards"),
                "应能取到默认皮肤里的 cards Carousel");

            UI.Modal.TopScreen.Get<PBtn>("confirm").SimulateClick();
            Assert.AreSame(items[0], task.GetAwaiter().GetResult(),
                "确认返回居中(默认 current=0)项");
        }
    }
}
