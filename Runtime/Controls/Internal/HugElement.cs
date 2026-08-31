using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The layout-group half of <c>height="hug"</c> (spec 2026-08-31-hug-reveal-flip-checked-design
    /// §1.4.3). Publishes "my content size, clamped" to the parent <c>LayoutGroup</c> as a rigid
    /// preferred size (<c>min = preferred</c>, <c>flexible = 0</c> — LGC-D17's "strictly N×N"
    /// contract, because a hug size is a computed constant, not a range).
    ///
    /// <para>Only attached where the node does not already answer for itself: a
    /// <c>&lt;ScrollList&gt;</c> (whose root carries no layout element at all), or any
    /// <c>clamp(min, hug, max)</c> (whose bounds have to be applied somewhere). A bare <c>hug</c> on
    /// <c>&lt;VStack&gt;</c> / <c>&lt;HStack&gt;</c> / <c>&lt;Grid&gt;</c> needs nothing — the group
    /// already reports its own preferred size, which is exactly what hug means.</para>
    ///
    /// <para><b>Priority 1</b> so it wins over the node's own <c>LayoutGroup</c> (priority 0) when
    /// both are present — that is the clamped case, where the bounds must be the answer the parent
    /// sees. It reads its content through <see cref="ContentSize"/> (supplied by the owning control's
    /// <see cref="IHugContent"/>) rather than <c>LayoutUtility</c>, which would read this component
    /// back and recurse.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    internal sealed class HugElement : UIBehaviour, ILayoutElement
    {
        private struct AxisSpec
        {
            public bool On;
            public float Min;
            public float Max;
        }

        private AxisSpec _x;
        private AxisSpec _y;

        /// <summary>Content size per axis (0 = X, 1 = Y). Set by <c>ApplyCommon</c> every pass.</summary>
        internal Func<int, float> ContentSize;

        internal void SetAxis(int axis, bool on, float min, float max, Func<int, float> content)
        {
            var spec = new AxisSpec { On = on, Min = min, Max = max };
            ref var slot = ref (axis == 0 ? ref _x : ref _y);
            ContentSize = content;
            // Same LGC-D18 contract as ClampFitter: ApplyCommon replays this on every ReSolve, and a
            // replay that changes nothing must dirty nothing.
            if (Same(slot, spec)) return;
            slot = spec;
            SetDirty();
        }

        private static bool Same(in AxisSpec a, in AxisSpec b) =>
            a.On == b.On && a.Min == b.Min && a.Max == b.Max;

        internal void ClearAxis(int axis) =>
            SetAxis(axis, false, float.NegativeInfinity, float.PositiveInfinity, ContentSize);

        internal bool AxisEnabled(int axis) => axis == 0 ? _x.On : _y.On;

        private float Resolve(int axis)
        {
            var spec = axis == 0 ? _x : _y;
            if (!spec.On) return -1f;
            var content = ContentSize != null ? ContentSize(axis) : 0f;
            return Mathf.Max(0f, Mathf.Clamp(content, spec.Min, spec.Max));
        }

        // Nothing to precompute: Resolve reads the owning control's content, which uGUI has already
        // calculated bottom-up by the time a parent group reads these properties.
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        // -1 is uGUI's "no opinion" sentinel — an axis without hug must not constrain anything.
        public float minWidth => Resolve(0);
        public float preferredWidth => Resolve(0);
        public float flexibleWidth => _x.On ? 0f : -1f;
        public float minHeight => Resolve(1);
        public float preferredHeight => Resolve(1);
        public float flexibleHeight => _y.On ? 0f : -1f;

        // Unity 6.x's ILayoutElement also asks for a maximum. A clamped hug already reports its
        // upper bound through min == preferred, so there is nothing left to cap: stay at the "no
        // opinion" sentinel and behave exactly like the LayoutElement path this replaces.
        public float maxWidth => -1f;
        public float maxHeight => -1f;

        public int layoutPriority => 1;

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
    }
}
