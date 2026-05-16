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
    }
}
