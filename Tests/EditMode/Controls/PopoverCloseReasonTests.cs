using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Every row of the close table (spec 2026-10-19 §4.4), as <see cref="Popover.OnClosed"/> reports it.
    /// <c>AnchorLost</c> is pinned by <see cref="PopoverFollowTests"/>.
    /// </summary>
    public class PopoverCloseReasonTests
    {
        private PuguiScreen _s;
        private Popover _m;
        private readonly List<PopoverCloseReason> _closes = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closes.Clear();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='a' anchor='center' size='40x40'>A</Btn>
  <Btn id='b' anchor='top-left' size='40x40'>B</Btn>
  <Popover id='m' on='click@a'><Frame width='50' height='20'/></Popover>
  <Popover id='other'><Frame width='50' height='20'/></Popover>
  <TabMenu id='menu' anchor='top-right' transition='0'><Tab id='x' text='X' isOn='true'/></TabMenu>
</Screen></PromptUGUI>");
            _s = UI.Open("S");
            _m = _s.Get<Popover>("m");
            _m.OnClosed.Subscribe(_closes.Add);
            _m.Open();
            Assume.That(_m.IsOpen, "precondition");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private void AssertClosedAs(PopoverCloseReason reason)
        {
            Assert.IsFalse(_m.IsOpen);
            CollectionAssert.AreEqual(new[] { reason }, _closes);
        }

        [Test]
        public void Close_is_Code()
        {
            _m.Close();
            AssertClosedAs(PopoverCloseReason.Code);
        }

        [Test]
        public void The_trigger_again_is_Trigger()
        {
            _s.Get<Btn>("a").SimulateClick();
            AssertClosedAs(PopoverCloseReason.Trigger);
        }

        [Test]
        public void Escape_is_Escape()
        {
            _m.GameObject.GetComponent<ModalEscapeListener>().FireForTests();
            AssertClosedAs(PopoverCloseReason.Escape);
        }

        [Test]
        public void A_press_outside_is_OutsidePress()
        {
            OutsidePressProbe.HitForTests = _ => _s.Get<Btn>("b").GameObject;
            var frame = _m.Core.OpenFrame + 1;
            OutsidePressProbe.PressesForTests = () => new[] { new OutsidePressProbe.Press(Vector2.zero, frame) };
            OutsidePressProbe.TickForTests(1);
            AssertClosedAs(PopoverCloseReason.OutsidePress);
        }

        [Test]
        public void A_dialog_over_it_is_Covered()
        {
            _ = MessageBox.Open("over", MsgBtn.OK);
            AssertClosedAs(PopoverCloseReason.Covered);
        }

        [Test]
        public void An_unrelated_popup_is_Replaced()
        {
            _s.Get<TabMenu>("menu").Expand();
            AssertClosedAs(PopoverCloseReason.Replaced);

            _closes.Clear();
            _m.Open();
            _s.Get<Popover>("other").Open(_s.Get<Btn>("b"));
            AssertClosedAs(PopoverCloseReason.Replaced);
        }

        [Test]
        public void The_Screen_closing_is_HostGone()
        {
            UI.CloseImmediate("S");
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.HostGone }, _closes);
        }
    }
}
