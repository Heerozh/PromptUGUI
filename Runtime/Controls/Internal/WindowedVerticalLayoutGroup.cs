using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Content's layout group in a virtual <c>&lt;ScrollList&gt;</c> (spec 2026-09-29-scrolllist-virtualization §5.3).
    /// It lays out only the realized window of rows, and stands in for everything outside the window with two
    /// float extents: <see cref="Leading"/> (the items above the window) and <see cref="Trailing"/> (the items
    /// below it). Both are added to the minimum AND the preferred height, so the ContentSizeFitter sizes Content
    /// to the whole virtual list, and the base class's <c>surplusSpace</c> stays 0 — <c>childForceExpandHeight</c>
    /// never hands that space to a row. With both at 0 it is a plain VerticalLayoutGroup.
    /// <para>Floats, not <c>padding</c>: <c>RectOffset</c> is int, and rounding the offset would make the rows
    /// shimmer by up to half a unit whenever the window moves.</para>
    /// </summary>
    internal sealed class WindowedVerticalLayoutGroup : VerticalLayoutGroup
    {
        private float _leading;
        private float _trailing;

        internal float Leading
        {
            get => _leading;
            set
            {
                if (_leading == value) return;
                _leading = value;
                SetDirty();
            }
        }

        internal float Trailing
        {
            get => _trailing;
            set
            {
                if (_trailing == value) return;
                _trailing = value;
                SetDirty();
            }
        }

        public override void CalculateLayoutInputVertical()
        {
            base.CalculateLayoutInputVertical();
            var extra = _leading + _trailing;
            if (extra == 0f) return;
#if UNITY_6000_7_OR_NEWER
            // uGUI in 6000.7 added a max size to the layout inputs (SetLayoutInputForAxis(min, max, preferred,
            // flexible, axis)); the extents widen it like the other two — +∞ stays +∞.
            SetLayoutInputForAxis(minHeight + extra, maxHeight + extra, preferredHeight + extra, flexibleHeight, 1);
#else
            SetLayoutInputForAxis(minHeight + extra, preferredHeight + extra, flexibleHeight, 1);
#endif
        }

        public override void SetLayoutVertical()
        {
            base.SetLayoutVertical();
            if (_leading == 0f) return;
            for (var i = 0; i < rectChildren.Count; i++)
            {
                var child = rectChildren[i];
                var p = child.anchoredPosition;
                p.y -= _leading;
                child.anchoredPosition = p;
            }
        }
    }
}
