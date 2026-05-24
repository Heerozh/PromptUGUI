using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using Dropdown = PromptUGUI.Controls.Dropdown;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DropdownContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Dropdown_GetNativeSize_returns_default_size()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Dropdown id='d'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            var native = dropdown.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Dropdown must report a default native size");
            Assert.AreEqual(160f, native.Value.x);
            Assert.AreEqual(44f, native.Value.y);
        }

        [Test]
        public void Dropdown_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Dropdown id='d'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            Assert.AreEqual(160f, dropdown.RectTransform.sizeDelta.x, 0.5f,
                "BCS-D7 / DSS-D2: free-positioning + no size + has native → sizeDelta = native");
            Assert.AreEqual(44f, dropdown.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Dropdown_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Dropdown id='d' anchor='stretch' margin='8'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            Assert.AreEqual(-16f, dropdown.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, native fallback skipped");
            Assert.AreEqual(-16f, dropdown.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Dropdown_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Dropdown id='d'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            var le = dropdown.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D2: Dropdown under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(44f, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Dropdown_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Dropdown id='d' size='240x36'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var dropdown = screen.Get<Dropdown>("d");
            Assert.AreEqual(new Vector2(240f, 36f), dropdown.RectTransform.sizeDelta);
        }
    }
}
