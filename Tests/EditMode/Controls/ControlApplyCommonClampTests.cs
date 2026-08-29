using System.Reflection;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // width="clamp(min, N%, max)" through the real pipeline (UI.Open → ApplyCommon → ClampFitter →
    // LayoutRebuilder). Spec 2026-08-30-clamp-size-design §5.1 / §5.4. The parent Frame carries a
    // NUMERIC size so the geometry is independent of the EditMode canvas size.
    public class ControlApplyCommonClampTests
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

        private static string Box(float w, float h, string child) =>
            Doc($"<Frame id='box' anchor='top-left' width='{w}' height='{h}'>{child}</Frame>");

        private static PromptUGUI.Application.Screen Open(string xml)
        {
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Drain();
            return screen;
        }

        private static float Left(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMin.x + child.anchorMin.x * parent.rect.width;
        }

        private static float Right(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMax.x + child.anchorMax.x * parent.rect.width;
        }

        private static float Bottom(RectTransform child)
        {
            var parent = (RectTransform)child.parent;
            return child.offsetMin.y + child.anchorMin.y * parent.rect.height;
        }

        // ───── free-positioning geometry ─────

        [TestCase(300f, 167f)]
        [TestCase(500f, 232f)]
        [TestCase(800f, 250f)]
        public void Width_clamp_bottom_left_hugs_left(float parentW, float expected)
        {
            var screen = Open(Box(parentW, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100'/>"));
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(expected, rt.rect.width, 0.01f);
            Assert.AreEqual(0f, Left(rt), 0.01f, "left-anchored → box hugs the parent's left edge even when capped");
            Assert.AreEqual(100f, rt.rect.height, 0.01f, "unclamped axis untouched");
        }

        [Test]
        public void Width_clamp_right_anchor_hugs_right()
        {
            var screen = Open(Box(800f, 200f,
                "<Frame id='p' anchor='center-right' width='clamp(167, 46.4%, 250)' height='100'/>"));
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(250f, rt.rect.width, 0.01f);
            Assert.AreEqual(800f, Right(rt), 0.01f);
        }

        [Test]
        public void Width_clamp_center_anchor_stays_centered()
        {
            var screen = Open(Box(800f, 200f,
                "<Frame id='p' anchor='center' width='clamp(167, 46.4%, 250)' height='100'/>"));
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(250f, rt.rect.width, 0.01f);
            Assert.AreEqual(400f, (Left(rt) + Right(rt)) * 0.5f, 0.01f);
        }

        [Test]
        public void Margins_inset_inside_the_clamped_box()
        {
            var screen = Open(Box(800f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100' margin='0,16,0,16'/>"));
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(250f - 32f, rt.rect.width, 0.01f);
            Assert.AreEqual(16f, Left(rt), 0.01f);
        }

        [TestCase(300f, 200f)]
        [TestCase(600f, 330f)]
        [TestCase(800f, 400f)]
        public void Height_clamp_bottom_anchor(float parentH, float expected)
        {
            var screen = Open(Box(300f, parentH,
                "<Frame id='p' anchor='bottom-left' width='100' height='clamp(200, 55%, 400)'/>"));
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(expected, rt.rect.height, 0.01f);
            Assert.AreEqual(0f, Bottom(rt), 0.01f);
            Assert.AreEqual(100f, rt.rect.width, 0.01f);
        }

        [Test]
        public void Unclamped_clamp_is_bit_identical_to_plain_percent()
        {
            // 46.4% of 500 = 232 ∈ [1, 9999]: the clamp twin must land on exactly the % geometry.
            var screen = Open(Box(500f, 200f,
                "<Frame id='a' anchor='bottom-left' width='46.4%' height='100' margin='0,8,0,16'/>" +
                "<Frame id='b' anchor='bottom-left' width='clamp(1, 46.4%, 9999)' height='100' margin='0,8,0,16'/>"));
            var a = screen.Get<Frame>("a").RectTransform;
            var b = screen.Get<Frame>("b").RectTransform;
            Assert.AreEqual(a.offsetMin, b.offsetMin);
            Assert.AreEqual(a.offsetMax, b.offsetMax);
            Assert.AreEqual(a.anchorMin, b.anchorMin);
            Assert.AreEqual(a.anchorMax, b.anchorMax);
        }

        [Test]
        public void Fitter_is_attached_with_only_the_clamped_axis_enabled()
        {
            var screen = Open(Box(300f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100'/>"));
            var fitter = screen.Get<Frame>("p").GameObject.GetComponent<ClampFitter>();
            Assert.IsNotNull(fitter);
            Assert.IsTrue(fitter.enabled);
            Assert.IsTrue(fitter.AxisEnabled(0));
            Assert.IsFalse(fitter.AxisEnabled(1));
        }

        [Test]
        public void Nested_clamps_resolve_parent_first()
        {
            // Two clamp nodes, one inside the other. Apply order is child-before-parent (DFS post-order),
            // so this only works because LayoutRebuilder orders the passes by depth, not ApplyCommon.
            var screen = Open(Box(800f, 200f,
                "<Frame id='outer' anchor='top-left' width='clamp(100, 50%, 400)' height='100'>" +
                "<Frame id='inner' anchor='top-left' width='clamp(50, 50%, 120)' height='50'/>" +
                "</Frame>"));
            Assert.AreEqual(400f, screen.Get<Frame>("outer").RectTransform.rect.width, 0.01f);
            Assert.AreEqual(120f, screen.Get<Frame>("inner").RectTransform.rect.width, 0.01f);
        }

        [Test]
        public void Nested_clamps_in_range()
        {
            var screen = Open(Box(300f, 200f,
                "<Frame id='outer' anchor='top-left' width='clamp(100, 50%, 400)' height='100'>" +
                "<Frame id='inner' anchor='top-left' width='clamp(50, 50%, 120)' height='50'/>" +
                "</Frame>"));
            Assert.AreEqual(150f, screen.Get<Frame>("outer").RectTransform.rect.width, 0.01f);
            Assert.AreEqual(75f, screen.Get<Frame>("inner").RectTransform.rect.width, 0.01f);
        }

        [Test]
        public void Parent_resize_then_forced_rebuild_recomputes()
        {
            var screen = Open(Box(300f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100'/>"));
            var box = screen.Get<Frame>("box").RectTransform;
            var rt = screen.Get<Frame>("p").RectTransform;
            Assert.AreEqual(167f, rt.rect.width, 0.01f);
            box.sizeDelta = new Vector2(800f, 200f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            Assert.AreEqual(250f, rt.rect.width, 0.01f);
            Assert.AreEqual(0f, Left(rt), 0.01f);
        }

        // ───── Variant / ReSolve ─────

        [Test]
        public void Variant_to_numeric_disables_fitter_axis_and_back()
        {
            var screen = Open(Box(300f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' width.wide='250' height='100'/>"));
            var ctrl = screen.Get<Frame>("p");
            var rt = ctrl.RectTransform;
            var fitter = ctrl.GameObject.GetComponent<ClampFitter>();
            Assert.AreEqual(167f, rt.rect.width, 0.01f);

            UI.Variants.Set("wide", true);
            Drain();
            Assert.IsFalse(fitter.AxisEnabled(0), "numeric width → fitter axis off");
            Assert.AreEqual(250f, rt.sizeDelta.x, 0.01f, "numeric path: sizeDelta carries the width");
            Assert.AreEqual(250f, rt.rect.width, 0.01f);

            UI.Variants.Set("wide", false);
            Drain();
            Assert.IsTrue(fitter.AxisEnabled(0), "back to clamp → fitter axis on");
            Assert.AreEqual(167f, rt.rect.width, 0.01f);
        }

        [Test]
        public void ReSolve_twice_is_idempotent()
        {
            var screen = Open(Box(300f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100' margin='0,4,0,4'/>"));
            var rt = screen.Get<Frame>("p").RectTransform;
            var min0 = rt.offsetMin;
            var max0 = rt.offsetMax;
            screen.ReSolve();
            Drain();
            screen.ReSolve();
            Drain();
            Assert.AreEqual(min0, rt.offsetMin);
            Assert.AreEqual(max0, rt.offsetMax);
            Assert.AreEqual(0, PendingLayoutRebuilds(), "drained queue stays empty");
        }

        [Test]
        public void ReSolve_with_unchanged_spec_in_range_dirties_nothing()
        {
            // LGC-D18 spirit: 46.4% of 500 = 232 is in range, so ApplyCommon's baseline equals the
            // fitter's output and a replay must not enqueue a layout rebuild.
            var screen = Open(Box(500f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100'/>"));
            Assume.That(PendingLayoutRebuilds(), Is.EqualTo(0), "guard: drained before the replay");
            screen.ReSolve();
            Assert.AreEqual(0, PendingLayoutRebuilds());
        }

        [Test]
        public void Hidden_then_shown_recomputes_on_enable()
        {
            var screen = Open(Box(800f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, 46.4%, 250)' height='100' hidden='true'/>"));
            var ctrl = screen.Get<Frame>("p");
            ctrl.Hidden = false;
            Drain();
            Assert.AreEqual(250f, ctrl.RectTransform.rect.width, 0.01f);
            Assert.AreEqual(0f, Left(ctrl.RectTransform), 0.01f);
        }

        // ───── errors ─────

        [Test]
        public void Percent_clamp_inside_layout_group_throws_with_stretch_hint()
        {
            UI.LoadDocument("test", Doc(
                "<VStack id='v' width='200' height='200'><Frame id='p' width='clamp(167, 46%, 250)' height='40'/></VStack>"));
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("clamp(min, stretch, max)", ex.Message);
        }

        [Test]
        public void Stretch_clamp_under_free_positioning_parent_throws_with_percent_hint()
        {
            UI.LoadDocument("test", Box(300f, 200f,
                "<Frame id='p' anchor='bottom-left' width='clamp(167, stretch, 250)' height='40'/>"));
            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("clamp(min, N%, max)", ex.Message);
        }

        [Test]
        public void Clamp_on_stretched_anchor_axis_throws()
        {
            UI.LoadDocument("test", Box(300f, 200f,
                "<Frame id='p' anchor='bottom-stretch' width='clamp(167, 46%, 250)' height='40'/>"));
            Assert.Throws<ParseException>(() => UI.Open("S"));
        }
    }
}
