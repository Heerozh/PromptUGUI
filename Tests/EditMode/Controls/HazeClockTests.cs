using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The fog clock (spec 2026-09-17 haze §5.5): one global unscaled time the drifting materials
    /// read, published once per canvas render. Only a drifting material needs it, tests can pin it,
    /// and a reset leaves nothing subscribed.
    /// </summary>
    public class HazeClockTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Frame Load(string frameAttrs)
        {
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' {frameAttrs}/>
</Screen></PromptUGUI>");
            return UI.Open("S").Get<Frame>("f");
        }

        [Test]
        public void Reset_LeavesTheClockStoppedAtZero()
        {
            Assert.IsFalse(HazeClock.IsRunning);
            Assert.AreEqual(0f, Shader.GetGlobalFloat(HazeClock.TimeId), 0.0001f);
        }

        [Test]
        public void StillFog_DoesNotStartTheClock()
        {
            // Nothing reads the time while hazeDrift is 0, so nothing is subscribed per frame.
            Load("color='#222' haze='40'");
            Assert.IsFalse(HazeClock.IsRunning);
        }

        [Test]
        public void DriftingFog_StartsTheClock_Once()
        {
            Load("color='#222' haze='40' hazeDrift='6'");
            Assert.IsTrue(HazeClock.IsRunning);
            var before = HazeClock.SubscriptionsForTests;
            HazeClock.Ensure();
            HazeClock.Ensure();
            Assert.AreEqual(before, HazeClock.SubscriptionsForTests, "Ensure is idempotent");
        }

        [Test]
        public void Tick_PublishesTheUnscaledTime()
        {
            HazeClock.Ensure();
            Canvas.ForceUpdateCanvases();   // fires willRenderCanvases, which is the tick
            Assert.Greater(Shader.GetGlobalFloat(HazeClock.TimeId), 0f);
        }

        [Test]
        public void PinnedTime_SurvivesATick()
        {
            // A render test needs a stable time between two snapshots; a tick must not overwrite it.
            HazeClock.Ensure();
            HazeClock.SetTimeForTests(3f);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(3f, Shader.GetGlobalFloat(HazeClock.TimeId), 0.0001f);
        }

        [Test]
        public void Reset_UnsubscribesAndUnpins()
        {
            HazeClock.Ensure();
            HazeClock.SetTimeForTests(3f);
            UI.ResetForTests();
            Assert.IsFalse(HazeClock.IsRunning);
            Assert.AreEqual(0f, Shader.GetGlobalFloat(HazeClock.TimeId), 0.0001f);
            HazeClock.Ensure();
            Canvas.ForceUpdateCanvases();
            Assert.AreNotEqual(3f, Shader.GetGlobalFloat(HazeClock.TimeId), "the pin is gone after a reset");
        }
    }
}
