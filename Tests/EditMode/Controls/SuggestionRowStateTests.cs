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
    /// A row template's <c>&lt;Show on="state-*"&gt;</c> blocks read the row's
    /// <see cref="FocuslessPress"/> (spec 2026-10-18 §5.5): <c>selected</c> is the keyboard highlight,
    /// <c>hover</c> the pointer, folded the way a Toggle's state is.
    /// </summary>
    public class SuggestionRowStateTests
    {
        private const string Row =
            "<Template name='Row'><Frame height='30'>" +
            "<Show id='n' on='state-normal'><Frame id='nf'/></Show>" +
            "<Show id='h' on='state-hover'><Frame id='hf'/></Show>" +
            "<Show id='sel' on='state-selected'><Frame id='sf'/></Show>" +
            "</Frame></Template>";

        private IControl _row;
        private FocuslessPress _press;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + "<Screen name='S'><InputField id='f' width='240' height='44'>"
                                 + "<Suggestions id='s' itemTemplate='Row'/></InputField></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var list = screen.Get<Suggestions>("s");
            // Open: pointer events only reach a row in an active hierarchy.
            screen.Get<InputField>("f").SetEditingForTests(true);
            list.NotifyUserInput();
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a" }), (IControl row, string v) => rows.Add(row));
            _row = rows[0];
            _press = _row.GameObject.transform.parent.GetComponent<FocuslessPress>();
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private bool On(string id) => _row.Get<Show>(id).GameObject.activeSelf;

        private void Pointer(ExecuteEvents.EventFunction<IPointerEnterHandler> f) =>
            ExecuteEvents.Execute(_press.gameObject, new PointerEventData(EventSystem.current), f);

        [Test]
        public void Show_state_selected_follows_the_highlight()
        {
            Assert.IsFalse(On("sel"));
            _press.SetOn(true);
            Assert.IsTrue(On("sel"));
            _press.SetOn(false);
            Assert.IsFalse(On("sel"));
        }

        [Test]
        public void Show_state_hover_follows_the_pointer()
        {
            Pointer(ExecuteEvents.pointerEnterHandler);
            Assert.IsTrue(On("h"));
            ExecuteEvents.Execute(_press.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            Assert.IsFalse(On("h"));
        }

        [Test]
        public void Hover_overrides_selected_while_inside()
        {
            _press.SetOn(true);
            Pointer(ExecuteEvents.pointerEnterHandler);

            Assert.IsTrue(On("h"));
            Assert.IsFalse(On("sel"));
        }

        [Test]
        public void Show_state_normal_hides_while_a_claimed_state_is_on()
        {
            Assert.IsTrue(On("n"));
            _press.SetOn(true);
            Assert.IsFalse(On("n"));
            _press.SetOn(false);
            Assert.IsTrue(On("n"));
        }
    }
}
