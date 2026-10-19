using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Watches for presses outside the open pass-through popups (spec 2026-10-19 §5.4): each press of the
    /// frame is raycast like the EventSystem would, and <see cref="PopupChain.DismissOutside"/> decides
    /// what closes. Nothing is consumed — the press goes on to whatever it landed on.
    ///
    /// <para>Driven by the open popups' <see cref="PopupPanelGuard"/> (ordered before the EventSystem),
    /// once per frame however many there are. Presses are latched as the Input System updates, so a
    /// fixed / manual update mode cannot make one fall between frames; without the Input System the legacy
    /// <c>Input</c> is polled.</para>
    /// </summary>
    internal static class OutsidePressProbe
    {
        internal readonly struct Press
        {
            public readonly Vector2 Position;
            public readonly int Frame;

            public Press(Vector2 position, int frame)
            {
                Position = position;
                Frame = frame;
            }
        }

        /// <summary>Tests: the presses of the batch, instead of the devices.</summary>
        internal static Func<IReadOnlyList<Press>> PressesForTests;

        /// <summary>Tests: what a position hits, instead of a raycast (EditMode has no sized Overlay canvas).</summary>
        internal static Func<Vector2, GameObject> HitForTests;

        private static readonly List<Press> s_latched = new();
        private static readonly List<RaycastResult> s_hits = new();
        private static int s_lastBatch = int.MinValue;

        /// <summary>Once per frame, from any open pass-through popup.</summary>
        internal static void Tick() => Process(Time.frameCount);

        /// <summary>Tests: decide one batch; a repeated id is a second caller in the same frame.</summary>
        internal static void TickForTests(int batch) => Process(batch);

        private static void Process(int batch)
        {
            if (batch == s_lastBatch) return;
            s_lastBatch = batch;
#if ENABLE_INPUT_SYSTEM
            EnsureLatching();
#endif
            IReadOnlyList<Press> presses;
            if (PressesForTests != null)
            {
                presses = PressesForTests();
            }
            else
            {
#if !ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
                PollLegacy();
#endif
                presses = s_latched;
            }
            try
            {
                if (presses == null || presses.Count == 0 || !PopupChain.HasPassThrough) return;
                // A Block step lets nothing through (same as Escape, spec 2026-10-18 §4.4 step 1).
                if (UI.Tutorial.IsBlockingInput) return;
                // An open <Dropdown> list hangs off the root canvas: picking from one inside a popover
                // must not read as a press outside it.
                if (PuiDropdown.ExpandedCount > 0) return;
                if (HitForTests == null && EventSystem.current == null) return;

                for (var i = 0; i < presses.Count && PopupChain.HasPassThrough; i++)
                {
                    var hit = HitAt(presses[i].Position);
                    PopupChain.DismissOutside(hit != null ? hit.transform : null, presses[i].Frame);
                }
            }
            finally
            {
                s_latched.Clear();
            }
        }

        private static GameObject HitAt(Vector2 position)
        {
            if (HitForTests != null) return HitForTests(position);
            var es = EventSystem.current;
            if (es == null) return null;
            var data = new PointerEventData(es) { position = position };
            s_hits.Clear();
            es.RaycastAll(data, s_hits);
            var top = s_hits.Count > 0 ? s_hits[0].gameObject : null;
            s_hits.Clear();
            return top;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool s_latching;

        private static void EnsureLatching()
        {
            if (s_latching) return;
            s_latching = true;
            InputSystem.onAfterUpdate += Latch;
        }

        // After every input update: what went down in it. Editor updates (the Game view out of focus,
        // edit-mode device polling) are not the game's presses.
        private static void Latch()
        {
            if (InputState.currentUpdateType == InputUpdateType.Editor) return;
            if (PopupChain.IsEmpty)
            {
                s_latched.Clear();
                return;
            }
            // Keep only this frame's: nothing consumed them while no pass-through popup was ticking.
            if (s_latched.Count > 0 && s_latched[0].Frame != Time.frameCount) s_latched.Clear();

            var frame = Time.frameCount;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                                  || mouse.middleButton.wasPressedThisFrame))
                s_latched.Add(new Press(mouse.position.ReadValue(), frame));
            var pen = Pen.current;
            if (pen != null && pen.tip.wasPressedThisFrame)
                s_latched.Add(new Press(pen.position.ReadValue(), frame));
            var touch = Touchscreen.current;
            if (touch != null)
                foreach (var t in touch.touches)
                    if (t.press.wasPressedThisFrame)
                        s_latched.Add(new Press(t.position.ReadValue(), frame));
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        private static void PollLegacy()
        {
            s_latched.Clear();
            var frame = Time.frameCount;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                s_latched.Add(new Press(Input.mousePosition, frame));
            for (var i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began) s_latched.Add(new Press(t.position, frame));
            }
        }
#endif

        internal static void ResetForTests()
        {
            PressesForTests = null;
            HitForTests = null;
            s_latched.Clear();
            s_lastBatch = int.MinValue;
        }
    }
}
