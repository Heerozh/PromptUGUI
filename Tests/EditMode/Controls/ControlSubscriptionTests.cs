using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ControlSubscriptionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // 极简追踪 disposable：被 Dispose 时翻 flag。
        private sealed class Flag : System.IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        private static IScreen Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [Test]
        public void AddTo_Control_Disposes_On_Control_Dispose()
        {
            var screen = Open("<Frame id='f'/>");
            var f = screen.Get<Frame>("f");
            var flag = new Flag().AddTo(f);
            Assert.IsFalse(flag.Disposed);
            f.Dispose();
            Assert.IsTrue(flag.Disposed, "control 的订阅袋在 Dispose 时释放被跟踪的订阅");
        }

        [Test]
        public void Control_Dispose_Recursively_Disposes_Child_Subscriptions()
        {
            var screen = Open("<Frame id='outer'><Text id='inner'/></Frame>");
            var outer = screen.Get<Frame>("outer");
            var inner = screen.Get<Text>("inner");
            var flag = new Flag().AddTo(inner);
            outer.Dispose();
            Assert.IsTrue(flag.Disposed, "销毁父节点递归释放子节点的订阅袋");
        }

        [Test]
        public void Control_Double_Dispose_Is_Idempotent()
        {
            var screen = Open("<Frame id='f'/>");
            var f = screen.Get<Frame>("f");
            new Flag().AddTo(f);
            f.Dispose();
            Assert.DoesNotThrow(() => f.Dispose(), "二次 Dispose 不抛");
        }
    }
}
