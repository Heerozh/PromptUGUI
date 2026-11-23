using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Tests.Tutorial
{
    /// <summary>
    /// Between two tutorial steps nothing is blocked (spec 2026-11-23-input-correctness §5; tutorial spec §3 "during Block
    /// steps", §6 "advance → hide the visuals"): the mask stops taking the pointer as the step ends, its dim goes once no step
    /// has used it for a frame — a step begun right away reuses it — and <c>t.Navigate</c> holds the input while the route
    /// switches.
    /// </summary>
    public class TutorialBetweenStepsTests
    {
        private const string Head = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private readonly Dictionary<string, string> _files = new();
        private AwaitableCompletionSource<string> _gate;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files.Clear();
            _gate = null;
            _files["shop"] = Head + "<Screen name='shop'><Frame id='f'/></Screen></PromptUGUI>";
            UI.SourceResolver = src =>
            {
                if (src == "shop" && _gate != null) return _gate.Awaitable;
                return AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            };
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TutorialOverlayView View => UI.Tutorial.ViewForTests;

        // A Block step that any tap ends; returns once it has ended.
        private static void RunTapAnywhereStep(TutorialFlow flow)
        {
            var step = flow.Step(null, text: "hi", advance: Advance.TapAnywhere);
            UI.Tutorial.TickForTests(0.016f);
            View.Mask.GetComponent<TutorialClickRelay>().FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assume.That(step.GetAwaiter().IsCompleted, "precondition: the step ended");
        }

        [Test]
        public void After_a_block_step_the_mask_takes_no_pointer()
        {
            var flow = UI.Tutorial.BeginSessionForTests();
            RunTapAnywhereStep(flow);

            Assert.IsFalse(View.Mask.raycastTarget, "a dialog or a button awaited between steps must be reachable");
            Assert.IsFalse(UI.Tutorial.IsBlockingInput);
        }

        [Test]
        public void The_dim_goes_once_no_step_has_used_it_for_a_frame()
        {
            var flow = UI.Tutorial.BeginSessionForTests();
            RunTapAnywhereStep(flow);

            UI.Tutorial.TickForTests(0.016f);
            UI.Tutorial.TickForTests(0.016f);

            Assert.IsFalse(View.Mask.enabled, "no step, no dim (tutorial spec §6)");
        }

        [Test]
        public void A_step_begun_right_after_reuses_the_dim()
        {
            var flow = UI.Tutorial.BeginSessionForTests();
            var first = flow.Step(null, text: "one", advance: Advance.TapAnywhere);
            UI.Tutorial.TickForTests(0.016f);
            View.Mask.GetComponent<TutorialClickRelay>().FireForTests();
            Assume.That(first.GetAwaiter().IsCompleted);

            flow.Step(null, text: "two", advance: Advance.TapAnywhere);   // what `await t.Step(a); await t.Step(b);` does
            for (var i = 0; i < 4; i++)
            {
                Assert.IsTrue(View.Mask.enabled, $"tick {i}: the dim never went between the two steps");
                UI.Tutorial.TickForTests(0.016f);
            }
            Assert.IsTrue(View.Mask.raycastTarget, "the second Block step blocks again");
        }

        [Test]
        public void Navigate_holds_the_input_until_the_route_switched()
        {
            UI.Router.Map("shop", "shop");
            var flow = UI.Tutorial.BeginSessionForTests();
            RunTapAnywhereStep(flow);
            _gate = new AwaitableCompletionSource<string>();

            var nav = flow.Navigate("shop");
            Assert.IsTrue(View.InputHeldForTests, "the route is switching: taps go nowhere");

            _gate.SetResult(_files["shop"]);
            Assume.That(nav.GetAwaiter().IsCompleted, "precondition: navigated");
            Assert.IsFalse(View.InputHeldForTests, "released once the page is up");
            Assert.IsNotNull(UI.Get("shop"));
        }
    }
}
