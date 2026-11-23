using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    /// <summary>
    /// A navigation abandoned while its first load is still in flight opens nothing (spec 2026-11-23-close-reliability §5): no
    /// OnEnter, no layer, no popup of the new page closed; and the loads it left behind do not break the next navigation.
    /// </summary>
    public class RouterStaleActivationTests
    {
        private const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private readonly Dictionary<string, string> _files = new();
        private readonly Dictionary<string, List<AwaitableCompletionSource<string>>> _gates = new();
        private readonly HashSet<string> _gated = new();
        private readonly List<string> _requests = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files.Clear();
            _gates.Clear();
            _gated.Clear();
            _requests.Clear();
            UI.SourceResolver = src =>
            {
                _requests.Add(src);
                if (!_gated.Contains(src)) return AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
                var acs = new AwaitableCompletionSource<string>();
                if (!_gates.TryGetValue(src, out var list)) _gates[src] = list = new List<AwaitableCompletionSource<string>>();
                list.Add(acs);
                return acs.Awaitable;
            };
            _files["home"] = Head + "<Screen name='home'><Btn id='t' anchor='top-left' size='80x40'>T</Btn>" +
                             "<Popover id='p' on='click@t' transition='0'><Frame width='40' height='40'/></Popover></Screen></PromptUGUI>";
            _files["shop"] = Head + "<Screen name='shop'><Frame id='f'/></Screen></PromptUGUI>";
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private void Release(string src)
        {
            var list = _gates[src];
            _gates.Remove(src);
            foreach (var acs in list) acs.SetResult(_files[src]);
        }

        // ───── A5 ─────

        [Test]
        public void A_navigation_abandoned_during_its_load_never_runs_OnEnter([Values] bool unloadAll)
        {
            var entered = 0;
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", onEnter: (s, q) => entered++);
            UI.Router.Open("home").GetAwaiter().GetResult();
            _gated.Add("shop");

            _ = UI.Router.Open("shop");
            if (unloadAll) UI.UnloadAll();
            else _ = UI.Router.Reset();
            Release("shop");

            Assert.AreEqual(0, entered, "the stale activation opened nothing");
            Assert.IsNull(UI.Get("shop"));
        }

        [Test]
        public void A_stale_activation_leaves_the_popup_of_the_page_shown_since_open()
        {
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop");
            UI.Router.Open("home").GetAwaiter().GetResult();
            _gated.Add("shop");
            _ = UI.Router.Open("shop");

            _ = UI.Router.Reset();
            UI.Router.Open("home").GetAwaiter().GetResult();
            var home = UI.Get("home");
            var pop = home.Get<Popover>("p");
            pop.Open(home.Get<Btn>("t"));
            Assume.That(pop.IsOpen, "precondition: a popup open on the page the player is on");
            Release("shop");

            Assert.IsTrue(pop.IsOpen, "no layer was put over it");
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
        }

        // ───── A6 ─────

        [Test]
        public void Navigating_again_to_a_route_still_loading_after_Reset_opens_it()
        {
            UI.Router.Map("shop", "shop");
            _gated.Add("shop");
            _ = UI.Router.Open("shop");

            _ = UI.Router.Reset();
            var again = UI.Router.Open("shop");
            Release("shop");

            Assert.IsTrue(again.GetAwaiter().IsCompleted);
            Assert.DoesNotThrow(() => again.GetAwaiter().GetResult());
            Assert.IsNotNull(UI.Get("shop"));
            CollectionAssert.AreEqual(new[] { "shop" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void A_load_that_finishes_after_UnloadAll_does_not_block_loading_it_again()
        {
            UI.Router.Map("shop", "shop");
            _gated.Add("shop");
            _ = UI.Router.Open("shop");

            UI.UnloadAll();
            var reload = UI.LoadDocumentAsync("shop");   // a reconnect loads its documents again
            Release("shop");

            Assert.IsTrue(reload.GetAwaiter().IsCompleted);
            Assert.DoesNotThrow(() => reload.GetAwaiter().GetResult());
            Assert.IsNotNull(UI.Open("shop"));
        }
    }
}
