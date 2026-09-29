using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using ScrollRect = UnityEngine.UI.ScrollRect;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>PuiScrollRect</c>（2026-09-29 scrolllist-virtualization spec §5.8）：每个 ScrollList 都带的 ScrollRect 子类。
    /// 行为与基类相同，外加：<c>IsDragging</c>、<c>ShiftContentY</c>（挪内容不被当成拖动 / 甩动、滚动条跟着刷新）。
    /// LateUpdate 钩子与「用户移动」判据只在 Play 模式可见，见 PlayMode 的 <c>ScrollListVirtualPlayTests</c>。
    /// </summary>
    public class PuiScrollRectTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RowTemplate =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private static ScrollList OpenList(int rows = 20)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + RowTemplate
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + "<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");
            var items = new List<string>();
            for (var i = 0; i < rows; i++) items.Add("i" + i);
            list.BindItems(Observable.Return<IReadOnlyList<string>>(items),
                           (IControl row, string s) => row.Get<Text>("label").TextValue = s);
            Canvas.ForceUpdateCanvases();
            return list;
        }

        private static PuiScrollRect ScrollOf(ScrollList list) => (PuiScrollRect)list.GameObject.GetComponent<ScrollRect>();

        private static RectTransform ContentOf(ScrollList list) =>
            (RectTransform)list.GameObject.transform.Find("Viewport/Content");

        private static PointerEventData PressAtViewportCentre(ScrollList list)
        {
            var viewport = (RectTransform)list.GameObject.transform.Find("Viewport");
            var world = viewport.TransformPoint(viewport.rect.center);
            var screen = RectTransformUtility.WorldToScreenPoint(null, world);
            return new PointerEventData(EventSystem.current)
            {
                position = screen,
                pressPosition = screen,
                button = PointerEventData.InputButton.Left,
            };
        }

        [Test]
        public void ScrollList_root_carries_a_PuiScrollRect()
        {
            var list = OpenList();
            Assert.IsInstanceOf<PuiScrollRect>(list.GameObject.GetComponent<ScrollRect>());
        }

        [Test]
        public void ShiftContentY_moves_the_content_and_the_scrollbar_value()
        {
            var list = OpenList();
            var scroll = ScrollOf(list);
            var content = ContentOf(list);
            Assume.That(scroll.verticalScrollbar, Is.Not.Null, "guard: the stock bar is wired");
            var y0 = content.anchoredPosition.y;

            scroll.ShiftContentY(50f);

            Assert.AreEqual(y0 + 50f, content.anchoredPosition.y, 0.01f);
            Assert.AreEqual(scroll.verticalNormalizedPosition, scroll.verticalScrollbar.value, 0.001f,
                "the bar follows the shift at once");
            Assert.Less(scroll.verticalScrollbar.value, 1f, "no longer at the top");
        }

        [Test]
        public void ShiftContentY_during_a_drag_moves_the_drag_origin_too()
        {
            var list = OpenList();
            var scroll = ScrollOf(list);
            var content = ContentOf(list);
            var e = PressAtViewportCentre(list);
            scroll.OnInitializePotentialDrag(e);
            scroll.OnBeginDrag(e);
            var y0 = content.anchoredPosition.y;

            scroll.ShiftContentY(50f);
            scroll.OnDrag(e);   // the pointer has not moved

            Assert.AreEqual(y0 + 50f, content.anchoredPosition.y, 0.01f,
                "the next drag event keeps the shift instead of snapping back to where the drag started");
            scroll.OnEndDrag(e);
        }

        [Test]
        public void IsDragging_tracks_begin_end_and_disable()
        {
            var list = OpenList();
            var scroll = ScrollOf(list);
            var e = PressAtViewportCentre(list);

            Assert.IsFalse(scroll.IsDragging);
            scroll.OnBeginDrag(e);
            Assert.IsTrue(scroll.IsDragging);
            scroll.OnEndDrag(e);
            Assert.IsFalse(scroll.IsDragging);

            scroll.OnBeginDrag(e);
            list.GameObject.SetActive(false);
            Assert.IsFalse(scroll.IsDragging, "a disabled ScrollRect is not dragging");
        }

        [Test]
        public void A_right_button_drag_is_not_a_drag()
        {
            var list = OpenList();
            var scroll = ScrollOf(list);
            var e = PressAtViewportCentre(list);
            e.button = PointerEventData.InputButton.Right;

            scroll.OnBeginDrag(e);

            Assert.IsFalse(scroll.IsDragging, "ScrollRect itself ignores non-left drags");
        }
    }
}
