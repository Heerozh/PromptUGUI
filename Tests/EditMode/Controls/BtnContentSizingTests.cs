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
    }
}
