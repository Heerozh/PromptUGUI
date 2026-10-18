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
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Why the field keeps editing while the list is used (spec 2026-10-18 §6.5) — three structural
    /// rules, checked the way the input module resolves its handlers: a press anywhere in the panel
    /// finds the field as its select handler; nothing in a row is a Selectable; every press is taken
    /// by something in the panel before it can bubble to the field's text handling.
    /// </summary>
    public class SuggestionsFocusTests
    {
        private const string Row = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private InputField _field;
        private Suggestions _list;
        private IControl _row;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + "<Screen name='S'><InputField id='f' width='240' height='44'>"
                                 + "<Suggestions id='s' itemTemplate='Row'/></InputField></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            _field = screen.Get<InputField>("f");
            _list = screen.Get<Suggestions>("s");
            // Open: the input module only reaches components in an active hierarchy.
            _field.SetEditingForTests(true);
            _list.NotifyUserInput();
            var rows = new List<IControl>();
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b" }),
                            (IControl row, string v) => rows.Add(row));
            _row = rows[0];
            Assert.IsTrue(_list.IsOpen, "precondition");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private GameObject Label => _row.Get<Text>("label").GameObject;
        private GameObject Shell => _row.GameObject.transform.parent.gameObject;
        private GameObject Blank => _list.Viewport.gameObject;

        [Test]
        public void The_select_handler_above_a_row_or_the_blank_panel_is_the_field()
        {
            Assert.AreSame(_field.GameObject, ExecuteEvents.GetEventHandler<ISelectHandler>(Label));
            Assert.AreSame(_field.GameObject, ExecuteEvents.GetEventHandler<ISelectHandler>(Shell));
            Assert.AreSame(_field.GameObject, ExecuteEvents.GetEventHandler<ISelectHandler>(Blank));
        }

        [Test]
        public void A_row_has_no_Selectable()
            => Assert.AreEqual(0, Shell.GetComponentsInChildren<Selectable>(true).Length);

        [Test]
        public void A_press_on_the_blank_panel_stops_at_the_sink()
        {
            Assert.AreSame(_list.GameObject, ExecuteEvents.GetEventHandler<IPointerDownHandler>(Blank));
            Assert.AreSame(_list.GameObject, ExecuteEvents.GetEventHandler<IPointerClickHandler>(Blank));
            Assert.IsNotNull(_list.GameObject.GetComponent<PointerSink>());
        }

        [Test]
        public void A_press_on_a_row_goes_to_its_press()
        {
            Assert.AreSame(Shell, ExecuteEvents.GetEventHandler<IPointerDownHandler>(Label));
            Assert.AreSame(Shell, ExecuteEvents.GetEventHandler<IPointerClickHandler>(Label));
            Assert.IsNotNull(Shell.GetComponent<FocuslessPress>());
        }

        [Test]
        public void Dragging_or_scrolling_a_row_scrolls_the_panel()
        {
            Assert.AreSame(_list.GameObject, ExecuteEvents.GetEventHandler<IDragHandler>(Label));
            Assert.AreSame(_list.GameObject, ExecuteEvents.GetEventHandler<IScrollHandler>(Label));
        }

        [Test]
        public void The_whole_row_takes_the_pointer()
        {
            var catcher = Shell.GetComponent<Graphic>();
            Assert.IsNotNull(catcher, "a press between the template's own graphics still lands on the row");
            Assert.IsTrue(catcher.raycastTarget);
        }
    }
}
