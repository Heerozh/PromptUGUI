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
    }
}
