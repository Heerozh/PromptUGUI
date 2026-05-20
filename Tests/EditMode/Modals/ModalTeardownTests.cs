using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalTeardownTests : ModalTestFixture
    {
        private const string LoadingXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading1'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
  </Screen>
</PromptUGUI>";

        public override void SetUp()
        {
            base.SetUp();
            Files["test/Loading1"] = LoadingXml;
            Loading.XmlSrc = "test/Loading1";
        }

        [Test]
        public void UnloadAll_tears_down_loading_overlay()
        {
            Loading.Open("x");
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);

            UI.UnloadAll();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount,
                "UnloadAll 必须清掉 Loading overlay");
        }

        [Test]
        public void ResetForTests_tears_down_loading_overlay()
        {
            Loading.Open("x");
            UI.ResetForTests();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }

        [Test]
        public void LoadingHandle_Close_after_teardown_is_noop()
        {
            var handle = Loading.Open("about to be torn down");
            UI.ResetForTests();
            Assert.DoesNotThrow(() => handle.Close());
            Assert.IsTrue(handle.IsClosed);
        }

        [Test]
        public void Loading_and_MessageBox_coexist()
        {
            // spec §1.2 原死锁场景的回归:Loading 期间开 MessageBox,两者同时存在。
            var loading = Loading.Open("working...");
            var mbox = UI.Modal.OpenAsync(new MessageBoxRequest { Text = "q", Buttons = MsgBtn.OK });

            Assert.AreEqual(1, LoadingOverlay.ActiveCount, "Loading overlay 仍在");
            Assert.IsTrue(UI.Modal.IsAnyOpen, "MessageBox 同时显示,没被 Loading 挡在队列后");

            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, mbox.GetAwaiter().GetResult());
            Assert.AreEqual(1, LoadingOverlay.ActiveCount, "关掉 MessageBox 后 Loading 仍在");

            loading.Close();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }
    }
}
