using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// The cooldown clock on real frames (spec 2026-10-13 §5.6): it hangs off
    /// <c>Canvas.willRenderCanvases</c>, so it has to keep firing when the cooldown's whole Canvas
    /// is switched off — the case EditMode cannot show.
    /// </summary>
    public class CooldownPlayTests
    {
        private const string Doc = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>" +
            "<Cooldown id='c' anchor='center' size='64x64' countdown='true'/>" +
            "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator ACooldown_RunsOutOnRealFrames()
        {
            UI.LoadDocument("t", Doc);
            var c = UI.Open("S").Get<Cooldown>("c");
            var ready = 0;
            c.OnReady.Subscribe(_ => ready++);

            c.Start(0.2f);
            yield return new WaitForSeconds(0.05f);
            Assert.Less(c.Value, 1f, "the sweep moves on its own");
            Assert.Greater(c.Value, 0f);
            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(1, ready);
            Assert.AreEqual(0f, c.Value);
        }

        [UnityTest]
        public IEnumerator ACooldownUnderAnInactiveCanvas_StillGoesReady()
        {
            UI.LoadDocument("t", Doc);
            var screen = UI.Open("S");
            var c = screen.Get<Cooldown>("c");
            var ready = 0;
            c.OnReady.Subscribe(_ => ready++);

            c.Start(0.2f);
            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.gameObject.SetActive(false);
            Assume.That(Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None),
                Is.Empty, "guard: no active canvas left anywhere");

            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, ready, "a hidden skill bar still knows when its cooldowns run out");

            canvas.gameObject.SetActive(true);
        }

        [UnityTest]
        public IEnumerator ScaledTime_StandsStillWhileTheGameIsPaused()
        {
            UI.LoadDocument("t", Doc);
            var c = UI.Open("S").Get<Cooldown>("c");
            var previous = Time.timeScale;
            try
            {
                c.Start(0.2f);
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.AreEqual(1f, c.Value, 0.05f, "time='scaled' (the default) pauses with the game");
            }
            finally
            {
                Time.timeScale = previous;
            }
        }
    }
}
