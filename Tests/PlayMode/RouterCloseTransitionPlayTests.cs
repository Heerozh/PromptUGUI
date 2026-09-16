using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using Screen = PromptUGUI.Application.Screen;      // disambiguates UnityEngine.Screen

namespace PromptUGUI.Tests.PlayMode
{
    /// <summary>
    /// The Router and exit animations (spec 2026-09-16-close-transition-design §5.5): a deactivated
    /// page plays its exit; with the default <c>Overlap</c> the next page is built at once, with
    /// <c>Sequential</c> only after the ghost is destroyed.
    /// </summary>
    public class RouterCloseTransitionPlayTests
    {
        private static string PageXml(string name) =>
            "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
            $"<Screen name='{name}'>" +
            "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.25s' easing='linear'>" +
            "<Frame id='f' anchor='stretch'/></Animation>" +
            "</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(
                src is "home" or "shop" or "bag" ? PageXml(src) : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.Map("bag", "bag", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static IEnumerator Await(Awaitable aw)
        {
            for (int i = 0; i < 120 && !aw.GetAwaiter().IsCompleted; i++) yield return null;
            aw.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator Back_plays_the_child_exit_over_the_still_open_parent()
        {
            yield return Await(UI.Router.Open("shop"));
            var home = UI.Get("home");
            var shop = UI.Get("shop");
            yield return new WaitForSeconds(0.3f);

            var changed = 0;
            UI.Router.Changed += () => changed++;
            yield return Await(UI.Router.Back());

            Assert.AreEqual(1, changed, "the chain changed on Back");
            Assert.AreEqual("home", UI.Router.Current);
            Assert.IsTrue(shop.IsClosing, "the child is playing its exit");
            Assert.IsNotNull(shop.RootGameObject);
            Assert.IsNull(UI.Get("shop"));
            Assert.AreSame(home, UI.Get("home"), "the parent was never closed");

            yield return new WaitForSeconds(0.35f);
            yield return null;
            yield return null;
            Assert.IsNull(shop.RootGameObject, "the ghost finished on its own");
            Assert.IsNotNull(home.RootGameObject);
        }

        [UnityTest]
        public IEnumerator Overlap_builds_the_next_page_while_the_old_one_exits()
        {
            yield return Await(UI.Router.Open("shop"));
            var shop = UI.Get("shop");
            yield return new WaitForSeconds(0.3f);

            var open = UI.Router.Open("bag");
            for (int i = 0; i < 10 && UI.Get("bag") == null; i++) yield return null;
            Assert.IsNotNull(UI.Get("bag"), "the sibling is built at once");
            Assert.IsTrue(shop.IsClosing, "while the old page is still fading");
            yield return Await(open);

            yield return new WaitForSeconds(0.35f);
            yield return null;
            yield return null;
            Assert.IsNull(shop.RootGameObject);
            Assert.IsNotNull(UI.Get("bag").RootGameObject);
        }

        [UnityTest]
        public IEnumerator Sequential_waits_for_the_old_page_to_be_destroyed_first()
        {
            UI.Router.Transition = RouteTransition.Sequential;
            yield return Await(UI.Router.Open("shop"));
            var shop = UI.Get("shop");
            yield return new WaitForSeconds(0.3f);

            var open = UI.Router.Open("bag");
            yield return null;
            yield return null;
            Assert.IsTrue(shop.IsClosing, "the old page exits first");
            Assert.IsNull(UI.Get("bag"), "and the new one is not built until it is gone");

            yield return Await(open);
            Assert.IsNull(shop.RootGameObject, "Open completed only after the ghost was destroyed");
            Assert.IsNotNull(UI.Get("bag"));
            Assert.AreEqual("bag", UI.Router.Current);
        }

        [UnityTest]
        public IEnumerator Transition_is_reset_between_tests()
        {
            Assert.AreEqual(RouteTransition.Overlap, UI.Router.Transition, "default");
            UI.Router.Transition = RouteTransition.Sequential;
            UI.ResetForTests();
            Assert.AreEqual(RouteTransition.Overlap, UI.Router.Transition);
            yield return null;
        }
    }
}
