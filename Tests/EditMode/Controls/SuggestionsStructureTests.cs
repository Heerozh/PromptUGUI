using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;
using Scrollbar = PromptUGUI.Controls.Scrollbar;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Suggestions&gt;</c> as a part element of <c>&lt;InputField&gt;</c> (spec 2026-10-18
    /// §5.1 / §5.4): who adopts it, what the panel is made of, and what it does where it does not
    /// belong. Opening, rows and placement have their own fixtures.
    /// </summary>
    public class SuggestionsStructureTests
    {
        private const string RowTemplate = "<Template name='Row'><Text id='label' height='32'/></Template>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string body)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + RowTemplate
                                 + "<Screen name='S'>" + body + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static Screen OpenField(string suggestionsAttrs = "", string suggestionsChildren = "")
            => Open("<InputField id='f' width='240' height='44'>"
                    + $"<Suggestions id='s' itemTemplate='Row' {suggestionsAttrs}>{suggestionsChildren}</Suggestions>"
                    + "</InputField>");

        // ── adoption ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Adopted_by_its_InputField()
        {
            var screen = OpenField();
            var field = screen.Get<InputField>("f");
            var s = screen.Get<Suggestions>("s");

            Assert.AreSame(s, field.Suggestions);
            Assert.AreSame(field.GameObject.transform, s.GameObject.transform.parent,
                "under the field, so a press in the panel walks up to the field's ISelectHandler");
        }

        [Test]
        public void A_field_without_one_has_null()
            => Assert.IsNull(Open("<InputField id='f'/>").Get<InputField>("f").Suggestions);

        // ── the panel ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Panel_carries_canvas_raycaster_canvasgroup_scrollrect_viewport_content_and_is_inactive_while_closed()
        {
            var s = OpenField().Get<Suggestions>("s");
            var go = s.GameObject;

            Assert.IsNotNull(go.GetComponent<Canvas>());
            Assert.IsNotNull(go.GetComponent<GraphicRaycaster>());
            Assert.IsNotNull(go.GetComponent<CanvasGroup>());
            Assert.IsNotNull(go.GetComponent<PointerSink>());
            var scroll = go.GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll);
            Assert.IsFalse(scroll.horizontal);

            var viewport = go.transform.Find("Viewport");
            Assert.IsNotNull(viewport);
            Assert.IsNotNull(viewport.GetComponent<RectMask2D>());
            var content = viewport.Find("Content");
            Assert.IsNotNull(content);
            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>());
            Assert.IsNotNull(content.GetComponent<ContentSizeFitter>());
            Assert.AreSame(viewport, scroll.viewport);
            Assert.AreSame(content, scroll.content);

            Assert.IsFalse(go.activeSelf, "closed");
            Assert.IsFalse(s.IsOpen);
        }

        [Test]
        public void Builds_a_default_scrollbar_when_none_is_authored()
        {
            var s = OpenField().Get<Suggestions>("s");
            var scroll = s.GameObject.GetComponent<ScrollRect>();

            var bar = s.GameObject.transform.Find(Scrollbar.NodeName);
            Assert.IsNotNull(bar, "a direct child of the ScrollRect's transform");
            Assert.AreSame(bar.GetComponent<UnityEngine.UI.Scrollbar>(), scroll.verticalScrollbar);
        }

        [Test]
        public void Adopts_an_authored_scrollbar()
        {
            var screen = OpenField(suggestionsChildren: "<Scrollbar id='bar' thickness='6'/>");
            var s = screen.Get<Suggestions>("s");
            var scroll = s.GameObject.GetComponent<ScrollRect>();

            Assert.AreSame(screen.Get<Scrollbar>("bar").Bar, scroll.verticalScrollbar);
            Assert.AreEqual(1, s.GameObject.GetComponentsInChildren<UnityEngine.UI.Scrollbar>(true).Length,
                "no default bar beside the authored one");
        }

        [Test]
        public void The_scrollbar_is_display_only()
        {
            // uGUI's Scrollbar is a Selectable: pressing it would deselect the field being edited.
            foreach (var children in new[] { "", "<Scrollbar id='bar' thickness='6'/>" })
            {
                UI.ResetForTests();
                var s = OpenField(suggestionsChildren: children).Get<Suggestions>("s");
                var bar = s.GameObject.GetComponentInChildren<UnityEngine.UI.Scrollbar>(true);
                foreach (var g in bar.GetComponentsInChildren<Graphic>(true))
                    Assert.IsFalse(g.raycastTarget, $"'{g.name}' in the {(children == "" ? "default" : "authored")} bar");
            }
        }

        // ── where it does not belong ───────────────────────────────────────────────────────

        [Test]
        public void Outside_an_InputField_it_stays_inert_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("only meaningful as a direct child of <InputField>"));

            var s = Open("<Frame id='box'><Suggestions id='s' itemTemplate='Row'/></Frame>").Get<Suggestions>("s");

            Assert.IsFalse(s.GameObject.activeSelf);
            Assert.IsFalse(s.IsOpen);
        }

        [Test]
        public void A_second_one_is_parked_inactive_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("a second <Suggestions>"));

            var screen = Open("<InputField id='f'>"
                              + "<Suggestions id='s1' itemTemplate='Row'/><Suggestions id='s2' itemTemplate='Row'/>"
                              + "</InputField>");

            Assert.AreSame(screen.Get<Suggestions>("s1"), screen.Get<InputField>("f").Suggestions);
            Assert.IsFalse(screen.Get<Suggestions>("s2").GameObject.activeSelf);
        }

        // ── attributes ApplyCommon writes ──────────────────────────────────────────────────

        [Test]
        public void Layout_attributes_do_not_move_the_panel()
        {
            var screen = OpenField("anchor='center' size='50x50' hidden='false' hidden.mobile='false'");
            var s = screen.Get<Suggestions>("s");
            Assert.IsFalse(s.GameObject.activeSelf, "hidden='false' does not open a closed panel");

            UI.Variants.Set("mobile", true);

            Assert.IsFalse(s.GameObject.activeSelf, "nor does a variant that re-applies it");
        }

        [Test]
        public void An_authored_interactable_does_not_add_a_second_CanvasGroup()
        {
            var s = OpenField("interactable='false'").Get<Suggestions>("s");
            Assert.AreEqual(1, s.GameObject.GetComponents<CanvasGroup>().Length);
            Assert.IsFalse(s.GameObject.GetComponent<CanvasGroup>().interactable);
        }

        [Test]
        public void An_unknown_placement_throws()
        {
            var ex = Assert.Throws<ParseException>(() => OpenField("placement='left'"));
            StringAssert.Contains("placement", ex.Message);
        }

        [Test]
        public void A_negative_maxHeight_or_gap_warns_and_falls_back()
        {
            LogAssert.Expect(LogType.Warning, new Regex("maxHeight='-5'"));
            LogAssert.Expect(LogType.Warning, new Regex("gap='-1'"));

            var s = OpenField("maxHeight='-5' gap='-1'").Get<Suggestions>("s");

            Assert.AreEqual(Suggestions.DefaultMaxHeight, s.MaxHeightValue);
            Assert.AreEqual(Suggestions.DefaultGap, s.GapValue);
        }

        [Test]
        public void Reads_its_panel_attributes()
        {
            var s = OpenField("maxHeight='200' gap='6' placement='above' spacing='3' padding='4' transition='0'")
                .Get<Suggestions>("s");
            var vlg = s.GameObject.transform.Find("Viewport/Content").GetComponent<VerticalLayoutGroup>();

            Assert.AreEqual(200f, s.MaxHeightValue);
            Assert.AreEqual(6f, s.GapValue);
            Assert.IsTrue(s.PrefersAbove);
            Assert.AreEqual(3f, vlg.spacing);
            Assert.AreEqual(4, vlg.padding.left);
            Assert.AreEqual(0f, s.TransitionSeconds);
        }

        [Test]
        public void Defaults()
        {
            var s = OpenField().Get<Suggestions>("s");
            Assert.AreEqual(320f, s.MaxHeightValue);
            Assert.AreEqual(4f, s.GapValue);
            Assert.IsFalse(s.PrefersAbove);
            Assert.AreEqual(0.1f, s.TransitionSeconds, 1e-5f);
        }
    }
}
