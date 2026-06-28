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

        private const string RoundXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='Round'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

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

        // 重复 load 同名 screen 仍按设计 fail-fast(显式生命周期管理,无隐含动作),但报错信息要给出
        // 正确操作:先 UnloadDocument / UnloadAll,而非只甩一句 "already loaded"。
        [Test]
        public void Duplicate_load_throws_with_unload_guidance()
        {
            UI.LoadDocument("Round", RoundXml);
            var ex = Assert.Throws<System.InvalidOperationException>(
                () => UI.LoadDocument("Round", RoundXml));
            StringAssert.Contains("UnloadDocument", ex.Message);
        }
    }
}
