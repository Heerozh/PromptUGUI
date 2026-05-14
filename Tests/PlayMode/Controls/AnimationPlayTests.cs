using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using Animation = PromptUGUI.Controls.Animation;  // disambiguates UnityEngine.Animation

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class AnimationPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Fade_low_level_reaches_to_value()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' fade='0:1' duration='0.1s'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");

            // Wait for the motion to complete; LitMotion runs on Update so a few frames suffice.
            yield return new WaitForSeconds(0.2f);

            var cg = anim.GameObject.GetComponent<CanvasGroup>();
            Assert.IsNotNull(cg, "Animation must have a CanvasGroup for fade");
            Assert.AreEqual(1f, cg.alpha, 0.01f);
        }

        [UnityTest]
        public IEnumerator Translate_low_level_reaches_to_offset()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' translate='0,-50:0,0' duration='0.1s'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            yield return new WaitForSeconds(0.2f);

            var proxy = (RectTransform)anim.GameObject.transform.Find("_offsetProxy");
            Assert.AreEqual(Vector2.zero, proxy.anchoredPosition, "Translate must end at 0,0");
        }

        [UnityTest]
        public IEnumerator Scale_low_level_reaches_to_value()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' scale='0.5:1' duration='0.1s'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            yield return new WaitForSeconds(0.2f);

            var proxy = (RectTransform)anim.GameObject.transform.Find("_offsetProxy");
            Assert.AreEqual(Vector3.one, proxy.localScale);
        }

        [UnityTest]
        public IEnumerator Preset_fadein_completes_to_alpha_1()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' type='fadein' duration='0.1s'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            yield return new WaitForSeconds(0.2f);
            var cg = screen.Get<Animation>("a").GameObject.GetComponent<CanvasGroup>();
            Assert.AreEqual(1f, cg.alpha, 0.01f);
        }

        [UnityTest]
        public IEnumerator Preset_slidein_left_ends_at_origin()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' type='slidein-left' duration='0.1s'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            yield return new WaitForSeconds(0.2f);
            var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");
            Assert.AreEqual(Vector2.zero, proxy.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator On_loop_pulse_oscillates_scale()
        {
            // pulse animates scale 1.0→1.05 and on="loop" implies yoyo (infinite).
            // With yoyo, after one full duration (the "to" end) the motion reverses.
            // A non-looping animation would freeze at 1.05 after duration; a yoyo loops back to 1.0.
            // Strategy: wait 3× the duration so we are well into multiple cycles.
            // At that point, if yoyo is running the scale must be somewhere in [1.0, 1.05] and NOT
            // permanently stuck at exactly the "to" value of 1.05.
            // We sample at 1.5× duration (guaranteed mid-reverse: scale strictly between 1.0 and 1.05)
            // and verify it is LESS than the peak (proving reverse happened).
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' type='pulse' on='loop' duration='0.1s' easing='linear'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");

            // Wait to the peak (end of forward pass)
            yield return new WaitForSeconds(0.1f);
            var sPeak = proxy.localScale.x;

            // Wait another half-duration into the reverse pass
            yield return new WaitForSeconds(0.05f);
            var sMidReverse = proxy.localScale.x;

            // sPeak should be ~1.05 (the "to" value); sMidReverse should be ~1.025 (halfway back).
            // sMidReverse must be strictly less than sPeak — proving the yoyo reversed.
            Assert.IsTrue(sPeak > 1.001f, $"scale at peak ({sPeak}) must be above 1.0");
            Assert.IsTrue(sMidReverse < sPeak - 0.005f,
                $"scale mid-reverse ({sMidReverse}) must be less than peak ({sPeak}) — yoyo must reverse");
        }

        [UnityTest]
        public IEnumerator Loop_count_3_runs_three_times_then_stops()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' translate='0,0:50,0' duration='0.05s' loop='count:3' on='open'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");
            yield return new WaitForSeconds(0.05f * 3 + 0.05f);  // 3 loops + grace
            // After 3 loops with Restart mode, position is at "to" (50,0)
            Assert.AreEqual(new Vector2(50, 0), proxy.anchoredPosition);
        }

        [UnityTest]
        public IEnumerator Count_animation_writes_final_value_to_Text()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' count='0:1000' format='{0:F0}' duration='0.1s'><Text id='label'>0</Text></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            yield return new WaitForSeconds(0.2f);
            var label = screen.Get<Text>("a/label");
            Assert.AreEqual("1000", label.GameObject.GetComponent<TMPro.TMP_Text>().text);
        }

        [UnityTest]
        public IEnumerator Count_with_target_refs_screen_scope_Text()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Text id='score'>0</Text>" +
                "<Animation id='a' count='0:500' format='{0:F0}' target='@score' duration='0.1s' on='open'/>" +
                $"{Footer}");
            var screen = UI.Open("S");
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual("500", screen.Get<Text>("score").GameObject.GetComponent<TMPro.TMP_Text>().text);
        }

        [UnityTest]
        public IEnumerator CharColor_zero_stagger_all_chars_reach_to_color()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' char-color='1,1,1,1:1,0,0,1' duration='0.1s'><Text id='label'>ABC</Text></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var tmp = screen.Get<Text>("a/label").GameObject.GetComponent<TMPro.TMP_Text>();
            tmp.ForceMeshUpdate();
            yield return new WaitForSeconds(0.2f);

            // After motion, LitMotion has written the final Color (red) to meshInfo.colors32
            // via TMP's UpdateVertexData. Do NOT call ForceMeshUpdate() here — it would
            // regenerate geometry and reset vertex colors to their defaults.
            for (int i = 0; i < 3; i++)
            {
                var c = tmp.textInfo.characterInfo[i];
                if (!c.isVisible) continue;
                var mi = c.materialReferenceIndex;
                var vi = c.vertexIndex;
                var color = tmp.textInfo.meshInfo[mi].colors32[vi];
                Assert.AreEqual(255, color.r);
                Assert.AreEqual(0, color.g);
                Assert.AreEqual(0, color.b);
            }
        }
    }
}
