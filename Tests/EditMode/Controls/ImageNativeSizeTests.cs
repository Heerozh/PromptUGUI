using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using PromptUGUIImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageNativeSizeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Image_with_sprite_GetNativeSize_returns_sprite_rect_over_ppu()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<PromptUGUIImage>("i");
            var unityImg = img.GameObject.GetComponent<Image>();
            Assert.IsNotNull(unityImg.sprite, "sprite must resolve for this test to be meaningful");

            var native = img.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Image with sprite must report a native size");
            var expectedW = unityImg.sprite.rect.width / unityImg.pixelsPerUnit;
            var expectedH = unityImg.sprite.rect.height / unityImg.pixelsPerUnit;
            Assert.AreEqual(expectedW, native.Value.x, 0.5f);
            Assert.AreEqual(expectedH, native.Value.y, 0.5f);
        }

        [Test]
        public void Image_no_sprite_GetNativeSize_returns_null()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<PromptUGUIImage>("i");
            Assert.IsFalse(img.GetNativeSize().HasValue,
                "sprite-less Image keeps the old (0,0) fallback — author must specify size or anchor=stretch");
        }

        [Test]
        public void Image_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Image id='i' sprite='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<PromptUGUIImage>("i");
            var native = img.GetNativeSize().Value;
            Assert.AreEqual(native.x, img.RectTransform.sizeDelta.x, 0.5f,
                "free-positioning + no size + has sprite → sizeDelta = sprite native size");
            Assert.AreEqual(native.y, img.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Image_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Image id='i' sprite='PromptUGUI/Defaults/pugui#pugui_9slice_round' anchor='stretch' margin='8'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<PromptUGUIImage>("i");
            Assert.AreEqual(-16f, img.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, native fallback skipped");
            Assert.AreEqual(-16f, img.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Image_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Image id='i' sprite='PromptUGUI/Defaults/pugui#pugui_9slice_round' size='120x60'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<PromptUGUIImage>("i");
            Assert.AreEqual(new Vector2(120f, 60f), img.RectTransform.sizeDelta);
        }

        [Test]
        public void Image_in_HStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='100'>
    <Image id='i' sprite='PromptUGUI/Defaults/pugui#pugui_9slice_round'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<PromptUGUIImage>("i");
            var le = img.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Image under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            var native = img.GetNativeSize().Value;
            Assert.AreEqual(native.x, le.preferredWidth, 0.5f);
            Assert.AreEqual(native.y, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }
    }
}
