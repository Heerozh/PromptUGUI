using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class LoadingOverlayTests : ModalTestFixture
    {
        private const string LoadingXml = @"<?xml version='1.0' encoding='utf-8'?>
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
            Files["test/Loading1"] = LoadingXml;
            Loading.XmlSrc = "test/Loading1";
        }

        [Test]
        public void Open_shows_overlay_with_text()
        {
            var handle = Loading.Open("加载中...");

            Assert.IsNotNull(handle);
            Assert.IsFalse(handle.IsClosed);
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);

            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            var text = screen.Get<PromptUGUI.Controls.Text>("text");
            Assert.IsTrue(text.GameObject.activeSelf);
            Assert.AreEqual("加载中...", text.TmpComponent.text);
        }

        [Test]
        public void Close_destroys_overlay_and_marks_handle()
        {
            var handle = Loading.Open("hi");
            handle.Close();

            Assert.IsTrue(handle.IsClosed);
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }

        [Test]
        public void Close_is_idempotent()
        {
            var handle = Loading.Open("hi");
            handle.Close();
            Assert.DoesNotThrow(() => handle.Close());
            Assert.IsTrue(handle.IsClosed);
        }

        [Test]
        public void Concurrent_opens_each_get_their_own_overlay()
        {
            var h1 = Loading.Open("one");
            var h2 = Loading.Open("two");

            Assert.AreEqual(2, LoadingOverlay.ActiveCount);

            h1.Close();
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);
            Assert.IsFalse(h2.IsClosed);

            h2.Close();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }

        [Test]
        public void Text_null_hides_text_node()
        {
            Loading.Open(null);
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            Assert.IsFalse(screen.Get<PromptUGUI.Controls.Text>("text").GameObject.activeSelf);
        }

        [Test]
        public void Text_empty_hides_text_node()
        {
            Loading.Open("");
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            Assert.IsFalse(screen.Get<PromptUGUI.Controls.Text>("text").GameObject.activeSelf);
        }

        [Test]
        public void Custom_xml_without_text_id_does_not_throw()
        {
            const string custom = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading2'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame anchor='center' size='200x100'><Image anchor='stretch' color='white'/></Frame>
  </Screen>
</PromptUGUI>";
            Files["test/Loading2"] = custom;
            Loading.XmlSrc = "test/Loading2";

            var handle = Loading.Open("text 传了但 XML 没 text 元素");
            Assert.IsNotNull(handle);
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);
            handle.Close();
        }

        [Test]
        public void Overlay_has_no_escape_listener()
        {
            Loading.Open("press ESC, nothing");
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            Assert.IsNull(screen.RootGameObject.GetComponent<ModalEscapeListener>(),
                "Loading overlay 不响应 ESC,不应挂 ModalEscapeListener");
        }

        [Test]
        public void SortingOrder_below_modal_band()
        {
            // 显式设两个 band 值,避免被别的测试遗留的 SortingOrderBase 污染。
            UI.Modal.SortingOrderBase = 1000;
            Loading.SortingOrder = 500;
            Loading.Open("x");
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
            // 注:不断言 canvas.overrideSorting —— 根 ScreenSpaceOverlay canvas 上
            // overrideSorting 不会回读为 true(只对嵌套 canvas 有意义)。sortingOrder
            // 才是实际生效、可验证的量。
            Assert.AreEqual(Loading.SortingOrder, canvas.sortingOrder);
            Assert.Less(canvas.sortingOrder, UI.Modal.SortingOrderBase,
                "Loading 必须在 dialog 之下");
        }
    }
}
