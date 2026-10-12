using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Cooldown&gt;</c>'s clock (spec 2026-10-13 §5.6–§5.7): timestamps on a pinned clock,
    /// the ready / cooling edges from every path, hidden cooldowns that keep counting, the
    /// <c>time=</c> switch, ReSolve, and the countdown text.
    /// </summary>
    public class CooldownClockTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Cooldown Load(string attrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Cooldown id='c' anchor='center' size='64x64' {attrs}/>
</Screen></PromptUGUI>");
            return UI.Open("S").Get<Cooldown>("c");
        }

        private static void At(float seconds)
        {
            CooldownClock.SetTimeForTests(seconds);
            CooldownClock.TickForTests();
        }

        private static string LabelText(Cooldown c) =>
            c.GameObject.transform.Find("Label").GetComponent<TMP_Text>().text;

        private sealed class Edges
        {
            public int Ready, Cooling;
            public Edges(Cooldown c)
            {
                c.OnReady.Subscribe(_ => Ready++);
                c.OnCooling.Subscribe(_ => Cooling++);
            }
        }

        // ───── the sweep ─────

        [Test]
        public void Start_SweepsWithTheClock()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            c.Start(8f);

            Assert.IsTrue(c.IsRunning);
            Assert.IsTrue(c.IsCooling);
            Assert.AreEqual(1f, c.Value, 1e-5f);
            Assert.AreEqual(8f, c.Total);

            At(102f);
            Assert.AreEqual(0.75f, c.Value, 1e-5f);
            Assert.AreEqual(6f, c.Remaining, 1e-4f);
        }

        [Test]
        public void RunningOut_GoesReadyExactlyOnce()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);
            c.Start(8f);
            Assert.AreEqual(1, edges.Cooling);

            At(108f);
            Assert.AreEqual(0f, c.Value);
            Assert.IsFalse(c.IsRunning);
            Assert.IsFalse(c.IsCooling);
            Assert.AreEqual(1, edges.Ready);
            Assert.AreEqual(0, CooldownClock.RunningCount, "a finished cooldown leaves the clock");

            At(120f);
            Assert.AreEqual(1, edges.Ready, "nothing more after the edge");
        }

        [Test]
        public void StartMidway_ReTimesWithoutRestarting()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);

            c.Start(8f, 3.2f);
            Assert.AreEqual(0.4f, c.Value, 1e-5f, "a reconnect picks the cooldown up half-way");

            c.Start(8f, 6f);
            Assert.AreEqual(0.75f, c.Value, 1e-5f, "a server sync re-times it");
            Assert.AreEqual(1, edges.Cooling, "…without saying cooling again");

            c.Start(8f, 20f);
            Assert.AreEqual(1f, c.Value, "more than the total left is clamped to the total");
        }

        [TestCase(8f, 0f)]
        [TestCase(8f, -1f)]
        [TestCase(0f, 3f)]
        [TestCase(float.NaN, 3f)]
        public void StartWithNothingLeft_IsStop(float total, float remaining)
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);
            c.Start(10f);

            c.Start(total, remaining);
            Assert.IsFalse(c.IsRunning);
            Assert.AreEqual(0f, c.Value);
            Assert.AreEqual(1, edges.Ready);
        }

        [Test]
        public void Stop_IsReadyNow_OnceOnly()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);

            c.Stop();
            Assert.AreEqual(0, edges.Ready, "stopping a ready cooldown is not an edge");

            c.Start(5f);
            c.Stop();
            c.Stop();
            Assert.AreEqual(1, edges.Ready);
            Assert.AreEqual(0, CooldownClock.RunningCount);
        }

        [Test]
        public void APushedValue_StopsTheClock_AndStillHasEdges()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);

            c.Value = 0.4f;
            Assert.AreEqual(1, edges.Cooling, "a server-pushed fraction can drive on=\"cooling\" too");
            Assert.IsFalse(c.IsRunning);
            Assert.AreEqual(0f, c.Total, "a pushed fraction has no seconds");
            Assert.AreEqual(0f, c.Remaining);

            c.Start(10f);
            c.Value = 0.5f;
            Assert.IsFalse(c.IsRunning, "pushing a value takes over from the clock");
            At(200f);
            Assert.AreEqual(0.5f, c.Value, "…and the clock no longer moves it");

            c.Value = 0f;
            Assert.AreEqual(1, edges.Ready);
        }

        // ───── time= ─────

        [Test]
        public void Scaled_IsTheDefaultClock()
        {
            CooldownClock.SetTimeForTests(100f, 500f);
            var c = Load("");
            c.Start(10f);

            CooldownClock.SetTimeForTests(100f, 505f);
            CooldownClock.TickForTests();
            Assert.AreEqual(1f, c.Value, 1e-5f, "a paused game (scaled time standing still) pauses the cooldown");

            CooldownClock.SetTimeForTests(105f, 505f);
            CooldownClock.TickForTests();
            Assert.AreEqual(0.5f, c.Value, 1e-5f);
        }

        [Test]
        public void Unscaled_RunsOnRealTime()
        {
            CooldownClock.SetTimeForTests(100f, 500f);
            var c = Load("time='unscaled'");
            c.Start(10f);

            CooldownClock.SetTimeForTests(100f, 505f);
            CooldownClock.TickForTests();
            Assert.AreEqual(0.5f, c.Value, 1e-5f);
        }

        [Test]
        public void SwitchingClocksMidRun_KeepsTheSecondsLeft()
        {
            CooldownClock.SetTimeForTests(100f, 500f);
            var c = Load("time.alt='unscaled'");
            c.Start(10f);

            CooldownClock.SetTimeForTests(102f, 500f);
            CooldownClock.TickForTests();
            UI.Variants.Set("alt", true);
            Assert.AreEqual(8f, c.Remaining, 1e-4f, "the 8 s left carry over to the other clock");

            CooldownClock.SetTimeForTests(102f, 502f);
            CooldownClock.TickForTests();
            Assert.AreEqual(0.6f, c.Value, 1e-5f);
        }

        // ───── hidden, closed, ReSolve ─────

        [Test]
        public void AHiddenCooldown_KeepsCounting_AndGoesReadyOnTime()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);
            c.Start(4f);
            c.GameObject.SetActive(false);

            At(104f);
            Assert.AreEqual(1, edges.Ready, "the skill bar does not stop cooling because its page is hidden");

            c.GameObject.SetActive(true);
            var panel = c.GameObject.transform.Find("Fill/" + ProceduralSurface.NodeName).GetComponent<ProceduralPanel>();
            Assert.IsFalse(panel.IsPanelVisible, "shown again, it draws the ready state");
        }

        [Test]
        public void ClosingTheScreen_TakesTheCooldownOffTheClock()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            var edges = new Edges(c);
            c.Start(4f);

            UI.Close("S");
            Assert.AreEqual(0, CooldownClock.RunningCount);
            Assert.DoesNotThrow(() => At(110f));
            Assert.AreEqual(0, edges.Ready, "a closed Screen's cooldown never says ready");
        }

        [Test]
        public void AVariantReSolve_NeverStopsARunningCooldown()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("value='0' fillColor='#ff0000' fillColor.alt='#00ff00'");
            c.Start(10f);

            UI.Variants.Set("alt", true);
            Assert.IsTrue(c.IsRunning, "not even in the frame it started, when the fraction is exactly 1");
            Assert.AreEqual(1f, c.Value, 1e-5f);
        }

        [Test]
        public void TheClock_TicksFromTheCanvasRenderHook()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("");
            c.Start(10f);

            CooldownClock.SetTimeForTests(105f);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(0.5f, c.Value, 1e-5f);
        }

        // ───── countdown ─────

        [TestCase(7.2f, 1f, "8")]
        [TestCase(7f, 1f, "7")]
        [TestCase(0.43f, 1f, "0.5")]
        [TestCase(0.4f, 1f, "0.4")]
        [TestCase(0.01f, 1f, "0.1")]
        [TestCase(0.00001f, 1f, "0.1")]
        [TestCase(0.43f, 0f, "1")]
        [TestCase(2.5f, 3f, "2.5")]
        [TestCase(59.2f, 1f, "1:00")]
        [TestCase(89.4f, 1f, "1:30")]
        [TestCase(600f, 1f, "10:00")]
        [TestCase(0f, 1f, "")]
        public void FormatRemaining_RoundsUp(float seconds, float tenthsBelow, string expected)
        {
            Assert.AreEqual(expected, Cooldown.FormatRemaining(seconds, tenthsBelow));
        }

        [Test]
        public void TheCountdown_ShowsWhileTheClockRuns()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("countdown='true'");
            var label = c.GameObject.transform.Find("Label");
            Assert.IsTrue(label.gameObject.activeSelf);
            Assert.AreEqual("", LabelText(c), "ready: no number");

            c.Start(8f);
            Assert.AreEqual("8", LabelText(c));
            At(100.5f);
            Assert.AreEqual("8", LabelText(c));
            At(101.2f);
            Assert.AreEqual("7", LabelText(c));
            At(107.6f);
            Assert.AreEqual("0.4", LabelText(c));
            At(108f);
            Assert.AreEqual("", LabelText(c), "0 is ready, not a number");

            c.Value = 0.5f;
            Assert.AreEqual("", LabelText(c), "a pushed fraction has no seconds to show");
        }

        [Test]
        public void TheCountdown_IsWrittenOnlyWhenTheTextChanges()
        {
            CooldownClock.SetTimeForTests(100f);
            var c = Load("countdown='true'");
            c.Start(8f);
            var writes = c.LabelWritesForTests;

            for (var t = 100.1f; t < 100.95f; t += 0.1f) At(t);
            Assert.AreEqual(writes, c.LabelWritesForTests, "nine frames inside one second, no TMP rebuild");
            At(101.1f);
            Assert.AreEqual(writes + 1, c.LabelWritesForTests);
        }

        [Test]
        public void TheCountdown_IsOffByDefault_AndTakesAFormatter()
        {
            CooldownClock.SetTimeForTests(100f);
            var off = Load("");
            Assert.IsFalse(off.GameObject.transform.Find("Label").gameObject.activeSelf);
            off.Start(8f);
            Assert.AreEqual("", LabelText(off), "no countdown asked for, no text written");

            var c = Load("countdown='true'");
            c.CountdownFormatter = s => $"{Mathf.CeilToInt(s)}s";
            c.Start(8f);
            Assert.AreEqual("8s", LabelText(c));
            c.CountdownFormatter = null;
            Assert.AreEqual("8", LabelText(c));
        }

        [Test]
        public void TheCountdownLabel_DefaultsToWhite24()
        {
            var c = Load("countdown='true'");
            var tmp = c.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(24f, tmp.fontSize);
            Assert.AreEqual(Color.white, tmp.color);
            Assert.IsFalse(tmp.raycastTarget);

            var styled = Load("countdown='true' fontSize='18' textColor='#ff0000'");
            var t2 = styled.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(18f, t2.fontSize);
            Assert.AreEqual(Color.red, t2.color);
        }
    }
}
