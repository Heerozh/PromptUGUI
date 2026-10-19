using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using InputField = PromptUGUI.Controls.InputField;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Popover closeOnEditEnd="true"&gt;</c> (spec 2026-10-19-input-begin-edit §4.2): the edit in its panel
    /// ending closes it one tick after the end was seen — not when the edit begins again in between, not while
    /// a suggestion row is held (SG-D15), not when the user went on to another control in the panel.
    /// </summary>
    public class PopoverCloseOnEditEndTests
    {
        private PuguiScreen _s;
        private Popover _m;
        private InputField _f;
        private GameObject _es;
        private readonly List<PopoverCloseReason> _closes = new();
        private readonly List<IControl> _rows = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closes.Clear();
            _rows.Clear();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Hit'><Frame height='30'><Text id='label'>x</Text></Frame></Template>
  <Screen name='S'>
    <Btn id='a' anchor='center' size='40x40'>A</Btn>
    <Popover id='m' on='click@a' closeOnEditEnd='true' transition='0'>
      <InputField id='f' width='160' height='40'>
        <Suggestions id='list' itemTemplate='Hit' transition='0'/>
      </InputField>
      <Btn id='inside' width='80' height='30'>1</Btn>
    </Popover>
    <Popover id='plain' transition='0'><InputField id='g' width='160' height='40'/></Popover>
  </Screen>
</PromptUGUI>");
            _s = UI.Open("S");
            _m = _s.Get<Popover>("m");
            _f = _s.Get<InputField>("f");
            _m.OnClosed.Subscribe(_closes.Add);
            _m.Open();
            Assume.That(_m.IsOpen, "precondition");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) Object.DestroyImmediate(_es);
        }

        private EventSystem Es
        {
            get
            {
                if (_es == null) _es = new GameObject("ES", typeof(EventSystem));
                return _es.GetComponent<EventSystem>();
            }
        }

        private void Tick(int n = 1)
        {
            for (var i = 0; i < n; i++) _m.TickForTests();
        }

        private void EditThenEnd()
        {
            _f.SetEditingForTests(true);
            Tick();
            _f.SetEditingForTests(false);
        }

        [Test]
        public void The_end_of_the_edit_closes_it_one_tick_after_it_was_seen()
        {
            EditThenEnd();

            Tick();
            Assert.IsTrue(_m.IsOpen, "the tick that sees the end only notes it");
            Tick();

            Assert.IsFalse(_m.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.EditEnded }, _closes);
        }

        [Test]
        public void An_edit_that_begins_again_in_between_keeps_it_open()
        {
            EditThenEnd();
            Tick();
            _f.SetEditingForTests(true);   // BeginEdit from the submit handler, a press on another field
            Tick(3);

            Assert.IsTrue(_m.IsOpen);
        }

        [Test]
        public void A_BeginEdit_not_yet_carried_out_counts_as_editing()
        {
            EditThenEnd();
            _f.SetEditingForTests(null);   // back to the live state: TMP is not editing
            Assume.That(!_f.IsEditing);
            Assert.IsTrue(_f.BeginEdit());

            Tick(3);

            Assert.IsTrue(_m.IsOpen, "TMP carries the request out in its LateUpdate, which EditMode never runs");
        }

        [Test]
        public void Never_edited_keeps_it_open()
        {
            Tick(5);
            Assert.IsTrue(_m.IsOpen);
        }

        [Test]
        public void Without_the_attribute_nothing_closes()
        {
            var plain = _s.Get<Popover>("plain");
            plain.Open(_s.Get("a"));
            Assume.That(plain.IsOpen);
            var g = _s.Get<InputField>("g");
            g.SetEditingForTests(true);
            plain.TickForTests();
            g.SetEditingForTests(false);
            plain.TickForTests();
            plain.TickForTests();

            Assert.IsTrue(plain.IsOpen);
        }

        [Test]
        public void An_open_suggestion_list_holds_it_until_the_press_is_decided()
        {
            var list = _s.Get<Suggestions>("list");
            _f.SetEditingForTests(true);
            list.NotifyUserInput();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b" }), (IControl row, string v) => _rows.Add(row));
            Assume.That(list.IsOpen);
            Tick();
            var row = _rows[0].GameObject.transform.parent.gameObject;
            var e = new PointerEventData(null) { button = PointerEventData.InputButton.Left, eligibleForClick = true };
            ExecuteEvents.Execute(row, e, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(row, e, ExecuteEvents.pointerDownHandler);

            _f.SetEditingForTests(false);   // Android hid the keyboard under the press
            Assume.That(list.IsOpen, "SG-D15: the list waits for the release");
            Tick(3);
            Assert.IsTrue(_m.IsOpen, "the panel must still take the release");

            ExecuteEvents.Execute(row, e, ExecuteEvents.pointerExitHandler);
            ExecuteEvents.Execute(row, e, ExecuteEvents.pointerUpHandler);   // released elsewhere
            Assume.That(!list.IsOpen);
            Tick(2);

            Assert.IsFalse(_m.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.EditEnded }, _closes);
        }

        [Test]
        public void Going_on_to_another_control_in_the_panel_keeps_it_open()
        {
            EditThenEnd();
            Es.SetSelectedGameObject(_s.Get("inside").GameObject);
            Tick(3);
            Assert.IsTrue(_m.IsOpen);

            Es.SetSelectedGameObject(null);
            Tick(3);
            Assert.IsTrue(_m.IsOpen, "that edit's end no longer counts");
        }

        [Test]
        public void A_new_edit_after_that_counts_again()
        {
            EditThenEnd();
            Es.SetSelectedGameObject(_s.Get("inside").GameObject);
            Tick(2);
            Es.SetSelectedGameObject(null);

            EditThenEnd();
            Tick(2);

            Assert.IsFalse(_m.IsOpen);
        }

        [Test]
        public void The_selection_left_on_the_field_goes_back_to_the_anchor()
        {
            Es.SetSelectedGameObject(_f.GameObject);
            EditThenEnd();
            Tick(2);

            Assert.IsFalse(_m.IsOpen);
            Assert.AreSame(_s.Get("a").GameObject, Es.currentSelectedGameObject);
        }

        [Test]
        public void An_edit_seen_before_a_close_does_not_carry_into_the_next_open()
        {
            _f.SetEditingForTests(true);
            Tick();
            _m.Close();
            _f.SetEditingForTests(false);
            _closes.Clear();

            _m.Open();
            Tick(3);

            Assert.IsTrue(_m.IsOpen);
        }
    }
}
