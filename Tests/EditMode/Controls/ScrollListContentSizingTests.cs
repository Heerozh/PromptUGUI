using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ScrollListContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ScrollList_vertical_GetNativeSize_returns_vertical_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <ScrollList id='l'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var native = list.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(160f, native.Value.x, "vertical ScrollList: cross axis x = 160");
            Assert.AreEqual(200f, native.Value.y, "vertical ScrollList: main axis y = 200");
        }

        [Test]
        public void ScrollList_horizontal_GetNativeSize_returns_horizontal_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <ScrollList id='l' direction='horizontal'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var native = list.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(200f, native.Value.x, "horizontal ScrollList: main axis x = 200");
            Assert.AreEqual(160f, native.Value.y, "horizontal ScrollList: cross axis y = 160");
        }

        [Test]
        public void ScrollList_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x400'>
    <ScrollList id='l'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            Assert.AreEqual(160f, list.RectTransform.sizeDelta.x, 0.5f);
            Assert.AreEqual(200f, list.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void ScrollList_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <ScrollList id='l'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            var le = list.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D4: ScrollList under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(200f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void ScrollList_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x400'>
    <ScrollList id='l' size='300x250'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("l");
            Assert.AreEqual(new Vector2(300f, 250f), list.RectTransform.sizeDelta);
        }
    }
}
