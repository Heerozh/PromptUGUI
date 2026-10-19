using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A <c>&lt;Popover&gt;</c> and the focus (spec 2026-10-19 §4.8): with directional navigation on, an open
    /// panel with something focusable cages the focus — BindItems cells included — and closing puts it back
    /// on the anchor; a panel with nothing focusable neither cages nor moves it; a selection a mouse click
    /// left in the panel is moved out on close; a focus already elsewhere is left alone.
    /// </summary>
    public class PopoverFocusTests
    {
        private PuguiScreen _s;
        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                _ownEs = new GameObject("EventSystem", typeof(EventSystem));
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Cell'><Btn width='40' height='40'>·</Btn></Template>
  <Screen name='S'>
    <Btn id='a' anchor='center' size='40x40'>A</Btn>
    <Btn id='b' anchor='top-left' size='40x40'>B</Btn>
    <Popover id='m'><Btn id='first' width='80' height='30'>1</Btn><Btn id='second' width='80' height='30'>2</Btn></Popover>
    <Popover id='info'><Text width='120' height='30'>Just words</Text></Popover>
    <Popover id='grid'><ScrollList id='cells' width='90' height='90' columns='2' cellSize='40x40' spacing='4' itemTemplate='Cell'/></Popover>
  </Screen>
</PromptUGUI>");
            _s = UI.Open("S");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ownEs != null) Object.DestroyImmediate(_ownEs);
        }

        private static EventSystem Es() => Object.FindAnyObjectByType<EventSystem>();
        private Btn Btn(string id) => _s.Get<Btn>(id);
        private Popover Pop(string id) => _s.Get<Popover>(id);

        [Test]
        public void Opening_cages_the_focus_on_the_first_focusable()
        {
            UI.UseGamepadNavigation();
            Es().SetSelectedGameObject(Btn("a").GameObject);

            Pop("m").Open(Btn("a"));

            Assert.AreSame(Pop("m").GameObject, UI.Navigation.ContainmentRoot);
            Assert.AreSame(Btn("first").GameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void BindItems_cells_are_caged_too()
        {
            UI.UseGamepadNavigation();
            var list = _s.Get<ScrollList>("cells");
            using (list.BindItems(Observable.Return<IReadOnlyList<int>>(new[] { 1, 2, 3, 4 }), (IControl c, int i) => { }))
            {
                Pop("grid").Open(Btn("a"));

                var panel = Pop("grid").GameObject.transform;
                foreach (var slot in list.Slots)
                {
                    var sel = slot.GameObject.GetComponent<Selectable>();
                    var nav = sel.navigation;
                    Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, nav.mode, "converted like a declared control");
                    foreach (var target in new[] { nav.selectOnUp, nav.selectOnDown, nav.selectOnLeft, nav.selectOnRight })
                        Assert.IsTrue(target == null || target.transform.IsChildOf(panel),
                            "a direction from a cell never leaves the panel");
                }
            }
        }

        [Test]
        public void A_panel_without_focusables_does_not_cage_or_move_the_focus()
        {
            UI.UseGamepadNavigation();
            Es().SetSelectedGameObject(Btn("b").GameObject);

            Pop("info").Open(Btn("a"));

            Assert.AreNotSame(Pop("info").GameObject, UI.Navigation.ContainmentRoot);
            Assert.AreSame(Btn("b").GameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void Closing_returns_the_focus_to_the_anchor()
        {
            UI.UseGamepadNavigation();
            var m = Pop("m");
            m.Open(Btn("a"));
            m.GameObject.GetComponent<ModalEscapeListener>().FireForTests();

            Assert.AreSame(Btn("a").GameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void Uncaged_a_selection_left_in_the_panel_returns_to_the_anchor()
        {
            var m = Pop("m");
            m.Open(Btn("a"));
            Es().SetSelectedGameObject(Btn("second").GameObject);   // a mouse click selected a row

            m.Close();

            Assert.AreSame(Btn("a").GameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void OpenAt_restores_the_selection_from_before()
        {
            Es().SetSelectedGameObject(Btn("b").GameObject);
            var m = Pop("m");
            m.OpenAt(new Vector2(50f, 50f));
            Es().SetSelectedGameObject(Btn("first").GameObject);

            m.Close();

            Assert.AreSame(Btn("b").GameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void A_focus_already_elsewhere_is_left_alone()
        {
            var m = Pop("m");
            m.Open(Btn("a"));
            Es().SetSelectedGameObject(Btn("b").GameObject);
            m.Close();
            Assert.AreSame(Btn("b").GameObject, Es().currentSelectedGameObject);
        }
    }
}
