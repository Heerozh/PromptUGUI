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
    }
}
