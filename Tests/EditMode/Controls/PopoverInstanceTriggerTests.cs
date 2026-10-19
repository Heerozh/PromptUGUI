using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Popover on="click@x"&gt;</c> where x is a template instance (spec 2026-10-19-id-path §4.3): the popover
    /// hangs from x, the one <c>&lt;Btn&gt;</c> inside it toggles it, and a selection the panel held goes back to
    /// that button when x itself is not selectable.
    /// </summary>
    public class PopoverInstanceTriggerTests
    {
        private PromptUGUI.Application.Screen _s;
        private Popover _pop;
        private GameObject _es;
        private readonly List<PopoverCloseReason> _closes = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closes.Clear();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='IconCell'><Frame width='44' height='44'>
    <Btn id='btn' anchor='stretch'>x</Btn><Frame id='badge' anchor='top-right' size='8x8'/>
  </Frame></Template>
  <Screen name='S'>
    <IconCell id='slot' anchor='center'/>
    <Popover id='pop' on='click@slot' transition='0'><Btn id='inside' width='80' height='30'>1</Btn></Popover>
  </Screen>
</PromptUGUI>");
            _s = UI.Open("S");
            _pop = _s.Get<Popover>("pop");
            _pop.OnClosed.Subscribe(_closes.Add);
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_es != null) UnityEngine.Object.DestroyImmediate(_es);
        }

        private Btn SlotBtn => _s.Get<Btn>("slot/btn");

        private static void Click(Btn b) => b.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

        [Test]
        public void Its_Btn_opens_it_anchored_to_the_instance()
        {
            Click(SlotBtn);

            Assert.IsTrue(_pop.IsOpen);
            Assert.AreSame(_s.Get("slot"), _pop.Anchor, "it hangs from what the id names");
        }

        [Test]
        public void Its_Btn_again_closes_it_as_Trigger()
        {
            Click(SlotBtn);
            Click(SlotBtn);

            Assert.IsFalse(_pop.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.Trigger }, _closes);
        }

        [Test]
        public void Open_without_an_anchor_uses_the_instance()
        {
            _pop.Open();
            Assert.AreSame(_s.Get("slot"), _pop.Anchor);
        }

        [Test]
        public void The_selection_goes_back_to_the_Btn_when_the_instance_is_not_selectable()
        {
            _es = new GameObject("ES", typeof(EventSystem));
            var es = _es.GetComponent<EventSystem>();
            Click(SlotBtn);
            es.SetSelectedGameObject(_s.Get("inside").GameObject);   // a click on a Btn in the panel selects it

            _pop.Close();

            Assert.AreSame(SlotBtn.GameObject, es.currentSelectedGameObject);
        }

        [Test]
        public void An_instance_whose_Btn_is_inside_the_popover_is_an_error()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t2", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='IconCell'><Frame width='44' height='44'><Btn id='btn' anchor='stretch'>x</Btn></Frame></Template>
  <Screen name='T'>
    <Popover id='pop' on='click@slot' transition='0'><IconCell id='slot'/></Popover>
  </Screen>
</PromptUGUI>");
            var ex = Assert.Catch<Exception>(() => UI.Open("T"));
            StringAssert.Contains("inside the popover itself", ex.Message);
        }
    }
}
