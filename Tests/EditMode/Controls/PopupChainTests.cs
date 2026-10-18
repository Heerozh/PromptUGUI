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

            CollectionAssert.AreEqual(new[] { "A:Owner" }, _log);
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

            CollectionAssert.AreEqual(new[] { "B:Owner", "A:Owner" }, _log);
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

        // ── M0's escape bookkeeping, moved here unchanged from TabMenu (spec 2026-10-18 plan P2) ──

        [Test]
        public void TryConsumeEscape_closes_the_top_and_reports_consumed()
        {
            var a = Entry("A");
            var b = NestedIn(a, "B");
            PopupChain.Push(a);
            PopupChain.Push(b);

            Assert.IsTrue(PopupChain.TryConsumeEscape());
            CollectionAssert.AreEqual(new[] { "B:Escape" }, _log, "Escape closes the top entry only");
            Assert.AreSame(a, PopupChain.Top);
        }

        [Test]
        public void An_escape_already_spent_this_frame_still_reads_as_consumed()
        {
            Assert.IsFalse(PopupChain.TryConsumeEscape(), "nothing open, nothing spent");

            PopupChain.NoteEscapeConsumed();
            Assert.IsTrue(PopupChain.TryConsumeEscape(), "another listener heard the same press");

            PopupChain.ForgetEscapeFrameForTests();
            Assert.IsFalse(PopupChain.TryConsumeEscape(), "a later press is a new press");
        }
    }
}
