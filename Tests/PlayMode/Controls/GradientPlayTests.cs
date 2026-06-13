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
    }
}
