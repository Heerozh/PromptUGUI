using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// An open <c>&lt;Popover&gt;</c> follows its anchor and closes when the anchor goes away (spec 2026-10-19
    /// §4.5 / §4.6): moved → re-placed on the side chosen at open; the screen resized → the side is chosen
    /// again; a keyboard sliding in → re-clamped only; destroyed / switched off / disabled / scrolled out of
    /// its viewport → <see cref="PopoverCloseReason.AnchorLost"/>. Driven with <c>TickForTests</c>, the
    /// body of the panel's <c>LateUpdate</c>.
    /// </summary>
    public class PopoverFollowTests
    {
        private PuguiScreen _s;
        private Popover _m;
        private readonly List<PopoverCloseReason> _closes = new();
        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closes.Clear();
            PopupBounds.BoundsOverride = () => new Rect(-500f, -300f, 1000f, 600f);
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ownEs != null) Object.DestroyImmediate(_ownEs);
        }

        private void Open(string extra = "")
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='a' anchor='center' size='40x40'>A</Btn>
  <Btn id='b' anchor='top-left' size='40x40'>B</Btn>
  <Popover id='m'><Btn id='inside' width='100' height='50'>I</Btn></Popover>" + extra + @"
</Screen></PromptUGUI>");
            _s = UI.Open("S");
            _m = _s.Get<Popover>("m");
            _m.OnClosed.Subscribe(_closes.Add);
        }

        private Canvas Root => _s.RootGameObject.GetComponent<Canvas>();
        private Rect InCanvas(RectTransform rt) => PopupCore.RectInCanvas(rt, Root);
        private Btn A => _s.Get<Btn>("a");

        // ── Following ─────────────────────────────────────────────────────────────────────

        [Test]
        public void It_follows_a_moving_anchor()
        {
            Open();
            _m.Open(A);
            A.RectTransform.anchoredPosition += new Vector2(100f, 50f);
            _m.TickForTests();

            var anchor = InCanvas(A.RectTransform);
            var panel = InCanvas(_m.RectTransform);
            Assert.AreEqual(anchor.xMin, panel.xMin, 0.5f);
            Assert.AreEqual(anchor.yMin - 4f, panel.yMax, 0.5f, "still 4 under the anchor");
        }

        [Test]
        public void The_side_stays_locked_while_open()
        {
            Open();
            _m.Open(A);
            // Down near the bottom edge: below no longer fits, above would.
            A.RectTransform.anchoredPosition = new Vector2(0f, -260f);
            _m.TickForTests();

            var panel = InCanvas(_m.RectTransform);
            Assert.AreEqual(-300f, panel.yMin, 0.5f, "kept below, pushed back inside — not flipped up");
        }

        [Test]
        public void A_new_canvas_size_rechooses_the_side()
        {
            Open();
            _m.Open(A);
            A.RectTransform.anchoredPosition = new Vector2(0f, -260f);
            // The screen turned: a new safe area.
            PopupBounds.BoundsOverride = () => new Rect(-300f, -300f, 600f, 700f);
            _m.TickForTests();

            var anchor = InCanvas(A.RectTransform);
            var panel = InCanvas(_m.RectTransform);
            Assert.AreEqual(anchor.yMax + 4f, panel.yMin, 0.5f, "a new screen: the side is chosen again (above)");
        }

        [Test]
        public void A_keyboard_only_change_reclamps_without_flipping()
        {
            Open();
            var safe = new Rect(-500f, -300f, 1000f, 600f);
            PopupBounds.SafeOverride = () => safe;
            _m.Open(A);
            // A keyboard slides in: the bounds lose their bottom 250, the safe area does not change. Below
            // the anchor (y -20) only 30 are left: unlocked, the panel would flip above.
            PopupBounds.BoundsOverride = () => new Rect(-500f, -50f, 1000f, 350f);
            _m.TickForTests();

            var panel = InCanvas(_m.RectTransform);
            Assert.AreEqual(-50f, panel.yMin, 0.5f, "re-clamped on its side (below), not flipped above");
        }

        // ── Anchor lost ───────────────────────────────────────────────────────────────────

        [Test]
        public void A_destroyed_inactive_or_disabled_anchor_closes_it_as_AnchorLost()
        {
            Open();
            var b = _s.Get<Btn>("b");

            _m.Open(A);
            A.Interactable = false;
            _m.TickForTests();
            Assert.IsFalse(_m.IsOpen, "disabled");
            A.Interactable = true;

            _m.Open(A);
            A.Hidden = true;
            _m.TickForTests();
            Assert.IsFalse(_m.IsOpen, "switched off");

            _m.Open(b);
            Object.DestroyImmediate(b.GameObject);
            _m.TickForTests();
            Assert.IsFalse(_m.IsOpen, "destroyed");

            CollectionAssert.AreEqual(
                new[] { PopoverCloseReason.AnchorLost, PopoverCloseReason.AnchorLost, PopoverCloseReason.AnchorLost },
                _closes);
        }

        [Test]
        public void An_anchor_scrolled_out_of_its_viewport_closes_it()
        {
            Open(@"<ScrollList id='l' anchor='center' size='200x100'>
                     <Frame height='40'><Btn id='row' anchor='center' size='30x30'>R</Btn></Frame>
                     <Frame height='400'/>
                   </ScrollList>");
            Canvas.ForceUpdateCanvases();
            _m.Open(_s.Get<Btn>("row"));
            Assume.That(_m.IsOpen);

            var content = _s.Get<ScrollList>("l").RectTransform.GetComponentInChildren<UnityEngine.UI.ScrollRect>().content;
            content.anchoredPosition += new Vector2(0f, 300f);   // the first row scrolls up and away
            _m.TickForTests();

            Assert.IsFalse(_m.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.AnchorLost }, _closes);
        }

        [Test]
        public void An_anchor_inside_another_popover_is_not_clipped_by_the_list_behind_it()
        {
            Open(@"<ScrollList id='l' anchor='center' size='200x100'>
                     <Frame height='40'>
                       <Popover id='outer'><Btn id='deep' width='60' height='30'>D</Btn></Popover>
                     </Frame>
                   </ScrollList>");
            Canvas.ForceUpdateCanvases();
            var outer = _s.Get<Popover>("outer");
            // Opened beside a button far outside the list's viewport.
            outer.Open(_s.Get<Btn>("b"));
            Assume.That(outer.IsOpen);

            _m.Open(_s.Get<Btn>("deep"));
            _m.TickForTests();

            Assert.IsTrue(_m.IsOpen, "the outer panel's own canvas ends the clipping walk");
        }

        [Test]
        public void AnchorLost_restores_the_selection_from_before_not_the_anchor()
        {
            Open();
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                _ownEs = new GameObject("EventSystem", typeof(EventSystem));
            var es = Object.FindAnyObjectByType<EventSystem>();
            var b = _s.Get<Btn>("b");
            es.SetSelectedGameObject(b.GameObject);

            _m.Open(A);
            es.SetSelectedGameObject(_s.Get<Btn>("inside").GameObject);   // a click on a cell selects it
            A.Hidden = true;
            _m.TickForTests();

            Assert.AreSame(b.GameObject, es.currentSelectedGameObject,
                "the anchor is gone (or now shows something else): back to what was selected before");
        }

        [Test]
        public void OpenAt_has_no_anchor_to_lose()
        {
            Open();
            _m.OpenAt(new Vector2(10f, 10f));
            A.Hidden = true;
            _m.TickForTests();
            Assert.IsTrue(_m.IsOpen);
        }
    }
}
