using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>Which parent edge a clamped axis hugs: Low = left / bottom, High = right / top.</summary>
    internal enum ClampAlign { Low, Center, High }

    /// <summary>
    /// Free-positioning half of <c>width="clamp(min, N%, max)"</c> (spec 2026-08-30-clamp-size-design
    /// §5.1 / §6.4). <c>ApplyCommon</c> writes the plain fractional baseline (anchor sub-range, pivot 0.5,
    /// margin offsets) and pushes the spec in here; this component then owns the axis: on every layout
    /// pass it reads the parent's rect, computes <c>box = clamp(f·P, min, max)</c>, insets the margins
    /// inside that box, hugs the anchor's edge, and writes <c>offsetMin / offsetMax</c> (pivot-agnostic).
    /// Unclamped it lands on exactly what the baseline already said, so it is a pure function of
    /// (spec, parent rect) and idempotent across ReSolve.
    ///
    /// <para>Same shape as uGUI's own <c>AspectRatioFitter</c>: <c>ILayoutSelfController</c>, dirtied by
    /// <c>OnRectTransformDimensionsChange</c>, written by <c>LayoutRebuilder</c>. The callback itself
    /// NEVER writes the RectTransform — writing there re-enters the setter's back-solving and loops
    /// against <c>ApplyCommon</c> (see <c>SafeAreaTests.Tracker_does_not_subscribe_to_rect_transform_dimensions_change</c>);
    /// marking dirty only defers the write to the rebuild pass, which is the whole point.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    internal sealed class ClampFitter : UIBehaviour, ILayoutSelfController
    {
        private struct AxisSpec
        {
            public bool On;
            public float Fraction;
            public float Min;
            public float Max;
            public float MarginLow;
            public float MarginHigh;
            public ClampAlign Align;
        }

        private AxisSpec _x;
        private AxisSpec _y;
        private bool _delayedDirty;
        private bool _selfWriting;

        internal void SetAxis(int axis, bool on, float fraction, float min, float max,
                              float marginLow, float marginHigh, ClampAlign align)
        {
            var spec = new AxisSpec
            {
                On = on,
                Fraction = fraction,
                Min = min,
                Max = max,
                MarginLow = marginLow,
                MarginHigh = marginHigh,
                Align = align,
            };
            ref var slot = ref (axis == 0 ? ref _x : ref _y);
            // Dirty only on a real spec change. ApplyCommon replays this on every ReSolve; when the
            // baseline it just wrote differs from our last output the RectTransform callback dirties
            // us anyway, and when it doesn't there is nothing to recompute (LGC-D18: a replay that
            // changes nothing must dirty nothing).
            if (Same(slot, spec)) return;
            slot = spec;
            SetDirty();
        }

        private static bool Same(in AxisSpec a, in AxisSpec b) =>
            a.On == b.On && a.Fraction == b.Fraction && a.Min == b.Min && a.Max == b.Max
            && a.MarginLow == b.MarginLow && a.MarginHigh == b.MarginHigh && a.Align == b.Align;

        internal void ClearAxis(int axis) =>
            SetAxis(axis, false, 0f, float.NegativeInfinity, float.PositiveInfinity, 0f, 0f, ClampAlign.Low);

        internal bool AxisEnabled(int axis) => axis == 0 ? _x.On : _y.On;

        public void SetLayoutHorizontal() => Apply(0);
        public void SetLayoutVertical() => Apply(1);

        // Spec §5.1:
        //   box  = clamp(f·P, min, max)         open bounds are ±Infinity → identity
        //   W    = box − (lo + hi)              margins inset INSIDE the box, as with plain %
        //   low  = lo | P − box + lo | (P − box)/2 + lo   per Low / High / Center alignment
        //   offsetMin = low − a0·P, offsetMax = (low + W) − a1·P
        // Offsets rather than sizeDelta / anchoredPosition: pivot-agnostic, so an author pivot= keeps
        // working. Unclamped this reproduces MarginResolver's stretch branch exactly (offsetMin = lo,
        // offsetMax = −hi), which is what makes the pass idempotent against ApplyCommon's baseline.
        private void Apply(int axis)
        {
            var spec = axis == 0 ? _x : _y;
            if (!spec.On) return;
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;

            var p = parent.rect.size[axis];
            var box = Mathf.Clamp(spec.Fraction * p, spec.Min, spec.Max);
            var w = box - (spec.MarginLow + spec.MarginHigh);
            float low;
            switch (spec.Align)
            {
                case ClampAlign.Low: low = spec.MarginLow; break;
                case ClampAlign.High: low = p - box + spec.MarginLow; break;
                default: low = (p - box) * 0.5f + spec.MarginLow; break;
            }
            var newMin = low - rt.anchorMin[axis] * p;
            var newMax = low + w - rt.anchorMax[axis] * p;

            var curMin = rt.offsetMin;
            var curMax = rt.offsetMax;
            if (Mathf.Approximately(curMin[axis], newMin) && Mathf.Approximately(curMax[axis], newMax))
                return;
            curMin[axis] = newMin;
            curMax[axis] = newMax;

            // Our own writes fire OnRectTransformDimensionsChange synchronously; don't re-dirty for them.
            _selfWriting = true;
            try
            {
                rt.offsetMin = curMin;
                rt.offsetMax = curMax;
            }
            finally
            {
                _selfWriting = false;
            }
        }

        private void SetDirty()
        {
            if (!IsActive()) return;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetDirty();
        }

        protected override void OnTransformParentChanged() => SetDirty();

        protected override void OnRectTransformDimensionsChange()
        {
            if (_selfWriting) return;
            if (CanvasUpdateRegistry.IsRebuildingLayout()) _delayedDirty = true;
            else SetDirty();
        }

        private void Update()
        {
            if (!_delayedDirty) return;
            _delayedDirty = false;
            SetDirty();
        }
    }
}
