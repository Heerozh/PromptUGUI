using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Application.Tutorial;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialRunTests
    {
        private Dictionary<string, int> _store;

        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Btn id='b' size='100x40'>x</Btn>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // MessageBox.XmlSrc 是可变静态字段;ModalTestFixture / RouterNavigateTests 等
            // 会把它改到 "test/Box1" 之类且永不还原(ResetForTests 不复位它)。这里钉回内置
            // 路径,让 EscDuringBlockStep_DoesNotCloseModal 不依赖跨 suite 的执行顺序。
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            _store = new Dictionary<string, int>();
            UI.Tutorial.UseProgressStore(
                id => _store.TryGetValue(id, out var v) ? v : 0,
                (id, n) => _store[id] = n);
            var files = new Dictionary<string, string> { ["home"] = Xml("home") };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        // 驱动一个 Block+TapAnywhere(null target)步骤到完成
        private static void ClickThrough()
        {
            UI.Tutorial.TickForTests(0.016f);
            UI.Tutorial.ViewForTests.Mask
                .GetComponent<TutorialClickRelay>().FireForTests();
            UI.Tutorial.TickForTests(0.016f);
        }

        [Test]
        public void Run_SavesProgressPerStep_AndSentinelOnFinish()
        {
            var run = UI.Tutorial.Run("t1", async t =>
            {
                await t.Step(null, text: "a");
                await t.Step(null, text: "b");
            });
            Assert.IsTrue(UI.Tutorial.IsActive);
            ClickThrough();
            Assert.AreEqual(1, _store["t1"]);
            ClickThrough();
            run.GetAwaiter().GetResult();
            Assert.AreEqual(int.MaxValue, _store["t1"]);
            Assert.IsFalse(UI.Tutorial.IsActive);
            Assert.IsNull(UI.Tutorial.ViewForTests);
        }

        [Test]
        public void Run_Resume_FastForwardsCompletedSteps()
        {
            _store["t1"] = 1;
            int shown = 0;
            var run = UI.Tutorial.Run("t1", async t =>
            {
                await t.Step(null, text: "a"); shown++;
                Assert.AreEqual(1, shown);
                await t.Step(null, text: "b"); shown++;
            });
            ClickThrough();
            run.GetAwaiter().GetResult();
            Assert.AreEqual(2, shown);
        }

        [Test]
        public void Run_Sentinel_WholeRunInstant_NoOverlay()
        {
            _store["t1"] = int.MaxValue;
            UI.Tutorial.Run("t1", async t =>
            {
                await t.Step(null, text: "a");
                await t.Step(null, text: "b");
            }).GetAwaiter().GetResult();
            Assert.IsNull(UI.Tutorial.ViewForTests, "全程 fast-forward 不应创建 overlay");
        }

        [Test]
        public void Run_BlocksRouterNavigation_AndReleasesAfter()
        {
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Open("home").GetAwaiter().GetResult());
            ClickThrough();
            run.GetAwaiter().GetResult();
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.AreEqual("home", UI.Router.Current);
        }

        [Test]
        public void Flow_Navigate_BypassesGuard()
        {
            var run = UI.Tutorial.Run("t1", async t =>
            {
                await t.Navigate("home");
                await t.Step(null, text: "a");
            });
            UI.Tutorial.TickForTests(0.016f);
            Assert.AreEqual("home", UI.Router.Current);
            ClickThrough();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void Run_BodyThrows_GuardRemoved_OverlayDestroyed()
        {
            var run = UI.Tutorial.Run("t1",
                t => throw new System.InvalidOperationException("boom"));
            Assert.Throws<System.InvalidOperationException>(() => run.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Tutorial.IsActive);
            Assert.IsNull(UI.Tutorial.ViewForTests);
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.AreEqual("home", UI.Router.Current);
        }

        [Test]
        public void Run_Reentry_Throws()
        {
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            Assert.Throws<System.InvalidOperationException>(
                () => UI.Tutorial.Run("t2", async t => await t.Step(null, text: "a")).GetAwaiter().GetResult());
            ClickThrough();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void Run_NoStore_AlwaysFromScratch()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed<string>(null);
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            Assert.IsTrue(UI.Tutorial.IsActive);
            ClickThrough();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void UnloadAll_MidRun_ClearsActive_AndReleasesGuard()
        {
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.IsActive);

            UI.UnloadAll();   // 生产 teardown 路径(区别于 ResetForTests)

            Assert.IsFalse(UI.Tutorial.IsActive, "UnloadAll 应清掉进行中的引导");
            Assert.IsNull(UI.Tutorial.ViewForTests);
            // _rejectAll guard 应已注销 → 导航不再被拒(home 路由 + resolver 在 UnloadAll 后仍在)
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.AreEqual("home", UI.Router.Current);
            _ = run;   // 挂起的 Run 被 teardown 抹掉,不 await
        }

        [Test]
        public void EscDuringBlockStep_DoesNotCloseModal()
        {
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.IsBlockingInput);
            _ = MessageBox.Open("hi");   // ad-hoc 模态(不 await)
            var listener = UI.Modal.TopScreen.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener);
            listener.FireForTests();
            Assert.IsTrue(UI.Modal.IsAnyOpen, "Block 引导期间 ESC 不得关掉模态");
            UI.Tutorial.ViewForTests.Mask.GetComponent<TutorialClickRelay>().FireForTests();
            run.GetAwaiter().GetResult();
        }
    }
}
