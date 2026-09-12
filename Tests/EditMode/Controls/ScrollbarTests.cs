using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using DropdownControl = PromptUGUI.Controls.Dropdown;
using PuiScreen = PromptUGUI.Application.Screen;
using PuiScrollbar = PromptUGUI.Controls.Scrollbar;
using UnityImage = UnityEngine.UI.Image;
using UnityScrollbar = UnityEngine.UI.Scrollbar;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Scrollbar&gt;</c> as a part element (spec 2026-09-12-scrollbar-part-element-design).
    /// The default bar a host builds when no <c>&lt;Scrollbar&gt;</c> is authored must be pixel-for-pixel
    /// what the two hosts drew before the tag existed — asserted on rects in the parent's space, not on
    /// the sizeDelta bookkeeping (§5.1 rewrites the numbers, not the picture).
    /// </summary>
    public class ScrollbarTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuiScreen OpenScreen(string body, string extra = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + extra
                    + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return screen;
        }

        private static ScrollList OpenList(string attrs, string children = "")
            => OpenScreen($"<ScrollList id='sl' width='150' height='200' {attrs}>{children}</ScrollList>")
                .Get<ScrollList>("sl");

        private static DropdownControl OpenDropdown(string attrs, string children = "")
            => OpenScreen($"<Dropdown id='dd' width='200' height='40' {attrs}>{children}</Dropdown>")
                .Get<DropdownControl>("dd");

        private static RectTransform BarNode(ScrollList list)
            => list.GameObject.transform.Find("Scrollbar") as RectTransform;

        private static RectTransform BarNode(DropdownControl dd)
            => dd.GameObject.transform.Find("Template/Scrollbar") as RectTransform;

        private static UnityScrollbar BarOf(RectTransform node) => node.GetComponent<UnityScrollbar>();
        private static RectTransform SlidingOf(RectTransform node) => (RectTransform)node.Find("Sliding Area");
        private static RectTransform HandleOf(RectTransform node) => (RectTransform)node.Find("Sliding Area/Handle");

        /// <summary>The rect of <paramref name="rt"/> expressed in its parent's local space.</summary>
        private static Rect InParent(RectTransform rt)
        {
            var parent = (RectTransform)rt.parent;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var min = parent.InverseTransformPoint(c[0]);
            var max = parent.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>The rect of <paramref name="rt"/> in the space of an ancestor.</summary>
        private static Rect In(RectTransform rt, RectTransform space)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var min = space.InverseTransformPoint(c[0]);
            var max = space.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void AssertRect(Rect expected, Rect actual, string what)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, 0.01f, what + " xMin");
            Assert.AreEqual(expected.xMax, actual.xMax, 0.01f, what + " xMax");
            Assert.AreEqual(expected.yMin, actual.yMin, 0.01f, what + " yMin");
            Assert.AreEqual(expected.yMax, actual.yMax, 0.01f, what + " yMax");
        }

        /// <summary>
        /// Spec §5.1 for one bar: thickness <paramref name="t"/>, padding (<paramref name="e"/> along,
        /// <paramref name="s"/> across), the uGUI handle at <paramref name="value"/> / <paramref name="size"/>
        /// (a ScrollRect writes both from its content — an empty list has size 1). Returns
        /// (bar, sliding, handle) — bar in the host's space, the other two in the bar's.
        /// </summary>
        private static (Rect bar, Rect sliding, Rect handle) Expected(
            Rect host, Rect barLocal, bool vertical, float t, float e, float s, float value, float size)
        {
            var h = Mathf.Max(1f, t - 2f * s);
            s = (t - h) / 2f;   // the inset as applied once the handle is clamped to 1
            var aMin = value * (1f - size);
            var aMax = aMin + size;
            var bar = vertical
                ? Rect.MinMaxRect(host.xMax - t, host.yMin, host.xMax, host.yMax)
                : Rect.MinMaxRect(host.xMin, host.yMin, host.xMax, host.yMin + t);
            Rect sliding, handle;
            if (vertical)
            {
                sliding = Rect.MinMaxRect(barLocal.xMin + s, barLocal.yMin + e + h / 2f,
                                          barLocal.xMax - s, barLocal.yMax - e - h / 2f);
                handle = Rect.MinMaxRect(barLocal.xMin + s, sliding.yMin + aMin * sliding.height - h / 2f,
                                         barLocal.xMax - s, sliding.yMin + aMax * sliding.height + h / 2f);
            }
            else
            {
                sliding = Rect.MinMaxRect(barLocal.xMin + e + h / 2f, barLocal.yMin + s,
                                          barLocal.xMax - e - h / 2f, barLocal.yMax - s);
                handle = Rect.MinMaxRect(sliding.xMin + aMin * sliding.width - h / 2f, barLocal.yMin + s,
                                         sliding.xMin + aMax * sliding.width + h / 2f, barLocal.yMax - s);
            }
            return (bar, sliding, handle);
        }

        private static void AssertGeometry(RectTransform node, bool vertical, float t, float e, float s)
        {
            var host = ((RectTransform)node.parent).rect;
            var ub = BarOf(node);
            var (bar, sliding, handle) = Expected(host, node.rect, vertical, t, e, s, ub.value, ub.size);
            AssertRect(bar, InParent(node), "bar");
            AssertRect(sliding, In(SlidingOf(node), node), "sliding area");
            AssertRect(handle, In(HandleOf(node), node), "handle");
        }

        // ───── the default bar is the stock bar ─────

        [Test]
        public void Default_vertical_bar_matches_the_stock_geometry()
        {
            var list = OpenList("");
            var node = BarNode(list);
            Assert.IsNotNull(node, "the host builds a default bar named 'Scrollbar' when none is authored");
            Assert.AreSame(list.GameObject.transform, node.parent, "a direct child of the ScrollRect host");

            // The M5.1 numbers, verbatim.
            Assert.AreEqual(new Vector2(1f, 0f), node.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), node.anchorMax);
            Assert.AreEqual(new Vector2(20f, 0f), node.sizeDelta);
            AssertGeometry(node, vertical: true, t: 20f, e: 0f, s: 0f);

            var bar = BarOf(node);
            Assert.AreEqual(UnityScrollbar.Direction.BottomToTop, bar.direction);
            Assert.AreSame(HandleOf(node), bar.handleRect);
            Assert.AreSame(HandleOf(node).GetComponent<UnityImage>(), bar.targetGraphic);

            var scroll = list.GameObject.GetComponent<ScrollRect>();
            Assert.AreSame(bar, scroll.verticalScrollbar);
            Assert.IsNull(scroll.horizontalScrollbar);
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport, scroll.verticalScrollbarVisibility);
            Assert.AreEqual(-3f, scroll.verticalScrollbarSpacing, 0.001f);

            Assert.AreEqual("pugui_9slice_inset", node.GetComponent<UnityImage>().sprite.name);
            Assert.AreEqual("pugui_9slice_round", HandleOf(node).GetComponent<UnityImage>().sprite.name);
            Assert.AreEqual(Color.white, node.GetComponent<UnityImage>().color);
            Assert.AreEqual(Color.white, HandleOf(node).GetComponent<UnityImage>().color);
        }

        [Test]
        public void Default_horizontal_bar_matches_the_stock_geometry()
        {
            var list = OpenList("direction='horizontal'");
            var node = BarNode(list);
            Assert.IsNotNull(node);

            Assert.AreEqual(new Vector2(0f, 0f), node.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0f), node.anchorMax);
            Assert.AreEqual(new Vector2(0f, 20f), node.sizeDelta);
            AssertGeometry(node, vertical: false, t: 20f, e: 0f, s: 0f);

            var bar = BarOf(node);
            Assert.AreEqual(UnityScrollbar.Direction.LeftToRight, bar.direction);

            var scroll = list.GameObject.GetComponent<ScrollRect>();
            Assert.AreSame(bar, scroll.horizontalScrollbar);
            Assert.IsNull(scroll.verticalScrollbar);
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport, scroll.horizontalScrollbarVisibility);
            Assert.AreEqual(-3f, scroll.horizontalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void Dropdown_default_bar_matches_the_stock_geometry()
        {
            var dd = OpenDropdown("");
            var node = BarNode(dd);
            Assert.IsNotNull(node, "the popup Template hosts the bar");

            Assert.AreEqual(new Vector2(20f, 0f), node.sizeDelta);
            AssertGeometry(node, vertical: true, t: 20f, e: 0f, s: 0f);

            var bar = BarOf(node);
            Assert.AreEqual(UnityScrollbar.Direction.BottomToTop, bar.direction);
            Assert.AreEqual(0f, bar.value, 0.001f);
            Assert.AreEqual(0.2f, bar.size, 0.001f);

            var scroll = dd.GameObject.transform.Find("Template").GetComponent<ScrollRect>();
            Assert.AreSame(bar, scroll.verticalScrollbar);
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport, scroll.verticalScrollbarVisibility);
            Assert.AreEqual(-3f, scroll.verticalScrollbarSpacing, 0.001f);
            // The popup viewport's static reservation follows the bar: -(thickness + spacing).
            var viewport = (RectTransform)dd.GameObject.transform.Find("Template/Viewport");
            Assert.AreEqual(-17f, viewport.sizeDelta.x, 0.001f);

            Assert.AreEqual("pugui_9slice_inset", node.GetComponent<UnityImage>().sprite.name);
            Assert.AreEqual("pugui_9slice_round", HandleOf(node).GetComponent<UnityImage>().sprite.name);
        }

        // ───── thickness / padding / spacing / overlay ─────

        [Test]
        public void Thickness_resizes_all_three_layers()
        {
            var node = BarNode(OpenList("", "<Scrollbar thickness='6'/>"));
            Assert.AreEqual(new Vector2(6f, 0f), node.sizeDelta);
            AssertGeometry(node, vertical: true, t: 6f, e: 0f, s: 0f);
        }

        [Test]
        public void Thickness_is_the_height_of_a_horizontal_bar()
        {
            var node = BarNode(OpenList("direction='horizontal'", "<Scrollbar thickness='6'/>"));
            Assert.AreEqual(new Vector2(0f, 6f), node.sizeDelta);
            AssertGeometry(node, vertical: false, t: 6f, e: 0f, s: 0f);
        }

        [Test]
        public void Padding_insets_the_handle_along_and_across()
        {
            var node = BarNode(OpenList("", "<Scrollbar thickness='8' padding='3,1.5'/>"));
            AssertGeometry(node, vertical: true, t: 8f, e: 3f, s: 1.5f);
            Assert.AreEqual(5f, In(HandleOf(node), node).width, 0.01f, "handle = thickness - 2*across");
        }

        [Test]
        public void Padding_transposes_on_a_horizontal_bar()
        {
            var node = BarNode(OpenList("direction='horizontal'", "<Scrollbar thickness='8' padding='3,1.5'/>"));
            AssertGeometry(node, vertical: false, t: 8f, e: 3f, s: 1.5f);
            Assert.AreEqual(5f, In(HandleOf(node), node).height, 0.01f);
        }

        [Test]
        public void A_single_padding_value_insets_all_four_sides()
        {
            var node = BarNode(OpenList("", "<Scrollbar thickness='8' padding='2'/>"));
            AssertGeometry(node, vertical: true, t: 8f, e: 2f, s: 2f);
        }

        [Test]
        public void Padding_zero_is_the_default()
        {
            var a = BarNode(OpenList("", "<Scrollbar padding='0'/>"));
            var sliding = In(SlidingOf(a), a);
            var handle = In(HandleOf(a), a);
            UI.ResetForTests();
            var b = BarNode(OpenList(""));
            AssertRect(sliding, In(SlidingOf(b), b), "sliding area");
            AssertRect(handle, In(HandleOf(b), b), "handle");
        }

        [Test]
        public void Padding_that_swallows_the_thickness_clamps_the_handle_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("padding"));
            var node = BarNode(OpenList("", "<Scrollbar thickness='6' padding='0,3'/>"));
            Assert.AreEqual(1f, In(HandleOf(node), node).width, 0.01f, "clamped to 1 rather than vanishing");
        }

        [Test]
        public void Spacing_lands_on_the_scrollrect()
        {
            var list = OpenList("", "<Scrollbar spacing='6'/>");
            Assert.AreEqual(6f, list.GameObject.GetComponent<ScrollRect>().verticalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void Spacing_lands_on_the_horizontal_axis_of_a_horizontal_bar()
        {
            var list = OpenList("direction='horizontal'", "<Scrollbar spacing='6'/>");
            Assert.AreEqual(6f, list.GameObject.GetComponent<ScrollRect>().horizontalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void Default_spacing_is_the_stock_overlap_capped_at_the_thickness()
        {
            Assert.AreEqual(-2f, OpenList("", "<Scrollbar thickness='2'/>")
                .GameObject.GetComponent<ScrollRect>().verticalScrollbarSpacing, 0.001f,
                "a bar thinner than the 3-unit overlap never grows the viewport");
            UI.ResetForTests();
            Assert.AreEqual(-3f, OpenList("", "<Scrollbar thickness='6'/>")
                .GameObject.GetComponent<ScrollRect>().verticalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void Overlay_draws_the_bar_over_the_content()
        {
            var scroll = OpenList("", "<Scrollbar overlay='true'/>").GameObject.GetComponent<ScrollRect>();
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHide, scroll.verticalScrollbarVisibility);
        }

        [Test]
        public void Overlay_false_is_the_viewport_expanding_default()
        {
            var scroll = OpenList("", "<Scrollbar overlay='false'/>").GameObject.GetComponent<ScrollRect>();
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport, scroll.verticalScrollbarVisibility);
        }

        [Test]
        public void Overlay_applies_to_a_horizontal_bar_too()
        {
            var scroll = OpenList("direction='horizontal'", "<Scrollbar overlay='true'/>")
                .GameObject.GetComponent<ScrollRect>();
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHide, scroll.horizontalScrollbarVisibility);
        }

        [Test]
        public void Dropdown_overlay_frees_the_popup_viewport()
        {
            var dd = OpenDropdown("", "<Scrollbar thickness='6' overlay='true'/>");
            var scroll = dd.GameObject.transform.Find("Template").GetComponent<ScrollRect>();
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHide, scroll.verticalScrollbarVisibility);
            var viewport = (RectTransform)dd.GameObject.transform.Find("Template/Viewport");
            Assert.AreEqual(0f, viewport.sizeDelta.x, 0.001f, "overlay: nothing reserved");
        }

        [Test]
        public void Dropdown_viewport_reservation_follows_thickness_and_spacing()
        {
            var dd = OpenDropdown("", "<Scrollbar thickness='6' spacing='2'/>");
            var viewport = (RectTransform)dd.GameObject.transform.Find("Template/Viewport");
            Assert.AreEqual(-8f, viewport.sizeDelta.x, 0.001f);
        }

        // ───── skin ─────

        [Test]
        public void Skin_attributes_land_on_track_and_handle()
        {
            var node = BarNode(OpenList("", "<Scrollbar sprite='' color='#ff0000' handle='' handleColor='#00ff00'/>"));
            Assert.IsNull(node.GetComponent<UnityImage>().sprite, "sprite='' clears the track sprite");
            Assert.AreEqual(Color.red, node.GetComponent<UnityImage>().color);
            Assert.IsNull(HandleOf(node).GetComponent<UnityImage>().sprite);
            Assert.AreEqual(Color.green, HandleOf(node).GetComponent<UnityImage>().color);
        }

        [Test]
        public void Dropdown_skin_attributes_land_on_track_and_handle()
        {
            var node = BarNode(OpenDropdown("", "<Scrollbar color='#111111' handleColor='#eeeeee'/>"));
            Assert.AreEqual(new Color32(0x11, 0x11, 0x11, 0xff), (Color32)node.GetComponent<UnityImage>().color);
            Assert.AreEqual(new Color32(0xee, 0xee, 0xee, 0xff), (Color32)HandleOf(node).GetComponent<UnityImage>().color);
        }

        // ───── adoption ─────

        [Test]
        public void An_authored_scrollbar_replaces_the_default_and_stays_out_of_content()
        {
            var screen = OpenScreen(
                "<ScrollList id='sl' width='150' height='200'><Scrollbar id='bar' thickness='6'/><Frame id='card' height='30'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");
            var root = list.GameObject.transform;

            var bars = 0;
            for (var i = 0; i < root.childCount; i++)
                if (root.GetChild(i).GetComponent<UnityScrollbar>() != null) bars++;
            Assert.AreEqual(1, bars, "the authored bar is the bar — no default is built beside it");

            var bar = screen.Get<PuiScrollbar>("sl/bar");
            Assert.IsNotNull(bar, "reachable by id path through the host");
            Assert.AreSame(root, bar.GameObject.transform.parent, "hosted on the ScrollRect root, not in Content");
            Assert.AreSame(bar.GameObject.GetComponent<UnityScrollbar>(),
                list.GameObject.GetComponent<ScrollRect>().verticalScrollbar);
            Assert.AreEqual(6f, ((RectTransform)bar.GameObject.transform).sizeDelta.x, 0.001f);

            Assert.AreEqual(1, list.SlotCount, "the bar is chrome, not a slot");
            var content = root.Find("Viewport/Content");
            Assert.AreEqual(1, content.childCount);
            Assert.AreEqual("card", content.GetChild(0).name);
        }

        [Test]
        public void A_template_whose_root_is_a_scrollbar_is_adopted()
        {
            var list = OpenScreen(
                "<ScrollList id='sl' width='150' height='200'><Bar/></ScrollList>",
                "<Template name='Bar'><Param name='t' default='6'/><Scrollbar thickness='{{t}}'/></Template>")
                .Get<ScrollList>("sl");
            var node = BarNode(list);
            Assert.AreEqual(6f, node.sizeDelta.x, 0.001f);
            Assert.AreSame(BarOf(node), list.GameObject.GetComponent<ScrollRect>().verticalScrollbar);
        }

        [Test]
        public void A_second_scrollbar_is_not_wired_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLLBAR-DUPLICATE"));
            var screen = OpenScreen(
                "<ScrollList id='sl' width='150' height='200'><Scrollbar id='a' thickness='6'/><Scrollbar id='b' thickness='9'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");
            var wired = list.GameObject.GetComponent<ScrollRect>().verticalScrollbar;
            Assert.AreSame(screen.Get<PuiScrollbar>("sl/a").GameObject.GetComponent<UnityScrollbar>(), wired,
                "document order: the first one is the bar");
            Assert.IsFalse(screen.Get<PuiScrollbar>("sl/b").GameObject.activeSelf, "the loser is parked inactive");
        }

        [Test]
        public void Dropdown_adopts_an_authored_scrollbar_into_its_popup()
        {
            var screen = OpenScreen("<Dropdown id='dd' width='200' height='40'><Scrollbar id='bar' thickness='6'/></Dropdown>");
            var dd = screen.Get<DropdownControl>("dd");
            var bar = screen.Get<PuiScrollbar>("dd/bar");
            var template = dd.GameObject.transform.Find("Template");
            Assert.AreSame(template, bar.GameObject.transform.parent);
            Assert.AreSame(bar.GameObject.GetComponent<UnityScrollbar>(),
                template.GetComponent<ScrollRect>().verticalScrollbar);
            Assert.AreEqual(-3f, template.GetComponent<ScrollRect>().verticalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void Frame_stays_topmost_above_an_authored_scrollbar()
        {
            var list = OpenList("frame='PromptUGUI/Defaults/pugui#pugui_9slice_round'", "<Scrollbar thickness='6'/>");
            var root = list.GameObject.transform;
            Assert.AreEqual(root.childCount - 1, root.Find("Frame").GetSiblingIndex());
        }

        // ───── orientation follows the host; the node is the same node ─────

        [Test]
        public void A_direction_variant_reorients_the_same_node()
        {
            var screen = OpenScreen(
                "<ScrollList id='sl' width='150' height='200' direction='vertical' direction.portrait='horizontal'>"
                + "<Scrollbar id='bar' thickness='6'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");
            var bar = screen.Get<PuiScrollbar>("sl/bar");
            var node = (RectTransform)bar.GameObject.transform;
            var scroll = list.GameObject.GetComponent<ScrollRect>();
            AssertGeometry(node, vertical: true, t: 6f, e: 0f, s: 0f);

            UI.Variants.Set("portrait", true);
            Canvas.ForceUpdateCanvases();
            Assert.AreSame(bar, screen.Get<PuiScrollbar>("sl/bar"), "same control instance");
            Assert.AreSame(node, bar.GameObject.transform, "same GameObject");
            AssertGeometry(node, vertical: false, t: 6f, e: 0f, s: 0f);
            Assert.AreEqual(UnityScrollbar.Direction.LeftToRight, BarOf(node).direction);
            Assert.AreSame(BarOf(node), scroll.horizontalScrollbar);
            Assert.IsNull(scroll.verticalScrollbar);

            UI.Variants.Set("portrait", false);
            Canvas.ForceUpdateCanvases();
            AssertGeometry(node, vertical: true, t: 6f, e: 0f, s: 0f);
            Assert.AreEqual(UnityScrollbar.Direction.BottomToTop, BarOf(node).direction);
            Assert.AreSame(BarOf(node), scroll.verticalScrollbar);
            Assert.IsNull(scroll.horizontalScrollbar);
        }

        [Test]
        public void A_thickness_variant_reaches_the_live_bar_and_reverts()
        {
            var list = OpenList("", "<Scrollbar thickness='20' thickness.portrait='4'/>");
            var node = BarNode(list);
            Assert.AreEqual(20f, node.sizeDelta.x, 0.001f);

            UI.Variants.Set("portrait", true);
            Assert.AreEqual(4f, node.sizeDelta.x, 0.001f);
            AssertGeometry(node, vertical: true, t: 4f, e: 0f, s: 0f);

            UI.Variants.Set("portrait", false);
            Assert.AreEqual(20f, node.sizeDelta.x, 0.001f, "the base value reverts when the variant clears");
        }


        // ───── procedural surfaces (M1) ─────

        private static ProceduralPanel SurfaceUnder(Transform layer)
        {
            var node = layer.Find(ProceduralSurface.NodeName);
            return node == null ? null : node.GetComponent<ProceduralPanel>();
        }

        [Test]
        public void Radius_moves_the_track_to_a_procedural_surface()
        {
            var node = BarNode(OpenList("", "<Scrollbar radius='pill' color='#ff0000'/>"));
            var panel = SurfaceUnder(node);
            Assert.IsNotNull(panel, "the track is the bar's primary surface");
            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.IsTrue(panel.CurrentParams.Pill);
            Assert.AreEqual(Color.red, panel.CurrentParams.FillTop, "color= is the SDF fill");

            var track = node.GetComponent<UnityImage>();
            Assert.IsNull(track.sprite, "the default inset sprite stands down");
            Assert.AreEqual(0f, track.color.a, 0.001f, "…and so does the Image's alpha");
            Assert.IsTrue(track.raycastTarget, "…but it still catches the pointer");
        }

        [Test]
        public void Handle_shape_attributes_give_the_handle_an_inner_surface()
        {
            var node = BarNode(OpenList("",
                "<Scrollbar handleRadius='pill' handleColor='#00ff00' handleBorderWidth='1' handleBorderColor='#0000ff' handleGlow='3' handleGlowColor='#00ffff'/>"));
            var handle = HandleOf(node);
            var panel = SurfaceUnder(handle);
            Assert.IsNotNull(panel, "handle* attributes shape the handle, not the track");
            Assert.IsNull(SurfaceUnder(node), "…and say nothing about the track");
            Assert.IsTrue(panel.CurrentParams.Pill);
            Assert.AreEqual(Color.green, panel.CurrentParams.FillTop);
            Assert.AreEqual(1f, panel.CurrentParams.BorderWidth);
            Assert.AreEqual(Color.blue, panel.CurrentParams.BorderColor);
            Assert.AreEqual(3f, panel.CurrentParams.GlowSize);
            Assert.AreEqual(Color.cyan, panel.CurrentParams.GlowColor);

            var image = handle.GetComponent<UnityImage>();
            Assert.IsNull(image.sprite);
            Assert.AreEqual(0f, image.color.a, 0.001f);
            Assert.AreSame(panel, BarOf(node).targetGraphic,
                "the uGUI Scrollbar's targetGraphic follows the visible layer, so ColorTint keeps working");
        }

        [Test]
        public void Handle_glow_color_follows_the_handle_color_when_unset()
        {
            var node = BarNode(OpenList("", "<Scrollbar handleRadius='pill' handleColor='#00ff00' handleGlow='3'/>"));
            var panel = SurfaceUnder(HandleOf(node));
            Assert.AreEqual(Color.green, panel.CurrentParams.GlowColor);
        }

        [Test]
        public void Handle_glow_grows_the_drawn_quad_and_the_bar_sits_outside_the_viewport_mask()
        {
            var list = OpenList("", "<Scrollbar thickness='6' handleRadius='pill' handleGlow='3'/>");
            var node = BarNode(list);
            var handle = HandleOf(node);
            var panel = SurfaceUnder(handle);

            var vh = new VertexHelper();
            panel.BuildMeshForTests(vh);
            var v = default(UIVertex);
            vh.PopulateUIVertex(ref v, 2);
            var half = handle.rect.width / 2f;
            Assert.AreEqual(half + 3f, v.uv0.x, 0.01f, "the quad is inflated by the glow, the layout is not");
            Assert.AreEqual(6f, In(handle, node).width, 0.01f);

            var viewport = list.GameObject.transform.Find("Viewport");
            Assert.IsFalse(node.IsChildOf(viewport), "the bar is the Viewport's sibling — nothing clips the glow");
        }

        [Test]
        public void A_base_less_handle_variant_toggles_the_handle_surface_wholesale()
        {
            var node = BarNode(OpenList("", "<Scrollbar handleRadius.portrait='pill'/>"));
            var before = SurfaceUnder(HandleOf(node));
            Assert.IsTrue(before == null || !before.gameObject.activeSelf, "no base: the Image draws");

            UI.Variants.Set("portrait", true);
            var panel = SurfaceUnder(HandleOf(node));
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.gameObject.activeSelf, "the variant turns the handle surface on");

            UI.Variants.Set("portrait", false);
            Assert.IsFalse(panel.gameObject.activeSelf, "…and off again: the Image is back");
            Assert.AreEqual(1f, HandleOf(node).GetComponent<UnityImage>().color.a, 0.001f);
        }

        [Test]
        public void A_style_pack_and_a_theme_reskin_the_bar()
        {
            // Themes register through the async load path (the sync LoadDocument bypasses it), so
            // this one goes through a fake resolver — the ThemeStyleSwitchTests pattern.
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                    + "<Style name='bar' thickness='6' radius='pill' color='#ff0000'/>"
                    + "<Theme name='hud'><Color name='ink' value='#000'/></Theme>"
                    + "<Theme name='px'><Color name='ink' value='#111'/><Style name='bar' thickness='8' color='#00ff00' radius=''/></Theme>"
                    + "<Screen name='S'><ScrollList id='sl' width='150' height='200'><Scrollbar class='bar'/></ScrollList></Screen>"
                    + "</PromptUGUI>";
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "main" ? xml : null);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Theme.Set("hud");
            var list = UI.Open("S").Get<ScrollList>("sl");
            Canvas.ForceUpdateCanvases();
            var node = BarNode(list);
            Assert.AreEqual(6f, node.sizeDelta.x, 0.001f);
            Assert.IsTrue(SurfaceUnder(node).CurrentParams.Pill);

            UI.Theme.Set("px");
            Assert.AreEqual(8f, node.sizeDelta.x, 0.001f, "the theme pack re-derives through class=");
            Assert.IsFalse(SurfaceUnder(node).CurrentParams.Pill, "radius='' resets the shape");
            Assert.AreEqual(Color.green, SurfaceUnder(node).CurrentParams.FillTop);
        }

        [Test]
        public void Dropdown_popup_clone_carries_the_procedural_state()
        {
            var dd = OpenDropdown("", "<Scrollbar thickness='6' radius='pill' color='#ff0000' handleRadius='pill' handleGlow='3'/>");
            var tmp = dd.GameObject.GetComponent<PuiDropdown>();
            Assert.IsNotNull(tmp, "the popup goes through PuiDropdown so the clone can be fixed up");

            var clone = tmp.CloneListForTests();
            try
            {
                var bar = (RectTransform)clone.transform.Find("Scrollbar");
                Assert.IsNotNull(bar);
                var track = SurfaceUnder(bar);
                var handle = SurfaceUnder(HandleOf(bar));
                Assert.IsNotNull(track);
                Assert.IsNotNull(handle);
                Assert.IsTrue(track.CurrentParams.Pill, "radius survived the Instantiate");
                Assert.AreEqual(Color.red, track.CurrentParams.FillTop, "…and so did the fill");
                Assert.IsTrue(handle.CurrentParams.Pill);
                Assert.AreEqual(3f, handle.CurrentParams.GlowSize);
                Assert.AreSame(bar.GetComponent<UnityScrollbar>(),
                    clone.GetComponent<ScrollRect>().verticalScrollbar, "Instantiate remaps the ScrollRect wiring");
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }
        [Test]
        public void Closing_a_screen_with_an_authored_bar_does_not_throw()
        {
            var screen = OpenScreen("<ScrollList id='sl' width='150' height='200'><Scrollbar id='bar'/></ScrollList>");
            Assert.DoesNotThrow(() => screen.Close());
        }
    }
}
