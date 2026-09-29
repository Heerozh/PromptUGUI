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
        private float _writtenY = float.NaN;

        internal IScrollTickHost Host { get; set; }

        internal bool IsDragging { get; private set; }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            // The editor ticks an [ExecuteAlways] ScrollRect too; a host's bind callbacks have no business running
            // in edit mode, and EditMode tests drive the list explicitly (VIR-P10).
            if (UnityEngine.Application.isPlaying) Host?.OnScrollLateUpdate();
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
            var y = content.anchoredPosition.y;
            var moved = y != _writtenY;
            _writtenY = y;
            return moved;
        }

        /// <summary>The list has just written Content's position itself — that is not user motion.</summary>
        internal void MarkWritten()
        {
            if (content != null) _writtenY = content.anchoredPosition.y;
        }

        /// <summary>
        /// Moves Content by <paramref name="dy"/> (positive = further down the list) without the move reading as a
        /// drag or a fling. While dragging, the drag origin moves along — the next <c>OnDrag</c> would otherwise put
        /// Content back — and the previous-position record is refreshed, so <c>LateUpdate</c>'s velocity estimate
        /// does not see a jump. The bar is written directly: <c>UpdateScrollbars</c> is private, and the
        /// <c>Scrollbar.value</c> setter would call back into <c>SetNormalizedPosition</c> and zero the velocity.
        /// </summary>
        internal void ShiftContentY(float dy)
        {
            if (content == null) return;
            if (dy != 0f)
            {
                var p = content.anchoredPosition;
                p.y += dy;
                content.anchoredPosition = p;
            }
            UpdateBounds();   // bounds depend on where Content is; normalizedPosition reads them
            if (IsDragging && dy != 0f)
            {
                m_ContentStartPosition.y += dy;
                UpdatePrevData();
            }
            RefreshVerticalBar();
            MarkWritten();
        }

        private void RefreshVerticalBar()
        {
            var bar = verticalScrollbar;
            if (bar == null || content == null || viewRect == null) return;
            var contentHeight = content.rect.height;
            bar.size = contentHeight > 0f ? Mathf.Clamp01(viewRect.rect.height / contentHeight) : 1f;
            bar.SetValueWithoutNotify(verticalNormalizedPosition);
        }
    }
}
