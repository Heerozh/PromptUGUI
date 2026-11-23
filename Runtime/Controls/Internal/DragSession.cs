using System;
using System.Collections.Generic;
using LitMotion;
using PromptUGUI.Application;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The one drag-and-drop session (spec 2026-11-16-drag-drop §4, §7): lifting a <c>drag=</c> source,
    /// arming the <c>drop=</c> targets that would take it, following the pointer, resolving the hover, and
    /// on release putting the structure back, telling C#, and flying the result into place.
    ///
    /// <para>Static because a drag crosses Screens — the source in one, the target in another — and only
    /// one is ever live (DND-D13). The per-frame work runs from a hidden runner, not from the source: the
    /// source may be switched off mid-drag, and noticing that is part of the job.</para>
    /// </summary>
    internal static class DragSession
    {
        internal const string PlaceholderName = "DragPlaceholder";

        private enum State
        {
            Idle,
            Dragging,   // lifted and following the pointer
            Pending,    // released onto a target, waiting for Accept / Reject (§7.10)
        }

        private static State s_state;
        private static Carry s_carry;
        private static DragSource s_source;
        private static PointerEventData s_pointer;
        private static DragInfo s_info;
        private static DropEvent s_pending;
        private static DropTargetMarker s_pendingTarget;
        private static float s_pendingElapsed;
        private static CarouselView s_carousel;

        private static readonly List<DropTargetMarker> s_targets = new();
        private static readonly List<DropTargetMarker> s_armed = new();
        private static readonly List<DropTargetMarker> s_denied = new();
        private static readonly HashSet<DropTargetMarker> s_judged = new();
        private static bool s_rejudge;
        private static DropTargetMarker s_hover;
        private static bool s_hoverDenied;

        private static Subject<DragInfo> s_lifted = new();
        private static Subject<DragInfo> s_hovered = new();
        private static Subject<DropEvent> s_dropped = new();
        private static Subject<DragInfo> s_ended = new();

        internal static Observable<DragInfo> Lifted => s_lifted;
        internal static Observable<DragInfo> Hovered => s_hovered;
        internal static Observable<DropEvent> Dropped => s_dropped;
        internal static Observable<DragInfo> Ended => s_ended;

        internal static bool IsIdle => s_state == State.Idle;
        internal static bool IsDragging => s_state == State.Dragging;
        internal static bool IsPending => s_state == State.Pending;

        /// <summary>The live drag, null outside one (a pending drop is not a drag any more).</summary>
        internal static DragInfo Current => s_state == State.Dragging ? s_info : null;

        /// <summary>Tests replace the raycast: screen point → what the pointer is over.</summary>
        internal static Func<Vector2, GameObject> HitTestForTests;

        // ───── what the lift took out of the layout ─────

        /// <summary>
        /// The source while it is carried: out of its layout group's flow behind a same-size placeholder, under a
        /// lift layer, following the pointer — and everything needed to put it back.
        /// </summary>
        internal sealed class Carry
        {
            internal readonly DragSource Source;
            internal readonly Control Ctrl;
            internal readonly RectTransform Host;
            internal readonly Camera Cam;
            internal RectTransform Parent;
            internal Vector2 LiftAnchored;
            internal Vector2 LiftLocal;

            /// <summary>Where it rests: its layout slot, or its own geometry in a free parent.</summary>
            internal Vector2 Home;

            /// <summary>Out of the flow right now (placeholder in).</summary>
            internal bool Out { get; private set; }

            private RectTransform _placeholder;
            private LayoutElement _le;
            private bool _prevIgnore;
            private bool _inFlowGroup;
            private bool _homeKnown;
            private LiftLayer.Token _layer;
            private MotionHandle _park;

            internal Carry(DragSource source, Camera cam)
            {
                Source = source;
                Ctrl = source.Owner;
                Host = Ctrl.LayoutHost;
                Cam = cam;
                Parent = Host.parent as RectTransform;
            }

            internal void TakeOut()
            {
                if (Out || Host == null) return;
                Parent = Host.parent as RectTransform;
                var group = Parent != null ? Parent.GetComponent<LayoutGroup>() : null;
                var le = Host.GetComponent<LayoutElement>();
                _inFlowGroup = group != null && (le == null || !le.ignoreLayout);
                if (!_inFlowGroup && !_homeKnown)
                {
                    Home = Host.anchoredPosition;
                    _homeKnown = true;
                }
                if (_inFlowGroup)
                {
                    _placeholder = FlowPlaceholder.Create(Host, Parent, PlaceholderName);
                    _placeholder.SetSiblingIndex(Host.GetSiblingIndex() + 1);
                    // No LayoutElement → one with all -1: the group then reads the element's own layout
                    // properties, so the sizing semantics do not change (same as ReorderDriver).
                    _le = le != null ? le : Host.gameObject.AddComponent<LayoutElement>();
                    _prevIgnore = _le.ignoreLayout;
                    _le.ignoreLayout = true;
                }
                Out = true;
            }

            /// <summary>Back into the flow. <paramref name="pin"/> keeps the picture where it is (a flight starts there).</summary>
            internal void PutBack(bool pin)
            {
                if (!Out) return;
                Out = false;
                StopPark();
                var visual = Host != null ? Host.anchoredPosition : default;
                FlowPlaceholder.Remove(_placeholder);
                _placeholder = null;
                if (_le != null) _le.ignoreLayout = _prevIgnore;
                _le = null;
                if (Host == null) return;
                if (_inFlowGroup)
                {
                    if (Parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(Parent);
                    Home = Host.anchoredPosition;
                }
                else
                {
                    Host.anchoredPosition = Home;
                }
                if (pin) Host.anchoredPosition = visual;
            }

            internal void Raise()
            {
                if (_layer == null && Host != null) _layer = LiftLayer.Raise(Host, UI.Drag.SortingOrder);
            }

            internal void Lower()
            {
                LiftLayer.Lower(_layer);
                _layer = null;
            }

            /// <summary>Back home at once, layer off.</summary>
            internal void SnapHome()
            {
                if (Host != null) Host.anchoredPosition = Home;
                Lower();
            }

            /// <summary>
            /// Home, shown flying there from <paramref name="fromScreen"/> — the lift it already has is handed over
            /// to the flight rather than taken off and put on again in the same frame.
            /// </summary>
            internal void FlyHomeFrom(Rect fromScreen, float duration)
            {
                var layer = _layer;
                _layer = null;
                if (Host == null)
                {
                    LiftLayer.Lower(layer);
                    return;
                }
                Host.anchoredPosition = Home;
                SettleFlight.FlyFrom(Host, fromScreen, morph: false, duration, UI.Drag.SortingOrder, layer);
            }

            /// <summary>Wait over <paramref name="target"/>'s centre (§7.10).</summary>
            internal void Park(RectTransform target, float duration)
            {
                if (Host == null || Parent == null || target == null) return;
                var targetCam = LiftLayer.CameraOf(target);
                var centre = LiftLayer.ScreenRect(target, targetCam).center;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Parent, centre, Cam, out var local)) return;
                var size = Host.rect.size;
                var pivot = Host.pivot;
                var s = Host.localScale;
                var centreFromPivot = new Vector2((0.5f - pivot.x) * size.x * s.x, (0.5f - pivot.y) * size.y * s.y);
                // anchoredPosition and the pivot move together; find the anchored value that puts the centre there.
                var nowCentre = (Vector2)Parent.InverseTransformPoint(Host.TransformPoint(Host.rect.center));
                var to = Host.anchoredPosition + (local - centreFromPivot) - (nowCentre - centreFromPivot);
                StopPark();
                if (duration <= 0f || !UnityEngine.Application.isPlaying)
                {
                    Host.anchoredPosition = to;
                    return;
                }
                _park = LMotion.Create(Host.anchoredPosition, to, duration)
                    .WithEase(Ease.OutCubic)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(Host, static (v, r) => { if (r != null) r.anchoredPosition = v; });
            }

            private void StopPark()
            {
                if (_park.IsActive()) _park.TryCancel();
            }

            internal Rect ScreenRect() => Host != null ? LiftLayer.ScreenRect(Host, Cam) : default;
        }

        // ───── registry ─────

        internal static void Register(DropTargetMarker m)
        {
            if (m == null || s_targets.Contains(m)) return;
            s_targets.Add(m);
            if (s_state == State.Dragging) s_rejudge = true;
        }

        internal static void Unregister(DropTargetMarker m)
        {
            s_targets.Remove(m);
            s_armed.Remove(m);
            s_denied.Remove(m);
            s_judged.Remove(m);
            if (ReferenceEquals(s_hover, m))
            {
                s_hover = null;
                s_hoverDenied = false;
                if (s_info != null && s_state == State.Dragging)
                {
                    SetTarget(s_info, null);
                    Emit(s_hovered, s_info);
                }
            }
            if (ReferenceEquals(s_pendingTarget, m)) s_pendingTarget = null;
        }

        /// <summary>What a target takes, or what it stands for, changed: judge it again next frame.</summary>
        internal static void Invalidate(DropTargetMarker m)
        {
            if (s_state != State.Dragging || m == null) return;
            s_judged.Remove(m);
            s_rejudge = true;
        }

        // ───── lift ─────

        internal static bool Begin(DragSource src, PointerEventData e)
        {
            if (s_state != State.Idle || src == null || e == null) return false;
            var ctrl = src.Owner;
            if (ctrl == null || ctrl.GameObject == null || ctrl.DragKind == null) return false;
            SettleFlight.Complete(ctrl.LayoutHost);   // a flight still landing it ends here

            var carry = new Carry(src, e.pressEventCamera);
            carry.TakeOut();
            carry.Raise();
            carry.LiftAnchored = carry.Host.anchoredPosition;
            if (carry.Parent == null
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(carry.Parent, e.pressPosition, carry.Cam, out carry.LiftLocal))
                carry.LiftLocal = Vector2.zero;

            s_state = State.Dragging;
            s_source = src;
            s_pointer = e;
            s_carry = carry;
            s_info = new DragInfo
            {
                Kind = ctrl.DragKind,
                Source = ctrl,
                Data = ctrl.DragData,
                SourceScreen = UI.OwnerScreenOf(ctrl),
                SourceIndex = RowIndexOf(ctrl),
                ScreenPosition = e.position,
            };

            EndPress(e);
            // The user picked it up (spec 2026-11-23-ui-sfx-cues §7.11) — before the lift hooks and OnLift. Not when it
            // went non-interactable during the hold: TryPress let the press through, SourceAlive takes it back next frame.
            if (IsInteractable(ctrl)) ctrl.RaiseSfx(PromptUGUI.Lint.SfxRules.Lift, kind: SfxKind);
            // Hooks first (an authored <Animation on="lift"> starts now); the default look only for a source
            // with no lift hook of its own — the two must not both write localScale.
            var marker = src.Marker;
            if (marker != null) marker.Lift();
            if (marker == null || !marker.HasLiftHook) src.Look.Apply(carry.Host);
            s_carousel = carry.Host.GetComponentInParent<CarouselView>();
            if (s_carousel != null) s_carousel.HeldByDrag = true;

            JudgeAll();
            Runner.Ensure(listenForEscape: true);
            Emit(s_lifted, s_info);
            return true;
        }

        /// <summary>
        /// What uGUI does to the pressed object when a drag begins (P12): it is let go — a long-pressed <c>&lt;Btn&gt;</c>
        /// must not keep its Pressed look while it flies about, nor click on release.
        /// </summary>
        private static void EndPress(PointerEventData e)
        {
            e.eligibleForClick = false;
            var pressed = e.pointerPress;
            if (pressed == null) return;
            e.pointerPress = null;
            e.rawPointerPress = null;
            ExecuteEvents.Execute(pressed, e, ExecuteEvents.pointerUpHandler);
        }

        // ───── arming ─────

        private enum Verdict
        {
            None,
            Armed,
            Denied,
        }

        private static void JudgeAll()
        {
            s_judged.Clear();
            s_rejudge = false;
            foreach (var m in s_targets.ToArray()) Judge(m);
        }

        private static void Judge(DropTargetMarker m)
        {
            if (m == null) return;
            s_judged.Add(m);
            Apply(m, VerdictOf(m));
        }

        private static Verdict VerdictOf(DropTargetMarker m)
        {
            var owner = m.Owner;
            if (owner == null || owner.GameObject == null || s_info == null) return Verdict.None;
            if (!m.Accepts(s_info.Kind) || !m.gameObject.activeInHierarchy) return Verdict.None;
            var host = s_carry.Host;
            // Its own container, and anything inside the carried thing, never takes it (P19).
            if (host != null && (host.IsChildOf(m.transform) || m.transform.IsChildOf(host))) return Verdict.None;
            var screen = UI.OwnerScreenOf(owner);
            if (screen != null && screen.IsClosing) return Verdict.None;
            if (!IsInteractable(owner)) return Verdict.Denied;
            var filter = owner.CanDrop;
            if (filter == null) return Verdict.Armed;

            // The filter sees the drag with Target set to the target it is asked about.
            bool ok;
            var info = s_info;
            var saved = (info.Target, info.TargetData, info.TargetScreen, info.TargetIndex);
            SetTarget(info, m);
            try { ok = filter(info); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ok = false;
            }
            finally
            {
                (info.Target, info.TargetData, info.TargetScreen, info.TargetIndex) = saved;
            }
            return ok ? Verdict.Armed : Verdict.Denied;
        }

        private static void Apply(DropTargetMarker m, Verdict v)
        {
            var armed = s_armed.Contains(m);
            var denied = s_denied.Contains(m);
            switch (v)
            {
                case Verdict.Armed:
                    if (denied) s_denied.Remove(m);
                    if (armed) return;
                    s_armed.Add(m);
                    if (!denied) m.SetHit(true);
                    m.SetArmed(true);
                    return;
                case Verdict.Denied:
                    if (armed)
                    {
                        s_armed.Remove(m);
                        m.SetArmed(false);
                    }
                    if (denied) return;
                    s_denied.Add(m);
                    if (!armed) m.SetHit(true);
                    return;
                default:
                    if (armed)
                    {
                        s_armed.Remove(m);
                        m.SetArmed(false);
                    }
                    if (denied) s_denied.Remove(m);
                    if (armed || denied) m.SetHit(false);
                    if (ReferenceEquals(s_hover, m)) SetHover(null, false);
                    return;
            }
        }

        /// <summary>A control, and every CanvasGroup above it, lets the pointer act on it.</summary>
        internal static bool IsInteractable(Control c)
        {
            if (c == null || c.GameObject == null || !c.PeekInteractable) return false;
            for (var t = c.GameObject.transform.parent; t != null; t = t.parent)
            {
                var g = t.GetComponent<CanvasGroup>();
                if (g == null) continue;
                if (!g.interactable) return false;
                if (g.ignoreParentGroups) break;
            }
            return true;
        }

        // ───── per frame ─────

        internal static void TickForTests(float dt) => Tick(dt);

        internal static void Tick(float dt)
        {
            switch (s_state)
            {
                case State.Dragging:
                    TickDragging(dt);
                    return;
                case State.Pending:
                    TickPending(dt);
                    return;
            }
        }

        private static void TickDragging(float dt)
        {
            if (!SourceAlive())
            {
                CancelDrag();
                return;
            }
            if (s_rejudge)
            {
                s_rejudge = false;
                foreach (var m in s_targets.ToArray())
                    if (m != null && !s_judged.Contains(m)) Judge(m);
            }
            Follow();
            if (AutoScroll(dt)) Follow();
            UpdateHover();
        }

        internal static void OnDrag(DragSource src)
        {
            if (s_state != State.Dragging || src != s_source) return;
            Follow();
            UpdateHover();
        }

        private static bool SourceAlive()
        {
            var c = s_carry;
            if (c == null || c.Host == null || s_source == null || !s_source.isActiveAndEnabled) return false;
            var ctrl = c.Ctrl;
            return ctrl.GameObject != null && ctrl.GameObject.activeInHierarchy && ctrl.DragKind != null
                   && IsInteractable(ctrl);
        }

        private static void Follow()
        {
            var c = s_carry;
            if (c?.Host == null || c.Parent == null || s_pointer == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(c.Parent, s_pointer.position, c.Cam, out var local))
                return;
            c.Host.anchoredPosition = c.LiftAnchored + (local - c.LiftLocal);
            s_info.ScreenPosition = s_pointer.position;
        }

        // The list under the pointer that holds somewhere this drag could go scrolls when the pointer nears its edge.
        private static readonly List<ScrollList> s_lists = new();

        private static bool AutoScroll(float dt)
        {
            if (s_pointer == null) return false;
            s_lists.Clear();
            CollectLists(s_armed);
            CollectLists(s_denied);
            var moved = false;
            foreach (var list in s_lists)
            {
                var viewport = list.ViewportRect;
                if (viewport == null || !viewport.gameObject.activeInHierarchy) continue;
                var cam = LiftLayer.CameraOf(viewport);
                if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, s_pointer.position, cam)) continue;
                moved |= EdgeAutoScroller.Step(viewport, list.ContentRect, list.IsHorizontal, s_pointer.position, cam, dt);
            }
            return moved;
        }

        private static void CollectLists(List<DropTargetMarker> markers)
        {
            foreach (var m in markers)
            {
                if (m == null) continue;
                var content = m.GetComponentInParent<ScrollListContentMarker>();
                if (content == null || content.Owner == null || s_lists.Contains(content.Owner)) continue;
                s_lists.Add(content.Owner);
            }
        }

        // ───── hover ─────

        private static readonly List<RaycastResult> s_hits = new();

        private static void UpdateHover()
        {
            if (s_pointer == null) return;
            var top = TopHit(s_pointer.position);
            var m = HoveredTarget(top, out var denied);
            SetHover(m, denied);
        }

        private static GameObject TopHit(Vector2 screen)
        {
            if (HitTestForTests != null) return HitTestForTests(screen);
            var es = EventSystem.current;
            if (es == null) return null;
            // The pointer's own event data: its position is the screen point, and it carries the display index.
            s_hits.Clear();
            es.RaycastAll(s_pointer, s_hits);
            return s_hits.Count > 0 ? s_hits[0].gameObject : null;
        }

        /// <summary>
        /// The target under the topmost hit (§7.4): walking up from it, the first one that takes this kind —
        /// one for another kind is see-through; one excluded from this drag (the source's own container) too.
        /// </summary>
        private static DropTargetMarker HoveredTarget(GameObject top, out bool denied)
        {
            denied = false;
            for (var t = top != null ? top.transform : null; t != null; t = t.parent)
            {
                var m = t.GetComponent<DropTargetMarker>();
                if (m == null || !m.Accepts(s_info.Kind)) continue;
                if (s_armed.Contains(m)) return m;
                if (s_denied.Contains(m))
                {
                    denied = true;
                    return m;
                }
            }
            return null;
        }

        private static void SetHover(DropTargetMarker m, bool denied)
        {
            if (ReferenceEquals(m, s_hover) && denied == s_hoverDenied) return;
            var old = s_hover;
            s_hover = m;
            s_hoverDenied = denied;
            if (old != null) old.SetHover(DropHover.None);
            if (m != null) m.SetHover(denied ? DropHover.Deny : DropHover.Over);
            if (s_info == null) return;
            SetTarget(s_info, m);
            Emit(s_hovered, s_info);
        }

        private static void SetTarget(DragInfo info, DropTargetMarker m)
        {
            var owner = m != null ? m.Owner : null;
            info.Target = owner;
            info.TargetData = owner?.DragData;
            info.TargetScreen = owner != null ? UI.OwnerScreenOf(owner) : null;
            info.TargetIndex = owner != null ? RowIndexOf(owner) : -1;
        }

        // ───── release ─────

        internal static void Release(DragSource src)
        {
            if (s_state != State.Dragging || src != s_source) return;
            Follow();
            UpdateHover();
            var carry = s_carry;
            var target = s_hover != null && !s_hoverDenied ? s_hover : null;
            var ghost = carry.ScreenRect();

            // 1. Structure first: whatever answers the drop sees a normal tree and may push into it (P9).
            carry.PutBack(pin: true);
            s_state = State.Idle;

            if (target == null)
            {
                Settle(carry, null, null, ghost, accepted: false);
                Finish(DragResult.Returned, null);
                return;
            }

            // 2. Tell C#.
            var drop = new DropEvent();
            s_info.CopyTo(drop);
            SetTarget(drop, target);
            drop.GhostRect = ghost;
            drop.Duration = carry.Ctrl.DragDuration;
            drop.InCallback = true;
            try { target.Owner?.RaiseDrop(drop); }
            catch (Exception ex) { Debug.LogException(ex); }
            Emit(s_dropped, drop);
            drop.InCallback = false;

            // 3. Land, go back, or wait.
            if (drop.Rejected)
            {
                drop.Resolved = true;
                Settle(carry, drop, target, ghost, accepted: false);
                Finish(DragResult.Returned, drop);
                return;
            }
            if (drop.Deferred)
            {
                Park(carry, drop, target);
                return;
            }
            drop.Resolved = true;
            Settle(carry, drop, target, ghost, accepted: true);
            Finish(DragResult.Dropped, drop);
        }

        /// <summary>
        /// The visual end of a drop (§7.6 step 4). Accepted: the one thing that now shows what was dropped flies in
        /// from where it was let go; the source is already home showing whatever the push gave it. Not accepted:
        /// the source itself flies home.
        /// </summary>
        private static void Settle(Carry carry, DropEvent drop, DropTargetMarker target, Rect from, bool accepted)
        {
            // Only outcomes of the user's drop come here — a release, or the answer to a deferred one. Cancels snap back
            // without settling, and raise nothing (spec 2026-11-23-ui-sfx-cues §7.11).
            CueOutcome(carry.Ctrl, target, accepted);
            var duration = carry.Ctrl != null ? carry.Ctrl.DragDuration : Control.DefaultDragDuration;
            if (accepted)
            {
                var land = drop.LandSet ? drop.LandOn : SingleSourceIn(target, drop.Kind);
                if (land != null && ReferenceEquals(land, carry.Ctrl))
                {
                    carry.FlyHomeFrom(from, duration);
                }
                else
                {
                    carry.SnapHome();
                    if (land is Control lc && lc.GameObject != null)
                        SettleFlight.FlyFrom(lc.LayoutHost, from, morph: true, duration, UI.Drag.SortingOrder);
                }
                FlyQueued(drop, duration);
                if (target != null) target.Receive();
            }
            else
            {
                carry.FlyHomeFrom(from, duration);
            }
            if (carry.Source != null) carry.Source.Look.Undo(instant: false);
        }

        // ───── UI sound cues (spec 2026-11-23-ui-sfx-cues §7.11) ─────

        private const string SfxKind = "drag";

        // land: what it went into decides first (an equip slot, a bin), then the dragged thing; return: the dragged thing.
        private static void CueOutcome(Control source, DropTargetMarker target, bool accepted)
        {
            if (source == null) return;
            if (!accepted)
            {
                source.RaiseSfx(PromptUGUI.Lint.SfxRules.Return, kind: SfxKind);
                return;
            }
            var into = target != null ? target.Owner : null;
            var sourceScreen = UI.OwnerScreenOf(source);
            var screen = into != null ? UI.OwnerScreenOf(into) : sourceScreen;
            if ((sourceScreen != null && sourceScreen.IsClosing) || (screen != null && screen.IsClosing)) return;
            UI.Sfx.Raise(SfxKind, PromptUGUI.Lint.SfxRules.Land, source, into, screen, into?.Sfx, source.Sfx);
        }

        private static void FlyQueued(DropEvent drop, float duration)
        {
            if (drop == null || drop.Flights.Count == 0) return;
            var flights = drop.Flights.ToArray();
            drop.Flights.Clear();
            foreach (var (element, from) in flights) Fly(element, from, duration);
        }

        private static void Fly(IControl element, IControl from, float duration)
        {
            if (element is not Control ec || ec.GameObject == null) return;
            var fromRt = from is Control fc ? fc.LayoutHost : from?.RectTransform;
            if (fromRt == null) return;
            var rect = LiftLayer.ScreenRect(fromRt, LiftLayer.CameraOf(fromRt));
            SettleFlight.FlyFrom(ec.LayoutHost, rect, morph: true, duration, UI.Drag.SortingOrder);
        }

        /// <summary>The single active drag source of <paramref name="kind"/> inside the target — a slot's card.</summary>
        private static Control SingleSourceIn(DropTargetMarker target, string kind)
        {
            if (target == null) return null;
            Control found = null;
            foreach (var s in target.GetComponentsInChildren<DragSource>(false))
            {
                if (s.Owner == null || s.Owner.DragKind != kind) continue;
                if (found != null) return null;
                found = s.Owner;
            }
            return found;
        }

        /// <summary>The source is put down: its <c>drop</c>, the targets let go, the end announced.</summary>
        private static void Finish(DragResult result, DropEvent drop)
        {
            var source = s_carry != null ? s_carry.Source : s_source;
            var info = s_info;
            if (source != null && source.Marker is { } marker) marker.Drop();
            Disarm(keepHover: false);
            if (s_carousel != null) s_carousel.HeldByDrag = false;
            s_carousel = null;
            s_state = State.Idle;
            s_carry = null;
            s_source = null;
            s_pointer = null;
            s_info = null;
            Runner.Ensure(listenForEscape: false);
            if (info == null) return;
            info.Result = result;
            if (drop != null) drop.Result = result;
            Emit(s_ended, info);
        }

        private static void Disarm(bool keepHover)
        {
            if (!keepHover && s_hover != null) s_hover.SetHover(DropHover.None);
            if (!keepHover) s_hover = null;
            s_hoverDenied = false;
            foreach (var m in s_armed.ToArray())
            {
                if (m == null) continue;
                m.SetArmed(false);
                m.SetHit(false);
            }
            foreach (var m in s_denied.ToArray())
                if (m != null) m.SetHit(false);
            s_armed.Clear();
            s_denied.Clear();
            s_judged.Clear();
            s_rejudge = false;
        }

        // ───── waiting for the answer (§7.10) ─────

        private static void Park(Carry carry, DropEvent drop, DropTargetMarker target)
        {
            s_state = State.Pending;
            s_pending = drop;
            s_pendingTarget = target;
            s_pendingElapsed = 0f;
            drop.Owner = carry;
            carry.TakeOut();
            carry.Park(target.Owner?.LayoutHost, carry.Ctrl.DragDuration);
            // The hovered target stays over and starts waiting; every other one lets go.
            s_hover = target;
            s_hoverDenied = false;
            Disarm(keepHover: true);
            target.SetPending(true);
        }

        private static void TickPending(float dt)
        {
            var carry = s_carry;
            if (carry == null || carry.Ctrl == null || carry.Ctrl.GameObject == null)
            {
                EndPending(DragResult.Cancelled);
                return;
            }
            var timeout = UI.Drag.PendingTimeout;
            if (timeout <= 0f) return;
            s_pendingElapsed += dt;
            if (s_pendingElapsed < timeout) return;
            Debug.LogWarning(
                $"[PromptUGUI] A deferred drop waited {timeout:0.##}s (UI.Drag.PendingTimeout) for DropEvent.Accept / " +
                "Reject and was given up: the dragged item went back. A later Accept still runs its apply.");
            EndPending(DragResult.Cancelled);
        }

        /// <summary>The structure under the parked source is about to change: put it back now, keep waiting (DND-D20).</summary>
        private static void LetGoOfPark()
        {
            var carry = s_carry;
            if (s_state != State.Pending || carry == null || !carry.Out) return;
            s_pending.GhostRect = carry.ScreenRect();
            carry.PutBack(pin: false);
            carry.Lower();
            if (carry.Source != null) carry.Source.Look.Undo(instant: true);
        }

        private static void EndPending(DragResult result)
        {
            if (s_state != State.Pending) return;
            LetGoOfPark();
            var drop = s_pending;
            var target = s_pendingTarget;
            drop.Resolved = true;
            s_pending = null;
            s_pendingTarget = null;
            if (target != null) target.SetPending(false);
            Finish(result, drop);
        }

        /// <summary><see cref="DropEvent.Accept"/> / <see cref="DropEvent.Reject"/> outside the callback.</summary>
        internal static void Resolve(DropEvent drop, bool accept, Action apply)
        {
            if (drop == null) return;
            if (drop.Resolved || !drop.Deferred || !ReferenceEquals(drop, s_pending))
            {
                // Answered already, never deferred, or the wait was cut short: the data still has to change.
                drop.Resolved = true;
                if (accept) RunApply(apply);
                return;
            }
            drop.Resolved = true;
            var carry = s_carry;
            var target = s_pendingTarget;
            // Parked: it flies from where it waits. Let go of earlier (a push, a ReSolve): from where it waited then.
            var from = carry.Out ? carry.ScreenRect() : drop.GhostRect;
            // 1. Structure first — apply pushes into a normal tree, nothing of this drop left in it.
            carry.PutBack(pin: true);
            s_pending = null;
            s_pendingTarget = null;
            s_state = State.Idle;
            if (target != null) target.SetPending(false);
            if (accept)
            {
                // 2. The data. 3. The landing.
                RunApply(apply);
                Settle(carry, drop, target, from, accepted: true);
                Finish(DragResult.Dropped, drop);
            }
            else
            {
                Settle(carry, drop, target, from, accepted: false);
                Finish(DragResult.Returned, drop);
            }
        }

        internal static void RunApply(Action apply)
        {
            if (apply == null) return;
            try { apply(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary><see cref="DropEvent.Land"/> after the drop has landed: fly it in from where it was let go, now.</summary>
        internal static void LandLate(DropEvent drop)
        {
            if (drop.LandOn is Control c && c.GameObject != null)
                SettleFlight.FlyFrom(c.LayoutHost, drop.GhostRect, morph: true, drop.Duration, UI.Drag.SortingOrder);
        }

        /// <summary><see cref="DropEvent.Fly"/> after the drop has landed.</summary>
        internal static void FlyNow(DropEvent drop, IControl element, IControl from) => Fly(element, from, drop.Duration);

        // ───── cancelling (§7.7) ─────

        private static void CancelDrag()
        {
            if (s_state != State.Dragging) return;
            var carry = s_carry;
            var src = s_source;
            s_state = State.Idle;
            if (src != null) src.Gesture.Cancel();   // the rest of this gesture goes nowhere
            carry.PutBack(pin: false);
            carry.Lower();
            if (src != null) src.Look.Undo(instant: true);
            Finish(DragResult.Cancelled, null);
        }

        /// <summary>
        /// The structure under <paramref name="root"/> is about to change — a list re-binding or clearing its rows, a
        /// Screen starting to close, a row handed to another item. A drag carrying something from there ends; a parked
        /// drop from or onto there lets go of its park and keeps waiting.
        /// </summary>
        internal static void BeforeStructureChange(Transform root)
        {
            if (root == null || s_carry == null) return;
            var host = s_carry.Host;
            if (s_state == State.Dragging)
            {
                if (host == null || host.IsChildOf(root)) CancelDrag();
                return;
            }
            if (s_state != State.Pending) return;
            if ((host != null && host.IsChildOf(root))
                || (s_pendingTarget != null && s_pendingTarget.transform.IsChildOf(root)))
                LetGoOfPark();
        }

        /// <summary>A layer opened at <paramref name="sortingOrder"/> (a modal, a routed page): a drag from below it ends.</summary>
        internal static void OnLayerAbove(int sortingOrder)
        {
            if (s_state != State.Dragging || s_carry?.Host == null) return;
            var root = LiftLayer.RootOf(s_carry.Host);
            var order = root != null ? root.sortingOrder : 0;
            if (order < sortingOrder) CancelDrag();
        }

        /// <summary>A Screen re-solves (Variant, theme, orientation, locale — all global): no gesture over a changing structure.</summary>
        internal static void OnReSolve()
        {
            if (s_state == State.Dragging) CancelDrag();
            else if (s_state == State.Pending) LetGoOfPark();
        }

        /// <summary>
        /// A control's subscriptions are released — it is about to be re-bound to another item, or destroyed. The
        /// source ends its drag (or lets go of its park); a target is judged again once its bind has run.
        /// </summary>
        internal static void OnReleased(Control c)
        {
            if (s_state == State.Idle || c == null) return;
            if (s_carry != null && ReferenceEquals(c, s_carry.Ctrl))
            {
                if (s_state == State.Dragging) CancelDrag();
                else LetGoOfPark();
                return;
            }
            if (s_state == State.Dragging && c.DropTarget != null) Invalidate(c.DropTarget);
        }

        internal static void OnSourceTurnedOff(Control c)
        {
            if (s_state == State.Dragging && s_carry != null && ReferenceEquals(c, s_carry.Ctrl)) CancelDrag();
        }

        internal static void OnSourceDestroyed(DragSource src)
        {
            if (s_carry == null || !ReferenceEquals(s_carry.Source, src)) return;
            if (s_state == State.Dragging) CancelDrag();
            else if (s_state == State.Pending) EndPending(DragResult.Cancelled);
        }

        /// <summary>Escape / the gamepad's East while dragging: put it back, and the press is spent.</summary>
        internal static bool TryEscape()
        {
            if (s_state != State.Dragging) return false;
            // The user put it back: a return, before the session ends (spec 2026-11-23-ui-sfx-cues §7.11). Every other
            // cancel — code, a push, a ReSolve, a close, a layer above — is silent.
            s_source?.Owner?.RaiseSfx(PromptUGUI.Lint.SfxRules.Return, kind: SfxKind);
            CancelDrag();
            return true;
        }

        /// <summary><c>UI.Drag.Cancel()</c>: a drag goes back; a wait ends.</summary>
        internal static void CancelFromCode()
        {
            if (s_state == State.Dragging) CancelDrag();
            else if (s_state == State.Pending) EndPending(DragResult.Cancelled);
        }

        // ───── helpers ─────

        /// <summary>
        /// The row index of <paramref name="c"/> (or of the row it sits in) in the nearest <c>&lt;ScrollList&gt;</c> /
        /// <c>&lt;Carousel&gt;</c>, -1 outside one.
        /// </summary>
        internal static int RowIndexOf(Control c)
        {
            if (c == null || c.GameObject == null) return -1;
            for (var t = c.GameObject.transform; t != null; t = t.parent)
            {
                var parent = t.parent;
                if (parent == null) break;
                var content = parent.GetComponent<ScrollListContentMarker>();
                if (content != null && content.Owner != null) return content.Owner.IndexOfRowHost(t);
                if (parent.name == "Strip" && parent.GetComponentInParent<CarouselView>() is { } carousel
                    && carousel.StripRect == parent)
                    return carousel.IndexOfCard(t);
            }
            return -1;
        }

        private static void Emit<T>(Subject<T> subject, T value)
        {
            try { subject.OnNext(value); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        internal static void ResetForTests()
        {
            if (s_state == State.Dragging) CancelDrag();
            else if (s_state == State.Pending) EndPending(DragResult.Cancelled);
            SettleFlight.CompleteAll();
            ResetStatics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_state = State.Idle;
            s_carry = null;
            s_source = null;
            s_pointer = null;
            s_info = null;
            s_pending = null;
            s_pendingTarget = null;
            s_pendingElapsed = 0f;
            s_carousel = null;
            s_targets.Clear();
            s_armed.Clear();
            s_denied.Clear();
            s_judged.Clear();
            s_rejudge = false;
            s_hover = null;
            s_hoverDenied = false;
            s_lifted = new Subject<DragInfo>();
            s_hovered = new Subject<DragInfo>();
            s_dropped = new Subject<DropEvent>();
            s_ended = new Subject<DragInfo>();
            HitTestForTests = null;
            SettleFlight.FlownForTests = null;
            UI.Drag.ResetSettingsForTests();
        }

        // ───── runner ─────

        /// <summary>
        /// The hidden per-frame driver (P14), and the Escape key source while dragging — a back press goes through
        /// <see cref="EscapeChain"/> like every other, so one press is decided once however many listeners heard it.
        /// </summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner s_instance;
            private PromptUGUI.Application.Modals.ModalEscapeListener _escape;

            internal static void Ensure(bool listenForEscape)
            {
                if (!UnityEngine.Application.isPlaying) return;
                if (s_instance == null)
                {
                    if (!listenForEscape) return;
                    var go = new GameObject("[PromptUGUI] Drag") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<Runner>();
                    s_instance._escape = go.AddComponent<PromptUGUI.Application.Modals.ModalEscapeListener>();
                    s_instance._escape.AlsoCancelButton = true;
                }
                if (s_instance._escape != null) s_instance._escape.enabled = listenForEscape;
            }

            private void Update() => Tick(Time.unscaledDeltaTime);
        }
    }
}
