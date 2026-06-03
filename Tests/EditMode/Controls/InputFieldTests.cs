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
        public void NativeSize_DefaultsTo160x44()
        {
            // A bare <InputField/> must report a native size like its sibling controls
            // (Dropdown is 160x44) so "write nothing = Unity default box" instead of
            // collapsing to sizeDelta (0,0). Free-positioning => native fills sizeDelta.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var rt = (RectTransform)f.GameObject.transform;
            Assert.AreEqual(160f, rt.sizeDelta.x, 1e-3f, "native width = 160");
            Assert.AreEqual(44f, rt.sizeDelta.y, 1e-3f, "native height = 44 (MinTapHeight)");
        }

        [Test]
        public void Geometry_NoSprite_TextAreaFillsField()
        {
            // sprite='' => no border => the Text Area inset (breathing room for the frame)
            // must collapse to 0, otherwise a short field (height=12) makes the Text Area
            // height negative: 12 - 13 = -1. The mask overscan tracks the inset to 0 too.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' sprite='' height='12'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ta = f.GameObject.transform.Find("Text Area") as RectTransform;
            Assert.AreEqual(new Vector2(0, 0), ta.sizeDelta, "no border => Text Area fills field");
            var rm = ta.GetComponent<RectMask2D>();
            Assert.AreEqual(new Vector4(0, 0, 0, 0), rm.padding, "no inset => no mask overscan");
        }

        [Test]
        public void Geometry_PaddingOverride_SetsTextAreaInset()
        {
            // padding='T,R,B,L'=2,4,2,4 => L=4,R=4,T=2,B=2 => offsetMin=(4,2) offsetMax=(-4,-2)
            // => sizeDelta=(-8,-4). Author value wins over the bordered default.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' padding='2,4,2,4'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ta = f.GameObject.transform.Find("Text Area") as RectTransform;
            Assert.AreEqual(new Vector2(-8, -4), ta.sizeDelta, "padding override drives Text Area inset");
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

        // --- Text styling -------------------------------------------------------

        // fontSize goes through TMP_InputField.pointSize, which SetGlobalPointSize fans
        // out to BOTH the text and placeholder components (matches the default prefab's
        // GlobalPointSize). Asserting both proves it isn't set on _text alone.
        [Test]
        public void Apply_FontSize_SetsBothTextAndPlaceholder()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' fontSize='28'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var input = f.GameObject.GetComponent<TMP_InputField>();
            Assert.AreEqual(28f, input.pointSize);
            var text = f.GameObject.transform.Find("Text Area/Text").GetComponent<TMP_Text>();
            var ph = f.GameObject.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>();
            Assert.AreEqual(28f, text.fontSize);
            Assert.AreEqual(28f, ph.fontSize);
        }

        // `color` is the bg; the typed text color is `textColor` (distinct attribute).
        [Test]
        public void Apply_TextColor_SetsTextComponentNotBg()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' textColor='#ff0000'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var text = f.GameObject.transform.Find("Text Area/Text").GetComponent<TMP_Text>();
            Assert.AreEqual(Color.red, text.color);
            // bg untouched (still the default control bg, not red)
            Assert.AreNotEqual(Color.red, f.GameObject.GetComponent<UnityImage>().color);
        }

        [Test]
        public void Apply_PlaceholderColor_SetsPlaceholderComponent()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' placeholderColor='#00ff00'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var ph = f.GameObject.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>();
            Assert.AreEqual(Color.green, ph.color);
        }

        // align reuses Text.ParseAlign (two independent axes) and applies to both the
        // text and the placeholder so the placeholder previews where typed text lands.
        [Test]
        public void Apply_Align_SetsBothTextAndPlaceholder()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' align='center-middle'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var text = f.GameObject.transform.Find("Text Area/Text").GetComponent<TMP_Text>();
            var ph = f.GameObject.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>();
            Assert.AreEqual(HorizontalAlignmentOptions.Center, text.horizontalAlignment);
            Assert.AreEqual(VerticalAlignmentOptions.Middle, text.verticalAlignment);
            Assert.AreEqual(HorizontalAlignmentOptions.Center, ph.horizontalAlignment);
            Assert.AreEqual(VerticalAlignmentOptions.Middle, ph.verticalAlignment);
        }

        // --- Caret / selection --------------------------------------------------

        // Setting caretColor must flip customCaretColor=true, otherwise TMP_InputField's
        // getter falls back to textComponent.color and the value is dead.
        [Test]
        public void Apply_CaretColor_EnablesCustomCaretColor()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' caretColor='#ff0000'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            var input = f.GameObject.GetComponent<TMP_InputField>();
            Assert.IsTrue(input.customCaretColor);
            Assert.AreEqual(Color.red, input.caretColor);
        }

        [Test]
        public void Apply_SelectionColor()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' selectionColor='#0000ff'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual(Color.blue, f.GameObject.GetComponent<TMP_InputField>().selectionColor);
        }

        [Test]
        public void Apply_CaretWidth()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' caretWidth='3'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.AreEqual(3, f.GameObject.GetComponent<TMP_InputField>().caretWidth);
        }

        // --- interactable bridge ------------------------------------------------

        // The common `interactable` attr is CanvasGroup-backed in Control; InputField must
        // also bridge it to the underlying Selectable so TMP shows its Disabled visual,
        // mirroring Btn/Toggle/Tab.OnAfterApply.
        [Test]
        public void Interactable_False_DisablesTMPInputField()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' interactable='false'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.IsFalse(f.GameObject.GetComponent<TMP_InputField>().interactable);
        }

        [Test]
        public void Interactable_DefaultsToTrue()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            Assert.IsTrue(f.GameObject.GetComponent<TMP_InputField>().interactable);
        }
    }
}
