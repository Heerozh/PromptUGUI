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
    /// SG-D15 (spec 2026-10-18 §6.5): an edit that ends while a row is held down — Android may hide the
    /// keyboard, ending the edit, before the tap on a suggestion is released — waits for that press: a
    /// release on the same row chooses it, a release anywhere else closes the list.
    /// </summary>
    public class SuggestionsPressDeferralTests
    {
        private InputField _field;
        private Suggestions _list;
        private readonly List<IControl> _rows = new();
        private readonly List<SuggestionSubmit> _submits = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
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
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b" }), (IControl row, string v) => _rows.Add(row));
            Assert.IsTrue(_list.IsOpen, "precondition");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private GameObject Row(int i) => _rows[i].GameObject.transform.parent.gameObject;

        private static PointerEventData Left() =>
            new(EventSystem.current) { button = PointerEventData.InputButton.Left, eligibleForClick = true };

        private static void Send<T>(GameObject go, ExecuteEvents.EventFunction<T> f) where T : IEventSystemHandler =>
            ExecuteEvents.Execute(go, Left(), f);

        [Test]
        public void Without_a_press_the_end_of_the_edit_closes_at_once()
        {
            _field.SetEditingForTests(false);
            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void Editing_end_during_a_row_press_waits_for_the_release_on_that_row()
        {
            Send<IPointerEnterHandler>(Row(1), ExecuteEvents.pointerEnterHandler);
            Send<IPointerDownHandler>(Row(1), ExecuteEvents.pointerDownHandler);

            _field.SetEditingForTests(false);
            Assert.IsTrue(_list.IsOpen, "the press is still deciding");

            Send<IPointerUpHandler>(Row(1), ExecuteEvents.pointerUpHandler);
            Send<IPointerClickHandler>(Row(1), ExecuteEvents.pointerClickHandler);

            Assert.AreEqual(1, _submits.Count);
            Assert.AreEqual(1, _submits[0].ChosenIndex);
            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void A_release_elsewhere_closes_it()
        {
            Send<IPointerEnterHandler>(Row(0), ExecuteEvents.pointerEnterHandler);
            Send<IPointerDownHandler>(Row(0), ExecuteEvents.pointerDownHandler);
            _field.SetEditingForTests(false);

            Send<IPointerExitHandler>(Row(0), ExecuteEvents.pointerExitHandler);
            Send<IPointerUpHandler>(Row(0), ExecuteEvents.pointerUpHandler);

            CollectionAssert.IsEmpty(_submits);
            Assert.IsFalse(_list.IsOpen);
        }
    }
}
