using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// What <see cref="PopupCore"/> gained for <c>&lt;Popover&gt;</c> (spec 2026-10-19 §5.1): a panel that is
    /// not its anchor's child, placed on a canvas-space rect the host solved; a slide that is an offset
    /// on top of the resting position; a closing panel that no longer catches the pointer; the root
    /// canvas found from the panel's parent when the host IS the panel; a cage only when there is
    /// something to focus; a selection left in the panel handed to the host's restore target.
    /// </summary>
    public class PopupCoreFreeTests
    {
        private PuguiScreen _screen;
        private Frame _pop;          // host == panel, as for a <Popover>
        private RectTransform _holder;
        private Btn _elsewhere;
        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            if (Es() == null) _ownEs = new GameObject("EventSystem", typeof(EventSystem));
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='elsewhere' anchor='center' size='40x40'>O</Btn>
  <Frame id='holder' anchor='center' size='10x10'>
    <Frame id='pop' anchor='center' size='200x100'/>
  </Frame>
</Screen></PromptUGUI>");
            _screen = UI.Open("S");
            _elsewhere = _screen.Get<Btn>("elsewhere");
            _holder = _screen.Get<Frame>("holder").RectTransform;
            _pop = _screen.Get<Frame>("pop");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ownEs != null) Object.DestroyImmediate(_ownEs);
        }

        private static EventSystem Es() => EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        private Canvas Root => _screen.RootGameObject.GetComponent<Canvas>();

        private PopupCore Make(Rect target, PopupSide side = PopupSide.Bottom, PopupAlign align = PopupAlign.Start,
                               PopupDismiss dismiss = PopupDismiss.PassThrough, bool cage = true)
            => new PopupCore(_pop, _pop.RectTransform, dismiss, cage, cancelButtonCloses: true)
            {
                Measure = () => PopupLayout.Free(target, side, align),
                TransitionSeconds = 0f,
            };

        // The panel's rect in root-canvas space, at its current size.
        private Rect TargetAt(float x, float y)
        {
            var size = PopupCore.RectInCanvas(_pop.RectTransform, Root).size;
            return new Rect(x, y, size.x, size.y);
        }

        private static void AssertRect(Rect expected, Rect actual, string why)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, 0.5f, why + " (xMin)");
            Assert.AreEqual(expected.yMin, actual.yMin, 0.5f, why + " (yMin)");
            Assert.AreEqual(expected.width, actual.width, 0.5f, why + " (width)");
            Assert.AreEqual(expected.height, actual.height, 0.5f, why + " (height)");
        }

        // ── Placement ─────────────────────────────────────────────────────────────────────

        [Test]
        public void A_free_layout_puts_the_panel_on_the_given_canvas_rect()
        {
            _holder.anchoredPosition = new Vector2(37f, -12f);
            _holder.pivot = new Vector2(0.2f, 0.7f);
            var target = TargetAt(-150f, 40f);
            var core = Make(target);

            core.Open();

            AssertRect(target, PopupCore.RectInCanvas(_pop.RectTransform, Root), "moved parent, off-centre pivot");
        }

        [Test]
        public void A_free_layout_survives_a_scaled_parent()
        {
            _holder.localScale = new Vector3(2f, 2f, 1f);
            var target = TargetAt(-300f, -80f);
            Assert.AreEqual(400f, target.width, 0.5f, "precondition: the panel is twice as large on the canvas");
            var core = Make(target, PopupSide.Top, PopupAlign.End);

            core.Open();

            AssertRect(target, PopupCore.RectInCanvas(_pop.RectTransform, Root), "scaled parent");
        }

        [Test]
        public void Pivot_follows_the_side_and_the_alignment()
        {
            var core = Make(TargetAt(0f, 0f), PopupSide.Top, PopupAlign.End);
            core.Open();
            Assert.AreEqual(new Vector2(1f, 0f), _pop.RectTransform.pivot, "the corner facing the anchor stays put");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), _pop.RectTransform.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), _pop.RectTransform.anchorMax);
        }

        [Test]
        public void Over_pivots_at_the_vertical_middle_and_does_not_slide()
        {
            var core = Make(TargetAt(0f, 0f), PopupSide.Over, PopupAlign.End);
            core.Open();
            Assert.AreEqual(new Vector2(1f, 0.5f), _pop.RectTransform.pivot, "grows away from the aligned edge");
            Assert.AreEqual(Vector2.zero, core.SlideFromForTests, "over the anchor: a fade, no slide");
        }

        [Test]
        public void A_side_still_slides_out_of_its_anchor()
        {
            var core = Make(TargetAt(0f, 0f), PopupSide.Top);
            core.Open();
            Assert.AreEqual(new Vector2(0f, -8f), core.SlideFromForTests, "above: starts 8 lower, toward the anchor");
        }

        [Test]
        public void A_free_layout_leaves_sizeDelta_alone()
        {
            var before = _pop.RectTransform.sizeDelta;
            Make(TargetAt(10f, 10f)).Open();
            Assert.AreEqual(before, _pop.RectTransform.sizeDelta, "the size belongs to the host's fitter");
        }

        [Test]
        public void Repositioning_during_the_transition_lands_on_the_new_rest()
        {
            var target = TargetAt(0f, 0f);
            var core = Make(target);
            core.Open();
            var rest = _pop.RectTransform.anchoredPosition;

            core.SetSlideOffsetForTests(new Vector2(0f, 8f));
            Assert.AreEqual(rest + new Vector2(0f, 8f), _pop.RectTransform.anchoredPosition, "the slide rides on the rest");

            target.x += 50f;
            core.Measure = () => PopupLayout.Free(target, PopupSide.Bottom, PopupAlign.Start);
            core.Reposition();
            Assert.AreEqual(rest + new Vector2(50f, 8f), _pop.RectTransform.anchoredPosition,
                "a mid-slide reposition moves the rest; the offset stays on top of it");

            core.SetSlideOffsetForTests(Vector2.zero);
            Assert.AreEqual(rest + new Vector2(50f, 0f), _pop.RectTransform.anchoredPosition,
                "and the slide lands on the new rest");
        }

        // ── Pointer / canvas / dismiss ────────────────────────────────────────────────────

        [Test]
        public void A_closing_panel_stops_blocking_the_pointer()
        {
            var core = Make(TargetAt(0f, 0f));
            var group = _pop.GameObject.GetComponent<CanvasGroup>();
            core.Open();
            Assert.IsTrue(group.blocksRaycasts);
            core.Close();
            Assert.IsFalse(group.blocksRaycasts, "a fading panel must not catch a press meant for what is behind it");
            core.Open();
            Assert.IsTrue(group.blocksRaycasts);
        }

        [Test]
        public void The_root_canvas_is_found_from_the_panel_parent_when_the_host_is_the_panel()
        {
            Root.sortingOrder = 7;
            var core = Make(TargetAt(0f, 0f), dismiss: PopupDismiss.Blocker);
            _pop.GameObject.SetActive(false);   // a closed popover is inactive

            Assert.AreEqual(7, core.HostRootOrder, "the panel's own nested canvas is not the root");

            core.Open();
            var blocker = _screen.RootGameObject.transform.Find(PopupCore.BlockerName);
            Assert.IsNotNull(blocker, "the catcher hangs off the root canvas, not off the panel");
            Assert.AreEqual(7 + PopupChain.SortingOffset, _pop.GameObject.GetComponent<Canvas>().sortingOrder);
        }

        [Test]
        public void PassThrough_builds_no_blocker_and_Dismiss_can_change_while_closed()
        {
            var core = Make(TargetAt(0f, 0f));
            core.Open();
            Assert.IsNull(_screen.RootGameObject.transform.Find(PopupCore.BlockerName));
            Assert.AreEqual(PopupDismiss.PassThrough, ((IPopupEntry)core).Dismiss);
            core.Close();

            core.Dismiss = PopupDismiss.Blocker;
            core.Open();
            var blocker = _screen.RootGameObject.transform.Find(PopupCore.BlockerName);
            Assert.IsNotNull(blocker);
            Assert.IsTrue(blocker.gameObject.activeSelf);
            Assert.AreSame(blocker.gameObject, ((IPopupEntry)core).Blocker);
        }

        [Test]
        public void Chain_facts_are_reported()
        {
            var anchor = _elsewhere.RectTransform;
            var core = Make(TargetAt(0f, 0f));
            core.Anchor = () => anchor;
            core.Open();
            var entry = (IPopupEntry)core;
            Assert.AreSame(anchor, entry.Anchor);
            Assert.AreEqual(Time.frameCount, entry.OpenFrame);
        }

        // ── Focus ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void The_cage_waits_for_something_focusable()
        {
            UI.UseGamepadNavigation();
            Es().SetSelectedGameObject(_elsewhere.GameObject);
            var core = Make(TargetAt(0f, 0f));

            core.Open();
            Assert.AreNotSame(_pop.GameObject, UI.Navigation.ContainmentRoot, "nothing to focus: no cage");
            Assert.AreSame(_elsewhere.GameObject, Es().currentSelectedGameObject, "the focus is not moved");
            core.Close();

            var row = ProceduralBuilders.AddChild(_pop.RectTransform, "Row");
            row.gameObject.AddComponent<UnityImage>();
            var btn = row.gameObject.AddComponent<Button>();
            core.Open();
            Assert.AreSame(_pop.GameObject, UI.Navigation.ContainmentRoot, "a focusable row: caged");
            Assert.AreSame(btn.gameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void RestoreTo_receives_a_selection_left_in_the_panel_even_when_uncaged()
        {
            var row = ProceduralBuilders.AddChild(_pop.RectTransform, "Row");
            row.gameObject.AddComponent<UnityImage>();
            row.gameObject.AddComponent<Button>();
            var core = Make(TargetAt(0f, 0f), cage: false);
            PopupCloseReason? askedFor = null;
            core.RestoreTo = r => { askedFor = r; return _elsewhere.GameObject; };
            core.RestoreWhenUncaged = true;

            core.Open();
            Es().SetSelectedGameObject(row.gameObject);   // a mouse click on a cell selects it
            core.Close(PopupCloseReason.Escape);

            Assert.AreSame(_elsewhere.GameObject, Es().currentSelectedGameObject,
                "an inactive selection would strand directional input");
            Assert.AreEqual(PopupCloseReason.Escape, askedFor, "the restore target may depend on why it closed");
        }

        [Test]
        public void A_selection_already_elsewhere_is_left_alone()
        {
            var core = Make(TargetAt(0f, 0f), cage: false);
            var other = _screen.Get<Btn>("elsewhere").GameObject;
            core.RestoreTo = _ => null;
            core.RestoreWhenUncaged = true;
            core.Open();
            Es().SetSelectedGameObject(other);
            core.Close();
            Assert.AreSame(other, Es().currentSelectedGameObject);
        }
    }
}
