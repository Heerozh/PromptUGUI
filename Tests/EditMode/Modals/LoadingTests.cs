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

        [Test]
        public void Close_is_idempotent()
        {
            var handle = Loading.Open("hi");
            handle.Close();
            Assert.IsTrue(handle.IsClosed);

            // 第二次 Close 不应该抛
            Assert.DoesNotThrow(() => handle.Close());
            Assert.IsTrue(handle.IsClosed);
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Text_null_hides_text_node()
        {
            Loading.Open(null);
            var text = UI.Get("test/Loading1").Get<PromptUGUI.Controls.Text>("text");
            Assert.IsFalse(text.GameObject.activeSelf);
        }

        [Test]
        public void Text_empty_hides_text_node()
        {
            Loading.Open("");
            var text = UI.Get("test/Loading1").Get<PromptUGUI.Controls.Text>("text");
            Assert.IsFalse(text.GameObject.activeSelf);
        }

        [Test]
        public void TryEscape_listener_does_not_close_loading()
        {
            var handle = Loading.Open("press ESC and see nothing");
            var listener = UI.Get("test/Loading1")
                .RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "Pump 必须给 Loading screen 也挂 ModalEscapeListener");

            listener.FireForTests();

            Assert.IsTrue(UI.Modal.IsAnyOpen, "Loading 应该仍然显示");
            Assert.IsFalse(handle.IsClosed);
            UI.Modal.CloseAll();   // teardown 干净点
        }

        [Test]
        public void Custom_xml_without_text_id_does_not_throw()
        {
            // 模拟用户自定义 XML 时把 text 元素删了的场景
            const string customXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading2'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame anchor='center' size='200x100'>
      <Image anchor='stretch' color='white'/>
    </Frame>
  </Screen>
</PromptUGUI>";
            Files["test/Loading2"] = customXml;
            Loading.XmlSrc = "test/Loading2";

            var handle = Loading.Open("仍然传 text 但 XML 没 text 元素");
            Assert.IsNotNull(handle, "Bind 不应该抛 KeyNotFoundException");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            handle.Close();
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }
    }
}
