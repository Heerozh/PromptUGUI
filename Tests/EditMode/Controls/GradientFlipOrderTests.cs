using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A gradient is evaluated on the mesh AS DRAWN, whatever order the attributes were written in
    /// (spec 2026-09-01 VGS §4.4, VGS-D3). Both are <c>BaseMeshEffect</c>s and uGUI runs those in
    /// component-add order, which otherwise follows the attribute setters — so
    /// <c>&lt;Icon color flip&gt;</c> and <c>&lt;Icon flip color&gt;</c> would paint upside down
    /// from each other, and the reflection recipe would be a coin toss.
    /// </summary>
    public class GradientFlipOrderTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        private static List<Type> EffectOrder(PromptUGUI.Application.Screen s, string id)
        {
            var types = new List<Type>();
            foreach (var fx in s.Get<PromptUGUI.Controls.IControl>(id).GameObject.GetComponents<BaseMeshEffect>())
                types.Add(fx.GetType());
            return types;
        }

        private static void AssertCanonical(PromptUGUI.Application.Screen s, string id)
        {
            var order = EffectOrder(s, id);
            Assert.AreEqual(2, order.Count, "expected exactly the flip and the tint, got: " + string.Join(", ", order));
            Assert.AreEqual(typeof(RotateFlipEffect), order[0], "the flip has to run first");
            Assert.AreEqual(typeof(GradientTint), order[1], "the gradient reads the flipped mesh");
        }

        [Test]
        public void ColorThenFlip_EffectsInCanonicalOrder()
        {
            var s = Open("<Image id='g' size='40x40' color='#fff,#000' flip='y'/>");
            AssertCanonical(s, "g");
        }

        [Test]
        public void FlipThenColor_SameOrder()
        {
            var s = Open("<Image id='g' size='40x40' flip='y' color='#fff,#000'/>");
            AssertCanonical(s, "g");
        }

        [Test]
        public void Icon_FollowsTheSameRule()
        {
            var stub = UnityEngine.Sprite.Create(
                UnityEngine.Texture2D.whiteTexture, new UnityEngine.Rect(0, 0, 1, 1), UnityEngine.Vector2.zero);
            UI.SpriteResolver = _ => stub;

            var s = Open("<Icon id='g' name='ui:x' size='40x40' color='#fff,#000' flip='y'/>");
            AssertCanonical(s, "g");
        }

        [Test]
        public void RawImage_FollowsTheSameRule()
        {
            var s = Open("<RawImage id='g' size='40x40' color='#fff,#000' flip='y'/>");
            AssertCanonical(s, "g");
        }

        [Test]
        public void Rotation_CountsToo()
        {
            var s = Open("<Image id='g' size='40x40' color='#fff,#000' rotation='90'/>");
            AssertCanonical(s, "g");
        }

        [Test]
        public void Variant_TurningSolidIntoAGradient_StillOrdered()
        {
            var s = Open("<Image id='g' size='40x40' flip='y' color='#fff' color.big='#fff,#000'/>");
            UI.Variants.Set("big", true);
            AssertCanonical(s, "g");
        }

        [Test]
        public void SolidColour_ReservesNothing()
        {
            // The identity promise: a node that asked for neither pays for neither.
            var s = Open("<Image id='g' size='40x40' color='#fff'/>");
            Assert.IsEmpty(EffectOrder(s, "g"));
        }

        [Test]
        public void GradientWithoutFlip_ReservesTheSlotButLeavesItOff()
        {
            var s = Open("<Image id='g' size='40x40' color='#fff,#000'/>");

            var order = EffectOrder(s, "g");
            Assert.AreEqual(typeof(RotateFlipEffect), order[0]);
            var fx = s.Get<PromptUGUI.Controls.IControl>("g").GameObject.GetComponent<RotateFlipEffect>();
            Assert.IsFalse(fx.enabled, "a reserved slot must not cost a mesh pass");
        }
    }
}
