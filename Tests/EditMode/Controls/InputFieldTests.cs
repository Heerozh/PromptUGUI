using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PInputField = PromptUGUI.Controls.InputField;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class InputFieldTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Build_HasBgImageAndTMPInputField()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var f = screen.Get<PInputField>("f");
            Assert.IsNotNull(f.GameObject.GetComponent<UnityImage>(), "root has Image bg");
            Assert.IsNotNull(f.GameObject.GetComponent<TMP_InputField>());
        }

        [Test]
        public void Geometry_TextAreaInsetMatchesPrefab()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ta = f.GameObject.transform.Find("Text Area") as RectTransform;
            Assert.IsNotNull(ta, "Text Area child must exist");
            Assert.AreEqual(new Vector2(0, 0), ta.anchorMin);
            Assert.AreEqual(new Vector2(1, 1), ta.anchorMax);
            Assert.AreEqual(new Vector2(-20, -13), ta.sizeDelta);
            Assert.AreEqual(new Vector2(0, -0.5f), ta.anchoredPosition);
        }

        [Test]
        public void Geometry_TextAreaHasRectMask2DWithPadding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ta = f.GameObject.transform.Find("Text Area").gameObject;
            var rm = ta.GetComponent<RectMask2D>();
            Assert.IsNotNull(rm, "Text Area uses RectMask2D (matches default prefab)");
            Assert.AreEqual(new Vector4(-8, -5, -8, -5), rm.padding);
        }

        [Test]
        public void Geometry_PlaceholderIsItalicHalfAlphaWithIgnoreLayout()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' placeholder='Enter...'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ph = f.GameObject.transform.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>();
            Assert.IsNotNull(ph, "Placeholder must be Text Area child");
            Assert.AreEqual(FontStyles.Italic, ph.fontStyle);
            Assert.That(ph.color.a, Is.EqualTo(0.5f).Within(0.005f));
            Assert.IsFalse(ph.raycastTarget);

            var le = ph.gameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.IsTrue(le.ignoreLayout);
        }

        [Test]
        public void Geometry_TextChildExists()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var text = f.GameObject.transform.Find("Text Area/Text")?.GetComponent<TMP_Text>();
            Assert.IsNotNull(text);
            Assert.IsFalse(text.raycastTarget);
        }

        [Test]
        public void Wired_TMPInputFieldRefsTextAndPlaceholder()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var input = f.GameObject.GetComponent<TMP_InputField>();
            Assert.IsNotNull(input.textComponent);
            Assert.AreEqual("Text", input.textComponent.gameObject.name);
            Assert.IsNotNull(input.placeholder);
            Assert.AreEqual("Placeholder", input.placeholder.gameObject.name);
        }
    }
}
