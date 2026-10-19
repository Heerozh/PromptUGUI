using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The open-popup stack (spec 2026-10-18 §4.2): which entries a new one closes, the order a
    /// parent and its children close in, sorting by depth, and the two bulk dismissals. Driven with
    /// fake entries so the rules are pinned without a popup core or a TabMenu.
    /// </summary>
    public class PopupChainTests
    {
        private sealed class FakeEntry : IPopupEntry
        {
            public string Name;
            public List<string> Log;
            public GameObject HostGameObject { get; set; }
            public RectTransform Panel { get; set; }
            public int HostRootOrder { get; set; }
            public Transform Anchor { get; set; }
            public GameObject Blocker { get; set; }
            public PopupDismiss Dismiss { get; set; } = PopupDismiss.Blocker;
            public int OpenFrame { get; set; } = int.MinValue;
            public void CloseFromChain(PopupCloseReason reason) => Log.Add($"{Name}:{reason}");
        }

        private readonly List<GameObject> _made = new();
        private readonly List<string> _log = new();

        [SetUp]
        public void SetUp()
        {
            PopupChain.ResetForTests();
            _log.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PopupChain.ResetForTests();
            foreach (var go in _made)
                if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
        }

        private GameObject Go(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            else _made.Add(go);
            return go;
        }

        // A host with its panel as a child, like TabMenu's handle and popup.
        private FakeEntry Entry(string name, Transform hostParent = null, int rootOrder = 0)
        {
            var host = Go(name + "Host", hostParent);
            var panel = Go(name + "Panel", host.transform);
            return new FakeEntry
            {
                Name = name,
                Log = _log,
                HostRootOrder = rootOrder,
                HostGameObject = host,
                Panel = (RectTransform)panel.transform,
            };
        }

        // An entry whose host lives inside another entry's panel — a popup opened from a popup.
        private FakeEntry NestedIn(FakeEntry parent, string name) => Entry(name, parent.Panel, parent.HostRootOrder);

        [Test]
        public void Push_closes_entries_that_are_not_ancestors()
        {
            var a = Entry("A");
            var c = Entry("C");
            PopupChain.Push(a);

            PopupChain.Push(c);

            CollectionAssert.AreEqual(new[] { "A:Replaced" }, _log);
            Assert.AreSame(c, PopupChain.Top);
            Assert.AreEqual(1, PopupChain.Count);
        }

        [Test]
        public void Push_keeps_ancestors_when_the_host_is_inside_an_open_panel()
        {
            var a = Entry("A");
            var b = NestedIn(a, "B");
            PopupChain.Push(a);

            var depth = PopupChain.Push(b);

            CollectionAssert.IsEmpty(_log);
            Assert.AreEqual(1, depth);
            Assert.AreSame(b, PopupChain.Top);
            Assert.AreEqual(2, PopupChain.Count);
        }

        [Test]
        public void Pushing_an_unrelated_entry_over_a_nested_pair_closes_both_top_down()
        {
            var a = Entry("A");
            var b = NestedIn(a, "B");
            var c = Entry("C");
            PopupChain.Push(a);
            PopupChain.Push(b);

            PopupChain.Push(c);

            CollectionAssert.AreEqual(new[] { "B:Replaced", "A:Replaced" }, _log);
            Assert.AreSame(c, PopupChain.Top);
        }

        [Test]
        public void Pushing_an_open_entry_again_changes_nothing()
        {
            var a = Entry("A");
            PopupChain.Push(a);

            Assert.AreEqual(0, PopupChain.Push(a));
            CollectionAssert.IsEmpty(_log);
            Assert.AreEqual(1, PopupChain.Count);
        }

        [Test]
        public void Closing_a_parent_closes_its_children_top_down()
        {
            var a = Entry("A");
            var b = NestedIn(a, "B");
            PopupChain.Push(a);
            PopupChain.Push(b);

            PopupChain.Close(a, PopupCloseReason.Escape);

            CollectionAssert.AreEqual(new[] { "B:Escape", "A:Escape" }, _log);
            Assert.IsTrue(PopupChain.IsEmpty);
        }

        [Test]
        public void Closing_an_entry_that_is_not_open_does_nothing()
        {
            PopupChain.Close(Entry("A"), PopupCloseReason.Owner);
            CollectionAssert.IsEmpty(_log);
        }

        [Test]
        public void Sorting_order_is_host_root_plus_offset_plus_two_per_depth()
        {
            var a = Entry("A", rootOrder: 10);
            var b = NestedIn(a, "B");
            PopupChain.Push(a);
            PopupChain.Push(b);

            Assert.AreEqual(10 + PopupChain.SortingOffset, PopupChain.PanelOrder(a));
            Assert.AreEqual(10 + PopupChain.SortingOffset + 2, PopupChain.PanelOrder(b));
            Assert.AreEqual(PopupChain.PanelOrder(b) - 1, PopupChain.BlockerOrder(b), "the catcher sits just below its panel");
        }

        [Test]
        public void DismissBelow_closes_entries_whose_host_root_sorts_lower()
        {
            var a = Entry("A", rootOrder: 0);
            var b = NestedIn(a, "B");
            PopupChain.Push(a);
            PopupChain.Push(b);

            PopupChain.DismissBelow(0);
            CollectionAssert.IsEmpty(_log, "a layer at the same order does not cover it");

            PopupChain.DismissBelow(10);
            CollectionAssert.AreEqual(new[] { "B:Covered", "A:Covered" }, _log);
            Assert.IsTrue(PopupChain.IsEmpty);
        }

        [Test]
        public void DismissBelow_leaves_entries_on_a_higher_layer()
        {
            var inModal = Entry("M", rootOrder: 1000);
            PopupChain.Push(inModal);

            PopupChain.DismissBelow(500);

            CollectionAssert.IsEmpty(_log);
            Assert.AreSame(inModal, PopupChain.Top);
        }

        [Test]
        public void DismissUnder_closes_entries_hosted_under_root()
        {
            var screen = Go("Screen");
            var other = Go("Other");
            var a = Entry("A", screen.transform);
            PopupChain.Push(a);

            PopupChain.DismissUnder(other.transform);
            CollectionAssert.IsEmpty(_log);

            PopupChain.DismissUnder(screen.transform);
            CollectionAssert.AreEqual(new[] { "A:HostGone" }, _log);
        }

        [Test]
        public void Remove_drops_an_entry_without_closing_others()
        {
            var a = Entry("A");
            var b = NestedIn(a, "B");
            PopupChain.Push(a);
            PopupChain.Push(b);

            PopupChain.Remove(b);

            CollectionAssert.IsEmpty(_log);
            Assert.AreSame(a, PopupChain.Top);
        }

        [Test]
        public void ResetForTests_empties_the_chain()
        {
            PopupChain.Push(Entry("A"));
            PopupChain.ResetForTests();
            Assert.IsTrue(PopupChain.IsEmpty);
            Assert.IsNull(PopupChain.Top);
        }

        // ── Anchors (spec 2026-10-19 §5.2) ───────────────────────────────────────────────

        // A Popover declared elsewhere, anchored to a control inside an open panel.
        private FakeEntry AnchoredIn(FakeEntry parent, string name, PopupDismiss dismiss = PopupDismiss.PassThrough)
        {
            var e = Entry(name);
            e.Anchor = Go(name + "Anchor", parent.Panel).transform;
            e.Dismiss = dismiss;
            return e;
        }

        [Test]
        public void An_anchor_inside_an_open_panel_makes_a_child()
        {
            var a = Entry("A");
            var b = AnchoredIn(a, "B");
            PopupChain.Push(a);

            PopupChain.Push(b);

            CollectionAssert.IsEmpty(_log, "B's anchor is in A's panel: A is its ancestor");
            Assert.AreEqual(2, PopupChain.Count);
        }

        [Test]
        public void DismissUnder_matches_anchors_and_uses_the_given_reason()
        {
            var row = Go("Row");
            var a = Entry("A");
            a.Anchor = Go("More", row.transform).transform;
            PopupChain.Push(a);

            PopupChain.DismissUnder(row.transform, PopupCloseReason.AnchorLost);

            CollectionAssert.AreEqual(new[] { "A:AnchorLost" }, _log);
        }

        [Test]
        public void A_close_handler_that_opens_another_popup_leaves_a_single_line()
        {
            var a = Entry("A");
            var c = Entry("C");
            var b = Entry("B");
            // A reacts to being replaced by opening C, an unrelated popup, in the middle of B's push.
            var reacting = new FakeReacting { Inner = a, OnClose = () => PopupChain.Push(c) };
            PopupChain.Push(reacting);

            PopupChain.Push(b);

            Assert.AreEqual(1, PopupChain.Count, "B is not C's descendant: C must not stay under it");
            Assert.AreSame(b, PopupChain.Top);
            CollectionAssert.AreEqual(new[] { "A:Replaced", "C:Replaced" }, _log);
        }

        private sealed class FakeReacting : IPopupEntry
        {
            public FakeEntry Inner;
            public System.Action OnClose;
            public GameObject HostGameObject => Inner.HostGameObject;
            public RectTransform Panel => Inner.Panel;
            public int HostRootOrder => Inner.HostRootOrder;
            public Transform Anchor => Inner.Anchor;
            public GameObject Blocker => Inner.Blocker;
            public PopupDismiss Dismiss => Inner.Dismiss;
            public int OpenFrame => Inner.OpenFrame;

            public void CloseFromChain(PopupCloseReason reason)
            {
                Inner.CloseFromChain(reason);
                OnClose?.Invoke();
            }
        }

        // ── Prune (spec §1.2-5) ───────────────────────────────────────────────────────────

        [Test]
        public void Prune_closes_an_entry_whose_panel_was_switched_off_from_above()
        {
            var holder = Go("Holder");
            var a = Entry("A", holder.transform);
            PopupChain.Push(a);

            holder.SetActive(false);

            Assert.IsNull(PopupChain.Top, "a popup nobody can see no longer answers for the chain");
            CollectionAssert.AreEqual(new[] { "A:HostGone" }, _log);
        }

        // ── Outside presses (spec §5.4) ───────────────────────────────────────────────────

        [Test]
        public void A_press_on_nothing_closes_from_the_lowest_pass_entry()
        {
            var p = Entry("P");
            p.Dismiss = PopupDismiss.PassThrough;
            p.OpenFrame = 1;
            PopupChain.Push(p);

            Assert.AreEqual(0, PopupChain.OutsideCloseFrom(null, 2));
        }

        [Test]
        public void A_press_on_the_panel_or_the_anchor_is_inside()
        {
            var p = Entry("P");
            p.Dismiss = PopupDismiss.PassThrough;
            p.Anchor = Go("Anchor").transform;
            PopupChain.Push(p);

            var inPanel = Go("Cell", p.Panel).transform;
            var inAnchor = Go("Icon", p.Anchor).transform;
            Assert.AreEqual(-1, PopupChain.OutsideCloseFrom(inPanel, 2));
            Assert.AreEqual(-1, PopupChain.OutsideCloseFrom(inAnchor, 2));
        }

        [Test]
        public void A_press_in_the_parent_closes_only_the_child()
        {
            var parent = Entry("P");
            parent.Dismiss = PopupDismiss.PassThrough;
            var child = AnchoredIn(parent, "C");
            PopupChain.Push(parent);
            PopupChain.Push(child);

            var inParent = Go("Cell", parent.Panel).transform;
            Assert.AreEqual(1, PopupChain.OutsideCloseFrom(inParent, 2));
            Assert.AreEqual(0, PopupChain.OutsideCloseFrom(null, 2), "outside both: from the parent up");
        }

        [Test]
        public void Blocker_and_Owner_entries_are_left_to_their_own_rules()
        {
            var menu = Entry("M");                                  // Blocker by default
            menu.Blocker = Go("Catcher");
            PopupChain.Push(menu);
            Assert.AreEqual(-1, PopupChain.OutsideCloseFrom(null, 2), "a menu's catcher decides for it");

            var child = AnchoredIn(menu, "C");
            PopupChain.Push(child);
            Assert.AreEqual(1, PopupChain.OutsideCloseFrom(menu.Blocker.transform, 2),
                "a press on the menu's catcher is outside the pass child above it");

            PopupChain.ResetForTests();
            var list = Entry("L");
            list.Dismiss = PopupDismiss.Owner;
            PopupChain.Push(list);
            Assert.AreEqual(-1, PopupChain.OutsideCloseFrom(null, 2), "a suggestion list closes by its field's rules");
        }

        [Test]
        public void A_press_from_the_frame_it_opened_is_not_outside()
        {
            var p = Entry("P");
            p.Dismiss = PopupDismiss.PassThrough;
            p.OpenFrame = 5;
            PopupChain.Push(p);

            Assert.AreEqual(-1, PopupChain.OutsideCloseFrom(null, 5), "the press that opened it");
            Assert.AreEqual(0, PopupChain.OutsideCloseFrom(null, 6));
        }

        [Test]
        public void DismissOutside_closes_with_OutsidePress()
        {
            var p = Entry("P");
            p.Dismiss = PopupDismiss.PassThrough;
            PopupChain.Push(p);

            Assert.IsTrue(PopupChain.DismissOutside(null, 2));
            CollectionAssert.AreEqual(new[] { "P:OutsidePress" }, _log);
        }
    }
}
