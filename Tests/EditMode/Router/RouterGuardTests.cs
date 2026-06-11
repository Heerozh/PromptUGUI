using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouterGuardTests
    {
        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='bg' anchor='stretch'/>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            { ["home"] = Xml("home"), ["shop"] = Xml("shop") };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Guard_ReturnsFalse_OpenThrows_ChainUnchanged_ChangedNotFired()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            int changed = 0;
            UI.Router.Changed += () => changed++;
            UI.Router.AddGuard(_ => false);

            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Open("shop").GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Guard_ReceivesTargetName()
        {
            string seen = null;
            UI.Router.AddGuard(n => { seen = n; return true; });
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.AreEqual("shop", seen);
        }

        [Test]
        public void Guard_AllTrue_NavigationProceeds()
        {
            UI.Router.AddGuard(_ => true);
            UI.Router.AddGuard(_ => true);
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.AreEqual("shop", UI.Router.Current);
        }

        [Test]
        public void RemoveGuard_RestoresNavigation()
        {
            System.Func<string, bool> g = _ => false;
            UI.Router.AddGuard(g);
            UI.Router.RemoveGuard(g);
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.AreEqual("shop", UI.Router.Current);
        }

        [Test]
        public void Guard_AlsoBlocks_NavigateUrl()
        {
            UI.Router.AddGuard(_ => false);
            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Navigate("shop").GetAwaiter().GetResult());
        }

        [Test]
        public void BypassGuardsOnce_AllowsExactlyOneNavigation()
        {
            UI.Router.AddGuard(_ => false);
            UI.Router.BypassGuardsOnce();
            UI.Router.Open("shop").GetAwaiter().GetResult();   // 放行一次
            Assert.AreEqual("shop", UI.Router.Current);
            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Open("home").GetAwaiter().GetResult());   // 标记已复位
        }

        [Test]
        public void ResetForTests_ClearsGuardsAndBypass()
        {
            UI.Router.AddGuard(_ => false);
            UI.Router.BypassGuardsOnce();
            UI.ResetForTests();
            SetUp();   // 重建路由表
            UI.Router.Open("shop").GetAwaiter().GetResult();   // 不再被拦
            Assert.AreEqual("shop", UI.Router.Current);
        }
    }
}
