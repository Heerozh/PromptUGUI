using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using Animation = PromptUGUI.Controls.Animation;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class AnimationTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Animation_creates_inner_offset_proxy()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' on='manual'><Text id='label'>hi</Text></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            Assert.IsNotNull(anim);
            var proxy = anim.GameObject.transform.Find("_offsetProxy");
            Assert.IsNotNull(proxy, "Animation must create _offsetProxy child GameObject");
            var rt = (RectTransform)proxy;
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(Vector2.zero, rt.offsetMin);
            Assert.AreEqual(Vector2.zero, rt.offsetMax);
        }

        [Test]
        public void Animation_children_parented_to_offset_proxy()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' on='manual'><Text id='label'>hi</Text></Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var label = screen.Get<Text>("a/label");
            Assert.AreEqual(
                anim.GameObject.transform.Find("_offsetProxy"),
                label.RectTransform.parent,
                "Text must be parented to _offsetProxy, not Animation root");
        }

        [Test]
        public void Animation_OnAfterApply_idempotent_when_attrs_unchanged()
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?>" +
                "<PromptUGUI version='1'><Screen name='S'>" +
                "<Animation id='a' fade='0:1' duration='0.3s' on='manual'><Frame id='f'/></Animation>" +
                "</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            // Manually trigger ReSolve via VariantStore (no actual change to spec)
            // If snapshot equality works, repeated ReSolve + Fire is harmless.
            Assert.DoesNotThrow(() => { anim.Fire(); screen.ReSolve(); anim.Fire(); });
        }
    }
}
