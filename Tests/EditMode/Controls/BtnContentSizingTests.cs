using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Btn_without_text_GetNativeSize_returns_icon_only_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var native = btn.GetNativeSize();
            Assert.IsTrue(native.HasValue, "Btn must report a native size (icon-only fallback)");
            Assert.AreEqual(80f, native.Value.x);
            Assert.AreEqual(44f, native.Value.y);
        }

        [Test]
        public void Btn_with_text_GetNativeSize_reports_label_preferred_plus_padding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>OK</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            label.ForceMeshUpdate();
            var textW = label.preferredWidth;

            var native = btn.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(textW + 32f, native.Value.x, 0.5f,
                "preferredWidth = label.preferredWidth + 16*2 padding");
            Assert.AreEqual(44f, native.Value.y, 0.5f,
                "preferredHeight = max(44, label.preferredHeight + 6*2)");
        }

        [Test]
        public void Btn_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Btn id='b'>Cancel</Btn>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var native = btn.GetNativeSize().Value;
            Assert.AreEqual(native.x, btn.RectTransform.sizeDelta.x, 0.5f,
                "BCS-D7: free-positioning + no size + has native → sizeDelta = native");
            Assert.AreEqual(native.y, btn.RectTransform.sizeDelta.y, 0.5f);
            Assert.AreEqual(44f, btn.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Btn_in_Frame_anchor_stretch_skips_native_fallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Btn id='b' anchor='stretch' margin='8'>OK</Btn>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            Assert.AreEqual(-16f, btn.RectTransform.sizeDelta.x, 0.5f,
                "anchor=stretch + margin=8: sizeDelta.x = -(l+r) = -16, 不被 native fallback 覆盖");
            Assert.AreEqual(-16f, btn.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Btn_in_HStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='44'>
    <Btn id='b'>OK</Btn>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "BCS-D6: Btn under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            var native = btn.GetNativeSize().Value;
            Assert.AreEqual(native.x, le.preferredWidth, 0.5f);
            Assert.AreEqual(native.y, le.preferredHeight, 0.5f);
            Assert.AreEqual(-1f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_Frame_explicit_size_overrides_native()
        {
            // BCS-D5: 显式 size 优先；fallback 只在没写 size 时启用
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Btn id='b' size='200x60'>OK</Btn>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            Assert.AreEqual(new Vector2(200f, 60f), btn.RectTransform.sizeDelta);
        }

        [Test]
        public void Btn_in_HStack_variant_text_change_updates_preferred()
        {
            // BCS-D9: ApplyCommon 在 Variant 切换时重跑 → GetNativeSize 重算 → LE.preferred 跟随
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='44'>
    <Btn id='b' text='OK' text.long='Confirm and Apply Changes'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("long", false);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            var preferredShort = le.preferredWidth;
            Assert.Greater(preferredShort, 0f, "base text='OK' preferred 应该 > 0");

            UI.Variants.Set("long", true);
            Assert.Greater(le.preferredWidth, preferredShort,
                "long variant 文字更长 → preferredWidth 应该更大");
        }
    }
}
