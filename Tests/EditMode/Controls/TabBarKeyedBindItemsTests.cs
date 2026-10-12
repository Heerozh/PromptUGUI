using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using Animation = PromptUGUI.Controls.Animation;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>TabBar.BindItems(..., key)</c> / <c>TabMenu.BindItems(..., key)</c>: rows follow their items by key,
    /// the same contract as <c>ScrollList</c>'s keyed overload. A row whose key survives a push is reused in
    /// place (re-bound, moved to its new place) — so the selection follows its key, and a row that did not
    /// change replays nothing. A selection whose key is gone is announced once.
    /// </summary>
    public class TabBarKeyedBindItemsTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // A wrapper row like a real list card: the Tab is the card face, the Animation unfolds a detail
        // line while the row is selected.
        private const string RowTemplate =
            "<Template name='Row'><VStack id='row'>"
            + "<Tab id='tab' height='40'/>"
            + "<Animation id='more' on='checked@tab' reverse-on='unchecked@tab' reveal='y' duration='0.2s'>"
            + "<Frame height='20'/></Animation>"
            + "<Text id='label'>-</Text>"
            + "</VStack></Template>";

        private static PromptUGUI.Application.Screen Open(string barAttrs = "allowSwitchOff='true'", string barChildren = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{RowTemplate}<Screen name='S'>
  <TabBar id='bar' direction='vertical' itemTemplate='Row' {barAttrs}>{barChildren}</TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private sealed class Feed
        {
            public readonly Subject<IReadOnlyList<string>> Subject = new();
            public int Binds;

            public Feed(TabBar bar) =>
                bar.BindItems<string, IControl, string>(Subject, (row, s) =>
                {
                    Binds++;
                    row.Get<Text>("label").TextValue = s;
                }, key: s => s);

            public void Push(params string[] items) => Subject.OnNext(items);
        }

        private static IControl RowOf(Tab tab) => tab.Parent;

        private static string LabelOf(Tab tab) =>
            RowOf(tab).Get<Text>("label").GameObject.GetComponent<TMPro.TMP_Text>().text;

        private static List<string> LabelsInSiblingOrder(TabBar bar)
        {
            var labels = new List<string>();
            foreach (Transform child in bar.RectTransform)
                if (child.gameObject.activeSelf)
                    labels.Add(child.Find("label").GetComponent<TMPro.TMP_Text>().text);
            return labels;
        }

        [Test]
        public void The_selected_item_stays_selected_through_insert_remove_and_reorder()
        {
            var bar = Open().Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b", "c", "d");
            var b = bar.GetAt(1);
            b.IsOn = true;

            var more = RowOf(b).Get<Animation>("more");
            var played = 0;
            using var fire = more.OnFire.Subscribe(_ => played++);
            using var reverse = more.OnReverse.Subscribe(_ => played++);
            var announced = new List<Tab>();
            using var selection = bar.OnSelectionChanged.Subscribe(announced.Add);

            feed.Push("e", "d", "b", "a");   // insert e, drop c, reverse the rest

            Assert.AreSame(b, bar.SelectedTab, "the row of 'b' was reused, so its Tab is still the one that is on");
            Assert.AreEqual(2, bar.SelectedIndex, "…at the item's new index");
            Assert.IsTrue(b.IsOn);
            Assert.AreEqual("b", LabelOf(b));
            CollectionAssert.AreEqual(new[] { "e", "d", "b", "a" }, LabelsInSiblingOrder(bar),
                "the rows are laid out in the new order");
            Assert.IsEmpty(announced, "the selection did not change, so nothing is announced");
            Assert.AreEqual(0, played, "the selected row's unfold animation is not replayed");
        }

        [Test]
        public void Surviving_keys_reuse_their_rows_and_every_row_is_rebound()
        {
            var bar = Open().Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b", "c");
            var a = bar.GetAt(0);
            var c = bar.GetAt(2);
            feed.Binds = 0;

            feed.Push("c", "x", "a");

            Assert.AreEqual(3, bar.Count);
            Assert.AreSame(c, bar.GetAt(0));
            Assert.AreSame(a, bar.GetAt(2));
            Assert.AreNotSame(a, bar.GetAt(1));
            Assert.AreEqual("x", LabelOf(bar.GetAt(1)));
            Assert.AreEqual(3, feed.Binds, "every row is bound again on every push, reused or not");
            Assert.AreEqual(3, LabelsInSiblingOrder(bar).Count, "the row of 'b' is gone");
        }

        [Test]
        public void Dropping_the_selected_item_announces_null_once()
        {
            var bar = Open().Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b", "c");
            bar.GetAt(1).IsOn = true;
            var announced = new List<Tab>();
            using var selection = bar.OnSelectionChanged.Subscribe(announced.Add);

            feed.Push("a", "c");

            Assert.AreEqual(-1, bar.SelectedIndex, "allowSwitchOff: nothing is picked in its place");
            CollectionAssert.AreEqual(new Tab[] { null }, announced);
        }

        [Test]
        public void Dropping_the_selected_item_of_a_bar_that_needs_one_announces_the_replacement()
        {
            var bar = Open(barAttrs: "").Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b", "c");
            bar.GetAt(1).IsOn = true;
            var announced = new List<Tab>();
            using var selection = bar.OnSelectionChanged.Subscribe(announced.Add);

            feed.Push("a", "c");

            Assert.AreEqual(0, bar.SelectedIndex, "the bar falls back on its first tab, as it does at open");
            CollectionAssert.AreEqual(new[] { bar.GetAt(0) }, announced);
        }

        [Test]
        public void A_push_with_nothing_selected_selects_nothing()
        {
            var bar = Open().Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b");
            var announced = new List<Tab>();
            using var selection = bar.OnSelectionChanged.Subscribe(announced.Add);

            feed.Push("b", "c", "a");

            Assert.AreEqual(-1, bar.SelectedIndex);
            Assert.IsEmpty(announced);
        }

        [Test]
        public void Reused_and_new_rows_still_report_their_clicks()
        {
            var bar = Open().Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b");
            feed.Push("b", "c", "a");
            var announced = new List<Tab>();
            using var selection = bar.OnSelectionChanged.Subscribe(announced.Add);

            bar.GetAt(0).IsOn = true;
            bar.GetAt(1).IsOn = true;

            CollectionAssert.AreEqual(new[] { bar.GetAt(0), bar.GetAt(1) }, announced);
            Assert.IsFalse(bar.GetAt(0).IsOn, "still one group: selecting a new row turned the reused one off");
        }

        [Test]
        public void The_first_keyed_push_replaces_static_rows_whole()
        {
            var bar = Open(barChildren: "<Row/><Row/><Row/>").Get<TabBar>("bar");
            var feed = new Feed(bar);

            feed.Push("a");

            Assert.AreEqual(1, bar.Count);
            CollectionAssert.AreEqual(new[] { "a" }, LabelsInSiblingOrder(bar));
        }

        [Test]
        public void A_duplicate_key_rejects_the_whole_push()
        {
            var bar = Open().Get<TabBar>("bar");
            var feed = new Feed(bar);
            feed.Push("a", "b");
            var a = bar.GetAt(0);

            LogAssert.Expect(LogType.Exception, new Regex("share the key"));
            feed.Push("x", "y", "x");

            Assert.AreEqual(2, bar.Count, "the bar keeps showing the previous push");
            Assert.AreSame(a, bar.GetAt(0));
            CollectionAssert.AreEqual(new[] { "a", "b" }, LabelsInSiblingOrder(bar));
        }

        [Test]
        public void TabMenu_keeps_the_selected_item_selected_by_key()
        {
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{RowTemplate}<Screen name='S'><TabMenu id='m' itemTemplate='Row'/></Screen></PromptUGUI>");
            var m = UI.Open("S").Get<TabMenu>("m");
            var feed = new Subject<IReadOnlyList<string>>();
            using var sub = m.BindItems<string, IControl, string>(feed,
                (row, s) => row.Get<Tab>("tab").Text = s, key: s => s);
            feed.OnNext(new[] { "a", "b", "c" });
            var b = m.GetAt(1);
            b.IsOn = true;

            feed.OnNext(new[] { "c", "b" });

            Assert.AreSame(b, m.SelectedTab);
            Assert.AreEqual(1, m.SelectedIndex);
        }
    }
}
