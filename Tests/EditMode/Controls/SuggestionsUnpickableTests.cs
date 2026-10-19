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
    /// Rows that cannot be chosen (spec 2026-10-19-suggestions-width-pickable §4.2): a bind that sets
    /// <c>slot.Interactable = false</c> gets a row that shows (<c>state-disabled</c>), is skipped by the highlight
    /// and does nothing when pressed — "No match", "Loading…".
    /// </summary>
    public class SuggestionsUnpickableTests
    {
        private InputField _field;
        private Suggestions _list;
        private readonly List<IControl> _rows = new();
        private readonly List<SuggestionSubmit> _submits = new();
        private Subject<IReadOnlyList<string>> _source;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                 + "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
                                 + "<Screen name='S'><InputField id='f' width='240' height='44'>"
                                 + "<Suggestions id='s' itemTemplate='Row' transition='0'/></InputField></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            _field = screen.Get<InputField>("f");
            _list = screen.Get<Suggestions>("s");
            _rows.Clear();
            _submits.Clear();
            _list.OnSubmit.Subscribe(_submits.Add);
            _field.SetEditingForTests(true);
            _list.NotifyUserInput();
            _source = new Subject<IReadOnlyList<string>>();
            // An item starting with '!' is a message: shown, never chosen.
            _list.BindItems(_source, (IControl row, string v) =>
            {
                _rows.Add(row);
                if (v.StartsWith("!")) row.Interactable = false;
            });
        }

        [TearDown]
        public void TearDown()
        {
            _source?.Dispose();
            UI.ResetForTests();
        }

        private void Push(params string[] items)
        {
            _rows.Clear();
            _source.OnNext(items);
        }

        private FocuslessPress PressOf(int i) => _rows[i].GameObject.transform.parent.GetComponent<FocuslessPress>();

        private static PointerEventData Left() =>
            new(null) { button = PointerEventData.InputButton.Left, eligibleForClick = true };

        [Test]
        public void A_row_bound_not_interactable_reads_disabled()
        {
            Push("a", "!none", "b");

            Assert.AreEqual(InteractState.Disabled, PressOf(1).Current);
            Assert.AreNotEqual(InteractState.Disabled, PressOf(0).Current);
        }

        [Test]
        public void The_next_push_starts_every_row_pickable_again()
        {
            Push("!none");
            Push("a");

            Assert.AreNotEqual(InteractState.Disabled, PressOf(0).Current, "the reused row is pickable unless the bind says not");
        }

        [Test]
        public void The_highlight_skips_rows_that_cannot_be_chosen()
        {
            Push("a", "!sep", "b");

            _list.MoveHighlight(+1);
            Assert.AreEqual(0, _list.HighlightedIndex);
            _list.MoveHighlight(+1);
            Assert.AreEqual(2, _list.HighlightedIndex, "skipped row 1");
            _list.MoveHighlight(-1);
            Assert.AreEqual(0, _list.HighlightedIndex);
        }

        [Test]
        public void With_nothing_to_choose_the_arrows_do_nothing()
        {
            Push("!No match");

            _list.MoveHighlight(+1);
            Assert.AreEqual(-1, _list.HighlightedIndex);
            _list.MoveHighlight(-1);
            Assert.AreEqual(-1, _list.HighlightedIndex);
        }

        [Test]
        public void Setting_the_highlight_on_one_is_ignored()
        {
            Push("a", "!sep");
            _list.HighlightedIndex = 1;
            Assert.AreEqual(-1, _list.HighlightedIndex);
        }

        [Test]
        public void A_message_alone_opens_the_list()
        {
            Push("!No match");
            Assert.IsTrue(_list.IsOpen, "one non-empty push: the message shows");
            Assert.AreEqual(1, _list.Count);
        }

        [Test]
        public void Clicking_one_chooses_nothing_and_keeps_editing()
        {
            Push("!No match");
            var shell = PressOf(0).gameObject;

            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerClickHandler);

            CollectionAssert.IsEmpty(_submits);
            Assert.IsTrue(_list.IsOpen);
            Assert.IsTrue(_field.IsEditing);
        }

        [Test]
        public void Choosing_one_from_code_does_nothing()
        {
            Push("a", "!sep");
            _list.CommitChosen(1);
            CollectionAssert.IsEmpty(_submits);
            Assert.IsTrue(_list.IsOpen);
        }

        [Test]
        public void An_edit_ending_while_one_is_held_closes_without_choosing()
        {
            Push("a", "!sep");
            var shell = PressOf(1).gameObject;
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerDownHandler);

            _field.SetEditingForTests(false);
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(shell, Left(), ExecuteEvents.pointerClickHandler);

            CollectionAssert.IsEmpty(_submits);
            Assert.IsFalse(_list.IsOpen);
        }
    }
}
