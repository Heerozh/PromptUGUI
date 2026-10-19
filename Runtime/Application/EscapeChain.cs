using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>Which back key a listener heard.</summary>
    internal enum EscapeKey
    {
        /// <summary>Keyboard Escape, or the gamepad's Start.</summary>
        Escape,

        /// <summary>The gamepad's East (Cancel) button — heard only by listeners that bind it (popups).</summary>
        CancelButton,
    }

    /// <summary>
    /// The one place that decides what an Escape press does (spec 2026-10-18 §4.4). The
    /// <see cref="Modals.ModalEscapeListener"/> components stay where they are — on every ad-hoc
    /// dialog, every routed Modal and every open popup — but they are only key sources now: each
    /// hands its press here, and one press is decided once however many of them heard it.
    ///
    /// <para>Order: a blocking tutorial step swallows it; else an open <c>&lt;Dropdown&gt;</c> list
    /// hides; else the open popup closes (unless it sits under the top modal); else the top ad-hoc
    /// dialog answers; else a routed Modal on top goes back.
    /// The gamepad's East button stops after the popup — it never closes a modal.</para>
    /// </summary>
    internal static class EscapeChain
    {
        private static int s_lastPress = int.MinValue;
        private static bool s_lastResult;
        private static int s_nextTestPress = -1;

        /// <summary>
        /// The frame a press was spent closing a popup. A text field processes the same key in the
        /// EventSystem's update, after the Input System callbacks; it reads this so it does not also
        /// cancel its edit for a press that only closed its suggestion list.
        /// </summary>
        internal static int PopupConsumedFrame { get; private set; } = int.MinValue;

        /// <summary>
        /// Decides <paramref name="key"/> once per <paramref name="pressId"/>: a second listener that
        /// heard the same press gets the first answer and changes nothing. Real listeners pass
        /// <c>Time.frameCount</c>; test presses come from <see cref="NextTestPress"/>.
        /// </summary>
        internal static bool Dispatch(EscapeKey key, int pressId)
        {
            if (pressId == s_lastPress) return s_lastResult;
            s_lastPress = pressId;
            s_lastResult = Decide(key);
            return s_lastResult;
        }

        private static bool Decide(EscapeKey key)
        {
            // A Block step lets nothing through — not even the popup it may be pointing into.
            if (UI.Tutorial.IsBlockingInput) return true;

            // An open <Dropdown> list sorts above everything, so it is always what the user sees.
            if (PuiDropdown.HideExpanded()) return true;

            var top = PopupChain.Top;
            if (top != null && top.HostRootOrder >= TopModalOrder())
            {
                PopupConsumedFrame = Time.frameCount;
                PopupChain.Close(top, PopupCloseReason.Escape);
                return true;
            }

            if (key == EscapeKey.CancelButton) return false;
            if (UI.Modal.TryHandleEscape()) return true;
            return UI.Router.TryHandleEscape();
        }

        // The sortingOrder of the highest dialog on screen, ad-hoc or routed; a popup below it (one a
        // script opened under a dialog) is not what the user is looking at.
        internal static int TopModalOrder()
        {
            var order = int.MinValue;
            var adHoc = UI.Modal.TopScreen;
            if (adHoc != null) order = Mathf.Max(order, RootOrder(adHoc));
            var routed = UI.Router.TopModalScreen;
            if (routed != null) order = Mathf.Max(order, RootOrder(routed));
            return order;
        }

        private static int RootOrder(Screen screen)
        {
            var root = screen.RootGameObject;
            var canvas = root != null ? root.GetComponent<Canvas>() : null;
            return canvas != null ? canvas.sortingOrder : int.MinValue;
        }

        /// <summary>A fresh press id for every test call — never equal to a frame count.</summary>
        internal static int NextTestPress() => s_nextTestPress--;

        /// <summary><see cref="PopupConsumedFrame"/> outlives a <c>[Test]</c>'s frozen frame count otherwise.</summary>
        internal static void ForgetPopupConsumedFrameForTests() => PopupConsumedFrame = int.MinValue;

        internal static void ResetForTests()
        {
            s_lastPress = int.MinValue;
            s_lastResult = false;
            s_nextTestPress = -1;
            PopupConsumedFrame = int.MinValue;
        }
    }
}
