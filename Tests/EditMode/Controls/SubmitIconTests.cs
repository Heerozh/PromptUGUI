using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;InputField submitIcon&gt;</c> (spec 2026-10-18 §5.3 / §6.4): a submit button inside the
    /// field's right end that means the same as Enter, and that pressing never takes the selection
    /// away from the field.
    /// </summary>
    public class SubmitIconTests
    {
        private const string Icon = "PromptUGUI/Defaults/pugui#pugui_caret";
        private const string Row = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string fieldAttrs, string children = "")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + $"<Screen name='S'><InputField id='f' anchor='center' width='240' height='44' {fieldAttrs}>"
                                 + children + "</InputField></Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static Transform IconOf(InputField f) => f.GameObject.transform.Find("SubmitIcon");
        private static UnityImage GlyphOf(InputField f) => IconOf(f).Find("Glyph").GetComponent<UnityImage>();

        private static void Click(InputField f) =>
            ExecuteEvents.Execute(IconOf(f).gameObject,
                                  new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                                  ExecuteEvents.pointerClickHandler);

        [Test]
        public void Without_submitIcon_nothing_is_built()
            => Assert.IsNull(IconOf(Open("").Get<InputField>("f")));

        [Test]
        public void Built_with_its_size_and_color()
        {
            var f = Open($"submitIcon='{Icon}' submitIconSize='20' submitIconColor='#FF0000'").Get<InputField>("f");

            Assert.IsNotNull(IconOf(f));
            var glyph = GlyphOf(f);
            Assert.IsNotNull(glyph.sprite);
            Assert.AreEqual(new Vector2(20f, 20f), glyph.rectTransform.sizeDelta);
            Assert.AreEqual(Color.red, glyph.color);
            Assert.IsFalse(glyph.raycastTarget, "the square around it takes the pointer");
        }

        [Test]
        public void Defaults_to_24_px_and_the_glyph_colour()
        {
            var glyph = GlyphOf(Open($"submitIcon='{Icon}'").Get<InputField>("f"));
            Assert.AreEqual(new Vector2(24f, 24f), glyph.rectTransform.sizeDelta);
            Assert.AreEqual(ProceduralBuilders.DefaultGlyphColor, glyph.color);
        }

        [Test]
        public void The_click_area_is_a_square_as_tall_as_the_field_and_the_text_makes_room_for_it()
        {
            var f = Open($"submitIcon='{Icon}' padding='4,6,4,8'").Get<InputField>("f");
            Canvas.ForceUpdateCanvases();
            var area = f.GameObject.transform.Find("Text Area") as RectTransform;
            var icon = (RectTransform)IconOf(f);

            Assert.AreEqual(44f, icon.rect.width, 0.5f);
            Assert.AreEqual(44f, icon.rect.height, 0.5f);
            Assert.AreEqual(-(6f + 44f), area.offsetMax.x, 0.01f, "the author's right padding, plus the square");
            Assert.AreEqual(8f, area.offsetMin.x, 0.01f);
        }

        [Test]
        public void Pressing_it_never_moves_the_selection_off_the_field()
        {
            var f = Open($"submitIcon='{Icon}'").Get<InputField>("f");
            Assert.IsNull(IconOf(f).GetComponent<Selectable>());
            Assert.AreSame(f.GameObject, ExecuteEvents.GetEventHandler<ISelectHandler>(IconOf(f).gameObject));
            Assert.AreSame(IconOf(f).gameObject, ExecuteEvents.GetEventHandler<IPointerDownHandler>(IconOf(f).gameObject),
                           "it takes the press, so TMP does not move the caret");
        }

        // ── submitting ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Without_a_list_it_raises_the_fields_submit()
        {
            var f = Open($"submitIcon='{Icon}' text='hi'").Get<InputField>("f");
            var submits = new List<string>();
            f.OnSubmit.Subscribe(submits.Add);

            Click(f);

            CollectionAssert.AreEqual(new[] { "hi" }, submits);
        }

        [Test]
        public void With_a_list_and_no_highlight_it_is_a_typed_submit()
        {
            var screen = Open($"submitIcon='{Icon}' text='ap'", "<Suggestions id='s' itemTemplate='Row' transition='0'/>");
            var f = screen.Get<InputField>("f");
            var list = screen.Get<Suggestions>("s");
            var log = new List<string>();
            f.OnSubmit.Subscribe(t => log.Add("field:" + t));
            list.OnSubmit.Subscribe(s => log.Add($"list:{s.Text}:{s.ChosenIndex}"));
            f.SetEditingForTests(true);

            Click(f);

            CollectionAssert.AreEqual(new[] { "field:ap", "list:ap:-1" }, log);
            Assert.IsFalse(f.IsEditing, "a submit while editing ends the edit");
        }

        [Test]
        public void With_a_highlight_it_chooses_that_suggestion()
        {
            var screen = Open($"submitIcon='{Icon}' text='ap'", "<Suggestions id='s' itemTemplate='Row' transition='0'/>");
            var f = screen.Get<InputField>("f");
            var list = screen.Get<Suggestions>("s");
            var log = new List<string>();
            f.OnSubmit.Subscribe(t => log.Add("field:" + t));
            list.OnSubmit.Subscribe(s => log.Add($"list:{s.Text}:{s.ChosenIndex}"));
            f.SetEditingForTests(true);
            list.NotifyUserInput();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "apple", "apricot" }), (IControl r, string v) => { });
            list.HighlightedIndex = 1;

            Click(f);

            CollectionAssert.AreEqual(new[] { "list:ap:1" }, log);
            Assert.IsFalse(list.IsOpen);
        }

        [Test]
        public void Not_interactable_it_reads_disabled_and_does_not_submit()
        {
            var f = Open($"submitIcon='{Icon}' interactable='false' text='hi'").Get<InputField>("f");
            var submits = new List<string>();
            f.OnSubmit.Subscribe(submits.Add);

            Click(f);

            CollectionAssert.IsEmpty(submits);
            Assert.AreEqual(InteractState.Disabled, IconOf(f).GetComponent<FocuslessPress>().Current);
            Assert.Less(GlyphOf(f).color.a, 1f, "dimmed like a disabled uGUI control");
        }
    }
}
