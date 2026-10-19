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
    /// A popover anchored to a control in a <c>&lt;ScrollList&gt;</c> row (spec 2026-10-19 §4.6 / §5.5): it
    /// stays open while the row shows the same item — every push re-binds every row — and closes as
    /// <see cref="PopoverCloseReason.AnchorLost"/> when the row is retired, parked, or handed to another item.
    /// </summary>
    public class ScrollListPopoverAnchorTests
    {
        private const string RowTemplate =
            "<Template name='Row'><Frame height='40'><Btn id='more' anchor='center' size='30x30'>…</Btn></Frame></Template>";

        private PuguiScreen _s;
        private ScrollList _list;
        private Popover _menu;
        private readonly List<PopoverCloseReason> _closes = new();
        private readonly Subject<IReadOnlyList<string>> _feed = new();
        private System.IDisposable _binding;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closes.Clear();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
        }

        [TearDown]
        public void TearDown()
        {
            _binding?.Dispose();
            UI.ResetForTests();
        }

        private void Open(string listAttrs = "", bool keyed = true)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + RowTemplate
                                 + "<Screen name='S'>"
                                 + $"<ScrollList id='l' anchor='center' size='200x200' itemTemplate='Row' {listAttrs}/>"
                                 + "<Popover id='menu'><Frame width='60' height='30'/></Popover>"
                                 + "</Screen></PromptUGUI>");
            _s = UI.Open("S");
            _list = _s.Get<ScrollList>("l");
            _menu = _s.Get<Popover>("menu");
            _menu.OnClosed.Subscribe(_closes.Add);
            _binding = keyed
                ? _list.BindItems(_feed, (IControl r, string s) => { }, key: s => s)
                : _list.BindItems(_feed, (IControl r, string s) => { });
        }

        private void Push(params string[] items)
        {
            _feed.OnNext(items);
            Canvas.ForceUpdateCanvases();
        }

        private Btn More(int slot) => _list.Slots[slot].Get<Btn>("more");

        [Test]
        public void A_keyed_push_of_the_same_items_keeps_it_open()
        {
            Open();
            Push("a", "b", "c");
            _menu.Open(More(1));

            Push("a", "b", "c");

            Assert.IsTrue(_menu.IsOpen, "the row still shows b");
        }

        [Test]
        public void A_positional_push_keeps_it_open()
        {
            Open(keyed: false);
            Push("a", "b", "c");
            _menu.Open(More(1));

            Push("x", "y", "z");

            Assert.IsTrue(_menu.IsOpen, "without keys a row's identity is its index");
        }

        [Test]
        public void A_push_that_drops_the_item_closes_it_as_AnchorLost()
        {
            Open();
            Push("a", "b", "c");
            _menu.Open(More(1));

            Push("a", "c");

            Assert.IsFalse(_menu.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.AnchorLost }, _closes);
        }

        [Test]
        public void An_empty_push_closes_it()
        {
            Open();
            Push("a", "b", "c");
            _menu.Open(More(0));

            Push();

            Assert.IsFalse(_menu.IsOpen);
        }

        [Test]
        public void Virtual_scrolling_that_hands_the_row_to_another_item_closes_it()
        {
            Open("virtualize='true'");
            var items = new string[60];
            for (var i = 0; i < items.Length; i++) items[i] = "i" + i;
            Push(items);
            var first = _list.Slots[0];
            _menu.Open(first.Get<Btn>("more"));
            Assume.That(_menu.IsOpen);

            // Far down: the rows of the top leave the window and are handed to the items entering it.
            var content = (RectTransform)_list.GameObject.transform.Find("Viewport/Content");
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 1600f);
            _list.RefreshWindow();   // the top rows leave: kept as rows for the items entering, or parked

            Assert.IsFalse(_menu.IsOpen, "the row it hung from now shows another item");
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.AnchorLost }, _closes);
        }

        [Test]
        public void A_close_handler_that_pushes_the_same_list_does_not_throw()
        {
            Open();
            Push("a", "b", "c");
            _menu.Open(More(1));
            var pushed = false;
            _menu.OnClosed.Subscribe(_ =>
            {
                if (pushed) return;
                pushed = true;
                _feed.OnNext(new[] { "a" });
            });

            Assert.DoesNotThrow(() => Push("a", "c"));
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(1, _list.ItemCount, "the handler's push ran after the one that closed it");
        }

        [Test]
        public void A_virtual_close_handler_that_pushes_the_same_list_does_not_throw()
        {
            Open("virtualize='true'");
            Push("a", "b", "c");
            _menu.Open(More(1));
            var pushed = false;
            _menu.OnClosed.Subscribe(_ =>
            {
                if (pushed) return;
                pushed = true;
                _feed.OnNext(new[] { "a" });
            });

            Assert.DoesNotThrow(() => Push("a", "c"));
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(1, _list.ItemCount);
        }
    }
}
