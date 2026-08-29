using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Drives ClampFitter directly (no UI.Open): a hand-built parent RT + child RT carrying the
    // fractional anchors ApplyCommon would have written. Spec 2026-08-30-clamp-size-design §5.1.
    public class ClampFitterTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        private RectTransform MakeParent(float w, float h)
        {
            _root = new GameObject("parent", typeof(RectTransform));
            var rt = (RectTransform)_root.transform;
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // Child with the fractional sub-range on `axis` as ComputeFractionalAnchor writes it and a
        // point anchor + fixed 40px on the other axis.
        private static (RectTransform rt, ClampFitter fitter) MakeChild(RectTransform parent, int axis,
            float fraction, ClampAlign align)
        {
            var go = new GameObject("child", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            float a0, a1;
            switch (align)
            {
                case ClampAlign.Low: a0 = 0f; a1 = fraction; break;
                case ClampAlign.High: a0 = 1f - fraction; a1 = 1f; break;
                default: a0 = (1f - fraction) * 0.5f; a1 = (1f + fraction) * 0.5f; break;
            }
            var min = Vector2.zero;
            var max = Vector2.zero;
            var pivot = Vector2.zero;
            var size = new Vector2(40f, 40f);
            min[axis] = a0; max[axis] = a1; pivot[axis] = 0.5f; size[axis] = 0f;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            var fitter = go.AddComponent<ClampFitter>();
            return (rt, fitter);
        }

        private static float Low(RectTransform child, RectTransform parent, int axis)
            => child.offsetMin[axis] + child.anchorMin[axis] * parent.rect.size[axis];

        private static float High(RectTransform child, RectTransform parent, int axis)
            => child.offsetMax[axis] + child.anchorMax[axis] * parent.rect.size[axis];

        [TestCase(300f, 167f)]   // 46.4% of 300 = 139.2 → floor 167
        [TestCase(500f, 232f)]   // in range
        [TestCase(800f, 250f)]   // 371.2 → cap 250
        public void Width_clamped_and_hugs_left(float parentW, float expected)
        {
            var parent = MakeParent(parentW, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Low);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 0f, 0f, ClampAlign.Low);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(expected, child.rect.width, 0.01f);
            Assert.AreEqual(0f, Low(child, parent, 0), 0.01f, "left-aligned box hugs the parent's left edge");
        }

        [Test]
        public void Width_capped_hugs_right_when_high_aligned()
        {
            var parent = MakeParent(800f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.High);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 0f, 0f, ClampAlign.High);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(250f, child.rect.width, 0.01f);
            Assert.AreEqual(800f, High(child, parent, 0), 0.01f, "right-aligned box hugs the parent's right edge");
        }

        [Test]
        public void Width_capped_stays_centered_when_center_aligned()
        {
            var parent = MakeParent(800f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Center);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 0f, 0f, ClampAlign.Center);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(250f, child.rect.width, 0.01f);
            Assert.AreEqual(400f, (Low(child, parent, 0) + High(child, parent, 0)) * 0.5f, 0.01f);
        }

        [Test]
        public void Margins_inset_inside_the_clamped_box()
        {
            var parent = MakeParent(800f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Low);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 16f, 16f, ClampAlign.Low);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(250f - 32f, child.rect.width, 0.01f, "box 250 minus both margins");
            Assert.AreEqual(16f, Low(child, parent, 0), 0.01f, "left margin from the parent's left edge");
        }

        [Test]
        public void Unclamped_range_equals_plain_fractional_offsets()
        {
            // 46.4% of 500 = 232 ∈ [167, 250] → offsets must be exactly what MarginResolver's
            // stretch branch writes for a plain % axis: offsetMin = lo, offsetMax = -hi.
            var parent = MakeParent(500f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Low);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 16f, 8f, ClampAlign.Low);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(16f, child.offsetMin.x, 0.0001f);
            Assert.AreEqual(-8f, child.offsetMax.x, 0.0001f);
        }

        [TestCase(300f, 200f)]   // 55% of 300 = 165 → floor 200
        [TestCase(600f, 330f)]   // in range
        [TestCase(800f, 400f)]   // 440 → cap 400
        public void Height_clamped_on_vertical_axis(float parentH, float expected)
        {
            var parent = MakeParent(300f, parentH);
            var (child, fitter) = MakeChild(parent, 1, 0.55f, ClampAlign.Low);
            fitter.SetAxis(1, true, 0.55f, 200f, 400f, 0f, 0f, ClampAlign.Low);
            fitter.SetLayoutVertical();
            Assert.AreEqual(expected, child.rect.height, 0.01f);
            Assert.AreEqual(0f, Low(child, parent, 1), 0.01f, "bottom-aligned box hugs the parent's bottom edge");
        }

        [Test]
        public void Disabled_axis_leaves_the_rect_alone()
        {
            var parent = MakeParent(800f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Low);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 0f, 0f, ClampAlign.Low);
            fitter.ClearAxis(0);
            child.offsetMin = new Vector2(7f, child.offsetMin.y);
            child.offsetMax = new Vector2(-3f, child.offsetMax.y);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(7f, child.offsetMin.x, 0.0001f);
            Assert.AreEqual(-3f, child.offsetMax.x, 0.0001f);
            Assert.IsFalse(fitter.AxisEnabled(0));
        }

        [Test]
        public void Second_pass_is_a_no_op()
        {
            var parent = MakeParent(800f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Low);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 4f, 4f, ClampAlign.Low);
            fitter.SetLayoutHorizontal();
            var min1 = child.offsetMin;
            var max1 = child.offsetMax;
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(min1, child.offsetMin);
            Assert.AreEqual(max1, child.offsetMax);
        }

        [Test]
        public void Follows_parent_resize_on_next_pass()
        {
            var parent = MakeParent(300f, 200f);
            var (child, fitter) = MakeChild(parent, 0, 0.464f, ClampAlign.Low);
            fitter.SetAxis(0, true, 0.464f, 167f, 250f, 0f, 0f, ClampAlign.Low);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(167f, child.rect.width, 0.01f);
            parent.sizeDelta = new Vector2(800f, 200f);
            fitter.SetLayoutHorizontal();
            Assert.AreEqual(250f, child.rect.width, 0.01f);
            Assert.AreEqual(0f, Low(child, parent, 0), 0.01f);
        }

        [Test]
        public void No_parent_is_a_no_op()
        {
            _root = new GameObject("orphan", typeof(RectTransform));
            var fitter = _root.AddComponent<ClampFitter>();
            fitter.SetAxis(0, true, 0.5f, 10f, 20f, 0f, 0f, ClampAlign.Low);
            Assert.DoesNotThrow(() => fitter.SetLayoutHorizontal());
        }
    }
}
