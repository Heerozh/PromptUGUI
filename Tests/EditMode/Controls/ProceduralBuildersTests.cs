using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ProceduralBuildersTests
    {
        [Test]
        public void Palette_BgColors_AreWhite()
        {
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultBtnColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultControlBgColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultTrackColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultFillColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultHandleColor);
            Assert.AreEqual(Color.white, ProceduralBuilders.DefaultPopupBgColor);
        }

        [Test]
        public void Palette_ContainerColor_IsTranslucentWhite()
        {
            var c = ProceduralBuilders.DefaultContainerColor;
            Assert.AreEqual(1f, c.r);
            Assert.AreEqual(1f, c.g);
            Assert.AreEqual(1f, c.b);
            Assert.That(c.a, Is.EqualTo(0.392f).Within(0.001f));
        }

        [Test]
        public void Palette_GlyphColor_IsDarkGrey()
        {
            var c = ProceduralBuilders.DefaultGlyphColor;
            Assert.That(c.r, Is.EqualTo(0.196f).Within(0.001f));
            Assert.That(c.g, Is.EqualTo(0.196f).Within(0.001f));
            Assert.That(c.b, Is.EqualTo(0.196f).Within(0.001f));
            Assert.AreEqual(1f, c.a);
        }

        [Test]
        public void Palette_LabelColor_IsDarkGrey()
        {
            var c = ProceduralBuilders.DefaultLabelColor;
            Assert.That(c.r, Is.EqualTo(0.196f).Within(0.001f));
            Assert.AreEqual(1f, c.a);
        }

        [Test]
        public void Palette_PlaceholderColor_IsDarkGreyHalfAlpha()
        {
            var c = ProceduralBuilders.DefaultPlaceholderColor;
            Assert.That(c.r, Is.EqualTo(0.196f).Within(0.001f));
            Assert.That(c.a, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void AddText_AppliesDefaultLabelColorAndFontSizeFourteen()
        {
            var go = new GameObject("Parent", typeof(RectTransform));
            try
            {
                var parent = (RectTransform)go.transform;
                var tmp = ProceduralBuilders.AddText(parent, "Label");
                Assert.AreEqual(ProceduralBuilders.DefaultLabelColor, tmp.color);
                Assert.AreEqual(14f, tmp.fontSize);
                Assert.AreEqual(TextAlignmentOptions.Center, tmp.alignment);
                Assert.IsFalse(tmp.raycastTarget);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ApplyDefaultSimpleSprite_DefaultsPreserveAspectFalse()
        {
            var go = new GameObject("Img", typeof(RectTransform));
            try
            {
                var img = go.AddComponent<UnityImage>();
                ProceduralBuilders.ApplyDefaultSimpleSprite(img, ProceduralBuilders.SpriteCheckmark);
                Assert.IsFalse(img.preserveAspect, "default preserveAspect should be false");
                Assert.AreEqual(UnityImage.Type.Simple, img.type);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ApplyDefaultSimpleSprite_OptInPreserveAspectTrue()
        {
            var go = new GameObject("Img", typeof(RectTransform));
            try
            {
                var img = go.AddComponent<UnityImage>();
                ProceduralBuilders.ApplyDefaultSimpleSprite(img, ProceduralBuilders.SpriteCheckmark, preserveAspect: true);
                Assert.IsTrue(img.preserveAspect);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
