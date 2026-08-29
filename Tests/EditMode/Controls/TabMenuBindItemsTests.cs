using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Dynamic rows for <c>&lt;TabMenu&gt;</c> — the same <c>BindItems</c> / <c>itemTemplate</c>
    /// contract <c>&lt;TabBar&gt;</c> has (they share <c>TabGroupCore</c>), plus the one thing that
    /// is genuinely different: rows are built inside a popup that is switched off while collapsed.
    /// </summary>
    public class TabMenuBindItemsTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static TabMenu OpenMenu(string innerXml) => Open(innerXml).Get<TabMenu>("m");

        private static RectTransform Popup(TabMenu m) => (RectTransform)m.RectTransform.Find("Popup");

        private static TMP_Text CaptionOf(TabMenu m)
            => m.RectTransform.Find("Label").GetComponent<TMP_Text>();

        private static TMP_Text LabelOf(Tab t)
            => t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();

        private static IDisposable BindStrings(TabMenu m, params string[] items)
            => m.BindItems(Observable.Return<IReadOnlyList<string>>(items),
                           (Tab tab, string s) => tab.Text = s);

        [Test]
        public void Builds_rows_inside_the_popup()
        {
            var m = OpenMenu("<TabMenu id='m'/>");
            using var sub = BindStrings(m, "A", "B", "C");

            Assert.AreEqual(3, m.Count);
            Assert.AreSame(Popup(m).Find("Content"), m.GetAt(0).RectTransform.parent,
                           "dynamic rows land in the menu, not on the handle");
        }

        [Test]
        public void Replaces_static_rows()
        {
            var m = OpenMenu("<TabMenu id='m'><Tab text='static'/></TabMenu>");
            Assert.AreEqual(1, m.Count);

            using var sub = BindStrings(m, "dyn1", "dyn2");
            Assert.AreEqual(2, m.Count);
        }

        [Test]
        public void Caption_shows_the_first_bound_row()
        {
            var m = OpenMenu("<TabMenu id='m'/>");
            using var sub = BindStrings(m, "World", "Guild");

            Assert.AreEqual(0, m.SelectedIndex, "auto-selects the first, as TabBar does");
            Assert.AreEqual("World", CaptionOf(m).text);
        }

        // The regression this whole design danced around: a TMP added by AddComponent on an
        // INACTIVE GameObject never runs Awake, and then reports a preferred width of 0 forever.
        // Rows bound while the menu is collapsed are built inside exactly such a parent, so the
        // rebuild has to activate the popup for the duration and switch it back afterwards.
        [Test]
        public void Rows_bound_while_collapsed_still_measure()
        {
            var m = OpenMenu("<TabMenu id='m'/>");
            Assert.IsFalse(Popup(m).gameObject.activeSelf, "precondition: collapsed");

            using var sub = BindStrings(m, "A wide enough channel name");

            var label = LabelOf(m.GetAt(0));
            Assert.Greater(label.GetPreferredValues(label.text).x, 0f,
                           "a row built inside an inactive popup must still know how wide it is");
            Assert.IsFalse(Popup(m).gameObject.activeSelf, "…and the menu is left closed");
        }

        [Test]
        public void Binding_while_expanded_leaves_the_menu_open()
        {
            var m = OpenMenu("<TabMenu id='m' transition='0'/>");
            m.Expand();

            using var sub = BindStrings(m, "A", "B");

            Assert.IsTrue(m.IsExpanded);
            Assert.IsTrue(Popup(m).gameObject.activeSelf);
            Assert.Greater(Popup(m).rect.height, 0f, "…and is resized for its new contents");
        }

        [Test]
        public void An_empty_list_clears_the_caption()
        {
            var m = OpenMenu("<TabMenu id='m'><Tab text='static'/></TabMenu>");
            Tab seen = null;
            var fired = false;
            using var selection = m.OnSelectionChanged.Subscribe(t => { seen = t; fired = true; });

            using var sub = m.BindItems(
                Observable.Return<IReadOnlyList<string>>(new string[0]),
                (Tab tab, string s) => tab.Text = s);

            Assert.AreEqual(0, m.Count);
            Assert.AreEqual(-1, m.SelectedIndex);
            Assert.IsNull(m.SelectedTab);
            Assert.AreEqual("", CaptionOf(m).text, "nothing selected means nothing to mirror");
            Assert.IsTrue(fired);
            Assert.IsNull(seen, "…and subscribers are told so explicitly");
        }

        [Test]
        public void Picking_a_bound_row_switches_the_caption_and_closes()
        {
            var m = OpenMenu("<TabMenu id='m' transition='0'/>");
            using var sub = BindStrings(m, "World", "Guild");
            m.Expand();

            m.GetAt(1).IsOn = true;

            Assert.AreEqual("Guild", CaptionOf(m).text);
            Assert.IsFalse(m.IsExpanded);
        }

        [Test]
        public void ItemTemplate_wrapping_a_Tab_is_found()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame height='40'><Tab id='tab' anchor='stretch'/></Frame></Template>
  <Screen name='S'><TabMenu id='m' itemTemplate='Row'/></Screen>
</PromptUGUI>");
            var m = UI.Open("S").Get<TabMenu>("m");

            using var sub = m.BindItems<string, IControl>(
                Observable.Return<IReadOnlyList<string>>(new[] { "A", "B" }),
                (slot, text) => slot.Get<Tab>("tab").Text = text);

            Assert.AreEqual(2, m.Count, "the <Tab> nested inside the wrapper is what carries tab semantics");
            Assert.AreEqual("A", CaptionOf(m).text);
            Assert.AreSame(Popup(m).Find("Content"), ((Control)m.GetAt(0)).RectTransform.parent.parent,
                           "the wrapper is the row; the Tab sits inside it");
        }

        [Test]
        public void Rebinding_replaces_the_previous_rows()
        {
            var m = OpenMenu("<TabMenu id='m'/>");
            var items = new ReactiveProperty<IReadOnlyList<string>>(new[] { "A", "B" });
            using var sub = m.BindItems(items, (Tab tab, string s) => tab.Text = s);
            Assert.AreEqual(2, m.Count);

            items.Value = new[] { "X" };

            Assert.AreEqual(1, m.Count);
            Assert.AreEqual("X", CaptionOf(m).text);
        }
    }
}
