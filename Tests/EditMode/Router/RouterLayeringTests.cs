using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    /// <summary>
    /// Routed Pages sort by their depth in the route tree (spec 2026-10-01-router-page-layering §4):
    /// slot = PageSortingOrderBase + depth × PageSortingOrderStep, depth = the Pages ahead of it in
    /// the chain. Tab and Prompt nodes own no canvas and do not count.
    /// </summary>
    public class RouterLayeringTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        private const string ShopWithTabsXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='shop'>
  <TabBar id='bar' anchor='stretch'>
    <Tab id='deals'>Deals</Tab>
    <Tab id='cart'>Cart</Tab>
  </TabBar>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["shop"] = ShopWithTabsXml,
                ["battle"] = PageXml("battle"),
                ["item"] = PageXml("item"),
                ["deal"] = PageXml("deal"),
                ["settings"] = PageXml("settings"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            MapTree();
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        // home ─┬─ shop ─┬─ item
        //       │        └─ shop/deals (tab) ── deal
        //       └─ battle
        private static void MapTree(Action<IScreen, RouteQuery> onShopEnter = null)
        {
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home", onEnter: onShopEnter);
            UI.Router.Map("battle", "battle", parent: "home");
            UI.Router.Map("item", "item", parent: "shop");
            UI.Router.MapTab("shop/deals", parent: "shop", tabId: "bar/deals");
            UI.Router.Map("deal", "deal", parent: "shop/deals");
        }

        private static void Open(string route) => UI.Router.Open(route).GetAwaiter().GetResult();

        private static Canvas CanvasOf(string screen) =>
            UI.Get(screen).RootGameObject.GetComponent<Canvas>();

        private static int Order(string screen) => CanvasOf(screen).sortingOrder;

        [Test]
        public void Root_child_and_grandchild_sort_strictly_upward()
        {
            Open("item");   // home / shop / item

            Assert.AreEqual(0, Order("home"));
            Assert.AreEqual(10, Order("shop"));
            Assert.AreEqual(20, Order("item"));
        }

        [Test]
        public void Switching_between_siblings_does_not_accumulate()
        {
            Open("shop");
            Open("battle");
            Assert.AreEqual(10, Order("battle"), "a sibling takes the same depth");
            Open("shop");
            Assert.AreEqual(10, Order("shop"), "back and forth never climbs");
            Assert.AreEqual(0, Order("home"));
        }

        [Test]
        public void Back_and_renavigating_leave_the_order_alone()
        {
            Open("item");
            UI.Router.Back().GetAwaiter().GetResult();   // home / shop
            Assert.AreEqual(10, Order("shop"));
            Open("shop");                                  // same chain → RefreshTarget only
            Assert.AreEqual(10, Order("shop"));
            Assert.AreEqual(0, Order("home"));
        }

        [Test]
        public void A_tab_in_the_chain_adds_no_depth()
        {
            Open("deal");   // home / shop / shop/deals / deal
            Assert.AreEqual(20, Order("deal"), "two pages ahead of it; the tab owns no canvas");
        }

        [Test]
        public void The_knobs_shift_and_space_the_page_band()
        {
            UI.Router.PageSortingOrderBase = 100;
            UI.Router.PageSortingOrderStep = 5;
            Open("item");

            Assert.AreEqual(100, Order("home"));
            Assert.AreEqual(105, Order("shop"));
            Assert.AreEqual(110, Order("item"));
        }

        [Test]
        public void ResetForTests_restores_the_knobs()
        {
            UI.Router.PageSortingOrderBase = 100;
            UI.Router.PageSortingOrderStep = 5;
            UI.ResetForTests();

            Assert.AreEqual(0, UI.Router.PageSortingOrderBase);
            Assert.AreEqual(10, UI.Router.PageSortingOrderStep);
        }

        [Test]
        public void The_step_has_to_clear_an_exiting_page_and_an_open_TabMenu()
        {
            // A depth owns its slot, one below it (a page playing its exit) and up to
            // PopupSortingOffset above it (an expanded TabMenu): four values the next depth must not reach.
            var min = TabMenu.PopupSortingOffset + 2;
            Assert.Throws<ArgumentOutOfRangeException>(() => UI.Router.PageSortingOrderStep = min - 1);
            Assert.AreEqual(10, UI.Router.PageSortingOrderStep, "a rejected value leaves the step alone");
            Assert.DoesNotThrow(() => UI.Router.PageSortingOrderStep = min);
        }

        [Test]
        public void The_router_overrides_the_configurator_on_routed_pages_only()
        {
            UI.CanvasConfigurator = (canvas, _) => canvas.sortingOrder = 7;
            Open("shop");
            Assert.AreEqual(0, Order("home"));
            Assert.AreEqual(10, Order("shop"));

            UI.LoadDocument("inline", PageXml("hud"));   // not a route
            Assert.AreEqual(7, UI.Open("hud").RootGameObject.GetComponent<Canvas>().sortingOrder,
                "a non-routed screen keeps whatever the configurator gave it");
        }

        [Test]
        public void OnEnter_runs_after_the_order_is_written_and_may_adjust_it()
        {
            UI.Router.Clear();
            var seen = -1;
            MapTree(onShopEnter: (_, __) =>
            {
                var canvas = CanvasOf("shop");
                seen = canvas.sortingOrder;
                canvas.sortingOrder = 42;
            });
            Open("shop");

            Assert.AreEqual(10, seen);
            Assert.AreEqual(42, Order("shop"));
        }

        [Test]
        public void A_page_reaching_the_Loading_band_warns()
        {
            UI.Router.PageSortingOrderBase = Loading.SortingOrder - 5;
            var warnings = new List<string>();
            void Capture(string message, string _, LogType type)
            {
                if (type == LogType.Warning && message.Contains("Loading.SortingOrder")) warnings.Add(message);
            }
            UnityEngine.Application.logMessageReceived += Capture;
            try
            {
                Open("home");
                Assert.IsEmpty(warnings, "depth 0 still sorts below Loading");
                Open("shop");
                Assert.AreEqual(1, warnings.Count, "depth 1 lands on / above Loading");
                StringAssert.Contains("'shop'", warnings[0]);
                StringAssert.Contains("PageSortingOrderBase", warnings[0]);
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= Capture;
            }
        }

        [Test]
        public void The_page_band_stays_under_Loading_and_routed_modals()
        {
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "item");
            Open("settings");   // home / shop / item / settings(modal)

            Assert.Less(Order("item"), Loading.SortingOrder);
            Assert.GreaterOrEqual(Order("settings"), UI.Modal.SortingOrderBase);
        }
    }
}
