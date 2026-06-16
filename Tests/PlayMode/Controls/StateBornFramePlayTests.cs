using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // Born-frame instant rule over REAL frames (a tween only animates with a running player loop;
    // in EditMode LMotion / uGUI's TweenRunner complete immediately). The complementary born-frame
    // SNAP unit test lives in EditMode StateBornFrameTests.
    //
    // The disabled axis is exercised through a Btn with disabledModulate set: that keeps the
    // StateTintReactor driving the bg colour (a real ~0.1s LMotion fade), the path the born-frame
    // gate governs. A plain Btn's disabled look is the default grayscale material swap, which is
    // always instant and born-frame-independent, so it cannot exercise the snap-vs-fade gate.
    public class StateBornFramePlayTests
    {
        // base (#FFFFFF white) × #808080 multiplier = the disabled-modulate target.
        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // The ONLY thing allowed to make these transitions instant is the born-frame gate under
            // test — never the test-only force-instant escape hatch.
            StateTintReactor.TestForceInstant = false;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        [UnityTest]
        public IEnumerator Disable_in_born_frame_snaps_instantly()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b' disabledModulate='#808080'/></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var baseColor = bg.color;
            var disabled = baseColor * Half;

            // Disable in the SAME (born) frame as Open — before any yield. The born-frame gate must
            // coerce the reactor to snap, so frame 1 already shows the disabled modulate.
            btn.Interactable = false;

            AssertColorsEqual(disabled, bg.color,
                "born-frame disable must snap to the disabled modulate, not start a fade from the enabled colour");
            yield break;
        }

        [UnityTest]
        public IEnumerator Disable_after_born_frame_fades_over_frames()
        {
            // Regression guard: the born-frame gate must NOT turn off the intentional runtime fade on
            // later frames (e.g. a modal Configure hook that greys a button a few frames after Open).
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b' disabledModulate='#808080'/></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var baseColor = bg.color;
            var disabled = baseColor * Half;

            yield return null;   // advance PAST the born frame
            yield return null;

            btn.Interactable = false;   // later frame => should FADE, not snap

            // Immediately after the flip the fade has barely started: still near the enabled colour,
            // NOT already at disabled (which an over-broad "always instant" gate would wrongly do).
            var afterFlip = bg.color;
            Assert.That(ColorDistance(afterFlip, baseColor), Is.LessThan(ColorDistance(afterFlip, disabled)),
                "right after a later-frame disable the colour must still be closer to base than disabled (it animates)");

            // ...and it settles at the disabled modulate once the fade window elapses.
            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(disabled, bg.color,
                "later-frame disable should settle at the disabled modulate after the fade window");
        }

        private static float ColorDistance(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);

        private static void AssertColorsEqual(Color expected, Color actual, string msg)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), $"{msg} (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), $"{msg} (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), $"{msg} (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), $"{msg} (a)");
        }
    }
}
