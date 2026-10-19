using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Popovers on the popup chain (spec 2026-10-19 §4.9): opened from a control inside an open popover, a
    /// popover is its child wherever it is declared — layered two above it, closed first by Escape and by a
    /// press in the parent; anything unrelated opening closes the whole line.
    /// </summary>
    public class PopoverNestingTests
    {
        private PuguiScreen _s;
        private Popover _m;
        private Popover _q;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame height='30'><Text id='t' anchor='stretch'/></Frame></Template>
  <Screen name='S'>
    <Btn id='a' anchor='center' size='40x40'>A</Btn>
    <Popover id='m'>
      <Btn id='first' width='80' height='30'>1</Btn>
      <Btn id='second' width='80' height='30'>2</Btn>
      <InputField id='field' width='160' height='30'>
        <Suggestions id='sug' itemTemplate='Row'/>
      </InputField>
    </Popover>
    <Popover id='q'><Frame width='60' height='20'/></Popover>
    <TabMenu id='menu' anchor='top-right' transition='0'><Tab id='x' text='X' isOn='true'/></TabMenu>
  </Screen>
</PromptUGUI>");
            _s = UI.Open("S");
            _m = _s.Get<Popover>("m");
            _q = _s.Get<Popover>("q");
            _m.Open(_s.Get<Btn>("a"));
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private int RootOrder => _s.RootGameObject.GetComponent<Canvas>().sortingOrder;

        [Test]
        public void A_popover_opened_from_inside_another_is_its_child()
        {
            _q.Open(_s.Get<Btn>("first"));

            Assert.IsTrue(_m.IsOpen, "its anchor is in m's panel: m is its parent");
            Assert.IsTrue(_q.IsOpen);
            Assert.AreEqual(2, PopupChain.Count);
            Assert.AreEqual(RootOrder + PopupChain.SortingOffset + 2, _q.GameObject.GetComponent<Canvas>().sortingOrder,
                "one level up the chain, two orders up");
        }

        [Test]
        public void Escape_closes_the_child_then_the_parent()
        {
            _q.Open(_s.Get<Btn>("first"));

            EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress());
            Assert.IsFalse(_q.IsOpen);
            Assert.IsTrue(_m.IsOpen);

            EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress());
            Assert.IsFalse(_m.IsOpen);
        }

        [Test]
        public void An_unrelated_menu_closes_the_whole_chain()
        {
            _q.Open(_s.Get<Btn>("first"));
            _s.Get<TabMenu>("menu").Expand();

            Assert.IsFalse(_q.IsOpen);
            Assert.IsFalse(_m.IsOpen);
            Assert.AreEqual(1, PopupChain.Count);
        }

        [Test]
        public void OpenAt_from_inside_a_popover_declared_outside_replaces_it()
        {
            _q.OpenAt(new Vector2(30f, 30f));

            Assert.IsTrue(_q.IsOpen);
            Assert.IsFalse(_m.IsOpen, "no anchor in m's panel and declared outside it: not its child");
        }

        [Test]
        public void A_field_with_suggestions_inside_a_popover_stacks_on_it()
        {
            var field = _s.Get<InputField>("field");
            var sug = field.Suggestions;
            field.SetEditingForTests(true);
            using (sug.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "apple", "apricot" }),
                       (IControl row, string s) => row.Get<Text>("t").TextValue = s))
            {
                sug.Open();

                Assert.IsTrue(sug.IsOpen);
                Assert.IsTrue(_m.IsOpen, "the list's field is inside the popover's panel");
                Assert.AreEqual(2, PopupChain.Count);
            }
        }

        [Test]
        public void A_press_inside_the_parent_closes_only_the_child()
        {
            _q.Open(_s.Get<Btn>("first"));
            OutsidePressProbe.HitForTests = _ => _s.Get<Btn>("second").GameObject;
            var frame = _q.Core.OpenFrame + 1;
            OutsidePressProbe.PressesForTests = () => new[] { new OutsidePressProbe.Press(Vector2.zero, frame) };

            OutsidePressProbe.TickForTests(1);

            Assert.IsFalse(_q.IsOpen);
            Assert.IsTrue(_m.IsOpen);
        }
    }
}
