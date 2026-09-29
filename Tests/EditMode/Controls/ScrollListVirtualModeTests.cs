using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>virtualize</c> 的模式选择（2026-09-29 scrolllist-virtualization spec §4.1 / §4.3 / VIR-P1）：实例化时定型，
    /// Content 换成 <c>WindowedVerticalLayoutGroup</c>；与网格 / 横向 / 拖排 / <c>reuseItems="false"</c> / Variant 切换冲突时，
    /// 控件自己警告一次并降级，Screen 照常打开。
    /// </summary>
    public class ScrollListVirtualModeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RowTemplate =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private static PromptUGUI.Application.Screen Open(string listAttrs, string styles = "", string children = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + styles + RowTemplate
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + $"<ScrollList id='sl' width='150' height='200' itemTemplate='Row' {listAttrs}>{children}</ScrollList>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return screen;
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static LayoutGroup GroupOf(ScrollList sl) => ContentOf(sl).GetComponent<LayoutGroup>();

        [Test]
        public void Virtualize_swaps_Content_to_the_windowed_group()
        {
            var list = Open("virtualize='true'").Get<ScrollList>("sl");
            Assert.IsInstanceOf<WindowedVerticalLayoutGroup>(GroupOf(list));
            Assert.AreEqual(1, ContentOf(list).GetComponents<LayoutGroup>().Length);
            Assert.IsTrue(list.IsVirtual);
        }

        [Test]
        public void Without_virtualize_Content_keeps_a_plain_VerticalLayoutGroup()
        {
            var list = Open("").Get<ScrollList>("sl");
            Assert.AreEqual(typeof(VerticalLayoutGroup), GroupOf(list).GetType());
            Assert.IsFalse(list.IsVirtual);
        }

        [Test]
        public void Virtualize_through_a_class_style_counts()
        {
            var list = Open("class='chat'", "<Style name='chat' virtualize='true'/>").Get<ScrollList>("sl");
            Assert.IsInstanceOf<WindowedVerticalLayoutGroup>(GroupOf(list));
        }

        [Test]
        public void Virtualize_with_columns_falls_back_to_the_grid_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-LAYOUT"));
            var list = Open("virtualize='true' columns='2' cellSize='40x40'").Get<ScrollList>("sl");
            Assert.IsInstanceOf<GridLayoutGroup>(GroupOf(list));
            Assert.IsFalse(list.IsVirtual);
        }

        [Test]
        public void Virtualize_with_horizontal_falls_back_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-LAYOUT"));
            var list = Open("virtualize='true' direction='horizontal'").Get<ScrollList>("sl");
            Assert.IsInstanceOf<HorizontalLayoutGroup>(GroupOf(list));
            Assert.IsFalse(list.IsVirtual);
        }

        [Test]
        public void Virtualize_with_reorder_keeps_reorder_off_and_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-REORDER"));
            var list = Open("virtualize='true' reorder='true'").Get<ScrollList>("sl");
            Assert.IsNull(ContentOf(list).GetComponent<ReorderDriver>(), "no drag-to-reorder machinery");
            Assert.IsFalse(list.ReorderEnabled);
        }

        [Test]
        public void Virtualize_with_reuseItems_false_warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-REUSE"));
            Open("virtualize='true' reuseItems='false'");
        }

        [Test]
        public void Virtualize_variant_flip_is_ignored_and_warns_once()
        {
            var screen = Open("virtualize='false' virtualize.alt='true'");
            var list = screen.Get<ScrollList>("sl");
            Assume.That(list.IsVirtual, Is.False);

            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-VARIANT"));
            UI.Variants.Set("alt", true);
            screen.ReSolve();

            Assert.IsFalse(list.IsVirtual, "the mode was fixed when the list was built");
            Assert.AreEqual(typeof(VerticalLayoutGroup), GroupOf(list).GetType());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Columns_variant_on_a_virtual_list_is_ignored_and_warns()
        {
            var screen = Open("virtualize='true' columns='0' columns.alt='3' cellSize='40x40'");
            var list = screen.Get<ScrollList>("sl");
            Assume.That(list.IsVirtual, Is.True);

            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-LAYOUT"));
            UI.Variants.Set("alt", true);

            Assert.IsInstanceOf<WindowedVerticalLayoutGroup>(GroupOf(list), "still one vertical column");
            Assert.IsFalse(list.IsGrid);
        }

        [Test]
        public void Direction_variant_on_a_virtual_list_is_ignored_and_warns()
        {
            var screen = Open("virtualize='true' direction='vertical' direction.alt='horizontal'");
            var list = screen.Get<ScrollList>("sl");

            LogAssert.Expect(LogType.Warning, new Regex("PUI-SCROLL-VIRTUAL-LAYOUT"));
            UI.Variants.Set("alt", true);

            Assert.IsInstanceOf<WindowedVerticalLayoutGroup>(GroupOf(list));
            Assert.IsFalse(list.IsHorizontal);
        }

        [Test]
        public void Static_children_lay_out_as_before_until_the_first_push()
        {
            var list = Open("virtualize='true' spacing='4'", "",
                            "<Frame height='30'/><Frame height='30'/>").Get<ScrollList>("sl");
            var content = ContentOf(list);

            Assert.AreEqual(2, list.SlotCount);
            Assert.AreEqual(2, content.childCount);
            var second = (RectTransform)content.GetChild(1);
            Assert.AreEqual(34f, -second.anchoredPosition.y - second.rect.height * (1f - second.pivot.y), 0.01f,
                "no leading space before the first push");
            Assert.AreEqual(64f, content.rect.height, 0.01f);
        }
    }
}
