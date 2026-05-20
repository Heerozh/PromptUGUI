using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalDocCacheTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Inst'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(src == "test/Inst" ? Xml : null);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void EnsureLoaded_then_OpenModalScreen_twice_yields_two_distinct_screens()
        {
            ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult();

            var (s1, k1) = UI.OpenModalScreen("test/Inst");
            var (s2, k2) = UI.OpenModalScreen("test/Inst");

            Assert.AreNotSame(s1, s2, "同一份 XML 应能实例化出两份独立 Screen");
            Assert.AreNotEqual(k1, k2, "instance key 必须唯一");
            Assert.IsNotNull(s1.RootGameObject);
            Assert.IsNotNull(s2.RootGameObject);

            UI.CloseModalScreen(k1);
            UI.CloseModalScreen(k2);
            Assert.IsNull(UI.Get(k1));
            Assert.IsNull(UI.Get(k2));
        }

        [Test]
        public void EnsureLoaded_is_idempotent()
        {
            ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult();
            Assert.DoesNotThrow(() =>
                ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult(),
                "第二次 EnsureLoaded 不应重复 LoadDocument 而抛 already loaded");
        }
    }
}
