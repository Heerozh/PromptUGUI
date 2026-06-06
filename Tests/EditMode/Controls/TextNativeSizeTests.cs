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
        public void Text_in_HStack_no_size_defers_both_axes_to_tmp()
        {
            // New contract: <Text> in a LayoutGroup never pins a one-time native snapshot — TMP is
            // itself a live ILayoutElement and drives both axes, so the size tracks content (e.g. a
            // wider runtime string widens the slot, a wrapped string grows the height). With both
            // axes omitted nothing else needs an LE, so none is attached.
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
            Assert.IsNull(le,
                "Text with both axes omitted pins no LayoutElement — its own TMP ILayoutElement drives both axes");
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
        public void Text_in_HStack_height_only_defers_width_to_tmp()
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
            Assert.IsNotNull(le, "explicit height still needs an LE to pin that axis");
            Assert.AreEqual(-1f, le.preferredWidth,
                "omitted width stays at the -1 sentinel → TMP's intrinsic ILayoutElement drives width");
            Assert.AreEqual(12f, le.preferredHeight, 0.5f,
                "explicit height → preferredHeight = author value");
        }

        [Test]
        public void Text_in_VStack_width_only_defers_height_to_tmp()
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
            Assert.AreEqual(100f, le.preferredWidth, 0.5f,
                "explicit width → preferredWidth = author value");
            Assert.AreEqual(-1f, le.preferredHeight,
                "omitted height defers to TMP's intrinsic ILayoutElement (was: frozen single-line native)");
        }

        [Test]
        public void Text_in_VStack_width_stretch_defers_height_to_tmp()
        {
            // The headline case: a stretch-width body label whose height must follow the wrapped
            // content. preferredHeight stays at the -1 sentinel so TMP measures the wrapped height
            // against the stretched width during the VerticalLayoutGroup pass.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='400'>
    <Text id='t' tr='false' width='stretch' wrap='true'>本段正文足够长，会在受限的宽度下自动换行成多行</Text>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var le = text.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "width=stretch needs an LE to carry the flexible width");
            Assert.AreEqual(0f, le.preferredWidth, "stretch → preferredWidth 0");
            Assert.AreEqual(1f, le.flexibleWidth, "stretch → flexibleWidth 1");
            Assert.AreEqual(-1f, le.preferredHeight,
                "omitted height must stay -1 so TMP's own ILayoutElement drives the wrapped height");
            Assert.AreEqual(-1f, le.flexibleHeight);
        }
    }
}
