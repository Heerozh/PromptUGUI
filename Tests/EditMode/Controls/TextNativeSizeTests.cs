using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TextNativeSizeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Text_with_content_GetNativeSize_returns_tmp_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t' tr='false'>Hello World</Text>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var native = text.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Text with non-empty content must report a native size");
            text.TmpComponent.ForceMeshUpdate();
            Assert.AreEqual(text.TmpComponent.preferredWidth, native.Value.x, 0.5f);
            Assert.AreEqual(text.TmpComponent.preferredHeight, native.Value.y, 0.5f);
            Assert.Greater(native.Value.x, 0f, "preferred width for non-empty text must be > 0");
        }

        [Test]
        public void Text_empty_GetNativeSize_returns_null()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Text id='t' tr='false' text=''/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.IsFalse(text.GetNativeSize().HasValue,
                "empty Text reports no native size — keeps the old (0,0) fallback so layout doesn't reserve invisible space");
        }

        [Test]
        public void Text_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Text id='t' tr='false'>Hello</Text>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var native = text.GetNativeSize().Value;
            Assert.AreEqual(native.x, text.RectTransform.sizeDelta.x, 0.5f,
                "free-positioning + no size + has native → sizeDelta = native");
            Assert.AreEqual(native.y, text.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Text_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Text id='t' tr='false' anchor='stretch' margin='8'>Hello</Text>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.AreEqual(-16f, text.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, native fallback skipped");
            Assert.AreEqual(-16f, text.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Text_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Text id='t' tr='false' size='200x40'>Hello</Text>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            Assert.AreEqual(new Vector2(200f, 40f), text.RectTransform.sizeDelta);
        }

        [Test]
        public void Text_in_HStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='44'>
    <Text id='t' tr='false'>Hi</Text>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var le = text.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Text under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            var native = text.GetNativeSize().Value;
            Assert.AreEqual(native.x, le.preferredWidth, 0.5f);
            Assert.AreEqual(native.y, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Text_in_Frame_height_only_width_falls_back_to_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Text id='t' tr='false' height='12'>Lv. 45</Text>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var native = text.GetNativeSize().Value;
            Assert.AreEqual(native.x, text.RectTransform.sizeDelta.x, 0.5f,
                "free-positioning + only height written + has native → width falls back to native");
            Assert.AreEqual(12f, text.RectTransform.sizeDelta.y, 0.5f,
                "explicit height stays at author's value");
        }

        [Test]
        public void Text_in_Frame_width_only_height_falls_back_to_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Text id='t' tr='false' width='200'>Hello</Text>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var native = text.GetNativeSize().Value;
            Assert.AreEqual(200f, text.RectTransform.sizeDelta.x, 0.5f,
                "explicit width stays at author's value");
            Assert.AreEqual(native.y, text.RectTransform.sizeDelta.y, 0.5f,
                "free-positioning + only width written + has native → height falls back to native");
        }

        [Test]
        public void Text_in_HStack_height_only_LE_preferredWidth_is_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='44'>
    <Text id='t' tr='false' height='12'>Hi</Text>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var le = text.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Text under LayoutGroup with partial size should still get LE");
            var native = text.GetNativeSize().Value;
            Assert.AreEqual(native.x, le.preferredWidth, 0.5f,
                "omitted width → preferredWidth = native");
            Assert.AreEqual(12f, le.preferredHeight, 0.5f,
                "explicit height → preferredHeight = author value");
        }

        [Test]
        public void Text_in_VStack_width_only_LE_preferredHeight_is_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Text id='t' tr='false' width='100'>Hi</Text>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var le = text.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            var native = text.GetNativeSize().Value;
            Assert.AreEqual(100f, le.preferredWidth, 0.5f,
                "explicit width → preferredWidth = author value");
            Assert.AreEqual(native.y, le.preferredHeight, 0.5f,
                "omitted height → preferredHeight = native");
        }
    }
}
