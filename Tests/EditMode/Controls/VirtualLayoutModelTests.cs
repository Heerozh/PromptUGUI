using NUnit.Framework;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>VirtualLayoutModel</c>（2026-09-29 scrolllist-virtualization spec §5.1）：虚拟列表每项的高度与状态 ——
    /// Unknown 用已测均值估算、Stale 用自己失效前的值、Collapsed 既不占高也不占间距；偏移、总高、窗口外的
    /// Leading / Trailing 都按「每项占 h + spacing，最后一项不带间距」算，与 VerticalLayoutGroup 的算法一致。
    /// </summary>
    public class VirtualLayoutModelTests
    {
        private static VirtualLayoutModel Model(float spacing, float padTop, float padBottom, params float[] heights)
        {
            var m = new VirtualLayoutModel();
            m.Reset(heights.Length);
            m.SetMetrics(spacing, padTop, padBottom);
            for (var i = 0; i < heights.Length; i++) m.SetMeasured(i, heights[i]);
            return m;
        }

        [Test]
        public void Offsets_and_total_follow_heights_spacing_and_padding()
        {
            var m = Model(2f, 5f, 7f, 10f, 20f, 30f);
            Assert.AreEqual(5f, m.OffsetOf(0), 1e-4);
            Assert.AreEqual(17f, m.OffsetOf(1), 1e-4);
            Assert.AreEqual(39f, m.OffsetOf(2), 1e-4);
            Assert.AreEqual(5f + 60f + 4f + 7f, m.Total, 1e-4, "padding + heights + (N-1)·spacing");
        }

        [Test]
        public void An_empty_model_is_just_its_padding()
        {
            var m = new VirtualLayoutModel();
            m.Reset(0);
            m.SetMetrics(3f, 4f, 6f);
            Assert.AreEqual(10f, m.Total, 1e-4);
            Assert.IsFalse(m.TryWindow(0f, 100f, out _, out _));
        }

        [Test]
        public void Unknown_items_use_the_mean_of_measured_ones()
        {
            var m = new VirtualLayoutModel();
            m.Reset(4);
            m.SetMetrics(0f, 0f, 0f);
            Assert.IsFalse(m.HasEstimate, "nothing measured yet");

            m.SetMeasured(0, 10f);
            m.SetMeasured(1, 30f);

            Assert.IsTrue(m.HasEstimate);
            Assert.AreEqual(20f, m.Estimate, 1e-4);
            Assert.AreEqual(20f, m.HeightOf(2), 1e-4);
            Assert.IsFalse(m.IsMeasured(2));
            Assert.AreEqual(10f + 30f + 20f + 20f, m.Total, 1e-4);
        }

        [Test]
        public void Remeasuring_an_item_replaces_its_share_of_the_mean()
        {
            var m = Model(0f, 0f, 0f, 10f, 30f);
            m.SetMeasured(1, 50f);
            Assert.AreEqual(30f, m.Estimate, 1e-4, "(10 + 50) / 2, not (10 + 30 + 50) / 3");
        }

        [Test]
        public void InvalidateAll_keeps_each_value_as_its_own_estimate_and_the_mean_as_fallback()
        {
            var m = Model(0f, 0f, 0f, 10f, 30f);
            m.Reset(4);   // Reset forgets everything, fallback included
            Assert.IsFalse(m.HasEstimate);

            m.SetMeasured(0, 10f);
            m.SetMeasured(1, 30f);
            m.InvalidateAll();

            Assert.IsFalse(m.IsMeasured(0), "measured → stale");
            Assert.AreEqual(10f, m.HeightOf(0), 1e-4, "a stale item estimates with its own old value");
            Assert.IsTrue(m.HasEstimate, "the old mean survives as the fallback");
            Assert.AreEqual(20f, m.Estimate, 1e-4);
            Assert.AreEqual(20f, m.HeightOf(3), 1e-4, "an unknown item uses the fallback");

            m.SetMeasured(0, 50f);
            Assert.AreEqual(50f, m.Estimate, 1e-4, "a fresh measurement takes over from the fallback");
        }

        [Test]
        public void Collapsed_items_take_no_height_and_no_gap()
        {
            var m = Model(4f, 0f, 0f, 10f, 10f, 10f);
            m.SetCollapsed(1);

            Assert.AreEqual(0f, m.HeightOf(1), 1e-4);
            Assert.AreEqual(14f, m.OffsetOf(2), 1e-4, "10 + 4 for item 0, nothing for the collapsed one");
            Assert.AreEqual(24f, m.Total, 1e-4, "two visible items, one gap");
            Assert.IsTrue(m.IsCollapsed(1));
        }

        [Test]
        public void A_collapsed_item_that_shows_again_is_measured_again()
        {
            var m = Model(4f, 0f, 0f, 10f, 10f);
            m.SetCollapsed(0);
            m.SetMeasured(0, 20f);
            Assert.IsFalse(m.IsCollapsed(0));
            Assert.AreEqual(20f + 4f + 10f, m.Total, 1e-4);
        }

        [Test]
        public void TryWindow_returns_the_items_that_intersect()
        {
            var m = Model(0f, 0f, 0f, 10f, 10f, 10f, 10f, 10f, 10f, 10f, 10f, 10f, 10f);

            Assert.IsTrue(m.TryWindow(15f, 35f, out var first, out var last));
            Assert.AreEqual(1, first);
            Assert.AreEqual(3, last);

            Assert.IsTrue(m.TryWindow(20f, 30f, out first, out last));
            Assert.AreEqual(2, first, "item 1 ends at 20: touching is not intersecting");
            Assert.AreEqual(2, last, "item 3 starts at 30: touching is not intersecting");
        }

        [Test]
        public void TryWindow_always_returns_at_least_one_item()
        {
            var inPadding = Model(0f, 100f, 0f, 10f, 10f, 10f);
            Assert.IsTrue(inPadding.TryWindow(0f, 50f, out var first, out var last));
            Assert.AreEqual(0, first);
            Assert.AreEqual(0, last);

            var inGap = Model(10f, 0f, 0f, 10f, 10f, 10f);   // items at [0,10) [20,30) [40,50)
            Assert.IsTrue(inGap.TryWindow(12f, 18f, out first, out last));
            Assert.AreEqual(first, last, "a range that falls between two items still yields one");

            var pastTheEnd = Model(0f, 0f, 0f, 10f, 10f);
            Assert.IsTrue(pastTheEnd.TryWindow(500f, 600f, out first, out last));
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, last);
        }

        [Test]
        public void IndexAt_finds_the_item_that_contains_a_position()
        {
            var m = Model(2f, 5f, 0f, 10f, 20f, 30f);   // offsets 5, 17, 39
            Assert.AreEqual(0, m.IndexAt(0f), "clamped to the first item");
            Assert.AreEqual(0, m.IndexAt(16f));
            Assert.AreEqual(1, m.IndexAt(17f));
            Assert.AreEqual(2, m.IndexAt(1000f), "clamped to the last item");
        }

        [Test]
        public void Remap_carries_heights_and_states()
        {
            var m = Model(0f, 0f, 0f, 10f, 20f, 30f);
            m.InvalidateAll();
            m.SetMeasured(1, 20f);
            m.SetMeasured(2, 30f);

            m.Remap(new[] { 1, 2, -1 }, 3);   // trim the first, append one

            Assert.AreEqual(3, m.Count);
            Assert.IsTrue(m.IsMeasured(0));
            Assert.AreEqual(20f, m.HeightOf(0), 1e-4);
            Assert.AreEqual(30f, m.HeightOf(1), 1e-4);
            Assert.IsFalse(m.IsMeasured(2), "the appended item is unknown");
            Assert.AreEqual(25f, m.HeightOf(2), 1e-4, "estimated from the items that are still measured");
        }

        [Test]
        public void Remap_keeps_a_stale_value_with_its_item()
        {
            var m = Model(0f, 0f, 0f, 10f, 40f);
            m.InvalidateAll();
            m.Remap(new[] { -1, 1 }, 2);
            Assert.AreEqual(40f, m.HeightOf(1), 1e-4);
        }

        [Test]
        public void Extents_add_up_to_the_total()
        {
            var heights = new[] { 12f, 7f, 30f, 18f, 5f, 22f, 9f };
            var m = Model(3f, 4f, 6f, heights);
            m.SetCollapsed(4);

            for (var f = 0; f < heights.Length; f++)
                for (var l = f; l < heights.Length; l++)
                {
                    var visible = 0;
                    var inWindow = 0f;
                    for (var i = f; i <= l; i++)
                    {
                        if (m.IsCollapsed(i)) continue;
                        inWindow += m.HeightOf(i);
                        visible++;
                    }
                    if (visible == 0) continue;   // the window is never all-collapsed in practice
                    var laidOut = 4f + m.ExtentBefore(f) + inWindow + (visible - 1) * 3f + m.ExtentAfter(l) + 6f;
                    Assert.AreEqual(m.Total, laidOut, 1e-3, $"window [{f}, {l}]");
                }
        }

        [Test]
        public void Changing_the_metrics_moves_the_offsets()
        {
            var m = Model(0f, 0f, 0f, 10f, 10f);
            m.SetMetrics(5f, 2f, 0f);
            Assert.AreEqual(17f, m.OffsetOf(1), 1e-4);
        }
    }
}
