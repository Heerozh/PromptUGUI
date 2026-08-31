using System.Reflection;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>width="hug"</c> / <c>height="hug"</c> through the real pipeline (UI.Open → ApplyCommon →
    /// ClampFitter / HugElement → LayoutRebuilder). Spec
    /// 2026-08-31-hug-reveal-flip-checked-design §1.4. The outer Frame carries a NUMERIC size so the
    /// geometry never depends on the EditMode canvas size.
    /// </summary>
    public class HugSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static readonly FieldInfo LayoutQueueField = typeof(CanvasUpdateRegistry)
            .GetField("m_LayoutRebuildQueue", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int PendingLayoutRebuilds()
        {
            Assert.IsNotNull(LayoutQueueField,
                "CanvasUpdateRegistry.m_LayoutRebuildQueue is gone — this Unity version needs a new seam");
            var queue = LayoutQueueField.GetValue(CanvasUpdateRegistry.instance);
            return (int)queue.GetType().GetProperty("Count").GetValue(queue);
        }

        private static void Drain() => Canvas.ForceUpdateCanvases();

        private static string Doc(string body) =>
            "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>\n"
            + body + "\n</Screen></PromptUGUI>";

        private static string Box(string child, float w = 400f, float h = 600f) =>
            Doc($"<Frame id='box' anchor='top-left' width='{w}' height='{h}'>{child}</Frame>");

        private static PromptUGUI.Application.Screen Open(string xml)
        {
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Drain();
            return screen;
        }

        private static float Top(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMax.y + child.anchorMax.y * parent.rect.height;
        }

        private static float Bottom(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMin.y + child.anchorMin.y * parent.rect.height;
        }

        private static float Left(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMin.x + child.anchorMin.x * parent.rect.width;
        }

        // Three 44-high rows with spacing 4 → 140.
        private const string ThreeRows =
            "<Btn id='a' height='44'/><Btn id='b' height='44'/><Btn id='c' height='44'/>";

        // ───── free positioning ─────

        [Test]
        public void Free_positioning_vstack_hugs_its_rows()
        {
            var s = Open(Box($"<VStack id='v' anchor='top-right' width='150' height='hug' spacing='4'>{ThreeRows}</VStack>"));
            var v = s.Get<VStack>("v").RectTransform;

            Assert.AreEqual(140f, v.rect.height, 0.01f, "3 rows of 44 + 2 gaps of 4");
            Assert.AreEqual(600f, Top(v), 0.01f, "a top anchor grows downward from the top edge");
            Assert.AreEqual(150f, v.rect.width, 0.01f, "the other axis is untouched");
        }

        [Test]
        public void Free_positioning_hug_grows_upward_from_a_bottom_anchor()
        {
            var s = Open(Box($"<VStack id='v' anchor='bottom-left' width='150' height='hug' spacing='4'>{ThreeRows}</VStack>"));
            var v = s.Get<VStack>("v").RectTransform;

            Assert.AreEqual(140f, v.rect.height, 0.01f);
            Assert.AreEqual(0f, Bottom(v), 0.01f);
        }

        [Test]
        public void Free_positioning_hug_follows_content_shrinking()
        {
            var s = Open(Box($"<VStack id='v' anchor='top-left' width='150' height='hug' spacing='4'>{ThreeRows}</VStack>"));
            var v = s.Get<VStack>("v").RectTransform;
            Assume.That(v.rect.height, Is.EqualTo(140f).Within(0.01f));

            s.Get<Btn>("a").Hidden = true;
            Drain();

            Assert.AreEqual(92f, v.rect.height, 0.01f, "2 rows of 44 + 1 gap of 4");
        }

        [Test]
        public void Free_positioning_hug_margins_inset_inside_the_content_box()
        {
            var s = Open(Box(
                $"<VStack id='v' anchor='top-left' width='150' height='hug' margin='10,_,_,_' spacing='4'>{ThreeRows}</VStack>"));
            var v = s.Get<VStack>("v").RectTransform;

            Assert.AreEqual(130f, v.rect.height, 0.01f, "content box 140 minus the 10 top margin");
            Assert.AreEqual(590f, Top(v), 0.01f);
        }

        [Test]
        public void Free_positioning_hug_stretches_an_out_of_flow_background_layer()
        {
            // The killer use case: a hug-height stack whose 9-slice skin lives inside it.
            var s = Open(Box(
                "<VStack id='v' anchor='top-left' width='150' height='hug' spacing='4'>" +
                "<Image id='bg' anchor='stretch' flow='false'/>" + ThreeRows + "</VStack>"));

            Assert.AreEqual(140f, s.Get<VStack>("v").RectTransform.rect.height, 0.01f);
            Assert.AreEqual(140f, s.Get<PromptUGUI.Controls.Image>("bg").RectTransform.rect.height, 0.01f,
                "the out-of-flow background follows the hugged rect");
        }

        [TestCase("clamp(_, hug, 100)", 100f)]    // content 140 → cap
        [TestCase("clamp(200, hug, _)", 200f)]    // content 140 → floor
        [TestCase("clamp(100, hug, 200)", 140f)]  // in range → untouched
        public void Free_positioning_clamp_hug_applies_both_bounds(string height, float expected)
        {
            var s = Open(Box(
                $"<VStack id='v' anchor='top-left' width='150' height='{height}' spacing='4'>{ThreeRows}</VStack>"));

            Assert.AreEqual(expected, s.Get<VStack>("v").RectTransform.rect.height, 0.01f);
        }

        [Test]
        public void Free_positioning_hstack_hugs_its_width()
        {
            var s = Open(Box(
                "<HStack id='h' anchor='top-left' width='hug' height='44' spacing='4'>" +
                "<Btn id='a' width='30'/><Btn id='b' width='30'/></HStack>"));

            Assert.AreEqual(64f, s.Get<HStack>("h").RectTransform.rect.width, 0.01f, "30 + 4 + 30");
        }

        [Test]
        public void Free_positioning_grid_hugs_its_rows()
        {
            var s = Open(Box(
                "<Grid id='g' anchor='top-left' columns='3' cellSize='40x40' spacing='4' width='128' height='hug'>" +
                "<Frame/><Frame/><Frame/><Frame/><Frame/><Frame/><Frame/></Grid>"));

            // 7 cells over 3 columns = 3 rows: 3*40 + 2*4 = 128.
            Assert.AreEqual(128f, s.Get<PromptUGUI.Controls.Grid>("g").RectTransform.rect.height, 0.01f);
        }

        // A list whose rows come from BindItems, inside a numeric-size Frame.
        private static string ListDoc(string listAttrs) =>
            "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>\n" +
            "  <Template name='Row'><Frame height='32'/></Template>\n" +
            "  <Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>" +
            $"<ScrollList id='list' anchor='top-left' width='150' sprite='' spacing='0' itemTemplate='Row' {listAttrs}/>" +
            "</Frame></Screen>\n</PromptUGUI>";

        private static ScrollList OpenList(string listAttrs, int rows)
        {
            UI.LoadDocument("test", ListDoc(listAttrs));
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            var items = new string[rows];
            for (var i = 0; i < rows; i++) items[i] = "r" + i;
            list.BindItems(
                R3.Observable.Return<System.Collections.Generic.IReadOnlyList<string>>(items),
                (IControl slot, string _) => { });
            Drain();
            return list;
        }

        [Test]
        public void Free_positioning_scrolllist_hugs_its_content()
        {
            // height="hug" on a list means "as tall as the rows", not "as tall as the viewport".
            var list = OpenList("height='hug'", rows: 5);
            var content = list.RectTransform.Find("Viewport/Content") as RectTransform;
            Assert.IsNotNull(content);

            // Row height is whatever the ScrollList's own content group gives a row — it leaves
            // childControlHeight off, so a row keeps its own rect rather than taking its
            // LayoutElement. hug's contract is not "32 per row", it is "the viewport is exactly as
            // tall as the content node", whatever that content turns out to be.
            var rowHeight = ((RectTransform)content.GetChild(0)).rect.height;
            Assume.That(rowHeight, Is.GreaterThan(0f), "guard: the rows measured");

            Assert.AreEqual(5f * rowHeight, list.RectTransform.rect.height, 0.01f,
                "5 rows, spacing 0 → the list is exactly its content");
            Assert.AreEqual(LayoutUtility.GetPreferredSize(content, 1), list.RectTransform.rect.height, 0.01f,
                "hug means: my height IS my content's preferred height");
        }

        [Test]
        public void Free_positioning_scrolllist_clamp_hug_caps_and_scrolls()
        {
            var list = OpenList("height='clamp(_, hug, 100)'", rows: 5);

            Assert.AreEqual(100f, list.RectTransform.rect.height, 0.01f, "content 160 capped at 100");
            var content = list.RectTransform.Find("Viewport/Content") as RectTransform;
            Assert.IsNotNull(content);
            Assert.Greater(content.rect.height, 100f, "the content still overflows, so the list scrolls");
        }

        // ───── inside a layout group ─────

        [Test]
        public void In_a_stack_a_bare_hug_matches_the_omitted_axis()
        {
            // Both stacks hold the same rows; one spells hug out, the other omits the axis.
            var rows = "<Btn height='44'/><Btn height='44'/><Btn height='44'/>";
            var s = Open(Box(
                "<VStack id='outer' anchor='top-left' width='150' height='400' spacing='0'>" +
                $"<VStack id='spelled' height='hug' spacing='4'>{rows}</VStack>" +
                $"<VStack id='omitted' spacing='4'>{rows}</VStack>" +
                "</VStack>"));

            var spelled = s.Get<VStack>("spelled").RectTransform.rect.height;
            var omitted = s.Get<VStack>("omitted").RectTransform.rect.height;

            Assert.AreEqual(omitted, spelled, 0.01f, "bare hug in a stack is the -1 sentinel path, spelled out");
            Assert.AreEqual(140f, spelled, 0.01f);
        }

        [Test]
        public void In_a_stack_a_bare_hug_on_a_self_reporting_container_attaches_nothing()
        {
            var s = Open(Box(
                $"<VStack id='outer' anchor='top-left' width='150' height='400'><VStack id='v' height='hug' spacing='4'>{ThreeRows}</VStack></VStack>"));
            var v = s.Get<VStack>("v");

            var element = v.RectTransform.GetComponent<HugElement>();
            Assert.IsTrue(element == null || !element.enabled,
                "the group already reports its own preferred size — no extra component needed");
        }

        [Test]
        public void In_a_stack_a_clamped_hug_is_rigid()
        {
            var s = Open(Box(
                $"<VStack id='outer' anchor='top-left' width='150' height='400'><VStack id='v' height='clamp(_, hug, 100)' spacing='4'>{ThreeRows}</VStack></VStack>"));
            var v = s.Get<VStack>("v");

            Assert.AreEqual(100f, v.RectTransform.rect.height, 0.01f);
            var element = v.RectTransform.GetComponent<HugElement>();
            Assert.IsNotNull(element, "bounds have to be published to the parent group");
            Assert.IsTrue(element.enabled);
            var le = (ILayoutElement)element;
            Assert.AreEqual(100f, le.preferredHeight, 0.01f);
            Assert.AreEqual(100f, le.minHeight, 0.01f, "LGC-D17: a hug size is a computed constant, not a range");
            Assert.AreEqual(0f, le.flexibleHeight, 0.01f);
        }

        [Test]
        public void In_a_stack_siblings_move_when_hugged_content_shrinks()
        {
            var s = Open(Box(
                "<VStack id='outer' anchor='top-left' width='150' height='400' spacing='0'>" +
                $"<VStack id='v' height='hug' spacing='4'>{ThreeRows}</VStack>" +
                "<Btn id='below' height='20'/></VStack>"));
            var below = s.Get<Btn>("below").RectTransform;
            var before = below.anchoredPosition.y;

            s.Get<Btn>("a").Hidden = true;
            Drain();

            Assert.Greater(below.anchoredPosition.y, before,
                "48 fewer units of content above → the sibling moves up (Unity: +y)");
        }

        // ───── variant / idempotency ─────

        [Test]
        public void Variant_flip_between_a_number_and_hug_is_reversible()
        {
            var s = Open(Box(
                $"<VStack id='v' anchor='top-left' width='150' height='200' height.compact='hug' spacing='4'>{ThreeRows}</VStack>"));
            var v = s.Get<VStack>("v").RectTransform;
            Assume.That(v.rect.height, Is.EqualTo(200f).Within(0.01f));

            UI.Variants.Set("compact", true);
            Drain();
            Assert.AreEqual(140f, v.rect.height, 0.01f, "hug takes over");

            UI.Variants.Set("compact", false);
            Drain();
            Assert.AreEqual(200f, v.rect.height, 0.01f, "and hands the axis back");

            var fitter = v.GetComponent<ClampFitter>();
            Assert.IsTrue(fitter == null || !fitter.AxisEnabled(1), "the fitter is retired, not left driving");
        }

        [Test]
        public void Steady_state_resolve_with_hug_dirties_nothing()
        {
            var s = Open(Box(
                $"<VStack id='v' anchor='top-left' width='150' height='hug' spacing='4'>{ThreeRows}</VStack>"));
            Drain();
            Assume.That(PendingLayoutRebuilds(), Is.EqualTo(0), "guard: the queue drained");

            UI.Variants.Set("unrelated", true);
            Drain();
            Canvas.ForceUpdateCanvases();
            Assume.That(PendingLayoutRebuilds(), Is.EqualTo(0), "guard: the queue drained again");

            UI.Variants.Set("unrelated", false);

            Assert.AreEqual(0, PendingLayoutRebuilds(), "a replay that changes nothing must dirty nothing");
            Assert.AreEqual(140f, s.Get<VStack>("v").RectTransform.rect.height, 0.01f);
        }

        // ───── the documented footgun ─────

        [Test]
        public void A_stretch_child_on_the_hug_axis_collapses_to_zero()
        {
            // PUI-HUG-STRETCH-CHILD (lint, Task 4) exists because of exactly this: the parent asks
            // its children how tall they want to be, and a stretch child answers "zero, plus
            // whatever is left over" — of which there is nothing.
            var s = Open(Box(
                "<VStack id='v' anchor='top-left' width='150' height='hug' spacing='0'>" +
                "<Btn id='a' height='stretch'/></VStack>"));

            Assert.AreEqual(0f, s.Get<Btn>("a").RectTransform.rect.height, 0.01f);
        }
    }
}
