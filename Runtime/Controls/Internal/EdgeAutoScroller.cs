using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Edge autoscroll for a single-axis scroller while something is being dragged over it — spec
    /// 2026-09-16-scrolllist-drag-reorder §5.6, shared with drag-and-drop (2026-11-16 §7.5): the pointer
    /// inside the leading / trailing band of the viewport pushes the content along the scroll axis,
    /// faster the deeper it goes, clamped to the content's bounds (never into the elastic zone, where
    /// the ScrollRect's LateUpdate would pull it back). The ScrollRect notices the moved content and
    /// raises its own <c>onValueChanged</c>, so a scrollbar follows without being told.
    /// </summary>
    internal static class EdgeAutoScroller
    {
        // The band along the viewport's leading / trailing edge that scrolls, and how fast at full
        // penetration (canvas units per second).
        private const float EdgeFraction = 0.15f;
        private const float EdgeMin = 24f;
        private const float EdgeMax = 80f;
        private const float Speed = 800f;

        /// <summary>One frame of autoscroll. True when the content moved.</summary>
        internal static bool Step(RectTransform viewport, RectTransform content, bool horizontal,
                                  Vector2 screen, Camera cam, float dt)
        {
            if (viewport == null || content == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, cam, out var p))
                return false;
            var view = viewport.rect;
            var rect = content.rect;
            var pos = content.anchoredPosition;

            if (horizontal)
            {
                var edge = Mathf.Clamp(view.width * EdgeFraction, EdgeMin, EdgeMax);
                var toRight = view.xMax - p.x;
                var toLeft = p.x - view.xMin;
                float dir = toRight < edge ? -1f : toLeft < edge ? 1f : 0f;
                if (dir == 0f) return false;
                var speed = Speed * (1f - Mathf.Min(toRight, toLeft) / edge);
                var max = Mathf.Max(0f, rect.width - view.width);
                pos.x = Mathf.Clamp(pos.x + dir * speed * dt, -max, 0f);
            }
            else
            {
                var edge = Mathf.Clamp(view.height * EdgeFraction, EdgeMin, EdgeMax);
                var toTop = view.yMax - p.y;
                var toBottom = p.y - view.yMin;
                float dir = toBottom < edge ? 1f : toTop < edge ? -1f : 0f;
                if (dir == 0f) return false;
                var speed = Speed * (1f - Mathf.Min(toTop, toBottom) / edge);
                var max = Mathf.Max(0f, rect.height - view.height);
                pos.y = Mathf.Clamp(pos.y + dir * speed * dt, 0f, max);
            }
            if (pos == content.anchoredPosition) return false;
            content.anchoredPosition = pos;
            return true;
        }
    }
}
