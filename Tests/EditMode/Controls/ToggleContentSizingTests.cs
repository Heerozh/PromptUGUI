using NUnit.Framework;
using PromptUGUI.Application;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Toggle = PromptUGUI.Controls.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Toggle_without_text_GetNativeSize_returns_icon_only_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var native = toggle.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Toggle must report a native size (checkbox-only fallback)");
            Assert.AreEqual(44f, native.Value.x);
            Assert.AreEqual(44f, native.Value.y);
        }

        [Test]
        public void Toggle_with_text_GetNativeSize_reports_label_preferred_plus_padding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'>静音</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var label = toggle.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            label.ForceMeshUpdate();
            var textW = label.preferredWidth;

            var native = toggle.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(textW + 28f, native.Value.x, 0.5f,
                "preferredWidth = label.preferredWidth + 23 (left checkmark zone) + 5 (right padding)");
            Assert.AreEqual(44f, native.Value.y, 0.5f,
                "preferredHeight = max(44, label.preferredHeight + 6*2); fontSize 14 → max picks 44");
        }

        [Test]
        public void Toggle_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Toggle id='t'>静音</Toggle>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var native = toggle.GetNativeSize().Value;
            Assert.AreEqual(native.x, toggle.RectTransform.sizeDelta.x, 0.5f,
                "BCS-D7 / TCS-D1: free-positioning + no size + has native → sizeDelta = native");
            Assert.AreEqual(native.y, toggle.RectTransform.sizeDelta.y, 0.5f);
            Assert.AreEqual(44f, toggle.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Toggle_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Toggle id='t' anchor='stretch' margin='8'>静音</Toggle>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            Assert.AreEqual(-16f, toggle.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, native fallback skipped");
            Assert.AreEqual(-16f, toggle.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Toggle_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Toggle id='t'>静音</Toggle>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var le = toggle.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / TCS-D1: Toggle under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            var native = toggle.GetNativeSize().Value;
            Assert.AreEqual(native.x, le.preferredWidth, 0.5f);
            Assert.AreEqual(native.y, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Toggle_in_Frame_explicit_size_overrides_native()
        {
            // TCS-D6: explicit size wins; fallback only when no size attrs at all
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Toggle id='t' size='200x60'>静音</Toggle>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            Assert.AreEqual(new Vector2(200f, 60f), toggle.RectTransform.sizeDelta);
        }

        [Test]
        public void Toggle_in_VStack_variant_text_change_updates_preferred()
        {
            // TCS-D7: ApplyCommon re-runs on Variant switch → GetNativeSize re-evaluates → LE.preferred follows
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Toggle id='t' text='短' text.long='长长长长长长'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("long", false);
            var screen = UI.Open("S");
            var toggle = screen.Get<Toggle>("t");
            var le = toggle.GameObject.GetComponent<LayoutElement>();
            var preferredShort = le.preferredWidth;
            Assert.Greater(preferredShort, 0f, "base text='短' preferred should be > 0");

            UI.Variants.Set("long", true);
            Assert.Greater(le.preferredWidth, preferredShort,
                "long variant has wider text → preferredWidth should grow");
        }
    }
}
