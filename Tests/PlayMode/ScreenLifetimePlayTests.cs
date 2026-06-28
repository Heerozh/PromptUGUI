using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    public class ScreenLifetimePlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        // 复现用户场景：玩家多次重连 → 场景重载销毁 Screen 的 root(不走 Screen.Close)→
        // 残留的 _themeHandler 仍挂在静态 UI.Theme.Changed 上 → 下次加载触发当前主题的
        // Changed → 死 Screen.ReSolve() 解引用已销毁 root → MissingReferenceException。
        [UnityTest]
        public IEnumerator ThemeChanged_AfterRootDestroyedExternally_DoesNotThrow_AndUnregisters()
        {
            UI.LoadDocument("test", Xml);
            UI.Theme.Set("dark");                        // Current 非 null → RaiseChangedIfCurrent 会派发
            var screen = UI.Open("S");
            Assert.IsNotNull(screen.RootGameObject);

            Object.Destroy(screen.RootGameObject);       // 模拟场景重载销毁,不走 Close()
            yield return null;                           // 等帧末 Destroy + relay.OnDestroy

            Assert.DoesNotThrow(() => UI.Theme.RaiseChangedIfCurrent("dark"),
                "root 已被外部销毁的 Screen 不应再响应 Theme.Changed");
            Assert.IsNull(UI.Get("S"), "外部销毁 root 后 Screen 应已从 _open 注销");
        }
    }
}
