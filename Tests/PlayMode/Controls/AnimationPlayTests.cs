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
            // pulse animates scale 1.0→1.05; on="loop" implies yoyo (infinite back-and-forth).
            // Sampling at two wall-clock instants (the old approach) is fragile: under a coarse or
            // unfocused editor frame cadence both samples can land on the same yoyo phase, so the
            // "reverse happened" inequality fails even though the animation is fine (historical flake
            // — both samples read 1.0333). Instead watch the scale across many frames over several
            // full cycles (no wall-clock-to-phase assumption) and assert it genuinely oscillates.
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' type='pulse' on='loop' duration='0.1s' easing='linear'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");

            // Sample every frame for ≥3 full cycles (cycle = 2×duration = 0.2s) AND ≥30 frames, so the
            // window holds many distinct phases regardless of per-frame dt (coarse or fine).
            float min = float.MaxValue, max = float.MinValue;
            float elapsed = 0f;
            int frames = 0;
            while (elapsed < 0.6f || frames < 30)
            {
                float s = proxy.localScale.x;
                min = Mathf.Min(min, s);
                max = Mathf.Max(max, s);
                elapsed += Time.deltaTime;
                frames++;
                yield return null;
            }

            // Rose toward the 1.05 peak (it animates up)...
            Assert.IsTrue(max > 1.03f, $"peak scale seen ({max}) must approach the 1.05 pulse target");
            // ...and came back down by a meaningful amount (the yoyo reversed — not frozen at the peak).
            Assert.IsTrue(max - min > 0.02f,
                $"scale must oscillate: span max({max}) - min({min}) too small — yoyo not reversing");
        }

        [UnityTest]
        public IEnumerator Loop_count_3_runs_three_times_then_stops()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' translate='0,0:50,0' duration='0.05s' loop='count:3' on='open'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var proxy = (RectTransform)screen.Get<Animation>("a").GameObject.transform.Find("_offsetProxy");
            yield return new WaitForSeconds(0.05f * 3 + 0.15f);  // 3 loops + grace (tripled for slow CI)
            // After 3 loops with Restart mode, position is at "to" (50,0).
            // Use component-wise tolerance — NUnit's Vector2 equality is exact-bit, but
            // LitMotion ends within float-epsilon of the to value, not bit-identical.
            Assert.AreEqual(50f, proxy.anchoredPosition.x, 0.01f);
            Assert.AreEqual(0f, proxy.anchoredPosition.y, 0.01f);
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
        public IEnumerator Fade_motion_stops_when_target_destroyed_outside_close()
        {
            // Repro for the scene-unload crash: MissingReferenceException on CanvasGroup.set_alpha.
            // Animation is a plain C# object (not a MonoBehaviour) — when its GameObject is destroyed
            // OUTSIDE the Screen.Close()/Dispose() path (e.g. an async scene load aborted half-way, or
            // a scene unloaded), CancelCurrent() never runs. Without binding the motion's lifetime to
            // the target GameObject, the LitMotion fade handle keeps ticking in the global
            // MotionDispatcher and writes alpha to the destroyed CanvasGroup on the next frame; LitMotion
            // surfaces that via Debug.LogException, which the Unity Test Framework treats as a failure.
            // The .AddTo lifetime link makes the linker's OnDestroy cancel the handle first → no tick.
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' fade='0:1' duration='10s' on='open'><Frame id='f'/></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var go = screen.Get<Animation>("a").GameObject;

            // Let the motion tick at least one frame so it is genuinely live.
            yield return null;

            // Simulate destruction that bypasses Screen.Close() (e.g. SceneManager.UnloadScene).
            Object.Destroy(go);

            // Pump several frames so LitMotion's update runner would tick the orphaned motion.
            // If the handle survived destruction it would log a MissingReferenceException here.
            yield return null;
            yield return null;
            yield return null;
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
                "<Animation id='a' char-color='#ffffff:#ff0000' duration='0.1s'><Text id='label'>ABC</Text></Animation>" +
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
