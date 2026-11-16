using System.Collections.Generic;
using LitMotion;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The drag-to-reorder session of a <see cref="ScrollList"/> (spec 2026-09-16-scrolllist-drag-reorder).
    /// Lives on Content — the rows' ancestor — so uGUI's handler search finds it before the root's
    /// <c>ScrollRect</c>. A gesture that is not a reorder (pressed off any row, moved before the hold
    /// elapsed) is forwarded up to the ScrollRect event by event, like <c>CarouselView</c> does; a
    /// reorder is consumed and the ScrollRect never hears of it. The arbitration itself is
    /// <see cref="LiftGesture"/>, shared with drag-and-drop.
    ///
    /// <para>Structure commits the moment the pointer is released; every animation is a visual
    /// <c>anchoredPosition</c> tween laid over the committed layout (FLIP), so a layout rebuild from
    /// anywhere else can only snap a row to where it was going anyway. The lifted row leaves the
    /// layout flow through <c>LayoutElement.ignoreLayout</c> (what <c>flow="false"</c> uses) and a
    /// same-size placeholder holds its slot — Content's preferred size never changes.</para>
    /// </summary>
    internal sealed class ReorderDriver : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, LiftGesture.IHost
    {
        internal const string PlaceholderName = "ReorderPlaceholder";
        internal const float DefaultTouchHold = LiftGesture.DefaultTouchHold;

        /// <summary>Tests substitute the device query (they have no input devices). null = real query.</summary>
        internal static System.Func<PointerEventData, bool> PressedProbeForTests
        {
            get => LiftGesture.PressedProbeForTests;
            set => LiftGesture.PressedProbeForTests = value;
        }

        private ScrollList _list;
        private RectTransform _content;
        private RectTransform _viewport;

        private LiftGesture _gesture;
        private IControl _row;
        private RectTransform _rowRt;
        private LayoutElement _rowLe;
        private RectTransform _placeholder;
        private int _from;
        private int _to;
        private Vector2 _liftLocal;
        private Vector2 _liftAnchored;
        private bool _handleWarned;
        private readonly LiftLook _look = new();

        // Layout positions as of the last rebuild — the reference frame for target computation.
        // The visual positions are being tweened and would jitter the answer.
        private readonly Dictionary<RectTransform, Vector2> _layoutPos = new();
        private readonly Dictionary<RectTransform, MotionHandle> _tweens = new();

        private LiftGesture Gesture => _gesture ??= new LiftGesture(this);

        internal bool IsSessionActive => _gesture != null && _gesture.IsSessionActive;

        internal void Init(ScrollList list, RectTransform content, RectTransform viewport)
        {
            _list = list;
            _content = content;
            _viewport = viewport;
        }

        // ───── uGUI events → the shared gesture ─────

        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData e) => Gesture.OnInitializePotentialDrag(e);

        void IBeginDragHandler.OnBeginDrag(PointerEventData e) => Gesture.OnBeginDrag(e);

        void IDragHandler.OnDrag(PointerEventData e) => Gesture.OnDrag(e);

        void IEndDragHandler.OnEndDrag(PointerEventData e) => Gesture.OnEndDrag(e);

        private void Update() => Gesture.Tick(Time.unscaledDeltaTime);

        internal void TickForTests(float dt) => Gesture.Tick(dt);

        // ───── LiftGesture.IHost ─────

        bool LiftGesture.IHost.TryPress(PointerEventData e, out float hold)
        {
            hold = 0f;
            if (!_list.ReorderEnabled) return false;

            var row = HitRow(e.pressPosition, e.pressEventCamera);
            if (row == null) return false;
            if (!string.IsNullOrEmpty(_list.ReorderHandle) && !HitHandle(row, e.pressPosition, e.pressEventCamera))
                return false;

            _row = row;
            _rowRt = LayoutHostOf(row);
            hold = ResolveHold(e);
            return true;
        }

        bool LiftGesture.IHost.Lift()
        {
            Lift();
            return true;
        }

        void LiftGesture.IHost.Drag(PointerEventData e)
        {
            FollowPointer(e.position, e.pressEventCamera);
            UpdateTarget();
        }

        void LiftGesture.IHost.Release() => Commit();

        void LiftGesture.IHost.Tick(float dt) => AutoScroll(dt);

        // A reorder moves along the scroll axis by definition: nothing to tell apart on the first frame.
        bool LiftGesture.IHost.LiftsOnBegin(PointerEventData e) => false;

        // Anything that is not a reorder belongs to the ScrollRect on the root — Content's parent is
        // the Viewport, ExecuteHierarchy walks up from there (same as CarouselView.ForwardToParent).
        void LiftGesture.IHost.Forward<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn)
        {
            var parent = transform.parent;
            if (parent != null) ExecuteEvents.ExecuteHierarchy(parent.gameObject, e, fn);
        }

        // ───── lift / drag / drop ─────

        private void Lift()
        {
            var pointer = Gesture.Pointer;
            _from = IndexOfRow(_row);
            _to = _from;

            _placeholder = FlowPlaceholder.Create(_rowRt, _content, PlaceholderName);

            _rowLe = _rowRt.GetComponent<LayoutElement>() ?? _rowRt.gameObject.AddComponent<LayoutElement>();
            _rowLe.ignoreLayout = true;
            _rowRt.SetAsLastSibling();
            _placeholder.SetSiblingIndex(_from);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            SnapshotLayout();
            _liftAnchored = _rowRt.anchoredPosition;
            ToContentLocal(pointer.pressPosition, pointer.pressEventCamera, out _liftLocal);
            // A long-press lift is not a click: the <Btn> under the finger must not fire on release.
            pointer.eligibleForClick = false;

            // Hooks first (an authored <Animation on="lift"> starts now); the default look only when
            // the row has no lift hook of its own — the two must not both write localScale.
            if (_list.NotifyLifted(_row)) _look.Apply(_rowRt);
        }

        private void FollowPointer(Vector2 screen, Camera cam)
        {
            if (!ToContentLocal(screen, cam, out var local)) return;
            var delta = local - _liftLocal;
            // A single column keeps its rows in the column; a single row keeps them in the row.
            if (!_list.IsGrid)
            {
                if (_list.IsHorizontal) delta.y = 0f;
                else delta.x = 0f;
            }
            _rowRt.anchoredPosition = _liftAnchored + delta;
        }

        private void UpdateTarget()
        {
            var to = ComputeTarget();
            if (to == _to) return;
            _to = to;
            // The lifted row is the last sibling, so among the others the placeholder's sibling
            // index IS the insertion index.
            Flip(() => _placeholder.SetSiblingIndex(to));
        }

        /// <summary>
        /// Commit the structure now, then animate towards it: placeholder out, row back into the
        /// flow at <c>_to</c>, slots permuted, and only then the "settle" tween from where the
        /// finger left the row to where the layout put it.
        /// </summary>
        private void Commit()
        {
            var from = _from;
            var to = _to;
            var row = _row;
            var rt = _rowRt;

            FlowPlaceholder.Remove(_placeholder);
            _placeholder = null;
            rt.SetSiblingIndex(to);
            _rowLe.ignoreLayout = false;
            _list.PermuteSlots(from, to);

            var visual = rt.anchoredPosition;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            SnapshotLayout();
            Tween(rt, visual, rt.anchoredPosition);
            _look.Undo(instant: false);

            ClearSession();
            _list.NotifyDropped(row);
            if (from != to) _list.RaiseReordered(from, to);
        }

        /// <summary>
        /// Structure is about to change under the session (a push, an apply pass, a clear): put the
        /// row back where it started, fire nothing, and let the rest of this gesture go nowhere —
        /// handing the ScrollRect a drag with no begin would make it jump.
        /// </summary>
        internal void Cancel()
        {
            if (_gesture == null || !_gesture.Cancel()) return;
            KillTweens();
            FlowPlaceholder.Remove(_placeholder);
            _placeholder = null;
            // The row may already be gone (Dispose tearing the list down) — restore what is left.
            if (_rowRt != null)
            {
                _rowRt.SetSiblingIndex(_from);
                if (_rowLe != null) _rowLe.ignoreLayout = false;
            }
            _look.Undo(instant: true);
            if (_content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var row = _row;
            ClearSession();
            _list.NotifyDropped(row);
        }

        private void ClearSession()
        {
            _row = null;
            _rowRt = null;
            _rowLe = null;
            _layoutPos.Clear();
        }

        // ───── target ─────

        /// <summary>
        /// Where the lifted row would be inserted among the OTHER rows (the index after removal —
        /// <c>List.RemoveAt(from); List.Insert(to, x)</c>). Hidden rows neither count nor get
        /// crossed: the answer is a count of VISIBLE rows, mapped back onto the slot list.
        /// </summary>
        private int ComputeTarget()
        {
            var lifted = Center(_rowRt, _rowRt.anchoredPosition);
            var slots = _list.Slots;
            int k;
            if (_list.IsGrid && _content.GetComponent<GridLayoutGroup>() is { } grid)
            {
                k = GridCellIndex(grid, lifted);
            }
            else
            {
                k = 0;
                for (var i = 0; i < slots.Count; i++)
                {
                    var rt = LayoutHostOf(slots[i]);
                    if (rt == _rowRt || !InFlow(rt) || !_layoutPos.TryGetValue(rt, out var pos)) continue;
                    var c = Center(rt, pos);
                    var precedes = _list.IsHorizontal ? c.x < lifted.x : c.y > lifted.y;
                    if (precedes) k++;
                }
            }

            // k-th visible row (excluding the lifted one) → insertion index among all other rows.
            var seen = 0;
            var without = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                var rt = LayoutHostOf(slots[i]);
                if (rt == _rowRt) continue;
                if (InFlow(rt))
                {
                    if (seen == k) return without;
                    seen++;
                }
                without++;
            }
            return without;
        }

        private int GridCellIndex(GridLayoutGroup grid, Vector2 lifted)
        {
            var cell = grid.cellSize;
            var gap = grid.spacing;
            var x = lifted.x - grid.padding.left + gap.x * 0.5f;
            var y = -lifted.y - grid.padding.top + gap.y * 0.5f;
            var columns = Mathf.Max(1, grid.constraintCount);
            var col = Mathf.Clamp(Mathf.FloorToInt(x / Mathf.Max(1f, cell.x + gap.x)), 0, columns - 1);
            var row = Mathf.Max(0, Mathf.FloorToInt(y / Mathf.Max(1f, cell.y + gap.y)));
            return row * columns + col;
        }

        // ───── FLIP ─────

        /// <summary>
        /// Record where every in-flow row is, mutate the structure, rebuild, and tween each row that
        /// moved from its old place to its new one. Positions are read from <c>anchoredPosition</c>,
        /// which after a layout pass is relative to Content's top-left for every row.
        /// </summary>
        private void Flip(System.Action mutate)
        {
            var slots = _list.Slots;
            var rows = new List<RectTransform>(slots.Count);
            var old = new List<Vector2>(slots.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                var rt = LayoutHostOf(slots[i]);
                if (rt == _rowRt || !InFlow(rt)) continue;
                rows.Add(rt);
                old.Add(rt.anchoredPosition);
            }

            mutate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

            for (var i = 0; i < rows.Count; i++)
            {
                var rt = rows[i];
                var target = rt.anchoredPosition;
                _layoutPos[rt] = target;
                if ((target - old[i]).sqrMagnitude < 0.0001f) continue;
                Tween(rt, old[i], target);
            }
        }

        private void Tween(RectTransform rt, Vector2 from, Vector2 to)
        {
            if (_tweens.TryGetValue(rt, out var running) && running.IsActive()) running.TryCancel();
            _tweens.Remove(rt);
            var duration = _list.ReorderDuration;
            if (duration <= 0f || !UnityEngine.Application.isPlaying || (to - from).sqrMagnitude < 0.0001f)
            {
                rt.anchoredPosition = to;
                return;
            }
            rt.anchoredPosition = from;
            _tweens[rt] = LMotion.Create(from, to, duration)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(rt, static (v, r) => { if (r != null) r.anchoredPosition = v; });
        }

        private void KillTweens()
        {
            foreach (var kv in _tweens)
            {
                if (kv.Value.IsActive()) kv.Value.TryCancel();
                if (kv.Key != null && _layoutPos.TryGetValue(kv.Key, out var pos)) kv.Key.anchoredPosition = pos;
            }
            _tweens.Clear();
        }

        private void SnapshotLayout()
        {
            _layoutPos.Clear();
            var slots = _list.Slots;
            for (var i = 0; i < slots.Count; i++)
            {
                var rt = LayoutHostOf(slots[i]);
                if (rt == _rowRt || !InFlow(rt)) continue;
                _layoutPos[rt] = rt.anchoredPosition;
            }
        }

        // ───── autoscroll ─────

        private void AutoScroll(float dt)
        {
            var pointer = Gesture.Pointer;
            if (pointer == null) return;
            if (!EdgeAutoScroller.Step(_viewport, _content, _list.IsHorizontal,
                    pointer.position, pointer.pressEventCamera, dt))
                return;
            // The finger did not move; the content under it did — the row stays under the finger.
            FollowPointer(pointer.position, pointer.pressEventCamera);
            UpdateTarget();
        }

        // ───── hit testing ─────

        private IControl HitRow(Vector2 screen, Camera cam)
        {
            var slots = _list.Slots;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var rt = LayoutHostOf(slot);
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                if (slot is Control c && !c.PeekInteractable) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam)) return slot;
            }
            return null;
        }

        /// <summary>
        /// Geometric, not raycast: the handle is usually an <c>&lt;Icon&gt;</c>, which is
        /// click-through by design. A row without the handle warns once and lifts from anywhere —
        /// the lint CLI (<c>PUI-REORDER-HANDLE-ID</c>) is where the author hears about it.
        /// </summary>
        private bool HitHandle(IControl row, Vector2 screen, Camera cam)
        {
            var handle = FindHandle(row, _list.ReorderHandle);
            if (handle == null)
            {
                if (!_handleWarned)
                {
                    _handleWarned = true;
                    UILog.Warn(_list,
                        $"[{PromptUGUI.Lint.ScrollListRules.ReorderHandleCode}] <ScrollList id='{_list.Id}' " +
                        $"reorderHandle='{_list.ReorderHandle}'>: no node with that id inside the row — the " +
                        "whole row lifts instead.");
                }
                return true;
            }
            var rt = LayoutHostOf(handle);
            return rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, cam);
        }

        internal static IControl FindHandle(IControl root, string id)
        {
            if (root.ScopedIds.TryGetValue(id, out var scoped)) return scoped;
            return FindById(root, id);
        }

        private static IControl FindById(IControl node, string id)
        {
            if (node is not Control ctrl) return null;
            foreach (var child in ctrl.Children)
            {
                if (child.Id == id) return child;
                var deeper = FindById(child, id);
                if (deeper != null) return deeper;
            }
            return null;
        }

        // ───── helpers ─────

        private float ResolveHold(PointerEventData e)
        {
            var hold = _list.ReorderHold;
            if (hold >= 0f) return hold;
            // auto: a mouse lifts at once (the wheel scrolls); a finger needs the hold, because on a
            // touch screen the drag IS the scroll — unless a handle already tells the two apart.
            if (!string.IsNullOrEmpty(_list.ReorderHandle)) return 0f;
            return LiftGesture.IsTouch(e) ? DefaultTouchHold : 0f;
        }

        private int IndexOfRow(IControl row)
        {
            var slots = _list.Slots;
            for (var i = 0; i < slots.Count; i++)
                if (slots[i] == row) return i;
            return -1;
        }

        private static RectTransform LayoutHostOf(IControl c) => c is Control ctrl ? ctrl.LayoutHost : c.RectTransform;

        private bool InFlow(RectTransform rt)
        {
            if (rt == null || rt == _placeholder || !rt.gameObject.activeInHierarchy) return false;
            var le = rt.GetComponent<LayoutElement>();
            return le == null || !le.ignoreLayout;
        }

        /// <summary>Rect centre in the row's anchored frame (after a layout pass: Content's top-left).</summary>
        private static Vector2 Center(RectTransform rt, Vector2 anchored)
            => anchored + Vector2.Scale(rt.rect.size, new Vector2(0.5f, 0.5f) - rt.pivot);

        private bool ToContentLocal(Vector2 screen, Camera cam, out Vector2 local)
            => RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, screen, cam, out local);

        private void OnDestroy()
        {
            foreach (var kv in _tweens)
                if (kv.Value.IsActive()) kv.Value.TryCancel();
            _tweens.Clear();
            _look.Kill();
            if (_placeholder != null)
            {
                if (UnityEngine.Application.isPlaying) Destroy(_placeholder.gameObject);
                else DestroyImmediate(_placeholder.gameObject);
            }
        }
    }
}
