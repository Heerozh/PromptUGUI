using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// When the list shows (spec 2026-10-18 §6.1): open ⟺ editing ∧ armed ∧ something to show ∧ the
    /// field usable. Typing arms it, <c>Open()</c> arms it; Escape, a submit, the end of the edit, a
    /// covering layer and <c>Close()</c> disarm it; an empty push only hides it.
    /// </summary>
    public class SuggestionsVisibilityTests
    {
        private const string Row = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private Screen _screen;
        private InputField _field;
        private Suggestions _list;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            Open();
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private void Open(string fieldAttrs = "", string extra = "")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + $"<Screen name='S'><InputField id='f' width='240' height='44' {fieldAttrs}>"
                                 + "<Suggestions id='s' itemTemplate='Row' transition='0'/></InputField>"
                                 + extra + "</Screen></PromptUGUI>");
            _screen = UI.Open("S");
            _field = _screen.Get<InputField>("f");
            _list = _screen.Get<Suggestions>("s");
        }

        private void Push(params string[] items) =>
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => { });

        private void Type(params string[] results)
        {
            _list.NotifyUserInput();
            Push(results);
        }

        [Test]
        public void Editing_alone_does_not_open()
        {
            Push("a", "b");
            _field.SetEditingForTests(true);
            Assert.IsFalse(_list.IsOpen, "focusing the field does not bring last round's rows back");
        }

        [Test]
        public void Input_arms_and_a_non_empty_push_opens()
        {
            _field.SetEditingForTests(true);
            _list.NotifyUserInput();
            Assert.IsFalse(_list.IsOpen, "nothing to show yet");

            Push("a", "b");

            Assert.IsTrue(_list.IsOpen);
            Assert.IsTrue(_list.GameObject.activeSelf);
        }

        [Test]
        public void Open_requires_editing()
        {
            Push("a");
            _list.Open();
            Assert.IsFalse(_list.IsOpen);

            _field.SetEditingForTests(true);
            _list.Open();
            Assert.IsTrue(_list.IsOpen, "Open() arms; showing still needs the edit");
        }

        [Test]
        public void An_empty_push_closes_without_disarming()
        {
            _field.SetEditingForTests(true);
            Type("a");
            Assert.IsTrue(_list.IsOpen);

            Push();
            Assert.IsFalse(_list.IsOpen);

            Push("b");
            Assert.IsTrue(_list.IsOpen, "still armed: the next non-empty push re-opens it");
        }

        [Test]
        public void Escape_closes_and_disarms()
        {
            _field.SetEditingForTests(true);
            Type("a");

            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress()));

            Assert.IsFalse(_list.IsOpen);
            Push("b");
            Assert.IsFalse(_list.IsOpen, "disarmed: a late result does not pop it back up");
        }

        [Test]
        public void Editing_end_closes_and_disarms()
        {
            _field.SetEditingForTests(true);
            Type("a");

            _field.SetEditingForTests(false);
            Assert.IsFalse(_list.IsOpen);

            _field.SetEditingForTests(true);
            Push("b");
            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void Close_disarms()
        {
            _field.SetEditingForTests(true);
            Type("a");

            _list.Close();
            Assert.IsFalse(_list.IsOpen);
            Push("b");
            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void Not_interactable_or_readOnly_never_opens()
        {
            UI.ResetForTests();
            Open("interactable='false'");
            _field.SetEditingForTests(true);
            Type("a");
            Assert.IsFalse(_list.IsOpen, "not interactable");

            UI.ResetForTests();
            Open("readOnly='true'");
            _field.SetEditingForTests(true);
            Type("a");
            Assert.IsFalse(_list.IsOpen, "read-only");
        }

        [Test]
        public void Setting_Interactable_false_from_code_closes()
        {
            _field.SetEditingForTests(true);
            Type("a");

            _field.Interactable = false;

            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void A_field_hidden_by_a_variant_closes()
        {
            UI.ResetForTests();
            Open("hidden.mobile='true'");
            _field.SetEditingForTests(true);
            Type("a");
            Assert.IsTrue(_list.IsOpen, "precondition");

            UI.Variants.Set("mobile", true);

            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void Covered_closes_and_ends_editing()
        {
            _field.SetEditingForTests(true);
            Type("a");

            PopupChain.DismissBelow(int.MaxValue);

            Assert.IsFalse(_list.IsOpen);
            Assert.IsFalse(_field.IsEditing, "no typing on into a field the user can no longer see");
        }

        [Test]
        public void A_modal_opened_over_the_field_closes_it_and_ends_editing()
        {
            _field.SetEditingForTests(true);
            Type("a");

            _ = MessageBox.Open("hi");

            Assert.IsFalse(_list.IsOpen);
            Assert.IsFalse(_field.IsEditing);
        }

        [Test]
        public void A_push_while_open_keeps_the_side_and_resets_the_highlight()
        {
            PopupBounds.BoundsOverride = () => new Rect(-400f, -400f, 800f, 800f);
            _field.SetEditingForTests(true);
            Type("a", "b", "c");
            _list.HighlightedIndex = 1;
            var highlights = new List<int>();
            _list.OnHighlightChanged.Subscribe(highlights.Add);
            var pivotY = _list.RectTransform.pivot.y;

            Push("a", "b", "c", "d");

            Assert.AreEqual(-1, _list.HighlightedIndex);
            CollectionAssert.AreEqual(new[] { -1 }, highlights);
            Assert.AreEqual(pivotY, _list.RectTransform.pivot.y, "same side");
            Assert.IsTrue(_list.IsOpen);
        }

        [Test]
        public void Opened_and_Closed_fire()
        {
            var log = new List<string>();
            _list.OnOpened.Subscribe(_ => log.Add("open"));
            _list.OnClosed.Subscribe(_ => log.Add("close"));
            _field.SetEditingForTests(true);

            Type("a");
            _list.Close();

            CollectionAssert.AreEqual(new[] { "open", "close" }, log);
        }
    }
}
