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
        public void ResolveChain_PageUnderModal_Throws()
        {
            // A page sorts in the page band, below every modal: under a modal it would be hidden
            // behind its own ancestor (spec 2026-10-01-router-page-layering §8).
            UI.Router.Map("home", "S/Home");
            UI.Router.Map("settings", "S/Settings", present: RoutePresent.Modal, parent: "home");
            UI.Router.MapTab("settings/audio", parent: "settings", tabId: "bar/audio");
            UI.Router.Map("detail", "S/Detail", parent: "settings/audio");

            var ex = Assert.Throws<RouteException>(() => UI.Router.ResolveChain("detail"));
            StringAssert.Contains("'detail'", ex.Message);
            StringAssert.Contains("'settings'", ex.Message);
            StringAssert.Contains("RoutePresent.Modal", ex.Message);
        }

        [Test]
        public void ResolveChain_ModalTabAndPromptUnderModal_StillResolve()
        {
            UI.Router.Map("home", "S/Home");
            UI.Router.Map("settings", "S/Settings", present: RoutePresent.Modal, parent: "home");
            UI.Router.Map("advanced", "S/Advanced", present: RoutePresent.Modal, parent: "settings");
            UI.Router.MapTab("settings/audio", parent: "settings", tabId: "bar/audio");
            UI.Router.MapPrompt("reset", parent: "settings", run: (q, ct) => null);

            Assert.DoesNotThrow(() => UI.Router.ResolveChain("advanced"));
            Assert.DoesNotThrow(() => UI.Router.ResolveChain("settings/audio"));
            Assert.DoesNotThrow(() => UI.Router.ResolveChain("reset"));
        }

        [Test]
        public void Open_Unmapped_Throws()
            => Assert.ThrowsAsync<RouteException>(async () => await UI.Router.Open("ghost"));

        [Test]
        public void MapTab_NullParent_Throws()
            => Assert.Throws<RouteException>(() => UI.Router.MapTab("t", null, "bar/x"));

        [Test]
        public void MapTab_NullTabId_Throws()
            => Assert.Throws<RouteException>(() => UI.Router.MapTab("t", "home", null));

        [Test]
        public void MapPrompt_NullRun_Throws()
            => Assert.Throws<RouteException>(() => UI.Router.MapPrompt("p", "home", null));

        [Test]
        public void Open_MultiScreenSrc_NoExplicitScreen_Throws()
        {
            var files = new System.Collections.Generic.Dictionary<string, string>
            {
                ["multi"] = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='A'><Image id='x' anchor='stretch'/></Screen>
  <Screen name='B'><Image id='x' anchor='stretch'/></Screen>
</PromptUGUI>",
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("m", "multi");   // no screen= → ambiguous
            Assert.ThrowsAsync<RouteException>(async () => await UI.Router.Open("m"));
        }

        [Test]
        public void Open_TabIdNotFound_Throws()
        {
            var files = new System.Collections.Generic.Dictionary<string, string>
            {
                ["home"] = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='home'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>",
                ["shop"] = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='shop'><Image id='x' anchor='stretch'/></Screen></PromptUGUI>",
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.MapTab("shop/deals", parent: "shop", tabId: "bar/deals");   // no such tab in shop
            Assert.ThrowsAsync<RouteException>(async () => await UI.Router.Open("shop/deals"));
        }
    }
}
