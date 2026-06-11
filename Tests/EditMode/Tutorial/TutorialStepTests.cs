using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialStepTests
    {
        private const string MainXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='main'>
  <Btn id='shopBtn' size='200x80'>Shop</Btn>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "main" ? MainXml : null);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TutorialFlow BeginFlow() => UI.Tutorial.BeginSessionForTests();

        [Test]
        public void Step_TargetNotResolvable_StaysPending_NoHole()
        {
            var flow = BeginFlow();
            var step = flow.Step("main/shopBtn", text: "hi");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            Assert.IsNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);
        }

        [Test]
        public void Step_TargetAppears_HoleOpens_AndTapTargetAdvances()
        {
            var flow = BeginFlow();
            var step = flow.Step("main/shopBtn", text: "hi");
            UI.Tutorial.TickForTests(0.016f);

            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("main");
            UI.Tutorial.TickForTests(0.016f);

            Assert.IsNotNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);
            var relay = UI.Get("main").Get<PromptUGUI.Controls.Btn>("shopBtn")
                .GameObject.GetComponent<TutorialClickRelay>();
            Assert.IsNotNull(relay, "TapTarget 应在目标 GO 挂 relay");
            relay.FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Step_Timeout_Throws()
        {
            var flow = BeginFlow();
            var step = flow.Step("main/missing", timeout: 1f);
            UI.Tutorial.TickForTests(0.6f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            UI.Tutorial.TickForTests(0.6f);
            var ex = Assert.Throws<System.TimeoutException>(() => step.GetAwaiter().GetResult());
            StringAssert.Contains("main/missing", ex.Message);
        }

        [Test]
        public void Step_TargetDestroyed_ReturnsToWaiting_HoleCloses()
        {
            var flow = BeginFlow();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("main");
            var step = flow.Step("main/shopBtn");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsNotNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);

            UI.Close("main");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            Assert.IsNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);
        }

        [Test]
        public void Step_NullTarget_TapAnywhere_MaskClickAdvances()
        {
            var flow = BeginFlow();
            var step = flow.Step(null, text: "干得好");
            UI.Tutorial.TickForTests(0.016f);
            var relay = UI.Tutorial.ViewForTests.Mask.GetComponent<TutorialClickRelay>();
            Assert.IsNotNull(relay);
            relay.FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Step_AdvanceWhen_PredicatePolledPerTick()
        {
            bool flag = false;
            var flow = BeginFlow();
            var step = flow.Step(null, advance: Advance.When(() => flag));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            flag = true;
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Step_AdvanceUntil_CompletesWithCondition()
        {
            var acs = new AwaitableCompletionSource();
            var flow = BeginFlow();
            var step = flow.Step(null, advance: Advance.Until(() => acs.Awaitable));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            acs.SetResult();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Step_TapAnywhere_HintMode_Throws()
        {
            var flow = BeginFlow();
            Assert.Throws<System.ArgumentException>(() =>
                flow.Step(null, mode: TutorialMode.Hint, advance: Advance.TapAnywhere));
        }

        [Test]
        public void Step_TapTarget_NullTarget_Throws()
        {
            var flow = BeginFlow();
            Assert.Throws<System.ArgumentException>(() =>
                flow.Step(null, advance: Advance.TapTarget));
        }

        [Test]
        public void HintMode_MaskDisabled_NotRaycastTarget()
        {
            var flow = BeginFlow();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("main");
            flow.Step("main/shopBtn", mode: TutorialMode.Hint);
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(UI.Tutorial.ViewForTests.Mask.enabled);
            Assert.IsFalse(UI.Tutorial.ViewForTests.Mask.raycastTarget);
            Assert.IsFalse(UI.Tutorial.IsBlockingInput);
        }

        [Test]
        public void BlockMode_IsBlockingInput_True_DuringStep_FalseAfter()
        {
            var flow = BeginFlow();
            var step = flow.Step(null, text: "x");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.IsBlockingInput);
            UI.Tutorial.ViewForTests.Mask.GetComponent<TutorialClickRelay>().FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
            Assert.IsFalse(UI.Tutorial.IsBlockingInput);
        }

        [Test]
        public void Step_Text_AppliedToBubble_NullText_BubbleHidden()
        {
            var flow = BeginFlow();
            flow.Step(null, text: "点这里");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.ViewForTests.BubbleRootActiveForTests);
            Assert.AreEqual("点这里", UI.Tutorial.ViewForTests.BubbleTextForTests);
        }
    }
}
