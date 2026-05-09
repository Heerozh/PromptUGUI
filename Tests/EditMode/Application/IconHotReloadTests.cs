#if UNITY_EDITOR
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application
{
    public class IconHotReloadTests
    {
        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
        }
        [TearDown] public void Teardown() => UI.ResetForTests();

        [Test]
        public void NotifyIconAssetsChanged_invokes_resolver_rebuild_callback()
        {
            var rebuildCount = 0;
            UI.HotReload.IconResolverRebuilder = () => rebuildCount++;
            UI.HotReload.NotifyIconAssetsChanged();
            Assert.AreEqual(1, rebuildCount);
        }

        [Test]
        public void NotifyIconAssetsChanged_no_op_when_disabled()
        {
            var rebuildCount = 0;
            UI.HotReload.IconResolverRebuilder = () => rebuildCount++;
            UI.HotReload.Enabled = false;
            UI.HotReload.NotifyIconAssetsChanged();
            Assert.AreEqual(0, rebuildCount);
        }
    }
}
#endif
