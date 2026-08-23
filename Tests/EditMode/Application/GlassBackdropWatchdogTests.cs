using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// Backdrop availability is latched true by the capture pass and, before this watchdog, was only
    /// ever cleared when the capture was stopped or the camera went missing entirely. Anything else
    /// that quietly stops production — the capture camera disabled for a cutscene, the pipeline
    /// swapped, Compatibility Mode — left every glass panel sampling one frozen frame forever, with
    /// <c>UI.Glass.IsActive</c> still reporting true.
    ///
    /// One frame-staleness check covers all of those causes at once, which is why it is a watchdog
    /// rather than a check per cause.
    /// </summary>
    public class GlassBackdropWatchdogTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void FreshBackdrop_StaysAvailable()
        {
            GlassRuntime.PublishBackdropForTests(frame: 100);
            Assert.IsTrue(UI.Glass.IsActive);

            // The very next frame is not stale — the pass simply has not run yet this frame.
            GlassRuntime.TickBackdropWatchdog(currentFrame: 101);
            Assert.IsTrue(UI.Glass.IsActive);
        }

        [Test]
        public void BackdropThatStoppedBeingProduced_GoesUnavailable()
        {
            GlassRuntime.PublishBackdropForTests(frame: 100);

            GlassRuntime.TickBackdropWatchdog(currentFrame: 103);

            Assert.IsFalse(UI.Glass.IsActive,
                "a backdrop nobody is refreshing is a frozen frame, not a backdrop");
        }

        [Test]
        public void ProductionResuming_MakesItAvailableAgain()
        {
            GlassRuntime.PublishBackdropForTests(frame: 100);
            GlassRuntime.TickBackdropWatchdog(currentFrame: 110);
            Assert.IsFalse(UI.Glass.IsActive);

            GlassRuntime.PublishBackdropForTests(frame: 111);
            Assert.IsTrue(UI.Glass.IsActive);
        }

        [Test]
        public void Watchdog_IsHarmlessWhenNothingWasEverPublished()
        {
            Assert.DoesNotThrow(() => GlassRuntime.TickBackdropWatchdog(currentFrame: 5));
            Assert.IsFalse(UI.Glass.IsActive);
        }
    }
}
