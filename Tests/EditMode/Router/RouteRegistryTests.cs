using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouteRegistryTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Map_ThenIsMapped()
        {
            UI.Router.Map("home", "Screens/Home");
            Assert.IsTrue(UI.Router.IsMapped("home"));
            Assert.IsFalse(UI.Router.IsMapped("nope"));
        }

        [Test]
        public void Map_Duplicate_Throws()
        {
            UI.Router.Map("home", "Screens/Home");
            Assert.Throws<RouteException>(() => UI.Router.Map("home", "Screens/Other"));
        }

        [Test]
        public void Map_NullSrc_Throws()
            => Assert.Throws<RouteException>(() => UI.Router.Map("home", null));

        [Test]
        public void ResolveChain_RootToLeaf_InOrder()
        {
            UI.Router.Map("home", "S/Home");
            UI.Router.Map("shop", "S/Shop", parent: "home");
            UI.Router.MapTab("shop/deals", parent: "shop", tabId: "bar/deals");
            var names = UI.Router.ResolveChain("shop/deals").Select(n => n.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "home", "shop", "shop/deals" }, names);
        }

        [Test]
        public void ResolveChain_UnmappedParent_Throws()
        {
            UI.Router.Map("shop", "S/Shop", parent: "ghost");
            Assert.Throws<RouteException>(() => UI.Router.ResolveChain("shop"));
        }

        [Test]
        public void ResolveChain_Cycle_Throws()
        {
            UI.Router.Map("a", "S/A", parent: "b");
            UI.Router.Map("b", "S/B", parent: "a");
            Assert.Throws<RouteException>(() => UI.Router.ResolveChain("a"));
        }

        [Test]
        public void ResolveChain_PromptAsParent_Throws()
        {
            UI.Router.Map("home", "S/Home");
            UI.Router.MapPrompt("ask", parent: "home", run: (q, ct) => null);
            UI.Router.Map("deep", "S/Deep", parent: "ask");
            Assert.Throws<RouteException>(() => UI.Router.ResolveChain("deep"));
        }

        [Test]
        public void Open_Unmapped_Throws()
            => Assert.ThrowsAsync<RouteException>(async () => await UI.Router.Open("ghost"));
    }
}
