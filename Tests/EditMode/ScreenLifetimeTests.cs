using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests
{
    public class ScreenLifetimeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        // 防御兜底：root 被外部销毁后再调 ReSolve()(例如 EditMode 不跑 OnDestroy 的路径,
        // 或哨兵回调时序未及)不应解引用已销毁的 RootGameObject。
        [Test]
        public void ReSolve_AfterRootDestroyed_DoesNotThrow()
        {
            UI.LoadDocument("test", Xml);
            var screen = UI.Open("S");
            Object.DestroyImmediate(screen.RootGameObject);
            Assert.DoesNotThrow(() => screen.ReSolve());
        }
    }
}
