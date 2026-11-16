using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Shared plumbing for the drag-and-drop tests (spec 2026-11-16-drag-drop §11). Events are hand-built
    /// <see cref="PointerEventData"/> delivered straight to a source's <see cref="DragSource"/> (the
    /// <c>ScrollListReorderTests</c> way); the session's per-frame work runs from
    /// <see cref="DragSession.TickForTests"/>, and the raycast is replaced by a geometric pick over the drop
    /// targets (<see cref="UseGeometricHits"/>). EditMode is <c>!isPlaying</c>: every flight lands at once.
    /// </summary>
    public abstract class DragDropTestBase
    {
        [SetUp]
        public virtual void SetUp() => UI.ResetForTests();

        [TearDown]
        public virtual void TearDown()
        {
            DragSession.HitTestForTests = null;
            LiftGesture.PressedProbeForTests = null;
            UI.ResetForTests();
        }

        protected const string TwoSlots =
            "<Frame id='slotA' anchor='top-left' size='100x100' margin='10,_,_,10' drop='unit'>" +
            "  <Frame id='a' anchor='stretch' drag='unit'/>" +
            "</Frame>" +
            "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'>" +
            "  <Frame id='b' anchor='stretch' drag='unit'/>" +
            "</Frame>";

        private protected static PromptUGUI.Application.Screen Open(string body, string templates = "", string name = "S")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + templates
                    + $"<Screen name='{name}'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("doc-" + name, xml);
            var screen = UI.Open(name);
            Canvas.ForceUpdateCanvases();
            return screen;
        }

        private protected static DragSource SourceOf(IControl c) => c.GameObject.GetComponent<DragSource>();

        private protected static DropTargetMarker TargetOf(IControl c) => c.GameObject.GetComponent<DropTargetMarker>();

        private protected static RectTransform Host(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        private protected static Vector2 ScreenOf(IControl c, Vector2 localOffset = default)
        {
            var rt = Host(c);
            var world = rt.TransformPoint((Vector3)rt.rect.center + (Vector3)localOffset);
            return RectTransformUtility.WorldToScreenPoint(null, world);
        }

        /// <summary>A screen point moved by (dx, dy) units of <paramref name="space"/>'s local axes.</summary>
        private protected static Vector2 Offset(RectTransform space, Vector2 screen, float dx, float dy)
        {
            var w = space.TransformVector(new Vector3(dx, dy, 0f));
            return screen + new Vector2(w.x, w.y);
        }

        private protected static PointerEventData Press(DragSource s, Vector2 screen, int pointerId = -1,
            PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            var e = new PointerEventData(EventSystem.current)
            {
                position = screen,
                pressPosition = screen,
                button = button,
                pointerId = pointerId,
                pointerDrag = s.gameObject,
                eligibleForClick = true,
            };
            ((IInitializePotentialDragHandler)s).OnInitializePotentialDrag(e);
            return e;
        }

        private protected static void Begin(DragSource s, PointerEventData e) => ((IBeginDragHandler)s).OnBeginDrag(e);

        private protected static void Move(DragSource s, PointerEventData e, Vector2 screen)
        {
            e.delta = screen - e.position;
            e.position = screen;
            ((IDragHandler)s).OnDrag(e);
        }

        private protected static void End(DragSource s, PointerEventData e) => ((IEndDragHandler)s).OnEndDrag(e);

        /// <summary>Mouse press on <paramref name="c"/>, first drag frame (the lift), returns the event.</summary>
        private protected static PointerEventData Lift(IControl c)
        {
            var s = SourceOf(c);
            var e = Press(s, ScreenOf(c));
            Begin(s, e);
            return e;
        }

        /// <summary>Lift <paramref name="c"/>, carry it over <paramref name="over"/>, tick a frame, release.</summary>
        private protected static void DragOnto(IControl c, IControl over)
        {
            var s = SourceOf(c);
            var e = Lift(c);
            Move(s, e, ScreenOf(over));
            DragSession.TickForTests(0.016f);
            End(s, e);
        }

        private protected static void Tick(float dt = 0.016f) => DragSession.TickForTests(dt);

        /// <summary>
        /// The raycast, replaced by geometry: the deepest active drop target whose rect holds the point —
        /// what a real raycast returns once the armed targets are hit-testable.
        /// </summary>
        private protected static void UseGeometricHits()
        {
            DragSession.HitTestForTests = screen =>
            {
                GameObject best = null;
                var bestDepth = -1;
                foreach (var m in Object.FindObjectsByType<DropTargetMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var rt = (RectTransform)m.transform;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null)) continue;
                    var depth = 0;
                    for (var t = m.transform; t != null; t = t.parent) depth++;
                    if (depth <= bestDepth) continue;
                    best = m.gameObject;
                    bestDepth = depth;
                }
                return best;
            };
        }

        private protected static Transform Placeholder(IControl c)
        {
            var parent = Host(c).parent;
            return parent != null ? parent.Find(DragSession.PlaceholderName) : null;
        }

        /// <summary>The lift layer while it is up: a lifted control's canvas stays on it disabled between lifts.</summary>
        private protected static Canvas LiftCanvasOf(IControl c)
        {
            var canvas = Host(c).GetComponent<Canvas>();
            return canvas != null && canvas.enabled ? canvas : null;
        }

        /// <summary>Records what the drag streams raise, in order.</summary>
        protected sealed class Recorder : System.IDisposable
        {
            public readonly List<string> Log = new();
            public readonly List<DropEvent> Drops = new();
            public readonly List<DragInfo> Ends = new();
            private readonly List<System.IDisposable> _subs = new();

            public Recorder()
            {
                _subs.Add(UI.Drag.OnLift.Subscribe(i => Log.Add("lift:" + i.Kind)));
                _subs.Add(UI.Drag.OnDrop.Subscribe(d =>
                {
                    Drops.Add(d);
                    Log.Add("drop:" + d.Target?.Id);
                }));
                _subs.Add(UI.Drag.OnEnd.Subscribe(i =>
                {
                    Ends.Add(i);
                    Log.Add("end:" + i.Result);
                }));
            }

            public void Dispose()
            {
                foreach (var s in _subs) s.Dispose();
            }
        }
    }
}
