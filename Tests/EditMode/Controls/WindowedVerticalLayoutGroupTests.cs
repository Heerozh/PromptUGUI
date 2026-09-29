using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>WindowedVerticalLayoutGroup</c>（2026-09-29 scrolllist-virtualization spec §5.3）：虚拟列表的 Content 组。
    /// 浮点 <c>Leading</c> / <c>Trailing</c> 加进 min 与 preferred（ContentSizeFitter 因此撑到虚拟总高，
    /// <c>childForceExpandHeight</c> 也不会把这段空白分给行），行整体下移 <c>Leading</c>。两者为 0 时与普通
    /// VerticalLayoutGroup 完全相同。夹具配置照 <c>ScrollList.ApplyLayoutMode</c>。
    /// </summary>
    public class WindowedVerticalLayoutGroupTests
    {
        private GameObject _canvas;

        [TearDown]
        public void TearDown()
        {
            if (_canvas != null) Object.DestroyImmediate(_canvas);
        }

        private RectTransform Build<TGroup>(out TGroup group, out RectTransform[] rows, int count = 3)
            where TGroup : HorizontalOrVerticalLayoutGroup
        {
            _canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_canvas.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            group = content.gameObject.AddComponent<TGroup>();
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            group.spacing = 4f;
            group.padding = new RectOffset(0, 0, 5, 6);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            rows = new RectTransform[count];
            for (var i = 0; i < count; i++)
            {
                var row = new GameObject($"Row{i}", typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(content, false);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
                rows[i] = row;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            return content;
        }

        // Distance from the content's top edge to a laid-out child's top edge.
        private static float TopOf(RectTransform row) =>
            -row.anchoredPosition.y - row.rect.height * (1f - row.pivot.y);

        [Test]
        public void Leading_and_trailing_add_to_the_content_height()
        {
            var content = Build<WindowedVerticalLayoutGroup>(out var group, out _);

            group.Leading = 100f;
            group.Trailing = 200f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            Assert.AreEqual(5f + 100f + 90f + 8f + 200f + 6f, content.rect.height, 0.01f);
        }

        [Test]
        public void Children_start_below_leading_and_keep_their_preferred_height()
        {
            var content = Build<WindowedVerticalLayoutGroup>(out var group, out var rows);

            group.Leading = 100f;
            group.Trailing = 200f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            for (var i = 0; i < rows.Length; i++)
            {
                Assert.AreEqual(30f, rows[i].rect.height, 0.01f,
                    $"row {i}: childForceExpandHeight did not hand it any of the extra space");
                Assert.AreEqual(5f + 100f + i * 34f, TopOf(rows[i]), 0.01f, $"row {i} position");
            }
        }

        [Test]
        public void Zero_extents_match_a_plain_VerticalLayoutGroup()
        {
            var windowed = Build<WindowedVerticalLayoutGroup>(out _, out var windowedRows);
            var windowedHeight = windowed.rect.height;
            var windowedTops = new float[windowedRows.Length];
            for (var i = 0; i < windowedRows.Length; i++) windowedTops[i] = TopOf(windowedRows[i]);
            Object.DestroyImmediate(_canvas);

            var plain = Build<VerticalLayoutGroup>(out _, out var plainRows);

            Assert.AreEqual(plain.rect.height, windowedHeight, 0.01f);
            for (var i = 0; i < plainRows.Length; i++)
                Assert.AreEqual(TopOf(plainRows[i]), windowedTops[i], 0.01f, $"row {i}");
        }

        [Test]
        public void Changing_leading_marks_the_layout_dirty()
        {
            var content = Build<WindowedVerticalLayoutGroup>(out var group, out var rows);

            group.Leading = 50f;
            Canvas.ForceUpdateCanvases();   // only picks up what was marked for rebuild

            Assert.AreEqual(5f + 50f, TopOf(rows[0]), 0.01f);
            Assert.AreEqual(5f + 50f + 90f + 8f + 6f, content.rect.height, 0.01f);
        }

        [Test]
        public void Inactive_children_take_no_space_or_gap()
        {
            var content = Build<WindowedVerticalLayoutGroup>(out var group, out var rows);

            rows[1].gameObject.SetActive(false);
            group.Leading = 10f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            Assert.AreEqual(5f + 10f + 34f, TopOf(rows[2]), 0.01f, "row 2 follows row 0 directly");
            Assert.AreEqual(5f + 10f + 30f + 4f + 30f + 6f, content.rect.height, 0.01f);
        }
    }
}
