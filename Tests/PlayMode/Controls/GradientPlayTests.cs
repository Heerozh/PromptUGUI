using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // PlayMode smoke: verifies the gradient BASE + solid hoverModulate premultiply path
    // survives a real frame loop (Awake, layout, mesh rebuild).  The EditMode suite already
    // covers the full state matrix; this test just confirms nothing breaks under Play.
    public class GradientPlayTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = true;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        [UnityTest]
        public IEnumerator GradientBtn_HasEnabledGradientTintAndIsClickable()
        {
            // Gradient BASE (#ffffff top, #000000 bottom) + solid hoverModulate — exercises
            // the gradient-base × modulate premultiply path in StateTintReactor.
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' color='#ffffff,#000000' hoverModulate='#808080'>Go</Btn>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            // Two frames: let Awake + layout settle.
            yield return null;
            yield return null;

            var btn = screen.Get<Btn>("b");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg, "Btn must have a background Image");

            // Gradient tint must be present and ENABLED (baseline = Normal state, gradient base).
            var tint = bg.GetComponent<GradientTint>();
            Assert.IsNotNull(tint, "GradientTint component must be present on Btn bg");
            Assert.IsTrue(tint.enabled, "GradientTint must be enabled at rest (Normal state)");

            // Verify the gradient stops match the authored values (#ffffff top, #000000 bottom).
            Assert.AreEqual(Color.white, tint.Top,
                "Top stop must be white (#ffffff) at rest");
            Assert.AreEqual(Color.black, tint.Bottom,
                "Bottom stop must be black (#000000) at rest");

            // Btn must be interactable and clickable.
            int clickCount = 0;
            btn.OnClick.Subscribe(_ => clickCount++);
            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(1, clickCount, "Btn.OnClick must fire when Button.onClick is invoked");
        }

        [UnityTest]
        public IEnumerator StopGradientImage_SurvivesFrames()
        {
            // The reflection recipe, end to end under a real frame loop: a flipped Image whose ramp
            // fades to nothing halfway down. The slicing path runs during the canvas rebuild, so a
            // pooled-list or ordering mistake would show up here and nowhere in EditMode.
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='g' width='64' height='64' color='#ffffff,#ffffff/0 50%' flip='y'/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            yield return null;
            yield return null;

            var img = screen.Get<PromptUGUI.Controls.Image>("g").GameObject.GetComponent<UnityImage>();
            var tint = img.GetComponent<GradientTint>();
            Assert.IsNotNull(tint);
            Assert.IsTrue(tint.enabled);
            Assert.IsTrue(tint.Spec.HasStops, "the stop must survive the frame loop");
            Assert.AreEqual(0.5f, tint.Spec.BottomStop, 1e-5f);

            var effects = img.GetComponents<UnityEngine.UI.BaseMeshEffect>();
            Assert.AreEqual(2, effects.Length);
            Assert.IsInstanceOf<RotateFlipEffect>(effects[0], "the flip has to run first");
            Assert.IsInstanceOf<GradientTint>(effects[1]);
        }
    }
}
