using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>Suggestions.BindItems</c> (spec 2026-10-18 §6.9): the ScrollList reuse contract — by
    /// position without a key, by key with one, subscriptions released before a reused row is bound
    /// again, every row rebuilt when <c>itemTemplate</c> changes.
    /// </summary>
    public class SuggestionsBindItemsTests
    {
        private const string Templates =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>" +
            "<Template name='Row2'><Frame height='40'><Text id='label'>y</Text></Frame></Template>" +
            "<Template name='BtnRow'><Btn id='b' height='30'>x</Btn></Template>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Suggestions Open(string attrs = "itemTemplate='Row'")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Templates
                                 + "<Screen name='S'><InputField id='f' width='240' height='44'>"
                                 + $"<Suggestions id='s' {attrs}/></InputField></Screen></PromptUGUI>");
            return UI.Open("S").Get<Suggestions>("s");
        }

        private static string LabelOf(IControl row) =>
            row.Get<Text>("label").GameObject.GetComponent<TMP_Text>().text;

        private static List<IControl> Push(Suggestions s, params string[] items)
        {
            var rows = new List<IControl>();
            s.BindItems(Observable.Return<IReadOnlyList<string>>(items),
                        (IControl row, string v) =>
                        {
                            rows.Add(row);
                            row.Get<Text>("label").TextValue = v;
                        });
            return rows;
        }

        private static List<IControl> PushKeyed(Suggestions s, params string[] items)
        {
            var rows = new List<IControl>();
            s.BindItems(Observable.Return<IReadOnlyList<string>>(items),
                        (IControl row, string v) =>
                        {
                            rows.Add(row);
                            row.Get<Text>("label").TextValue = v;
                        },
                        key: v => v);
            return rows;
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        [Test]
        public void Positional_push_reuses_rows_by_position()
        {
            var s = Open();
            var first = Push(s, "a0", "a1", "a2");
            var gos = first.ConvertAll(r => r.GameObject);

            var second = Push(s, "b0", "b1", "b2");

            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(first[i], second[i], $"row {i}");
                Assert.AreSame(gos[i], second[i].GameObject);
                Assert.AreEqual("b" + i, LabelOf(second[i]));
            }
            Assert.AreEqual(3, s.Content.childCount);
        }

        [Test]
        public void Keyed_push_reuses_rows_by_key()
        {
            var s = Open();
            var first = PushKeyed(s, "a", "b", "c");
            var byKey = new Dictionary<string, IControl> { ["a"] = first[0], ["b"] = first[1], ["c"] = first[2] };

            var second = PushKeyed(s, "c", "a", "b");

            Assert.AreSame(byKey["c"], second[0]);
            Assert.AreSame(byKey["a"], second[1]);
            Assert.AreSame(byKey["b"], second[2]);
            for (var j = 0; j < 3; j++)
                Assert.AreEqual(new[] { "c", "a", "b" }[j],
                                LabelOf(second[j]), $"sibling {j} shows item {j}");
            for (var j = 0; j < 3; j++)
                Assert.AreSame(s.Content.GetChild(j), second[j].GameObject.transform.parent, "sibling order = data order");
        }

        [Test]
        public void Reused_rows_release_subscriptions_before_rebind()
        {
            var s = Open();
            var subs = new List<CountingDisposable>();
            s.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b" }),
                        (IControl row, string v) =>
                        {
                            var d = new CountingDisposable();
                            d.AddTo(row);
                            subs.Add(d);
                        });
            var releasedBeforeBind = new List<bool>();

            s.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "c", "d" }),
                        (IControl row, string v) => releasedBeforeBind.Add(subs[releasedBeforeBind.Count].Disposed == 1));

            CollectionAssert.AreEqual(new[] { true, true }, releasedBeforeBind);
        }

        [Test]
        public void Changing_itemTemplate_rebuilds_every_row()
        {
            var s = Open();
            var first = Push(s, "a", "b");

            s.ItemTemplate = "Row2";
            var second = Push(s, "a", "b");

            Assert.AreNotSame(first[0], second[0]);
            Assert.AreNotSame(first[1], second[1]);
            Assert.IsTrue(first[0].GameObject == null, "the old rows are gone");
            Assert.AreEqual(2, s.Content.childCount);
        }

        [Test]
        public void Retired_rows_leave_Content_before_bind()
        {
            var s = Open();
            Push(s, "a", "b", "c", "d", "e");
            var childCountsSeen = new List<int>();

            s.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "x", "y" }),
                        (IControl row, string v) => childCountsSeen.Add(s.Content.childCount));

            CollectionAssert.AreEqual(new[] { 2, 2 }, childCountsSeen);
        }

        [Test]
        public void Count_is_the_last_push()
        {
            var s = Open();
            Assert.AreEqual(0, s.Count);
            Push(s, "a", "b", "c");
            Assert.AreEqual(3, s.Count);
            Push(s);
            Assert.AreEqual(0, s.Count);
        }

        [Test]
        public void Without_itemTemplate_the_push_logs_an_exception()
        {
            var s = Open("");
            LogAssert.Expect(LogType.Exception, new Regex("itemTemplate"));

            Push(s, "a");

            Assert.AreEqual(0, s.Content.childCount);
        }

        [Test]
        public void A_row_template_with_a_Btn_warns_once()
        {
            var s = Open("itemTemplate='BtnRow'");
            LogAssert.Expect(LogType.Warning, new Regex("Selectable"));

            s.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b" }), (IControl row, string v) => { });
            s.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }), (IControl row, string v) => { });

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Rows_pushed_while_closed_still_measure()
        {
            var s = Open();
            Assert.IsFalse(s.GameObject.activeSelf, "precondition: closed");

            var rows = Push(s, "A wide enough suggestion");

            var label = rows[0].Get<Text>("label").GameObject.GetComponent<TMP_Text>();
            Assert.Greater(label.GetPreferredValues(label.text).x, 0f,
                           "a row built inside an inactive panel must still know how wide it is");
            Assert.IsFalse(s.GameObject.activeSelf, "…and the list is left closed");
        }
    }
}
