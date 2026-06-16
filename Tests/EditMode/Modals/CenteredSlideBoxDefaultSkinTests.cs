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

            UI.Modal.TopScreen.Get<PBtn>("button0").SimulateClick();
            Assert.AreSame(items[0], task.GetAwaiter().GetResult(),
                "确认返回居中(默认 current=0)项");
        }

        [Test]
        public void Default_Skin_Pixel_Mode_Honors_Reference_Not_Error_Fallback()
        {
            // 复现宿主(像素游戏 DefaultScaleMode=Pixel)报的错:默认皮肤
            // CenteredSlideBox.ui.xml 缺 reference → ApplyPixel 落到 LogError +
            // scaleFactor=1 的兜底分支(Screen.cs ApplyPixel)。其余 6 个内置 <Screen>
            // 都声明 reference="1920x1080"。3840x2160 画布 / 1920x1080 设计 = 整数因子 2;
            // 缺 reference 时为 1(且记一条 error 让本测试自动失败)。
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);

            var items = new[] { "1", "2", "3" };
            var task = CenteredSlideBox.Open(items, (card, s) => { });

            var scaler = UI.Modal.TopScreen.RootGameObject
                .GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize,
                scaler.uiScaleMode);
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f,
                "像素模式下应按 reference=1920x1080 算出整数因子 2;若为 1 说明默认皮肤缺 reference 走了兜底");

            UI.Modal.TopScreen.Get<PBtn>("button0").SimulateClick();
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void Default_Skin_Does_Not_Loop()
        {
            var items = new[] { "1", "2", "3" };
            var task = CenteredSlideBox.Open(items, (card, s) => { });
            var cards = UI.Modal.TopScreen.Get<Carousel>("cards");

            cards.GoTo(99, animated: false);   // 远超界
            Assert.AreEqual(2, cards.Current,
                "默认皮肤应关闭循环:越界 GoTo 钳位到末张(2),而非环绕回 0");

            UI.Modal.TopScreen.Get<PBtn>("button0").SimulateClick();
            task.GetAwaiter().GetResult();
        }
    }
}
