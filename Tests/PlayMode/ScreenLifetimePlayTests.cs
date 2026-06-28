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

        private const string LobbyXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='Lobby'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

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

        // reconnect 隐患:routed Page 的 root 被外部销毁(场景重载)后 Router._chain 残留,再导航回
        // 同名界面以前是静默空白(UI.Get→null,RefreshTarget 跳过)。现在应 fail-fast 带 UnloadAll 指引。
        [UnityTest]
        public IEnumerator Router_navigate_after_routed_root_destroyed_throws_with_guidance()
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "lobby" ? LobbyXml : null);
            UI.Router.Map("Lobby", "lobby", "Lobby");

            var open1 = UI.Router.Open("Lobby");
            for (int i = 0; i < 30 && !open1.GetAwaiter().IsCompleted; i++) yield return null;
            open1.GetAwaiter().GetResult();                 // 第一次导航成功
            var screen = UI.Get("Lobby");
            Assert.IsNotNull(screen);

            Object.Destroy(screen.RootGameObject);          // 模拟场景重载销毁,不走 Router
            yield return null;                              // 哨兵从 _open 注销 Lobby

            var open2 = UI.Router.Open("Lobby");
            for (int i = 0; i < 30 && !open2.GetAwaiter().IsCompleted; i++) yield return null;
            System.Exception ex = null;
            try { open2.GetAwaiter().GetResult(); }
            catch (System.Exception e) { ex = e; }
            Assert.IsInstanceOf<RouteException>(ex,
                "routed screen 被外部销毁后导航应 fail-fast,而非静默空白");
            StringAssert.Contains("UnloadAll", ex.Message);
        }
    }
}
