using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class LoadingTests : ModalTestFixture
    {
        private const string LoadingTestXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading1'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame id='dialog' anchor='center' size='320x160'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='text' fontSize='16'/>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        public override void SetUp()
        {
            base.SetUp();
            Files["test/Loading1"] = LoadingTestXml;
            Loading.XmlSrc = "test/Loading1";
        }

        [Test]
        public void Open_returns_handle_and_modal_is_shown_with_text()
        {
            var handle = Loading.Open("加载中...");

            Assert.IsNotNull(handle);
            Assert.IsFalse(handle.IsClosed);
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            var screen = UI.Get("test/Loading1");
            Assert.IsNotNull(screen, "Loading screen 应该已经被 pump 实例化");

            var text = screen.Get<PromptUGUI.Controls.Text>("text");
            Assert.IsTrue(text.GameObject.activeSelf);
            Assert.AreEqual("加载中...", text.TmpComponent.text);
        }

        [Test]
        public void Close_dismisses_modal_and_marks_handle_closed()
        {
            var handle = Loading.Open("hi");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            handle.Close();

            Assert.IsTrue(handle.IsClosed);
            Assert.IsFalse(UI.Modal.IsAnyOpen);
            Assert.IsNull(UI.Get("test/Loading1"),
                "Loading screen 应该已经被 pump 关闭");
        }

        [Test]
        public void Close_before_pump_skips_modal_instantiation()
        {
            // MessageBox 先开 → pump 卡在它的 await waiter 上
            var mboxTask = UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = "first",
                Buttons = MsgBtn.OK,
            });

            // Loading 入队, 但 pump 没轮到它 (仍在处理 MessageBox)
            var loading = Loading.Open("queued");
            Assert.AreEqual(2, UI.Modal.QueuedCount);
            Assert.IsNull(UI.Get("test/Loading1"),
                "Loading 还在队列里, screen 还没创建");

            // 关闭 Loading handle —— 走 ResolveExternally → entry.Resolved=true
            loading.Close();
            Assert.IsTrue(loading.IsClosed);
            Assert.IsNull(UI.Get("test/Loading1"),
                "Close() 在 pre-show 不应该实例化 screen");

            // 关掉 MessageBox → pump 转到 Loading entry, 看到 Resolved=true, 直接 continue
            UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, mboxTask.GetAwaiter().GetResult());

            Assert.IsFalse(UI.Modal.IsAnyOpen);
            Assert.IsNull(UI.Get("test/Loading1"),
                "Loading screen 整个生命周期都不应该被创建过");
        }
    }
}
