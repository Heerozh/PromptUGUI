using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    public class RouterModalTabTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        private static string ModalXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='panel' anchor='center' size='300x200'/>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["settings"] = ModalXml("settings"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Modal_Activates_OnModalSortingBand()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "settings" }, UI.Router.Chain.ToList());
            var canvas = UI.Get("settings").RootGameObject.GetComponent<Canvas>();
            // Note: overrideSorting does not read back as true on a root ScreenSpaceOverlay
            // canvas (only meaningful for nested canvases — same as LoadingOverlayTests).
            // Verify sortingOrder is in the modal sorting band instead.
            Assert.GreaterOrEqual(canvas.sortingOrder, UI.Modal.SortingOrderBase);
        }

        [Test]
        public void Modal_Esc_GoesBackToParent()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            var esc = UI.Get("settings").RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(esc);
            esc.FireForTests();
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
            Assert.IsNull(UI.Get("settings"));
        }

        [Test]
        public void Modal_Deactivate_ClosesScreen()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.IsNull(UI.Get("settings"));
        }

        private const string ShopWithTabsXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='shop'>
  <TabBar id='bar' anchor='stretch'>
    <Tab id='deals'>Deals</Tab>
    <Tab id='cart'>Cart</Tab>
  </TabBar>
</Screen></PromptUGUI>";

        private void SetUpShopTabs()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["shop"] = ShopWithTabsXml,
                ["item"] = PageXml("item"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.MapTab("shop/deals", parent: "shop", tabId: "bar/deals");
            UI.Router.MapTab("shop/cart", parent: "shop", tabId: "bar/cart");
            UI.Router.Map("item", "item", parent: "shop/deals");   // drill-in 自某个 tab
        }

        [Test]
        public void Tab_Activate_SelectsTab_HostOpened()
        {
            SetUpShopTabs();
            UI.Router.Open("shop/deals").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop", "shop/deals" }, UI.Router.Chain.ToList());
            var deals = UI.Get("shop").Get<PromptUGUI.Controls.Tab>("bar/deals");
            Assert.IsTrue(deals.IsOn);
        }

        [Test]
        public void Tab_SiblingSwitch_DoesNotRebuildHost()
        {
            SetUpShopTabs();
            UI.Router.Open("shop/deals").GetAwaiter().GetResult();
            var hostBefore = UI.Get("shop");
            UI.Router.Open("shop/cart").GetAwaiter().GetResult();
            Assert.AreSame(hostBefore, UI.Get("shop"));    // 宿主未重建
            Assert.IsTrue(UI.Get("shop").Get<PromptUGUI.Controls.Tab>("bar/cart").IsOn);
            CollectionAssert.AreEqual(new[] { "home", "shop", "shop/cart" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Tab_DrillIn_FullChainReconciles()
        {
            SetUpShopTabs();
            UI.Router.Open("item").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(
                new[] { "home", "shop", "shop/deals", "item" }, UI.Router.Chain.ToList());
            Assert.IsTrue(UI.Get("shop").Get<PromptUGUI.Controls.Tab>("bar/deals").IsOn);
            Assert.IsNotNull(UI.Get("item"));
        }
    }
}
