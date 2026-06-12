using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DefaultSkinTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [TestCase(ProceduralBuilders.SpriteRoundedRect)]
        [TestCase(ProceduralBuilders.SpriteMaskRoundedRect)]
        [TestCase(ProceduralBuilders.SpriteCaret)]
        [TestCase(ProceduralBuilders.SpriteCheckmark)]
        [TestCase(ProceduralBuilders.SpriteInset)]
        [TestCase(ProceduralBuilders.SpritePressed)]
        [TestCase(ProceduralBuilders.SpriteKnob)]
        public void GetDefaultSprite_ResolvesAllSkinSections(string name)
        {
            ProceduralBuilders.ResetDefaultSpriteCacheForTests();
            Assert.IsNotNull(ProceduralBuilders.GetDefaultSprite(name), name);
        }

        [Test]
        public void ApplyDefaultInsetSprite_SetsInsetSliced()
        {
            var go = new GameObject("img", typeof(RectTransform));
            try
            {
                var img = go.AddComponent<UnityEngine.UI.Image>();
                ProceduralBuilders.ApplyDefaultInsetSprite(img);
                Assert.IsNotNull(img.sprite, "default inset sprite must resolve");
                Assert.AreEqual("pugui_9slice_inset", img.sprite.name);
                Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
