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
    }
}
