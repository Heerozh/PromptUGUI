using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
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

        [Test]
        public void Wired_TextComponentHasInputFieldDirtyVertsCallbacks()
        {
            // TMP_InputField 在 AddComponent 触发 OnEnable 时, 才把 MarkGeometryAsDirty/UpdateLabel
            // 注册到 textComponent 的 m_OnDirtyVertsCallback。如果注册时 textComponent 是 null
            // (textComponent 在 AddComponent 之后才赋值), caret 顶点永不 redraw → caret 永远不显示。
            // 这条断言保证 OnAttached 完成时 callback 已绑定; 修复方式: 强制再触一次 OnEnable cycle。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var input = f.GameObject.GetComponent<TMP_InputField>();
            var fld = typeof(TMP_Text).GetField("m_OnDirtyVertsCallback",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var cb = (UnityEngine.Events.UnityAction)fld.GetValue(input.textComponent);
            Assert.IsNotNull(cb, "textComponent.m_OnDirtyVertsCallback must be bound after OnAttached");
            var handlers = cb.GetInvocationList();
            var names = new System.Collections.Generic.List<string>();
            foreach (var d in handlers) names.Add(d.Method.Name);
            CollectionAssert.Contains(names, "MarkGeometryAsDirty",
                "TMP_InputField.MarkGeometryAsDirty must be registered on textComponent");
            CollectionAssert.Contains(names, "UpdateLabel",
                "TMP_InputField.UpdateLabel must be registered on textComponent");
        }

        [Test]
        public void Apply_TextAttribute()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' text='hello'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual("hello", f.GameObject.GetComponent<TMP_InputField>().text);
        }

        [Test]
        public void TextShorthand_BodyTextSetsText()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'>初始</InputField>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual("初始", f.GameObject.GetComponent<TMP_InputField>().text);
        }

        [Test]
        public void Apply_PlaceholderAttribute()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' placeholder='请输入'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ph = f.GameObject.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>();
            Assert.AreEqual("请输入", ph.text);
        }

        [Test]
        public void Apply_ContentTypePassword()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' contentType='password'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual(TMP_InputField.ContentType.Password,
                f.GameObject.GetComponent<TMP_InputField>().contentType);
        }

        [Test]
        public void Apply_LineTypeMultiNewline()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' lineType='multi-newline'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual(TMP_InputField.LineType.MultiLineNewline,
                f.GameObject.GetComponent<TMP_InputField>().lineType);
        }

        [Test]
        public void Apply_CharacterLimit()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' characterLimit='10'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual(10, f.GameObject.GetComponent<TMP_InputField>().characterLimit);
        }

        [Test]
        public void Apply_ReadOnly()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' readOnly='true'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.IsTrue(f.GameObject.GetComponent<TMP_InputField>().readOnly);
        }

        [Test]
        public void Event_OnValueChanged_FiresOnTextSet()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");

            string last = null;
            f.OnValueChanged.Subscribe(v => last = v);
            f.GameObject.GetComponent<TMP_InputField>().text = "abc";
            Assert.AreEqual("abc", last);
        }

        [Test]
        public void Event_OnEndEdit_FiresOnEndEditUnityCallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");

            string last = null;
            f.OnEndEdit.Subscribe(v => last = v);
            f.GameObject.GetComponent<TMP_InputField>().onEndEdit.Invoke("done");
            Assert.AreEqual("done", last);
        }

        [Test]
        public void Event_OnSubmit_FiresOnSubmitUnityCallback()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");

            string last = null;
            f.OnSubmit.Subscribe(v => last = v);
            f.GameObject.GetComponent<TMP_InputField>().onSubmit.Invoke("submitted");
            Assert.AreEqual("submitted", last);
        }
    }
}
