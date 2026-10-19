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
    /// The pass-through outside press (spec 2026-10-19 §5.4): a press anywhere but the panel, its anchor
    /// or a popup above it closes a <c>dismiss="pass"</c> popover and still goes on to what is under it.
    /// Presses and hits are injected — EditMode has no devices and no deterministic Overlay canvas; the
    /// real raycast is covered in PlayMode.
    /// </summary>
    public class OutsidePressProbeTests
    {
        private PuguiScreen _screen;
        private Popover _pop;
        private GameObject _hit;
        private int _batch;
        private readonly List<PopoverCloseReason> _closed = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closed.Clear();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='a' anchor='center' size='40x40'>A</Btn>
  <Btn id='b' anchor='top-left' size='40x40'>B</Btn>
  <Popover id='p'><Frame id='c' width='100' height='40'/></Popover>
</Screen></PromptUGUI>");
            _screen = UI.Open("S");
            _pop = _screen.Get<Popover>("p");
            _pop.OnClosed.Subscribe(_closed.Add);
            _pop.Open(_screen.Get<Btn>("a"));
            Assume.That(_pop.IsOpen, "precondition");

            OutsidePressProbe.HitForTests = _ => _hit;
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        // One press in a new batch, as if a frame after the popover opened.
        private void PressOn(GameObject hit, int frameOffset = 1)
        {
            _hit = hit;
            var frame = _pop.Core.OpenFrame + frameOffset;
            OutsidePressProbe.PressesForTests = () => new[] { new OutsidePressProbe.Press(Vector2.zero, frame) };
            OutsidePressProbe.TickForTests(++_batch);
        }

        [Test]
        public void A_press_outside_closes_the_pass_popup_with_OutsidePress()
        {
            PressOn(_screen.Get<Btn>("b").GameObject);

            Assert.IsFalse(_pop.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.OutsidePress }, _closed);
        }

        [Test]
        public void A_press_on_nothing_closes_it_too()
        {
            PressOn(null);
            Assert.IsFalse(_pop.IsOpen);
        }

        [Test]
        public void A_press_on_the_panel_or_its_anchor_does_not()
        {
            PressOn(_screen.Get<Frame>("c").GameObject);
            Assert.IsTrue(_pop.IsOpen, "inside the panel");

            var label = _screen.Get<Btn>("a").RectTransform.GetChild(0).gameObject;
            PressOn(label);
            Assert.IsTrue(_pop.IsOpen, "on the anchor: the click that follows toggles it instead");
        }

        [Test]
        public void A_press_in_the_frame_the_popup_opened_does_not()
        {
            PressOn(_screen.Get<Btn>("b").GameObject, frameOffset: 0);
            Assert.IsTrue(_pop.IsOpen, "the press that opened it is not an outside press");
        }

        [Test]
        public void A_blocking_tutorial_step_swallows_it()
        {
            var store = new Dictionary<string, int>();
            UI.Tutorial.UseProgressStore(id => store.TryGetValue(id, out var v) ? v : 0, (id, n) => store[id] = n);
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            UI.Tutorial.TickForTests(0.016f);
            Assume.That(UI.Tutorial.IsBlockingInput, "precondition: a Block step is up");

            PressOn(_screen.Get<Btn>("b").GameObject);

            Assert.IsTrue(_pop.IsOpen, "a Block step lets nothing through");
            _ = run;
        }

        [Test]
        public void Two_presses_in_one_batch_are_both_decided()
        {
            var b = _screen.Get<Btn>("b").GameObject;
            var c = _screen.Get<Frame>("c").GameObject;
            var hits = new Queue<GameObject>(new[] { c, b });
            OutsidePressProbe.HitForTests = _ => hits.Dequeue();
            var frame = _pop.Core.OpenFrame + 1;
            OutsidePressProbe.PressesForTests = () => new[]
            {
                new OutsidePressProbe.Press(Vector2.zero, frame),
                new OutsidePressProbe.Press(Vector2.one, frame),
            };

            OutsidePressProbe.TickForTests(1);

            Assert.IsFalse(_pop.IsOpen, "the second finger landed outside");
        }

        [Test]
        public void The_same_batch_is_not_decided_twice()
        {
            var calls = 0;
            OutsidePressProbe.HitForTests = _ => { calls++; return _screen.Get<Frame>("c").GameObject; };
            var frame = _pop.Core.OpenFrame + 1;
            OutsidePressProbe.PressesForTests = () => new[] { new OutsidePressProbe.Press(Vector2.zero, frame) };

            OutsidePressProbe.TickForTests(7);
            OutsidePressProbe.TickForTests(7);

            Assert.AreEqual(1, calls, "two popovers ticking in one frame decide each press once");
        }

        [Test]
        public void Block_mode_popovers_are_left_to_their_catcher()
        {
            _pop.Close();
            _pop.Dismiss = "block";
            _pop.Open(_screen.Get<Btn>("a"));

            PressOn(_screen.Get<Btn>("b").GameObject);

            Assert.IsTrue(_pop.IsOpen, "the catcher, not the probe, closes a blocking popover");
        }

        [Test]
        public void No_EventSystem_no_probe()
        {
            OutsidePressProbe.HitForTests = null;   // the real raycast, with no EventSystem in the scene
            foreach (var es in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            var frame = _pop.Core.OpenFrame + 1;
            OutsidePressProbe.PressesForTests = () => new[] { new OutsidePressProbe.Press(Vector2.zero, frame) };

            OutsidePressProbe.TickForTests(1);

            Assert.IsTrue(_pop.IsOpen, "without a raycast there is no telling inside from outside");
        }
    }
}
