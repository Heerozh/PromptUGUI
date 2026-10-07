using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PuiText = PromptUGUI.Controls.Text;
using ToggleControl = PromptUGUI.Controls.Toggle;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// The <c>*Modulate</c> channel over real frames — what EditMode, where nothing fades, cannot
    /// show: a colour written by code in the middle of a fade, the fade's clock, the Toggle check's
    /// own fade, and the default disabled grey reaching the glyphs TMP actually uploads.
    /// The synchronous contract is in EditMode <c>StateModulateChannelTests</c>.
    /// </summary>
    public class StateModulateChannelPlayTests
    {
        private const int Normal = 0;
        private const int Pressed = 2;
        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;   // the real fade is the point
            _timeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                + $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static Color Shown(Graphic g) => g.color * g.canvasRenderer.GetColor();

        private static void AssertColor(Color expected, Color actual, string because)
        {
            // 0.02: the fade has settled by then, but frame granularity makes a tighter bound fragile.
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), because + " (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), because + " (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), because + " (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), because + " (a)");
        }

        [UnityTest]
        public IEnumerator ATextColourWrittenMidFade_IsKept_AndTheFadeStillLands()
        {
            var screen = Open("<Btn id='b' width='80' height='30' pressedModulate='#808080'><Text id='t'>x</Text></Btn>");
            yield return null;   // past the born frame: a press now fades
            var btn = screen.Get<Btn>("b");
            var text = btn.Get<PuiText>("t");
            var tmp = text.GameObject.GetComponent<TMP_Text>();
            var pui = btn.GameObject.GetComponent<PuiButton>();

            pui.SimulateState(Pressed);
            yield return new WaitForSecondsRealtime(0.03f);   // mid-fade
            text.Color = "#FF0000";
            yield return new WaitForSecondsRealtime(0.2f);
            AssertColor(Color.red * Half, Shown(tmp), "pressed: the colour code wrote mid-fade, dimmed");

            pui.SimulateState(Normal);
            yield return new WaitForSecondsRealtime(0.2f);
            AssertColor(Color.red, Shown(tmp), "released: still that colour");
        }

        [UnityTest]
        public IEnumerator TheFade_RunsOnTheUiClock_EvenWhilePaused()
        {
            var screen = Open("<Btn id='b' width='80' height='30' pressedModulate='#808080'>x</Btn>");
            yield return null;
            var btn = screen.Get<Btn>("b");
            var pui = btn.GameObject.GetComponent<PuiButton>();
            var bg = pui.targetGraphic;
            var bgBase = Shown(bg);

            Time.timeScale = 0f;   // a pause menu
            pui.SimulateState(Pressed);
            yield return new WaitForSecondsRealtime(0.25f);

            AssertColor(bgBase * Half, Shown(bg), "a paused game still answers a press");
        }

        [UnityTest]
        public IEnumerator ToggleCheck_ShowsAtOnceWhenAuthoredOn_AndFadesInOnALaterCheck()
        {
            var screen = Open("<Toggle id='a' isOn='true'>a</Toggle><Toggle id='b'>b</Toggle>");
            var checkA = screen.Get<ToggleControl>("a").GameObject.transform.Find("Background/Checkmark")
                .GetComponent<CanvasGroup>();
            var b = screen.Get<ToggleControl>("b");
            var checkB = b.GameObject.transform.Find("Background/Checkmark").GetComponent<CanvasGroup>();

            Assert.AreEqual(1f, checkA.alpha, 1e-3f, "authored isOn: shown from the first frame, no fade-in");
            Assert.AreEqual(0f, checkB.alpha, 1e-3f, "authored off: hidden");

            yield return null;
            b.IsOn = true;
            Assert.That(checkB.alpha, Is.LessThan(0.5f), "a later check fades in rather than snapping");
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(1f, checkB.alpha, 1e-3f, "…and lands fully shown");

            b.IsOn = false;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(0f, checkB.alpha, 1e-3f, "unchecked: fades back out");
        }

        [UnityTest]
        public IEnumerator DefaultGrey_ReachesTheUploadedGlyphs_AndTheTextKeepsItsColour()
        {
            var screen = Open("<Btn id='b' width='80' height='30'><Text id='t'>Hi</Text></Btn>");
            yield return null;
            var btn = screen.Get<Btn>("b");
            var text = btn.Get<PuiText>("t");
            var tmp = text.GameObject.GetComponent<TMP_Text>();

            text.Color = "#FF0000";
            Canvas.ForceUpdateCanvases();   // the regular rebuild path, not ForceMeshUpdate
            var lit = tmp.mesh.colors32[0];

            btn.Interactable = false;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(DisabledGrayscaleController.Desaturate(lit), tmp.mesh.colors32[0],
                "disabled: the uploaded glyphs are grey");
            AssertColor(Color.red, tmp.color, "…and the text's own colour is untouched");

            btn.Interactable = true;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(lit, tmp.mesh.colors32[0], "re-enabled: lit again");
            yield return null;
        }
    }
}
