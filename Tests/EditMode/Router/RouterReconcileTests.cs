using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouterReconcileTests
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
            {
                ["home"] = Xml("home"),
                ["shop"] = Xml("shop"),
                ["battle"] = Xml("battle"),
                ["item"] = Xml("item"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            // src == screen name 简化:每个 src 内 <Screen name> 同名
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.Map("battle", "battle", parent: "home");
            UI.Router.Map("item", "item", parent: "shop");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static List<string> Chain() => UI.Router.Chain.ToList();

        [Test]
        public void Open_BuildsCanonicalChainFromRoot()
        {
            UI.Router.Open("shop").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop" }, Chain());
            Assert.AreEqual("shop", UI.Router.Current);
            Assert.IsNotNull(UI.Get("home"));
            Assert.IsNotNull(UI.Get("shop"));
        }

        [Test]
        public void Open_Child_PushesOnly()
        {
            UI.Router.Open("shop").GetAwaiter().GetResult();
            UI.Router.Open("item").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop", "item" }, Chain());
        }

        [Test]
        public void Open_Ancestor_PopsToIt()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();
            UI.Router.Open("shop").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop" }, Chain());
            Assert.IsNull(UI.Get("item"));   // item 屏被销毁
        }

        [Test]
        public void Open_SiblingBranch_ClosesToCommonAncestor()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();   // home/shop/item
            UI.Router.Open("battle").GetAwaiter().GetResult(); // home/battle
            CollectionAssert.AreEqual(new[] { "home", "battle" }, Chain());
            Assert.IsNull(UI.Get("shop"));
            Assert.IsNull(UI.Get("item"));
            Assert.IsNotNull(UI.Get("home"));  // 公共前缀 home 不重建
        }

        [Test]
        public void OnEnter_ReceivesQuery_OnTargetAndNewIntermediates()
        {
            var seen = new Dictionary<string, string>();
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = Xml("home"),
                ["shop"] = Xml("shop"),
                ["item"] = Xml("item"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home",
                onEnter: (s, q) => seen["shop"] = q["k"]);
            UI.Router.Map("item", "item", parent: "shop",
                onEnter: (s, q) => seen["item"] = q["k"]);

            var q = new RouteQuery(new Dictionary<string, string> { ["k"] = "v" });
            UI.Router.Open("item", q).GetAwaiter().GetResult();

            Assert.AreEqual("v", seen["shop"]);   // 新激活中间节点也收到
            Assert.AreEqual("v", seen["item"]);   // 目标收到
        }

        [Test]
        public void ReNavigate_SameTarget_RefreshesOnEnter_NoRebuild()
        {
            int count = 0;
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["home"] = Xml("home") };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home", onEnter: (s, q) => count++);

            UI.Router.Open("home").GetAwaiter().GetResult();
            var first = UI.Get("home");
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.AreEqual(2, count);                 // OnEnter 再触发
            Assert.AreSame(first, UI.Get("home"));     // 同一 Screen,未重建
        }

        [Test]
        public void Changed_FiresOnNavigation()
        {
            int fired = 0;
            UI.Router.Changed += () => fired++;
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.GreaterOrEqual(fired, 1);
        }

        [Test]
        public void ConcurrentOpen_LatestWins_BothAwaitablesComplete()
        {
            var a = UI.Router.Open("item");
            var b = UI.Router.Open("battle");
            b.GetAwaiter().GetResult();
            a.GetAwaiter().GetResult();   // 被取代的也应完成,不挂死
            CollectionAssert.AreEqual(new[] { "home", "battle" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void ResetForTests_ClearsChain()
        {
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.IsNotEmpty(UI.Router.Chain.ToList());
            UI.ResetForTests();
            Assert.IsEmpty(UI.Router.Chain.ToList());
            Assert.IsNull(UI.Router.Current);
        }

        [Test]
        public void Back_GoesToParent()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();   // home/shop/item
            UI.Router.Back().GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Back_AtRoot_IsNoop()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            UI.Router.Back().GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Back_EmptyChain_IsNoop()
            => Assert.DoesNotThrow(() => UI.Router.Back().GetAwaiter().GetResult());

        [Test]
        public void Reset_ClosesWholeChain()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();
            UI.Router.Reset().GetAwaiter().GetResult();
            Assert.IsEmpty(UI.Router.Chain.ToList());
            Assert.IsNull(UI.Get("home"));
            Assert.IsNull(UI.Get("shop"));
            Assert.IsNull(UI.Get("item"));
        }
    }
}
