using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;
using PuiText = PromptUGUI.Controls.Text;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Structure, attributes and the instant (<c>transition='0'</c>) fold of
    /// <c>&lt;Collapsible&gt;</c> — spec <c>2026-08-31-collapsible-design.md</c>. Sizing lives in
    /// <see cref="CollapsibleSizeTests"/>, the accordion in <c>CollapsibleGroupTests</c>, the
    /// animated fold in the PlayMode suite.
    /// </summary>
    public class CollapsibleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        internal static PuguiScreen Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        internal static RectTransform Header(Collapsible c) => (RectTransform)c.RectTransform.Find("Header");
        internal static RectTransform Body(Collapsible c) => (RectTransform)c.RectTransform.Find("Body");
        internal static RectTransform Content(Collapsible c) => (RectTransform)Body(c).Find("Content");
        private static UnityImage Arrow(Collapsible c) => Header(c).Find("Arrow").GetComponent<UnityImage>();
        private static float ArrowTurn(Collapsible c)
            => Arrow(c).GetComponent<RotateFlipEffect>().Rotation;
        private static TMP_Text Label(Collapsible c) => Header(c).Find("Label").GetComponent<TMP_Text>();

        // ── Structure ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Builds_a_header_and_a_body()
        {
            var c = Open("<Collapsible id='c' text='任务'><Btn id='r'/></Collapsible>").Get<Collapsible>("c");

            Assert.IsNotNull(Header(c), "header bar");
            Assert.IsNotNull(Body(c), "body");
            Assert.IsNotNull(Content(c), "…and the content node the author's children land in");
            Assert.IsNotNull(c.RectTransform.GetComponent<VerticalLayoutGroup>(),
                             "the root stacks header over body");
            Assert.IsNotNull(Header(c).GetComponent<PuiButton>(), "the whole header bar is the click target");
            Assert.IsNotNull(Body(c).GetComponent<RectMask2D>(), "the body clips what does not fit yet");
            Assert.IsNotNull(Body(c).GetComponent<CanvasGroup>(), "…and fades");
            Assert.IsNotNull(Content(c).GetComponent<VerticalLayoutGroup>(), "the body is a column");
        }

        [Test]
        public void Author_children_land_in_the_content_node()
        {
            var c = Open("<Collapsible id='c' text='任务'><Btn id='r1'/><Btn id='r2'/></Collapsible>")
                .Get<Collapsible>("c");

            Assert.AreEqual(2, Content(c).childCount);
            Assert.AreEqual("r1", Content(c).GetChild(0).name);
        }

        [Test]
        public void The_label_is_lazy()
        {
            var bare = Open("<Collapsible id='c'><Btn id='r'/></Collapsible>").Get<Collapsible>("c");
            Assert.IsTrue(string.IsNullOrEmpty(Label(bare).text), "no text= means no caption text");

            UI.ResetForTests();
            var titled = Open("<Collapsible id='c' text='任务'><Btn id='r'/></Collapsible>").Get<Collapsible>("c");
            Assert.AreEqual("任务", Label(titled).text);
        }

        [Test]
        public void Header_slot_children_land_in_the_header_host()
        {
            var s = Open(@"<Collapsible id='c'>
                             <Header><Text id='count' tr='false'>3</Text></Header>
                             <Btn id='r'/>
                           </Collapsible>");
            var c = s.Get<Collapsible>("c");

            var count = s.Get<PuiText>("count");
            Assert.IsTrue(count.RectTransform.IsChildOf(Header(c)), "header content sits in the header bar");
            Assert.IsFalse(count.RectTransform.IsChildOf(Content(c)), "…and not in the body");
            Assert.AreEqual(1, Content(c).childCount, "the body only got the non-header children");
        }

        [Test]
        public void Header_slot_children_are_reachable_by_path()
        {
            var s = Open(@"<Collapsible id='c'>
                             <Header><Text id='count' tr='false'>3</Text></Header>
                             <Btn id='r'/>
                           </Collapsible>");
            Assert.IsNotNull(s.Get<PuiText>("count"));
        }

        [Test]
        public void The_arrow_is_pinned_to_the_right_edge()
        {
            var c = Open("<Collapsible id='c' text='任务' width='150' headerHeight='24'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            Canvas.ForceUpdateCanvases();

            var arrow = Arrow(c).rectTransform;
            Assert.AreEqual(1f, arrow.anchorMin.x, 0.001f, "right-anchored, so a wider header keeps it at the edge");
            Assert.AreEqual(1f, arrow.pivot.x, 0.001f);
        }

        [Test]
        public void Arrow_empty_hides_the_caret()
        {
            var c = Open("<Collapsible id='c' text='任务' arrow=''><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            Assert.IsFalse(Arrow(c).enabled, "arrow='' hides it — a sprite-less Image would draw a block");
        }

        // ── expanded / transition='0' ──────────────────────────────────────────────────────

        [Test]
        public void Expanded_by_default()
        {
            var c = Open("<Collapsible id='c' text='任务'><Btn id='r'/></Collapsible>").Get<Collapsible>("c");
            Assert.IsTrue(c.IsExpanded, "a lone panel is open — the common case needs no attribute");
            Assert.IsTrue(Content(c).gameObject.activeSelf);
        }

        [Test]
        public void Collapsed_at_open_hides_and_zeroes_the_body()
        {
            var c = Open("<Collapsible id='c' text='任务' expanded='false' transition='0'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");

            Assert.IsFalse(c.IsExpanded);
            Assert.AreEqual(0f, LayoutUtility.GetPreferredHeight(Body(c)), 0.01f);
            Assert.AreEqual(0f, Body(c).GetComponent<CanvasGroup>().alpha, 0.01f);
            Assert.AreEqual(180f, ArrowTurn(c), 0.01f, "the caret points up while closed");
            Assert.IsFalse(Content(c).gameObject.activeSelf, "a closed body neither renders nor takes clicks");
        }

        [Test]
        public void Toggle_lands_on_the_end_state_immediately_with_no_transition()
        {
            var c = Open("<Collapsible id='c' text='任务' transition='0'><Btn id='r' height='32'/></Collapsible>")
                .Get<Collapsible>("c");
            Canvas.ForceUpdateCanvases();

            c.Toggle();
            Assert.IsFalse(c.IsExpanded);
            Assert.AreEqual(0f, LayoutUtility.GetPreferredHeight(Body(c)), 0.01f);
            Assert.AreEqual(180f, ArrowTurn(c), 0.01f);
            Assert.IsFalse(Content(c).gameObject.activeSelf);

            c.Toggle();
            Assert.IsTrue(c.IsExpanded);
            Assert.AreEqual(32f, LayoutUtility.GetPreferredHeight(Body(c)), 0.5f);
            Assert.AreEqual(0f, ArrowTurn(c), 0.01f);
            Assert.IsTrue(Content(c).gameObject.activeSelf);
        }

        [Test]
        public void Expand_and_collapse_are_idempotent()
        {
            var c = Open("<Collapsible id='c' text='任务' transition='0'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            var opened = 0;
            var closed = 0;
            c.OnExpanded.Subscribe(_ => opened++);
            c.OnCollapsed.Subscribe(_ => closed++);

            c.Expand();
            Assert.AreEqual(0, opened, "already open");
            c.Collapse();
            c.Collapse();
            Assert.AreEqual(1, closed, "the second close is a no-op");
        }

        [Test]
        public void OnToggled_reports_the_new_state()
        {
            var c = Open("<Collapsible id='c' text='任务' transition='0'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            bool? last = null;
            c.OnToggled.Subscribe(v => last = v);

            c.Collapse();
            Assert.AreEqual(false, last);
            c.Expand();
            Assert.AreEqual(true, last);
        }

        [Test]
        public void Clicking_the_header_folds_it()
        {
            var c = Open("<Collapsible id='c' text='任务' transition='0'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");

            Header(c).GetComponent<PuiButton>().onClick.Invoke();
            Assert.IsFalse(c.IsExpanded);
        }

        // ── State & interactable ───────────────────────────────────────────────────────────

        [Test]
        public void Interactable_false_disables_the_header_and_the_fold()
        {
            var c = Open("<Collapsible id='c' text='任务' transition='0' interactable='false'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");

            Assert.IsFalse(Header(c).GetComponent<PuiButton>().interactable);
            c.Toggle();
            Assert.IsTrue(c.IsExpanded, "a disabled header cannot fold the panel");
        }

        [Test]
        public void Broadcasts_header_interaction_state()
        {
            var c = Open("<Collapsible id='c' text='任务'><Btn id='r'/></Collapsible>").Get<Collapsible>("c");
            InteractState? last = null;
            c.OnState.Subscribe(s => last = s);
            Assert.AreEqual(InteractState.Normal, last, "state replays on subscribe");

            ExecuteEvents.Execute(Header(c).gameObject, new PointerEventData(EventSystem.current),
                                  ExecuteEvents.pointerEnterHandler);
            Assert.AreEqual(InteractState.Hover, last);
        }

        // ── Body attributes ────────────────────────────────────────────────────────────────

        [Test]
        public void Spacing_and_padding_go_to_the_body_column()
        {
            var c = Open("<Collapsible id='c' text='任务' spacing='6' padding='4,8,4,8'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            var vlg = Content(c).GetComponent<VerticalLayoutGroup>();

            Assert.AreEqual(6f, vlg.spacing, 0.01f);
            Assert.AreEqual(8, vlg.padding.left);
            Assert.AreEqual(4, vlg.padding.top);
        }

        [Test]
        public void HeaderHeight_drives_the_header_slot()
        {
            var c = Open("<Collapsible id='c' text='任务' headerHeight='24'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            Assert.AreEqual(24f, Header(c).GetComponent<LayoutElement>().preferredHeight, 0.01f);
        }

        [Test]
        public void MaxHeight_makes_the_body_scrollable()
        {
            var plain = Open("<Collapsible id='c' text='任务'><Btn id='r'/></Collapsible>").Get<Collapsible>("c");
            Assert.IsNull(Body(plain).GetComponent<ScrollRect>(), "no cap, no scrolling");

            UI.ResetForTests();
            var capped = Open("<Collapsible id='c' text='任务' maxHeight='100'><Btn id='r'/></Collapsible>")
                .Get<Collapsible>("c");
            var scroll = Body(capped).GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll);
            Assert.IsTrue(scroll.vertical);
            Assert.IsFalse(scroll.horizontal);
        }

        // ── Runtime-owned expanded ─────────────────────────────────────────────────────────

        [Test]
        public void A_resolve_does_not_undo_the_users_fold()
        {
            var s = Open("<Collapsible id='c' text='任务' transition='0'><Btn id='r'/></Collapsible>");
            var c = s.Get<Collapsible>("c");

            c.Collapse();
            UI.Variants.Set("mobile", true);   // any ReSolve

            Assert.IsFalse(c.IsExpanded, "expanded= is runtime-owned once the user has touched it");
        }

        [Test]
        public void A_variant_override_still_reaches_it()
        {
            var s = Open("<Collapsible id='c' text='任务' transition='0' expanded='true' expanded.portrait='false'><Btn id='r'/></Collapsible>");
            var c = s.Get<Collapsible>("c");
            Assert.IsTrue(c.IsExpanded);

            UI.Variants.Set("portrait", true);
            Assert.IsFalse(c.IsExpanded, "a declared variant value is not the user's own state");
        }
    }
}
