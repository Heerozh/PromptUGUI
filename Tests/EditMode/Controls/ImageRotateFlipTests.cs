using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>rotation</c> / <c>flip</c> as authored attributes on <c>&lt;Image&gt;</c> /
    /// <c>&lt;Icon&gt;</c> / <c>&lt;RawImage&gt;</c> (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §3.3 / §3.4). The geometry itself is covered by
    /// <see cref="RotateFlipEffectTests"/>; this is about the attribute landing, the layout staying
    /// untouched, and Variant round-trips.
    /// </summary>
    public class ImageRotateFlipTests
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

        private static RotateFlipEffect EffectOn(PromptUGUI.Controls.IControl c)
            => c.GameObject.GetComponent<RotateFlipEffect>();

        [Test]
        public void Rotation_lands_on_the_effect()
        {
            var s = Open("<Image id='i' size='40x40' rotation='90'/>");
            var fx = EffectOn(s.Get<PromptUGUI.Controls.Image>("i"));

            Assert.IsNotNull(fx);
            Assert.IsTrue(fx.enabled);
            Assert.AreEqual(90f, fx.Rotation, 0.001f);
            Assert.IsFalse(fx.FlipX);
            Assert.IsFalse(fx.FlipY);
        }

        [TestCase("x", true, false)]
        [TestCase("y", false, true)]
        [TestCase("xy", true, true)]
        public void Flip_lands_on_the_effect(string flip, bool x, bool y)
        {
            var s = Open($"<Image id='i' size='40x40' flip='{flip}'/>");
            var fx = EffectOn(s.Get<PromptUGUI.Controls.Image>("i"));

            Assert.IsNotNull(fx);
            Assert.AreEqual(x, fx.FlipX);
            Assert.AreEqual(y, fx.FlipY);
        }

        [Test]
        public void Rotation_and_flip_compose_on_one_component()
        {
            var s = Open("<Image id='i' size='40x40' rotation='180' flip='x'/>");
            var fx = EffectOn(s.Get<PromptUGUI.Controls.Image>("i"));

            Assert.AreEqual(180f, fx.Rotation, 0.001f);
            Assert.IsTrue(fx.FlipX);
        }

        [Test]
        public void Identity_values_attach_nothing()
        {
            var s = Open("<Image id='i' size='40x40' rotation='0' flip='none'/>");

            Assert.IsNull(EffectOn(s.Get<PromptUGUI.Controls.Image>("i")),
                "the common case must not pay for a component");
        }

        [Test]
        public void Nothing_written_attaches_nothing()
        {
            var s = Open("<Image id='i' size='40x40'/>");

            Assert.IsNull(EffectOn(s.Get<PromptUGUI.Controls.Image>("i")));
        }

        [Test]
        public void RawImage_takes_the_attributes_too()
        {
            var s = Open("<RawImage id='r' size='40x40' rotation='45'/>");
            var fx = EffectOn(s.Get<PromptUGUI.Controls.RawImage>("r"));

            Assert.IsNotNull(fx);
            Assert.AreEqual(45f, fx.Rotation, 0.001f);
        }

        [Test]
        public void The_rect_transform_is_untouched()
        {
            var plain = Open("<Image id='i' anchor='top-left' size='40x20' margin='6,_,_,8'/>");
            var pr = plain.Get<PromptUGUI.Controls.Image>("i").RectTransform;
            var expectedSize = pr.sizeDelta;
            var expectedPos = pr.anchoredPosition;

            UI.ResetForTests();
            var turned = Open("<Image id='i' anchor='top-left' size='40x20' margin='6,_,_,8' rotation='90' flip='y'/>");
            var tr = turned.Get<PromptUGUI.Controls.Image>("i").RectTransform;

            Assert.AreEqual(expectedSize, tr.sizeDelta, "rotation is mesh-level — the rect does not change");
            Assert.AreEqual(expectedPos, tr.anchoredPosition);
            Assert.AreEqual(Vector3.one, tr.localScale, "and neither does the transform");
            Assert.AreEqual(Quaternion.identity, tr.localRotation);
        }

        [Test]
        public void A_rotated_child_does_not_move_its_siblings()
        {
            const string Stack =
                "<HStack id='h' anchor='top-left' width='300' height='40' spacing='0'>" +
                "<Image id='a' size='40x40' {0}/><Image id='b' size='40x40'/></HStack>";

            var plain = Open(string.Format(Stack, ""));
            Canvas.ForceUpdateCanvases();
            var expected = plain.Get<PromptUGUI.Controls.Image>("b").RectTransform.anchoredPosition;

            UI.ResetForTests();
            var turned = Open(string.Format(Stack, "rotation='90'"));
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(expected, turned.Get<PromptUGUI.Controls.Image>("b").RectTransform.anchoredPosition,
                "the layout measures the un-rotated rect, so a rotated sibling claims exactly its own slot");
        }

        [Test]
        public void Variant_flip_is_reversible()
        {
            var s = Open("<Image id='i' size='40x40' rotation='0' rotation.portrait='180'/>");
            var img = s.Get<PromptUGUI.Controls.Image>("i");
            Assert.IsNull(EffectOn(img), "identity base attaches nothing");

            UI.Variants.Set("portrait", true);
            var fx = EffectOn(img);
            Assert.IsNotNull(fx);
            Assert.AreEqual(180f, fx.Rotation, 0.001f);
            Assert.IsTrue(fx.enabled);

            UI.Variants.Set("portrait", false);
            Assert.AreEqual(0f, fx.Rotation, 0.001f);
            Assert.IsFalse(fx.enabled, "back to identity: disabled, not destroyed — the flip stays idempotent");
        }

        [Test]
        public void An_invalid_flip_value_throws_at_open()
        {
            UI.LoadDocument("t", Header + "<Image id='i' size='40x40' flip='z'/>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("flip", ex.Message);
            StringAssert.Contains("'xy'", ex.Message);
        }

        [Test]
        public void Rotation_is_settable_from_code_for_tweening()
        {
            var s = Open("<Image id='i' size='40x40' rotation='0'/>");
            var img = s.Get<PromptUGUI.Controls.Image>("i");

            img.Rotation = 90f;

            var fx = EffectOn(img);
            Assert.IsNotNull(fx, "a code-side tween must be able to start from an identity-authored node");
            Assert.AreEqual(90f, fx.Rotation, 0.001f);
        }
    }
}
