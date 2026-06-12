using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Internal;
using UnityEngine;
using UnityEngine.UI;
using PromptUGUIImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageFitTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Sprite = "PromptUGUI/Defaults/pugui#pugui_9slice_round";

        private static PromptUGUIImage Build(string typeAttr)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' size='320x180'>
    <Image id='i' sprite='{Sprite}' type='{typeAttr}'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<PromptUGUIImage>("i");
        }

        [Test]
        public void Contain_AddsAspectRatioFitter_FitInParent_TypeSimple()
        {
            var img = Build("contain");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf, "contain must add an AspectRatioFitter");
            Assert.IsTrue(arf.enabled);
            Assert.AreEqual(AspectRatioFitter.AspectMode.FitInParent, arf.aspectMode);
            Assert.AreEqual(Image.Type.Simple, img.GameObject.GetComponent<Image>().type);
        }

        [Test]
        public void Cover_AddsAspectRatioFitter_EnvelopeParent_TypeSimple()
        {
            var img = Build("cover");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf);
            Assert.IsTrue(arf.enabled);
            Assert.AreEqual(AspectRatioFitter.AspectMode.EnvelopeParent, arf.aspectMode);
            Assert.AreEqual(Image.Type.Simple, img.GameObject.GetComponent<Image>().type);
        }

        [Test]
        public void Cover_AspectRatio_MatchesSpriteRect()
        {
            var img = Build("cover");
            var unityImg = img.GameObject.GetComponent<Image>();
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            var expected = unityImg.sprite.rect.width / unityImg.sprite.rect.height;
            Assert.AreEqual(expected, arf.aspectRatio, 0.001f);
        }

        [Test]
        public void Simple_NoAspectRatioFitter()
        {
            var img = Build("simple");
            Assert.IsNull(img.GameObject.GetComponent<AspectRatioFitter>());
        }

        [Test]
        public void NoTypeAttr_NoAspectRatioFitter()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='" + Sprite + @"'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            Assert.IsNull(img.GameObject.GetComponent<AspectRatioFitter>());
        }

        [Test]
        public void Cover_NoSprite_DoesNotThrow_NoAspectRatioUpdate()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' size='320x180'><Image id='i' type='cover'/></Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf);
            Assert.AreEqual(1f, arf.aspectRatio, 0.001f, "aspectRatio stays at default (1) when no sprite");
        }

        [Test]
        public void BaseCover_VariantSliced_TogglesFitterEnabled()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='box' size='320x180'>
    <Image id='i' sprite='" + Sprite + @"' type='cover' type.mobile='sliced'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsTrue(arf.enabled, "base cover → fitter enabled");

            UI.Variants.Set("mobile", true);
            Assert.IsFalse(arf.enabled, "variant sliced → fitter disabled");
            Assert.AreEqual(Image.Type.Sliced, img.GameObject.GetComponent<Image>().type);

            UI.Variants.Set("mobile", false);
            Assert.IsTrue(arf.enabled, "back to base cover → fitter re-enabled");
            var unityImg = img.GameObject.GetComponent<Image>();
            var expected = unityImg.sprite.rect.width / unityImg.sprite.rect.height;
            Assert.AreEqual(expected, arf.aspectRatio, 0.001f, "aspectRatio restored on re-enable");
        }

        [Test]
        public void Contain_AspectRatio_MatchesSpriteRect()
        {
            var img = Build("contain");
            var unityImg = img.GameObject.GetComponent<Image>();
            var arf = img.GameObject.GetComponent<AspectRatioFitter>();
            var expected = unityImg.sprite.rect.width / unityImg.sprite.rect.height;
            Assert.AreEqual(expected, arf.aspectRatio, 0.001f);
        }

        [Test]
        public void Image_autopick_tiled_for_hinted_sprite()
        {
            // Use a plain (no-border) sprite registered as tiled — DeriveType returns Tiled for registered sprites
            // regardless of border (see DefaultSkinTests.DeriveType_four_branches).
            var sp = UnityEngine.Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            SpriteRenderHints.Register(sp);
            UI.SpriteResolver = _ => sp;

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='ui:x'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            Assert.AreEqual(Image.Type.Tiled, img.GameObject.GetComponent<Image>().type,
                "hint-registered sprite must auto-pick Tiled");
        }

        [Test]
        public void Image_explicit_type_overrides_hint()
        {
            // Even a tiled-hinted sprite must not override an explicit type= attribute.
            var sp = UnityEngine.Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            SpriteRenderHints.Register(sp);
            UI.SpriteResolver = _ => sp;

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='ui:x' type='sliced'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var img = UI.Open("S").Get<PromptUGUIImage>("i");
            Assert.AreEqual(Image.Type.Sliced, img.GameObject.GetComponent<Image>().type,
                "explicit type= must override the tiled hint");
        }
    }
}
