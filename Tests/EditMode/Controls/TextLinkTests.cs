using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Points on a TMP text and clicks at them, the way the EventSystem delivers them: the click goes
    /// through <c>ExecuteEvents</c> to the text's own GameObject. With no camera a screen point is a
    /// world point (Overlay — <c>RectTransformUtility.ScreenPointToRay</c>), so a character's world
    /// centre is the screen point over it wherever the canvas happens to sit.
    /// </summary>
    internal static class LinkPointer
    {
        internal static Vector2 OverChar(TMP_Text tmp, int index)
        {
            tmp.ForceMeshUpdate(true);
            var c = tmp.textInfo.characterInfo[index];
            return tmp.transform.TransformPoint(new Vector3(
                (c.bottomLeft.x + c.topRight.x) * 0.5f, (c.descender + c.ascender) * 0.5f, 0f));
        }

        /// <summary>Over the first character of link <paramref name="link"/>.</summary>
        internal static Vector2 OverLink(TMP_Text tmp, int link = 0)
        {
            tmp.ForceMeshUpdate(true);
            return OverChar(tmp, tmp.textInfo.linkInfo[link].linkTextfirstCharacterIndex);
        }

        /// <summary>Over the last character — the tests' texts end in plain words.</summary>
        internal static Vector2 OffLinks(TMP_Text tmp)
        {
            tmp.ForceMeshUpdate(true);
            return OverChar(tmp, tmp.textInfo.characterCount - 1);
        }

        internal static void Click(TMP_Text tmp, Vector2 at,
            PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            var e = new PointerEventData(EventSystem.current)
            {
                position = at,
                pressPosition = at,
                button = button,
                eligibleForClick = true,
            };
            ExecuteEvents.Execute(tmp.gameObject, e, ExecuteEvents.pointerClickHandler);
        }
    }

    /// <summary>
    /// <c>Text.OnLinkClicked</c> (spec 2026-09-30-text-link-click): a click on a TMP
    /// <c>&lt;link="id"&gt;</c> emits the id, looked up in the text's layout when the click lands.
    /// Hit tests go through <c>Graphic.Raycast</c>, so the raycast-filter walk is uGUI's own.
    /// </summary>
    public class TextLinkTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Line = "<link=\"player:42\">Alice</link> says hi";

        private static Screen Open(string body, string top = "")
        {
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{top}<Screen name='S'>
  {body}
</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static Text OpenText(string attrs = "", string text = Line)
            => Open($"<Text id='t' size='400x40' wrap='false' {attrs}><![CDATA[{text}]]></Text>").Get<Text>("t");

        /// <summary>A Text inside a row that counts the clicks reaching it.</summary>
        private static (Text text, ClickCounter row) OpenInRow(string rowAttrs = "", string textAttrs = "")
        {
            var s = Open($"<Frame id='row' size='400x40' {rowAttrs}>" +
                         $"<Text id='t' anchor='stretch' wrap='false' {textAttrs}><![CDATA[{Line}]]></Text></Frame>");
            return (s.Get<Text>("t"), s.Get<Frame>("row").GameObject.AddComponent<ClickCounter>());
        }

        private static TMP_Text Tmp(Text t) => t.GameObject.GetComponent<TMP_Text>();

        private sealed class ClickCounter : MonoBehaviour, IPointerClickHandler
        {
            public int Clicks;
            public void OnPointerClick(PointerEventData e) => Clicks++;
        }

        // ===== what a click reports =====

        [Test]
        public void A_click_on_a_link_emits_its_id()
        {
            var t = OpenText();
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t)));

            CollectionAssert.AreEqual(new[] { "player:42" }, got);
        }

        [Test]
        public void The_link_is_looked_up_when_the_click_lands_not_when_subscribing()
        {
            // The chat row: subscribed once, then the text is rewritten for another message.
            var t = OpenText();
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            t.TextValue = "<link=\"item:7\">[Sword]</link> dropped";
            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t)));

            CollectionAssert.AreEqual(new[] { "item:7" }, got);
        }

        [Test]
        public void Of_two_links_the_one_under_the_pointer_is_reported()
        {
            var t = OpenText(text: "<link=\"a\">Alice</link> and <link=\"b\">Bob</link> left");
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t), 1));

            CollectionAssert.AreEqual(new[] { "b" }, got);
        }

        [Test]
        public void A_click_off_the_links_emits_nothing_and_goes_on_to_the_ancestors()
        {
            var (t, row) = OpenInRow(textAttrs: "raycastTarget='true'");
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OffLinks(Tmp(t)));

            CollectionAssert.IsEmpty(got);
            Assert.AreEqual(1, row.Clicks,
                "uGUI hands the click to the nearest click handler — the text; off a link it must pass it on, " +
                "or a linked text inside a <Btn> swallows the button's clicks");
        }

        [Test]
        public void A_link_click_does_not_also_click_the_ancestors()
        {
            var (t, row) = OpenInRow();
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t)));

            CollectionAssert.AreEqual(new[] { "player:42" }, got);
            Assert.AreEqual(0, row.Clicks);
        }

        [Test]
        public void Only_the_left_button_clicks_a_link()
        {
            var (t, row) = OpenInRow();
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t)), PointerEventData.InputButton.Right);

            CollectionAssert.IsEmpty(got);
            Assert.AreEqual(1, row.Clicks, "a right click belongs to the ancestors (a context menu)");
        }

        [Test]
        public void Interactable_false_on_the_text_mutes_its_links()
        {
            var (t, row) = OpenInRow(textAttrs: "interactable='false'");
            var got = new List<string>();
            using var _ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t)));

            CollectionAssert.IsEmpty(got);
            Assert.AreEqual(1, row.Clicks, "a muted link is plain text: the click goes on");
        }

        [Test]
        public void Interactable_false_on_an_ancestor_mutes_the_links()
        {
            var (t, _) = OpenInRow(rowAttrs: "interactable='false'");
            var got = new List<string>();
            using var __ = t.OnLinkClicked.Subscribe(got.Add);

            LinkPointer.Click(Tmp(t), LinkPointer.OverLink(Tmp(t)));

            CollectionAssert.IsEmpty(got, "CanvasGroup.interactable up the chain, as for a Selectable");
        }

        // ===== hit-testing =====

        [Test]
        public void Without_a_subscriber_the_text_stays_click_through_and_carries_nothing()
        {
            var t = OpenText();
            Assert.IsFalse(Tmp(t).raycastTarget);
            Assert.IsNull(t.GameObject.GetComponent<TextLinkClicker>(), "a plain <Text> pays nothing");
        }

        [Test]
        public void Subscribing_makes_the_links_hit_and_only_the_links()
        {
            var t = OpenText();
            var tmp = Tmp(t);
            using var _ = t.OnLinkClicked.Subscribe(__ => { });

            Assert.IsTrue(tmp.raycastTarget, "subscribing is the intent (same rule as Image.OnPointer*)");
            Assert.IsTrue(tmp.Raycast(LinkPointer.OverLink(tmp), null), "a link catches the pointer");
            Assert.IsFalse(tmp.Raycast(LinkPointer.OffLinks(tmp), null),
                "the rest of the text stays click-through — a mini chat over the game view must not eat taps");
        }

        [Test]
        public void RaycastTarget_true_keeps_the_whole_text_hit()
        {
            var t = OpenText("raycastTarget='true'");
            var tmp = Tmp(t);
            using var _ = t.OnLinkClicked.Subscribe(__ => { });

            Assert.IsTrue(tmp.Raycast(LinkPointer.OffLinks(tmp), null), "the author asked for the whole rect");
            Assert.IsTrue(tmp.Raycast(LinkPointer.OverLink(tmp), null));
        }

        [Test]
        public void RaycastTarget_false_with_a_subscriber_stays_off_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("raycastTarget=\"false\".*OnLinkClicked"));
            var t = OpenText("raycastTarget='false'");

            using var _ = t.OnLinkClicked.Subscribe(__ => { });

            Assert.IsFalse(Tmp(t).raycastTarget, "the author's explicit false wins");
        }

        [Test]
        public void A_ReSolve_keeps_the_links_hit()
        {
            // Every pass settles raycastTarget from what it declared; the subscription must survive it.
            var t = OpenText("color='#ffffff' color.mobile='#000000'");
            var tmp = Tmp(t);
            using var _ = t.OnLinkClicked.Subscribe(__ => { });

            UI.Variants.Set("mobile", true);
            UI.Variants.Set("mobile", false);

            Assert.IsTrue(tmp.raycastTarget);
            Assert.IsTrue(tmp.Raycast(LinkPointer.OverLink(tmp), null));
            Assert.IsFalse(tmp.Raycast(LinkPointer.OffLinks(tmp), null));
        }

        [Test]
        public void A_variant_only_raycastTarget_true_gives_the_hit_area_back_to_the_links_when_it_leaves()
        {
            var t = OpenText("raycastTarget.mobile='true'");
            var tmp = Tmp(t);
            using var _ = t.OnLinkClicked.Subscribe(__ => { });
            UI.Variants.Set("mobile", true);
            Assume.That(tmp.Raycast(LinkPointer.OffLinks(tmp), null), Is.True);

            UI.Variants.Set("mobile", false);

            Assert.IsTrue(tmp.raycastTarget, "the subscription still wants the links");
            Assert.IsFalse(tmp.Raycast(LinkPointer.OffLinks(tmp), null));
        }

        // ===== a recycled row =====

        [Test]
        public void A_reused_row_reports_the_link_of_the_item_it_shows_now_once()
        {
            var s = Open("<ScrollList id='list' anchor='stretch' itemTemplate='Row'/>",
                "<Template name='Row'><HStack width='stretch' height='40'>" +
                "<Text id='body' width='stretch' wrap='false'/></HStack></Template>");
            var list = s.Get<ScrollList>("list");
            var feed = new Subject<IReadOnlyList<string>>();
            var got = new List<string>();
            TMP_Text body = null;
            using var bind = list.BindItems(feed, (IControl row, string id) =>
            {
                var text = row.Get<Text>("body");
                text.TextValue = $"<link=\"{id}\">{id}</link> wrote";
                text.OnLinkClicked.Subscribe(got.Add).AddTo(row);   // once per bind — the chat-row pattern
                body = Tmp(text);
            });

            feed.OnNext(new[] { "msg:1" });
            var first = body;
            feed.OnNext(new[] { "msg:2" });   // the same row, bound again for another message
            Assume.That(body, Is.SameAs(first), "guard: the row was reused, not rebuilt");

            LinkPointer.Click(body, LinkPointer.OverLink(body));

            CollectionAssert.AreEqual(new[] { "msg:2" }, got,
                "the first bind's subscription left with the row's bag; the link is the one on screen now");
        }
    }
}
