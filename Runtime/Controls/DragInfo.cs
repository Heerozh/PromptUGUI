using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Controls
{
    /// <summary>How a drag ended (spec 2026-11-16-drag-drop §6.1).</summary>
    public enum DragResult
    {
        /// <summary>Not over yet: lifted, or a drop still waiting for its answer.</summary>
        None,

        /// <summary>Put into a target that took it.</summary>
        Dropped,

        /// <summary>Released over nothing that would take it, or rejected: it went back.</summary>
        Returned,

        /// <summary>Ended from outside — a structure change, a close, Escape, <c>UI.Drag.Cancel()</c>, a timeout.</summary>
        Cancelled,
    }

    /// <summary>
    /// One drag: what is carried, from where, and — while it hovers — over which target. The object
    /// <see cref="UI.Drag.Current"/> returns is live and changes as the drag goes on; a
    /// <see cref="IControl.CanDrop"/> callback sees <see cref="Target"/> set to the target it is asked about.
    /// </summary>
    public class DragInfo
    {
        internal DragInfo() { }

        /// <summary>The source's <c>drag=</c> kind.</summary>
        public string Kind { get; internal set; }

        /// <summary>The control carrying <c>drag=</c>.</summary>
        public IControl Source { get; internal set; }

        /// <summary><see cref="IControl.DragData"/> of the source, read when it was lifted.</summary>
        public object Data { get; internal set; }

        public IScreen SourceScreen { get; internal set; }

        /// <summary>Row index of the source in the nearest <c>&lt;ScrollList&gt;</c> / <c>&lt;Carousel&gt;</c>, else -1.</summary>
        public int SourceIndex { get; internal set; } = -1;

        /// <summary>The <c>drop=</c> control hovered — or dropped on, in a <see cref="DropEvent"/>; null over nothing.</summary>
        public IControl Target { get; internal set; }

        /// <summary><see cref="IControl.DragData"/> of the target.</summary>
        public object TargetData { get; internal set; }

        public IScreen TargetScreen { get; internal set; }

        /// <summary>Row index of the target in the nearest <c>&lt;ScrollList&gt;</c> / <c>&lt;Carousel&gt;</c>, else -1.</summary>
        public int TargetIndex { get; internal set; } = -1;

        /// <summary>The pointer, in screen pixels — for a drop outside the UI (a unit in the 3D world).</summary>
        public Vector2 ScreenPosition { get; internal set; }

        /// <summary>How it ended; <see cref="DragResult.None"/> until then.</summary>
        public DragResult Result { get; internal set; }

        internal void CopyTo(DragInfo other)
        {
            other.Kind = Kind;
            other.Source = Source;
            other.Data = Data;
            other.SourceScreen = SourceScreen;
            other.SourceIndex = SourceIndex;
            other.Target = Target;
            other.TargetData = TargetData;
            other.TargetScreen = TargetScreen;
            other.TargetIndex = TargetIndex;
            other.ScreenPosition = ScreenPosition;
            other.Result = Result;
        }
    }

    /// <summary>
    /// A drop onto a target that takes it (spec 2026-11-16-drag-drop §6). Raised by the target's
    /// <see cref="Control.OnDrop"/> and then <see cref="UI.Drag.OnDrop"/>, synchronously, once the structure
    /// is back to normal — a push from inside the callback is safe and is the expected way to answer.
    /// What the subscribers ask for here (<see cref="Reject"/>, <see cref="Defer"/>, <see cref="Land"/>,
    /// <see cref="Fly"/>) is settled after the last of them has run.
    /// </summary>
    public sealed class DropEvent : DragInfo
    {
        internal DropEvent() { }

        internal bool InCallback;
        internal bool Rejected;
        internal bool Deferred;
        internal bool Resolved;
        internal bool LandSet;
        internal IControl LandOn;
        internal readonly List<(IControl Element, IControl From)> Flights = new();

        /// <summary>What a deferred drop parked (the session settles it on Accept / Reject).</summary>
        internal Internal.DragSession.Carry Owner;

        /// <summary>Where the dragged thing was let go (or parked), in screen pixels — the start of the landing.</summary>
        internal Rect GhostRect;

        /// <summary>The source's <c>dragDuration</c>.</summary>
        internal float Duration;

        /// <summary>Waiting for <see cref="Accept"/> / <see cref="Reject"/> after a <see cref="Defer"/>.</summary>
        public bool IsPending => Deferred && !Resolved;

        /// <summary>
        /// The drop is refused after all: no <c>receive</c>, the source goes back to where it came from.
        /// Inside the callback, or later for a deferred drop. Wins over <see cref="Defer"/>.
        /// </summary>
        public void Reject()
        {
            if (InCallback)
            {
                Rejected = true;
                return;
            }
            Internal.DragSession.Resolve(this, accept: false, apply: null);
        }

        /// <summary>
        /// Do not land yet: the dragged thing waits over the target (which shows <c>drop-pending</c>) until
        /// <see cref="Accept"/> or <see cref="Reject"/> — for a server that has the final word (spec §7.10).
        /// Only inside the <c>OnDrop</c> callback.
        /// </summary>
        /// <exception cref="InvalidOperationException">Called outside the callback.</exception>
        public void Defer()
        {
            if (!InCallback)
                throw new InvalidOperationException(
                    "DropEvent.Defer() can only be called inside the OnDrop callback — the drop is settled as soon " +
                    "as the last subscriber returns.");
            Deferred = true;
        }

        /// <summary>
        /// A deferred drop is accepted: the structure is put back, then <paramref name="apply"/> runs (push the new
        /// data there), then it lands. On a drop that was not deferred it simply runs <paramref name="apply"/>.
        /// <paramref name="apply"/> always runs, even after the wait was cut short or answered already — data the
        /// server confirmed must not be lost because the screen moved on; only the animation is skipped then.
        /// </summary>
        public void Accept(Action apply = null)
        {
            if (InCallback)
            {
                // Accepting inside the callback is the plain synchronous drop.
                Deferred = false;
                Internal.DragSession.RunApply(apply);
                return;
            }
            Internal.DragSession.Resolve(this, accept: true, apply: apply);
        }

        /// <summary>
        /// What flies in from where it was dropped. Default: the one active drag source of the same kind inside the
        /// target (a slot's card), none if there are zero or several; null = nothing flies.
        /// </summary>
        public void Land(IControl element)
        {
            LandSet = true;
            LandOn = element;
            if (!InCallback && !IsPending) Internal.DragSession.LandLate(this);
        }

        /// <summary>
        /// One more flight: <paramref name="element"/> flies from where <paramref name="from"/> is to its own place —
        /// after a swap, the one that was displaced. Settled with the drop if called before it lands; at once if after.
        /// </summary>
        public void Fly(IControl element, IControl from)
        {
            if (element == null || from == null) return;
            if (InCallback || IsPending)
            {
                Flights.Add((element, from));
                return;
            }
            Internal.DragSession.FlyNow(this, element, from);
        }
    }
}
