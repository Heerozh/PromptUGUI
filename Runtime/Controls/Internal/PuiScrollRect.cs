using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>What a <see cref="PuiScrollRect"/> tells the list that owns it.</summary>
    internal interface IScrollTickHost
    {
        /// <summary>Right after <c>ScrollRect.LateUpdate</c> — inertia and elasticity have moved Content by then. Play mode only.</summary>
        public void OnScrollLateUpdate();

        public void OnScrollEnabled();
    }

    /// <summary>
    /// The ScrollRect every <c>&lt;ScrollList&gt;</c> carries (spec 2026-09-29-scrolllist-virtualization §5.8). It
    /// behaves exactly like its base unless the list uses the extras: a hook right after <c>LateUpdate</c>, whether a
    /// drag is in progress (<c>m_Dragging</c> is private), whether Content was moved by someone other than the list,
    /// and a way to move Content that neither reads as a drag or fling nor leaves the scrollbar behind.
    /// </summary>
    // Same as the base class; spelled out rather than trusting attribute inheritance.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    internal sealed class PuiScrollRect : ScrollRect
    {
        private Vector2 _written = new Vector2(float.NaN, float.NaN);

        internal IScrollTickHost Host { get; set; }

        internal bool IsDragging { get; private set; }

        protected override void LateUpdate()
        {
            var barsShown = ExpandingBarsShown();
            base.LateUpdate();
            // The editor ticks an [ExecuteAlways] ScrollRect too; a host's bind callbacks have no business running
            // in edit mode, and EditMode tests drive the list explicitly (VIR-P10).
            if (!UnityEngine.Application.isPlaying) return;
            // base.LateUpdate shows or hides a bar at its very end. With AutoHideAndExpandViewport that resizes the
            // viewport — in the canvas pass, after the host placed Content for this frame, so a list following its end
            // would render a frame short of it (the rows rewrap). Lay it out now: the host sees the final viewport.
            if (ExpandingBarsShown() != barsShown) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            Host?.OnScrollLateUpdate();
        }

        // The showing bars that take their room out of the viewport, as a bit mask.
        private int ExpandingBarsShown()
        {
            var shown = 0;
            if (verticalScrollbar != null && verticalScrollbar.gameObject.activeSelf
                && verticalScrollbarVisibility == ScrollbarVisibility.AutoHideAndExpandViewport) shown |= 1;
            if (horizontalScrollbar != null && horizontalScrollbar.gameObject.activeSelf
                && horizontalScrollbarVisibility == ScrollbarVisibility.AutoHideAndExpandViewport) shown |= 2;
            return shown;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Host?.OnScrollEnabled();
        }

        protected override void OnDisable()
        {
            IsDragging = false;
            base.OnDisable();
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            base.OnBeginDrag(eventData);
            // Mirrors the base: only a left-button drag on an active ScrollRect starts one.
            if (eventData.button == PointerEventData.InputButton.Left && IsActive()) IsDragging = true;
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            base.OnEndDrag(eventData);
            if (eventData.button == PointerEventData.InputButton.Left) IsDragging = false;
        }

        /// <summary>
        /// True when Content has moved since the list last wrote its position — the user dragged, wheeled or
        /// dragged the bar, or inertia / elasticity carried it (VIR-P5). Resets the mark.
        /// </summary>
        internal bool ConsumeUserMotion()
        {
            if (content == null) return false;
            var p = content.anchoredPosition;
            var moved = p.x != _written.x || p.y != _written.y;
            _written = p;
            return moved;
        }

        /// <summary>The list has just written Content's position itself — that is not user motion.</summary>
        internal void MarkWritten()
        {
            if (content != null) _written = content.anchoredPosition;
        }

        /// <summary>
        /// Moves Content by <paramref name="delta"/> without the move reading as a drag or a fling. While dragging, the
        /// drag origin moves along — the next <c>OnDrag</c> would otherwise put Content back — and the previous-position
        /// record is refreshed, so <c>LateUpdate</c>'s velocity estimate does not see a jump. The bars are written
        /// directly: <c>UpdateScrollbars</c> is private, and the <c>Scrollbar.value</c> setter would call back into
        /// <c>SetNormalizedPosition</c> and zero the velocity.
        /// </summary>
        internal void ShiftContent(Vector2 delta)
        {
            if (content == null) return;
            var moves = delta.x != 0f || delta.y != 0f;
            if (moves) content.anchoredPosition += delta;
            UpdateBounds();   // bounds depend on where Content is; normalizedPosition reads them
            if (IsDragging && moves)
            {
                m_ContentStartPosition += delta;
                UpdatePrevData();
            }
            RefreshBars();
            MarkWritten();
        }

        /// <summary><see cref="ShiftContent"/> down the list (positive <paramref name="dy"/> = further down).</summary>
        internal void ShiftContentY(float dy) => ShiftContent(new Vector2(0f, dy));

        private void RefreshBars()
        {
            if (content == null || viewRect == null) return;
            var view = viewRect.rect.size;
            var size = content.rect.size;
            var vertical = verticalScrollbar;
            if (vertical != null)
            {
                vertical.size = size.y > 0f ? Mathf.Clamp01(view.y / size.y) : 1f;
                vertical.SetValueWithoutNotify(verticalNormalizedPosition);
            }
            var horizontal = horizontalScrollbar;
            if (horizontal != null)
            {
                horizontal.size = size.x > 0f ? Mathf.Clamp01(view.x / size.x) : 1f;
                horizontal.SetValueWithoutNotify(horizontalNormalizedPosition);
            }
        }
    }
}
