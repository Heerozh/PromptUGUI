using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using Animation = PromptUGUI.Controls.Animation;  // disambiguates UnityEngine.Animation
using Screen = PromptUGUI.Application.Screen;      // disambiguates UnityEngine.Screen

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// LitMotion charges a motion's first tick with the whole of the frame it was scheduled in
    /// (MotionUpdateJob: Scheduled → time += deltaTime). For an <c>on="open"</c> entrance that
    /// frame is the build frame — 100–333 ms in the Editor — so the animation used to skip its
    /// first 15–100 %. Motions scheduled while <c>Screen.Open()</c> is still building are held
    /// through that one tick and released on the next frame.
    ///
    /// <para>Every test uses a linear 1 s fade, so the CanvasGroup alpha reads directly as the
    /// number of seconds LitMotion has charged to the motion.</para>
    /// </summary>
    public class AnimationOpenFramePlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private const string LinearFade =
            "<Animation id='a' fade='0:1' duration='1s' easing='linear'><Frame id='f'/></Animation>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        /// <summary>Burns wall-clock inside the current frame — stands in for an expensive build.</summary>
        private static void Spin(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { }
        }

        private static CanvasGroup AlphaOf(Screen screen)
            => screen.Get<Animation>("a").GameObject.GetComponent<CanvasGroup>();

        [UnityTest]
        public IEnumerator Build_frame_is_not_charged_to_an_on_open_animation()
        {
            yield return null;
            UI.LoadDocument("t", Header + LinearFade + Footer);
            var cg = AlphaOf(UI.Open("S"));
            Assert.AreEqual(0f, cg.alpha, "the from state is written on the build frame itself");

            Spin(200);
            yield return null;
            Assert.Less(cg.alpha, 0.05f,
                $"the 200 ms build frame must not be charged to the entrance (alpha={cg.alpha:F3})");

            yield return null;
            Assert.Greater(cg.alpha, 0f, "and it does start once a normal frame comes around");
            Assert.Less(cg.alpha, 0.05f, "charged only that normal frame's deltaTime");
        }

        [UnityTest]
        public IEnumerator Manual_fire_after_open_is_not_held()
        {
            UI.LoadDocument("t", Header +
                "<Animation id='a' on='manual' fade='0:1' duration='1s' easing='linear'><Frame id='f'/></Animation>" +
                Footer);
            var screen = UI.Open("S");
            yield return null;
            yield return null;

            screen.Get<Animation>("a").Fire();
            yield return null;
            // The interactive path keeps today's timing: it is moving on the very next frame.
            Assert.Greater(AlphaOf(screen).alpha, 0f, "a fire outside Open() must not gain a frame of latency");
        }

        [UnityTest]
        public IEnumerator Close_in_the_build_frame_leaves_no_error_next_frame()
        {
            UI.LoadDocument("t", Header + LinearFade + Footer);
            UI.Open("S");
            UI.Close("S");   // the held motion is cancelled before it is ever released
            yield return null;
            yield return null;
            // The test runner fails on any unexpected error log — nothing else to assert.
        }

        [UnityTest]
        public IEnumerator Count_animation_is_held_too()
        {
            yield return null;
            UI.LoadDocument("t", Header +
                "<Animation id='a' count='0:100' format='{0:F0}' duration='1s' easing='linear'>" +
                "<Text id='label'>0</Text></Animation>" + Footer);
            var screen = UI.Open("S");
            var tmp = screen.Get<Text>("a/label").GameObject.GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual("0", tmp.text, "ImmediateBind writes the from value on the build frame");

            Spin(200);
            yield return null;
            Assert.AreEqual("0", tmp.text, "the build frame must not push the counter ahead");
        }
    }
}
