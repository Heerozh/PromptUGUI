using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    internal enum DropHover
    {
        None,
        Over,
        Deny,
    }

    /// <summary>
    /// A <c>drop=</c> target (spec 2026-11-16-drag-drop §5.3, §7.3), on the control's own GameObject: what
    /// it takes, what state the current drag leaves it in, and the hooks that react to it. A component
    /// because the hover walk goes up the GameObjects from a raycast hit, and the hooks inside the target
    /// find it with <c>GetComponentInParent</c> — the <c>ExpandableMarker</c> idea. Installed as soon as a
    /// control declares <c>drop</c> in any variant; inert while it takes no kind.
    ///
    /// <para>Three independent states — armed, hovered (over / deny), pending — each with the
    /// persistent-pair hooks: <c>drop-armed</c> / <c>drop-disarmed</c>, <c>drop-over</c> | <c>drop-deny</c> /
    /// <c>drop-out</c>, <c>drop-pending</c> (ended by <c>drop-out</c>), and the <c>receive</c> edge.</para>
    /// </summary>
    internal sealed class DropTargetMarker : MonoBehaviour
    {
        internal Control Owner;

        private string[] _kinds;
        private bool _any;
        private List<Action> _shows;

        private readonly Subject<Unit> _armed = new();
        private readonly Subject<Unit> _disarmed = new();
        private readonly Subject<Unit> _over = new();
        private readonly Subject<Unit> _deny = new();
        private readonly Subject<Unit> _out = new();
        private readonly Subject<Unit> _pending = new();
        private readonly Subject<Unit> _receive = new();

        internal bool IsArmed { get; private set; }
        internal bool IsOver { get; private set; }
        internal bool IsDenied { get; private set; }
        internal bool IsPending { get; private set; }

        internal Observable<Unit> OnReceive => _receive;

        internal void SetKinds(string[] kinds, bool any)
        {
            _kinds = kinds;
            _any = any;
            DragSession.Invalidate(this);
        }

        internal bool Accepts(string kind)
            => kind != null && (_any || (_kinds != null && Array.IndexOf(_kinds, kind) >= 0));

        internal void SetArmed(bool on)
        {
            if (IsArmed == on) return;
            IsArmed = on;
            Emit(on ? _armed : _disarmed);
            Reevaluate();
        }

        /// <summary>
        /// Hover changes. Leaving over or deny — for any reason, the drop included — raises <c>drop-out</c>;
        /// switching straight from one to the other passes through it, so a <c>reverse-on="drop-out"</c>
        /// animation always gets its way back.
        /// </summary>
        internal void SetHover(DropHover hover)
        {
            var was = IsOver ? DropHover.Over : IsDenied ? DropHover.Deny : DropHover.None;
            if (was == hover) return;
            if (was != DropHover.None)
            {
                IsOver = false;
                IsDenied = false;
                Emit(_out);
            }
            if (hover == DropHover.Over)
            {
                IsOver = true;
                Emit(_over);
            }
            else if (hover == DropHover.Deny)
            {
                IsDenied = true;
                Emit(_deny);
            }
            Reevaluate();
        }

        internal void SetPending(bool on)
        {
            if (IsPending == on) return;
            IsPending = on;
            if (on) Emit(_pending);
            Reevaluate();
        }

        internal void Receive() => Emit(_receive);

        /// <summary>Hit-testable for this drag (armed or refusing), or back to what the control itself says.</summary>
        internal void SetHit(bool on)
        {
            if (Owner != null && Owner.GameObject != null) Owner.SetDragHit(session: on);
        }

        // ───── hooks ─────

        internal Observable<Unit> StreamFor(TriggerKind kind) => kind switch
        {
            TriggerKind.DropArmed => _armed,
            TriggerKind.DropDisarmed => _disarmed,
            TriggerKind.DropOver => _over,
            TriggerKind.DropDeny => _deny,
            TriggerKind.DropOut => _out,
            TriggerKind.DropPending => _pending,
            TriggerKind.Receive => _receive,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        /// <summary>Whether the target is in the state a persistent hook names (its "how it starts" too).</summary>
        internal bool InState(TriggerKind kind) => kind switch
        {
            TriggerKind.DropArmed => IsArmed,
            TriggerKind.DropDisarmed => !IsArmed,
            TriggerKind.DropOver => IsOver,
            TriggerKind.DropDeny => IsDenied,
            TriggerKind.DropOut => !IsOver && !IsDenied,
            TriggerKind.DropPending => IsPending,
            _ => false,
        };

        /// <summary>
        /// A <c>&lt;Show on="drop-…"&gt;</c>'s re-evaluation: called now and on every state change — the
        /// registration-style visibility <c>checked</c> uses.
        /// </summary>
        internal void RegisterShow(Action reevaluate)
        {
            (_shows ??= new List<Action>()).Add(reevaluate);
            reevaluate();
        }

        private void Reevaluate()
        {
            if (_shows == null) return;
            foreach (var show in _shows) show();
        }

        private static void Emit(Subject<Unit> subject)
        {
            try { subject.OnNext(Unit.Default); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnDestroy()
        {
            DragSession.Unregister(this);
            _armed.Dispose();
            _disarmed.Dispose();
            _over.Dispose();
            _deny.Dispose();
            _out.Dispose();
            _pending.Dispose();
            _receive.Dispose();
        }
    }
}
