using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The gesture half of a <c>drag=</c> source (spec 2026-11-16-drag-drop §7.1), on the control's own
    /// GameObject. uGUI looks for a drag handler from what was hit upward, and this one sits below any
    /// ScrollRect / Carousel / reorder driver the control lives in, so it hears every drag first;
    /// whatever is not a lift it hands up to them event by event through the shared
    /// <see cref="LiftGesture"/>. The session itself — lifting, hovering, dropping — is
    /// <see cref="DragSession"/>'s.
    ///
    /// <para>Installed as soon as a control declares <c>drag</c> in any variant (the hooks inside it
    /// resolve before its own setters run); inert while the kind is none.</para>
    /// </summary>
    internal sealed class DragSource : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, LiftGesture.IHost
    {
        // A finger moving more than this far off a single-axis scroller's axis is not scrolling it.
        private const float CrossAxisTan = 1.7320508f;   // tan 60°

        internal Control Owner;
        internal readonly LiftLook Look = new();
        private LiftGesture _gesture;
        private bool _handleWarned;

        internal LiftGesture Gesture => _gesture ??= new LiftGesture(this);

        /// <summary>The <c>lift</c> / <c>drop</c> signals, if any hook asked for them.</summary>
        internal LiftMarker Marker => GetComponent<LiftMarker>();

        // ───── uGUI events → the shared gesture ─────

        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData e) => Gesture.OnInitializePotentialDrag(e);

        void IBeginDragHandler.OnBeginDrag(PointerEventData e) => Gesture.OnBeginDrag(e);

        void IDragHandler.OnDrag(PointerEventData e) => Gesture.OnDrag(e);

        void IEndDragHandler.OnEndDrag(PointerEventData e) => Gesture.OnEndDrag(e);

        private void Update()
        {
            if (_gesture != null) _gesture.Tick(Time.unscaledDeltaTime);
        }

        internal void TickForTests(float dt) => Gesture.Tick(dt);

        // ───── LiftGesture.IHost ─────

        bool LiftGesture.IHost.TryPress(PointerEventData e, out float hold)
        {
            hold = 0f;
            if (Owner == null || Owner.DragKind == null) return false;
            // One at a time, and nothing while a drop waits for its answer (DND-D13 / D19).
            if (!DragSession.IsIdle) return false;
            if (!isActiveAndEnabled || !DragSession.IsInteractable(Owner)) return false;
            if (!string.IsNullOrEmpty(Owner.DragHandle) && !HitHandle(e)) return false;
            hold = ResolveHold(e);
            return true;
        }

        bool LiftGesture.IHost.Lift() => DragSession.Begin(this, Gesture.Pointer);

        void LiftGesture.IHost.Drag(PointerEventData e) => DragSession.OnDrag(this);

        void LiftGesture.IHost.Release() => DragSession.Release(this);

        void LiftGesture.IHost.Cancel() => DragSession.CancelSessionOf(this);

        // The session's runner owns the per-frame work (it must go on when this GameObject is switched off).
        void LiftGesture.IHost.Tick(float dt) { }

        bool LiftGesture.IHost.LiftsOnBegin(PointerEventData e) => CrossesOuterAxis(e);

        void LiftGesture.IHost.Forward<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn)
        {
            var parent = transform.parent;
            if (parent != null) ExecuteEvents.ExecuteHierarchy(parent.gameObject, e, fn);
        }

        // ───── hold ─────

        /// <summary>
        /// <c>dragHold="auto"</c> (spec §5.1, DND-D5): a mouse lifts on its first drag frame — nobody scrolls with
        /// a mouse drag; a finger waits only where a drag could also be a scroll, i.e. inside a scroller; a handle
        /// tells the two apart already. An authored duration applies to both.
        /// </summary>
        private float ResolveHold(PointerEventData e)
        {
            var authored = Owner.DragHold;
            if (authored >= 0f) return authored;
            if (!string.IsNullOrEmpty(Owner.DragHandle)) return 0f;
            if (!LiftGesture.IsTouch(e)) return 0f;
            return OuterScroller(out _, out _) ? LiftGesture.DefaultTouchHold : 0f;
        }

        /// <summary>
        /// Inside a single-axis scroller, a finger whose first movement is clearly across the axis (more than 60°
        /// off it) cannot be scrolling it: it lifts at once instead of waiting out the hold — drag an item up out of
        /// a horizontal bag. Only for <c>auto</c>; an authored hold is a hold.
        /// </summary>
        private bool CrossesOuterAxis(PointerEventData e)
        {
            if (Owner == null || Owner.DragHold >= 0f || !LiftGesture.IsTouch(e)) return false;
            if (!OuterScroller(out var horizontal, out var vertical) || horizontal == vertical) return false;
            var d = e.position - e.pressPosition;
            var along = horizontal ? Mathf.Abs(d.x) : Mathf.Abs(d.y);
            var across = horizontal ? Mathf.Abs(d.y) : Mathf.Abs(d.x);
            return across > along * CrossAxisTan;
        }

        /// <summary>
        /// The first drag consumer above this source — what a drag that is not a lift goes to. A ScrollRect knows its
        /// axes and a Carousel swipes sideways; anything else is a drag of unknown direction. Another drag source above
        /// is no scroller and is skipped.
        /// </summary>
        private bool OuterScroller(out bool horizontal, out bool vertical)
        {
            horizontal = vertical = false;
            for (var t = transform.parent; t != null; t = t.parent)
            {
                if (t.GetComponent<IBeginDragHandler>() is not { } handler) continue;
                switch (handler)
                {
                    case DragSource:
                        continue;
                    case ScrollRect sr:
                        horizontal = sr.horizontal;
                        vertical = sr.vertical;
                        return true;
                    case CarouselView:
                        horizontal = true;
                        return true;
                    default:
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Geometric, like <c>reorderHandle</c>: the handle is usually an icon, which is click-through. A handle
        /// that is not there warns once and the whole source lifts — the lint CLI (<c>PUI-DRAG-HANDLE-ID</c>) is
        /// where the author hears of it first.
        /// </summary>
        private bool HitHandle(PointerEventData e)
        {
            var handle = ReorderDriver.FindHandle(Owner, Owner.DragHandle);
            if (handle == null)
            {
                if (!_handleWarned)
                {
                    _handleWarned = true;
                    UILog.Warn(Owner,
                        $"[{PromptUGUI.Lint.DragDropRules.HandleCode}] dragHandle='{Owner.DragHandle}': no node with that " +
                        "id inside the drag source — the whole source lifts instead.");
                }
                return true;
            }
            var rt = handle is Control c ? c.LayoutHost : handle.RectTransform;
            return rt.gameObject.activeInHierarchy
                   && RectTransformUtility.RectangleContainsScreenPoint(rt, e.pressPosition, e.pressEventCamera);
        }

        private void OnDestroy()
        {
            Look.Kill();
            DragSession.OnSourceDestroyed(this);
        }
    }
}
