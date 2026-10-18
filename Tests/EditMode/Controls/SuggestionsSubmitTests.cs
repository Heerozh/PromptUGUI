using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using InputField = PromptUGUI.Controls.InputField;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The highlight and the submits (spec 2026-10-18 §6.2 / §6.4): the highlight cycles through
    /// "none → 0 → … → n−1 → none" and scrolls its row into view; a chosen suggestion is reported on
    /// <c>Suggestions.OnSubmit</c> only, with its index, and ends the edit.
    /// </summary>
    public class SuggestionsSubmitTests
    {
        private InputField _field;
        private Suggestions _list;
        private readonly List<IControl> _rows = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            Open("");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private void Open(string listAttrs)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                 + "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
                                 + "<Screen name='S'><InputField id='f' anchor='center' width='240' height='44'>"
                                 + $"<Suggestions id='s' itemTemplate='Row' transition='0' {listAttrs}/></InputField>"
                                 + "</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            _field = screen.Get<InputField>("f");
            _list = screen.Get<Suggestions>("s");
            _field.TextValue = "ap";
        }

        private void Show(int count)
        {
            _rows.Clear();
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "r" + i;
            _field.SetEditingForTests(true);
            _list.NotifyUserInput();
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => _rows.Add(row));
            Canvas.ForceUpdateCanvases();
        }

        private FocuslessPress PressOf(int i) => _rows[i].GameObject.transform.parent.GetComponent<FocuslessPress>();

        // ── highlight ──────────────────────────────────────────────────────────────────────

        [Test]
        public void HighlightedIndex_set_clamps_and_is_ignored_while_closed()
        {
            _list.HighlightedIndex = 1;
            Assert.AreEqual(-1, _list.HighlightedIndex, "closed: ignored");

            Show(3);
            _list.HighlightedIndex = 7;
            Assert.AreEqual(2, _list.HighlightedIndex);
            _list.HighlightedIndex = -5;
            Assert.AreEqual(-1, _list.HighlightedIndex);
        }

        [Test]
        public void OnHighlightChanged_fires_on_change_only()
        {
            Show(3);
            var seen = new List<int>();
            _list.OnHighlightChanged.Subscribe(seen.Add);

            _list.HighlightedIndex = 1;
            _list.HighlightedIndex = 1;
            _list.HighlightedIndex = -1;

            CollectionAssert.AreEqual(new[] { 1, -1 }, seen);
        }

        [Test]
        public void The_highlighted_row_reads_selected()
        {
            Show(3);
            _list.HighlightedIndex = 1;
            Assert.AreEqual(InteractState.Selected, PressOf(1).Current);
            Assert.AreEqual(InteractState.Normal, PressOf(0).Current);

            _list.HighlightedIndex = 2;
            Assert.AreEqual(InteractState.Normal, PressOf(1).Current);
        }

        [Test]
        public void MoveHighlight_cycles_through_none()
        {
            Show(2);
            var down = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                _list.MoveHighlight(+1);
                down.Add(_list.HighlightedIndex);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, -1, 0 }, down);

            _list.HighlightedIndex = -1;
            var up = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                _list.MoveHighlight(-1);
                up.Add(_list.HighlightedIndex);
            }
            CollectionAssert.AreEqual(new[] { 1, 0, -1 }, up);
        }

        // ── clicking a row ─────────────────────────────────────────────────────────────────

        [Test]
        public void Clicking_row_i_submits_it_and_closes()
        {
            Show(3);
            var fieldSubmits = new List<string>();
            var submits = new List<SuggestionSubmit>();
            _field.OnSubmit.Subscribe(fieldSubmits.Add);
            _list.OnSubmit.Subscribe(submits.Add);

            ExecuteEvents.Execute(PressOf(2).gameObject,
                                  new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                                  ExecuteEvents.pointerClickHandler);

            Assert.AreEqual(1, submits.Count);
            Assert.AreEqual("ap", submits[0].Text, "choosing does not rewrite the field");
            Assert.AreEqual(2, submits[0].ChosenIndex);
            Assert.IsTrue(submits[0].IsChosen);
            CollectionAssert.IsEmpty(fieldSubmits, "InputField.OnSubmit means 'the typed text was submitted'");
            Assert.IsFalse(_list.IsOpen);
            Assert.IsFalse(_field.IsEditing, "the edit ends");
        }

        // ── keeping the highlight in view ──────────────────────────────────────────────────

        [Test]
        public void Reveal_offset_brings_a_row_fully_into_view()
        {
            // Rows 30 tall, viewport 100, content 300, currently scrolled to 0.
            Assert.AreEqual(0f, Suggestions.RevealOffset(30f, 60f, 100f, 0f, 300f), "already visible: no move");
            Assert.AreEqual(50f, Suggestions.RevealOffset(120f, 150f, 100f, 0f, 300f), "below: bottom edge to the bottom");
            Assert.AreEqual(30f, Suggestions.RevealOffset(30f, 60f, 100f, 90f, 300f), "above: top edge to the top");
            Assert.AreEqual(200f, Suggestions.RevealOffset(270f, 330f, 100f, 0f, 300f), "never past the end");
        }

        [Test]
        public void Highlighting_the_last_row_scrolls_the_content()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            Open("maxHeight='60'");
            Show(6);

            _list.HighlightedIndex = 5;

            Assert.Greater(_list.Content.anchoredPosition.y, 0f);
        }
    }
}
