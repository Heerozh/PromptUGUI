using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// XML children of a <c>&lt;ScrollList&gt;</c> become its initial slots (requirements §R2), the
    /// same deal <c>&lt;Carousel&gt;</c> already offers: they live under Content, they scroll, they
    /// count towards the content size, and the first <c>BindItems</c> clears them for good.
    /// </summary>
    public class ScrollListStaticChildrenTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body, string templates = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>"
                    + templates
                    + $"<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>{body}</Frame></Screen>"
                    + "</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return screen;
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static void Bind(ScrollList list, int count)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "i" + i;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(items),
                (IControl slot, string _) => { });
            Canvas.ForceUpdateCanvases();
        }

        // ───── children land in Content ─────

        [Test]
        public void XML_children_are_parented_to_Content_not_to_the_list_root()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' sprite=''>"
                            + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");
            var content = ContentOf(list);

            Assert.AreSame(content, screen.Get<Frame>("a").GameObject.transform.parent,
                "a child on the list root would sit outside the viewport: neither clipped nor scrolled");
            Assert.AreSame(content, screen.Get<Frame>("b").GameObject.transform.parent);
            Assert.AreEqual(2, list.SlotCount, "static children ARE the initial slots");
        }

        [Test]
        public void Static_children_count_towards_the_content_size()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' sprite='' spacing='0' padding='0'>"
                          + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>")
                .Get<ScrollList>("sl");
            var content = ContentOf(list);

            // Not an absolute number: the list's own content group leaves childControlHeight off, so
            // a row keeps its own rect rather than taking its LayoutElement (same caveat HugSizingTests
            // spells out). What matters here is that the static rows are what Content measures.
            var rowHeight = ((RectTransform)content.GetChild(0)).rect.height;
            Assume.That(rowHeight, Is.GreaterThan(0f), "guard: the rows measured");

            Assert.AreEqual(2f * rowHeight, LayoutUtility.GetPreferredSize(content, 1), 0.01f,
                "Content's preferred height is the two static rows, so the list scrolls them");
        }

        [Test]
        public void A_list_with_static_children_needs_no_itemTemplate()
        {
            // itemTemplate is only required before BindItems; a purely static list never gets there.
            var list = Open("<ScrollList id='sl' width='150' height='200'><Frame id='a' height='30'/></ScrollList>")
                .Get<ScrollList>("sl");

            Assert.AreEqual(1, list.SlotCount);
        }

        [Test]
        public void A_static_child_may_be_a_Template_invocation_with_reachable_scoped_ids()
        {
            var screen = Open(
                "<ScrollList id='sl' width='150' height='200'><BuildSlot id='slot1'/></ScrollList>",
                "<Template name='BuildSlot'><Frame height='40'><Btn id='btn'/></Frame></Template>");

            Assert.IsNotNull(screen.Get<Btn>("slot1/btn"),
                "a template instance root keeps its own id scope wherever it is hosted");
            Assert.AreEqual(1, screen.Get<ScrollList>("sl").SlotCount);
        }

        [Test]
        public void BindItems_without_an_itemTemplate_leaves_the_static_children_alone()
        {
            // Rebuild throws InvalidOperationException BEFORE ClearSlots, so a list bound by mistake
            // still shows its placeholders rather than going blank. (R3 routes the throw to the
            // unhandled-exception handler, so it never reaches this caller.)
            var screen = Open("<ScrollList id='sl' width='150' height='200'>"
                            + "<Frame id='a' height='30'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("itemTemplate must be set before BindItems"));
            Bind(list, 3);

            Assert.AreEqual(1, list.SlotCount, "the placeholder survived the failed bind");
            Assert.IsFalse(screen.Get<Frame>("a").GameObject == null);
        }

        // ───── BindItems takes over ─────

        [Test]
        public void The_first_BindItems_destroys_the_static_children()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'>"
                            + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>",
                              "<Template name='Row'><Frame height='30'/></Template>");
            var list = screen.Get<ScrollList>("sl");
            var staticA = screen.Get<Frame>("a");

            Bind(list, 3);

            Assert.IsTrue(staticA.GameObject == null, "the placeholder card is gone, not merely hidden");
            Assert.AreEqual(3, list.SlotCount);
            Assert.AreEqual(3, ContentOf(list).childCount);
        }

        [Test]
        public void A_ReSolve_after_BindItems_does_not_readopt_the_dead_static_children()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'>"
                            + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>",
                              "<Template name='Row'><Frame height='30'/></Template>");
            var list = screen.Get<ScrollList>("sl");
            Bind(list, 3);

            Assert.DoesNotThrow(() => screen.ReSolve());

            Assert.AreEqual(3, list.SlotCount,
                "re-collecting the static children would resurrect references to destroyed GameObjects");
        }

        [Test]
        public void Repeated_ReSolve_before_BindItems_collects_the_static_children_once()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200'>"
                            + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");

            screen.ReSolve();
            screen.ReSolve();

            Assert.AreEqual(2, list.SlotCount, "the collection is a one-shot, not an append per apply pass");
        }

        [Test]
        public void Closing_a_bound_list_does_not_double_destroy()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'>"
                            + "<Frame id='a' height='30'/></ScrollList>",
                              "<Template name='Row'><Frame height='30'/></Template>");
            var list = screen.Get<ScrollList>("sl");
            Bind(list, 2);

            Assert.DoesNotThrow(() => screen.Close());
        }

        [Test]
        public void Closing_a_purely_static_list_disposes_the_static_children_safely()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200'>"
                            + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>");

            // ScrollList.Dispose clears the slots AND the base disposes the child GameObjects —
            // the static children are hit twice and must survive it.
            Assert.DoesNotThrow(() => screen.Close());
        }

        // ───── the grid + static children combination the requirement is actually about ─────

        [Test]
        public void Static_children_fill_a_grid_and_hug_reports_their_rows()
        {
            var list = Open(
                "<ScrollList id='sl' anchor='top-left' width='200' height='hug' sprite=''"
                + " columns='3' cellSize='40x40' spacing='0' padding='0'>"
                + "<Frame/><Frame/><Frame/><Frame/><Frame/><Frame/><Frame/></ScrollList>")
                .Get<ScrollList>("sl");

            Assert.AreEqual(7, list.SlotCount);
            Assert.AreEqual(120f, list.RectTransform.rect.height, 0.01f,
                "7 static cells over 3 columns = 3 rows x 40 — the same answer BindItems gives");
        }

        // ───── Content is configured BEFORE the children are instantiated into it ─────
        //
        // The apply pass is DFS post-order, so a child resolves its geometry against whatever group
        // Content carries at instantiation time. Without a pre-configure step a grid list's children
        // would measure against the boot VerticalLayoutGroup on the first pass and against the
        // GridLayoutGroup on every ReSolve after — first frame and re-solve would disagree.

        [Test]
        public void A_grid_childs_geometry_is_the_same_on_the_first_pass_as_after_a_ReSolve()
        {
            var screen = Open("<ScrollList id='sl' anchor='top-left' width='200' height='200'"
                            + " columns='3' cellSize='40x40' spacing='0' padding='0'>"
                            + "<Frame id='a'/><Frame id='b'/></ScrollList>");
            var a = screen.Get<Frame>("a").RectTransform;
            var first = a.sizeDelta;
            var firstAnchorMin = a.anchorMin;

            screen.ReSolve();
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(first, a.sizeDelta, "a cell must not resize itself on the first ReSolve");
            Assert.AreEqual(firstAnchorMin, a.anchorMin);
        }

        [Test]
        public void Text_with_scale_in_a_grid_list_gets_no_scale_host_wrapper()
        {
            // Same exclusion <Grid> gets for free (STW-D2: cellSize is the declared box). <Grid>
            // qualifies because GridLayoutGroup is not a HorizontalOrVerticalLayoutGroup; a grid
            // ScrollList only qualifies if Content already carries the grid when the child is built.
            var screen = Open("<ScrollList id='sl' anchor='top-left' width='200' height='200'"
                            + " columns='3' cellSize='40x40'>"
                            + "<Text id='t' scale='0.5'>hello</Text></ScrollList>");
            var text = (Control)screen.Get("t");

            Assert.AreEqual(text.RectTransform, text.LayoutHost,
                "a grid cell is sized by cellSize, so there is no main-axis measurement to bridge");
        }

        [Test]
        public void Text_with_scale_in_a_single_column_list_still_gets_the_wrapper()
        {
            var screen = Open("<ScrollList id='sl' anchor='top-left' width='200' height='200'>"
                            + "<Text id='t' width='stretch' wrap='true' scale='0.5'>hello</Text></ScrollList>");
            var text = (Control)screen.Get("t");

            Assert.AreNotEqual(text.RectTransform, text.LayoutHost, "wrapper expected");
            Assert.AreEqual("t [scale-host]", text.LayoutHost.gameObject.name);
        }

        // ───── the list is a layout group, so its children obey layout-group child rules ─────

        [Test]
        public void An_anchor_on_a_list_child_warns_like_any_layout_group_child()
        {
            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("anchor.*ignored.*layout group"));

            Open("<ScrollList id='sl' width='150' height='200'><Frame id='a' anchor='top-left'/></ScrollList>");
        }

        [Test]
        public void A_margin_on_a_list_child_warns_like_any_layout_group_child()
        {
            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("margin.*ignored.*layout group"));

            Open("<ScrollList id='sl' width='150' height='200'><Frame id='a' margin='4'/></ScrollList>");
        }

        [Test]
        public void An_out_of_flow_list_child_keeps_its_free_positioning_attributes()
        {
            // flow="false" leaves the layout flow, so anchor/margin mean what they always meant.
            Assert.DoesNotThrow(() => Open(
                "<ScrollList id='sl' width='150' height='200'>"
                + "<Frame id='a' flow='false' anchor='top-left' margin='4'/></ScrollList>"));
        }

        [Test]
        public void Flow_on_a_list_child_is_not_reported_as_inert()
        {
            // PUI-FLOW-OUTSIDE-GROUP fires for children of NON-layout-group parents. Now that the
            // list is one, a flow= child must stop being flagged.
            Assert.DoesNotThrow(() => Open(
                "<ScrollList id='sl' width='150' height='200'><Frame id='a' flow='true'/></ScrollList>"));
        }
    }
}
