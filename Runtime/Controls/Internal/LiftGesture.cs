using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The press → hold → lift → drag → release arbitration every "pick something up out of a
    /// scrolling container" gesture shares: a <c>&lt;ScrollList reorder&gt;</c> row
    /// (<see cref="ReorderDriver"/>) and a <c>drag=</c> source (<c>DragSource</c>). The rules are
    /// spec 2026-09-16-scrolllist-drag-reorder §5.1; the extraction is spec 2026-11-16-drag-drop §7.1.
    ///
    /// <para>The host is the MonoBehaviour uGUI delivers the drag events to. It hands each of them here
    /// and answers what this class cannot know: may this press lift anything, what lifting means, where
    /// a drag goes. Everything that is not a lift is handed on to whatever handles drags above the host,
    /// event by event — the host sits deeper than the ScrollRect (that is why uGUI found it first), and
    /// the ScrollRect must still see a scroll exactly as it would have without it.</para>
    /// </summary>
    internal sealed class LiftGesture
    {
        internal const float DefaultTouchHold = 0.4f;

        internal enum Phase
        {
            Idle,
            Pressed,    // on something liftable, hold counting down (or hold = 0: lift on the first drag frame)
            Armed,      // lifted, no drag yet — release without moving puts it down where it is
            Dragging,   // lifted and following the pointer
            Scrolling,  // not ours: every event forwarded
            Swallow,    // cancelled mid-gesture: the rest of this gesture goes nowhere
        }

        internal interface IHost
        {
            /// <summary>
            /// A left-button press reached the host: may it lift something? <paramref name="hold"/> is how
            /// long the pointer must stay down first; 0 lifts on the first drag frame.
            /// </summary>
            public bool TryPress(PointerEventData e, out float hold);

            /// <summary>Lift now. False when it no longer can (another session took over meanwhile): the gesture is then a scroll.</summary>
            public bool Lift();

            /// <summary>A drag event while lifted.</summary>
            public void Drag(PointerEventData e);

            /// <summary>The pointer was released while lifted (dragging, or still where it was lifted).</summary>
            public void Release();

            /// <summary>
            /// The system took the pointer away while lifted — a cancelled touch (a call, the notification shade, a system
            /// gesture, too many fingers, the app losing focus). End it as a cancel, not as a release (spec
            /// 2026-11-23-input-correctness §2.1).
            /// </summary>
            public void Cancel();

            /// <summary>Once a frame while dragging.</summary>
            public void Tick(float dt);

            /// <summary>The first drag frame arrived while a hold was still pending: lift right away instead of scrolling?</summary>
            public bool LiftsOnBegin(PointerEventData e);

            /// <summary>Hands an event to whatever handles drags above the host.</summary>
            public void Forward<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn) where T : IEventSystemHandler;
        }

        private readonly IHost _host;
        private Phase _phase;
        private PointerEventData _pointer;
        private float _holdRemaining;

        internal LiftGesture(IHost host) => _host = host;

        internal Phase Current => _phase;

        /// <summary>Lifted: from the lift to the release (or a cancel).</summary>
        internal bool IsSessionActive => _phase is Phase.Armed or Phase.Dragging;

        /// <summary>The event data of the press that started this gesture — the input module keeps updating it.</summary>
        internal PointerEventData Pointer => _pointer;

        // ───── uGUI events, handed over by the host ─────

        internal void OnInitializePotentialDrag(PointerEventData e)
        {
            // The ScrollRect zeroes its velocity here — a flinging list stops on press, as it always did.
            _host.Forward(e, ExecuteEvents.initializePotentialDrag);
            if (IsSessionActive) return;   // a second pointer while something is lifted: not ours

            _phase = Phase.Idle;
            _pointer = e;
            if (e.button != PointerEventData.InputButton.Left) return;
            if (!_host.TryPress(e, out var hold)) return;
            _holdRemaining = hold;
            _phase = Phase.Pressed;
        }

        internal void OnBeginDrag(PointerEventData e)
        {
            if (IsAnotherPointer(e)) return;
            switch (_phase)
            {
                case Phase.Pressed when _holdRemaining <= 0f:
                    // hold = 0: the first drag frame is the lift (never the press itself — a plain click
                    // on a <Btn> must not flicker).
                    LiftOrScroll(e);
                    return;
                case Phase.Pressed when _host.LiftsOnBegin(e):
                    LiftOrScroll(e);
                    return;
                case Phase.Pressed:
                    // The finger moved before the hold elapsed: this is a scroll.
                    _phase = Phase.Scrolling;
                    _host.Forward(e, ExecuteEvents.beginDragHandler);
                    return;
                case Phase.Armed:
                    _phase = Phase.Dragging;
                    return;
                case Phase.Swallow:
                    return;
                default:
                    _phase = Phase.Scrolling;
                    _host.Forward(e, ExecuteEvents.beginDragHandler);
                    return;
            }
        }

        private void LiftOrScroll(PointerEventData e)
        {
            if (_host.Lift())
            {
                _phase = Phase.Dragging;
                return;
            }
            _phase = Phase.Scrolling;
            _host.Forward(e, ExecuteEvents.beginDragHandler);
        }

        internal void OnDrag(PointerEventData e)
        {
            if (IsAnotherPointer(e)) return;
            switch (_phase)
            {
                case Phase.Dragging:
                    _host.Drag(e);
                    return;
                case Phase.Swallow:
                    return;
                default:
                    _host.Forward(e, ExecuteEvents.dragHandler);
                    return;
            }
        }

        internal void OnEndDrag(PointerEventData e)
        {
            if (IsAnotherPointer(e)) return;
            switch (_phase)
            {
                case Phase.Dragging:
                    if (IsSystemCancel(e))
                    {
                        // The host cancels while the session is still live (its own Cancel checks that), then it is over.
                        _host.Cancel();
                        _phase = Phase.Idle;
                        return;
                    }
                    // Idle before the host commits: whatever the commit raises sees the gesture over.
                    _phase = Phase.Idle;
                    _host.Release();
                    return;
                case Phase.Swallow:
                    _phase = Phase.Idle;
                    return;
                default:
                    _host.Forward(e, ExecuteEvents.endDragHandler);
                    _phase = Phase.Idle;
                    return;
            }
        }

        // ───── per frame ─────

        internal void Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.Pressed:
                    if (Released())
                    {
                        _phase = Phase.Idle;
                        return;
                    }
                    if (_holdRemaining <= 0f) return;   // hold = 0 lifts on the first drag frame
                    _holdRemaining -= dt;
                    if (_holdRemaining <= 0f) _phase = _host.Lift() ? Phase.Armed : Phase.Scrolling;
                    return;
                case Phase.Armed:
                    // uGUI has no "released without ever dragging" callback; the gesture looks for itself.
                    if (!Released()) return;
                    if (IsSystemCancel(_pointer))
                    {
                        _host.Cancel();
                        _phase = Phase.Idle;
                        return;
                    }
                    _phase = Phase.Idle;
                    _host.Release();
                    return;
                case Phase.Dragging:
                    _host.Tick(dt);
                    return;
                case Phase.Swallow:
                    if (Released()) _phase = Phase.Idle;
                    return;
            }
        }

        /// <summary>
        /// The host ends the gesture from outside — the structure under it is about to change. A press
        /// still counting down simply stops; a live session's remaining events go nowhere (handing the
        /// ScrollRect a drag with no begin would make it jump). Returns whether a session was live — the
        /// host then restores what it lifted.
        /// </summary>
        internal bool Cancel()
        {
            if (IsSessionActive)
            {
                _phase = Phase.Swallow;
                return true;
            }
            if (_phase == Phase.Pressed) _phase = Phase.Idle;
            return false;
        }

        /// <summary>
        /// A press still counting down to its lift ends without lifting anything — the drag-drop spec's §7.7 cancels hold
        /// while pressed too (spec 2026-11-23-input-correctness §2.2). The rest of the gesture goes nowhere, like a cancel
        /// mid-drag. False when it was not pressed.
        /// </summary>
        internal bool CancelPress()
        {
            if (_phase != Phase.Pressed) return false;
            _phase = Phase.Swallow;
            return true;
        }

        // A second finger while something is lifted: not this session's, and not a scroll either (the
        // ScrollRect tracks one drag) — its events go nowhere rather than rewriting the phase.
        private bool IsAnotherPointer(PointerEventData e)
            => IsSessionActive && _pointer != null && e.pointerId != _pointer.pointerId;

        // ───── pointer facts ─────

        /// <summary>
        /// Whether the pointer that started this gesture has been released. uGUI has no "released without
        /// ever dragging" callback, so the gesture looks for itself:
        /// <list type="bullet">
        /// <item>Input System module: ask the <b>device</b>. <c>InputSystemUIInputModule</c> shares ONE event
        /// object across its left / right / middle passes and copies each button's press state into it
        /// (<c>ButtonState.CopyPressStateTo</c>), so at the end of any frame in which the pointer changed,
        /// <c>pointerDrag</c> holds the <i>middle</i> button's — null — and says nothing about the finger or
        /// the left button (spec 2026-09-16 §14.4).</item>
        /// <item>Legacy <c>StandaloneInputModule</c> (and EditMode tests, which hand-build plain
        /// <c>PointerEventData</c>): one event object per button, <c>pointerDrag</c> cleared on release.</item>
        /// </list>
        /// </summary>
        internal bool Released() => IsReleased(_pointer);

        internal static bool IsReleased(PointerEventData pointer)
        {
            if (pointer == null) return true;
            if (PressedProbeForTests != null) return !PressedProbeForTests(pointer);
#if ENABLE_INPUT_SYSTEM
            if (pointer is UnityEngine.InputSystem.UI.ExtendedPointerEventData x && x.device != null)
                return !DevicePressed(x);
#endif
            return pointer.pointerDrag == null;
        }

        /// <summary>Tests substitute the device query (they have no input devices). null = real query.</summary>
        internal static System.Func<PointerEventData, bool> PressedProbeForTests;

        /// <summary>Tests substitute the "did the system cancel this touch" query. null = real query.</summary>
        internal static System.Func<PointerEventData, bool> CanceledProbeForTests;

        /// <summary>
        /// Whether the pointer ended because the system cancelled it rather than because it was let go. The input modules send
        /// the same pointer-up / end-drag for both (the Input System module takes a cancelled touch as a release; the legacy
        /// one only stops uGUI's own drop), so the gesture asks the device: the touch's phase stays <c>Canceled</c> through the
        /// frame its end is handed out.
        /// </summary>
        internal static bool IsSystemCancel(PointerEventData pointer)
        {
            if (pointer == null) return false;
            if (CanceledProbeForTests != null) return CanceledProbeForTests(pointer);
#if ENABLE_INPUT_SYSTEM
            if (pointer is UnityEngine.InputSystem.UI.ExtendedPointerEventData x)
            {
                if (x.device is not UnityEngine.InputSystem.Touchscreen ts) return false;
                foreach (var t in ts.touches)
                    if (t.touchId.ReadValue() == x.touchId)
                        return t.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled;
                return false;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (pointer.pointerId >= 0)   // StandaloneInputModule: a touch's pointerId is its fingerId
            {
                for (var i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.fingerId == pointer.pointerId) return t.phase == UnityEngine.TouchPhase.Canceled;
                }
            }
#endif
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool DevicePressed(UnityEngine.InputSystem.UI.ExtendedPointerEventData x)
        {
            switch (x.device)
            {
                case UnityEngine.InputSystem.Touchscreen ts:
                    // Multi-touch: our finger is the one with our touchId; a slot that has moved on to
                    // Ended / Canceled (or been reused) is a release.
                    foreach (var t in ts.touches)
                        if (t.touchId.ReadValue() == x.touchId) return t.isInProgress;
                    return false;
                case UnityEngine.InputSystem.Pointer p:
                    return p.press.isPressed;   // Mouse.leftButton / Pen.tip
                default:
                    return x.pointerDrag != null;
            }
        }
#endif

        internal static bool IsTouch(PointerEventData e)
        {
#if ENABLE_INPUT_SYSTEM
            if (e is UnityEngine.InputSystem.UI.ExtendedPointerEventData x)
                return x.pointerType == UnityEngine.InputSystem.UI.UIPointerType.Touch;
#endif
            return e.pointerId >= 0;   // uGUI's mouse buttons are the negative ids
        }
    }
}
