using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>Which back key a listener heard.</summary>
    internal enum EscapeKey
    {
        /// <summary>Keyboard Escape.</summary>
        Escape,

        /// <summary>The gamepad's East (Cancel) button — heard only by listeners that bind it (popups, a field being edited).</summary>
        CancelButton,

        /// <summary>The gamepad's Start: Escape for every step but the last — TMP does not cancel an edit on it.</summary>
        StartButton,
    }

    /// <summary>
    /// The one place that decides what an Escape press does (spec 2026-10-18 §4.4). The
    /// <see cref="Modals.ModalEscapeListener"/> components stay where they are — on every ad-hoc
    /// dialog, every routed Modal and every open popup — but they are only key sources now: each
    /// hands its press here, and one press is decided once however many of them heard it.
    ///
    /// <para>Order: a blocking tutorial step swallows it; else an open <c>&lt;Dropdown&gt;</c> list
    /// hides; else the open popup closes (unless it sits under the top modal); else the top ad-hoc
    /// dialog answers; else a routed Modal on top goes back; else a text field being edited owns it
    /// (spec 2026-10-19-input-begin-edit §4.4) — TMP cancels that edit, the chain only records the press.
    /// The gamepad's East button goes from the popup straight to the field — it never closes a modal;
    /// the gamepad's Start is Escape for every step but the field's.</para>
    /// </summary>
    internal static class EscapeChain
    {
        private static int s_lastPress = int.MinValue;
        private static bool s_lastResult;
        private static int s_nextTestPress = -1;

        /// <summary>
        /// The frame a press was spent closing a popup. The input module sends a text field its Cancel for
        /// the same press in this frame's EventSystem update — the same Input System update the chain heard
        /// it in; the field reads this so it does not also cancel its edit for a press that only closed its
        /// suggestion list.
        /// </summary>
        internal static int PopupConsumedFrame { get; private set; } = int.MinValue;

        /// <summary>
        /// How many frames after the press a field may still collect its Escape KeyDown (<see cref="EscapeOwed"/>).
        /// It has been seen a frame late; nobody collecting means the KeyDown went by before the chain heard it.
        /// </summary>
        internal const int OwedEscapeFrames = 3;

        private static int s_owedEscapeFrame = int.MinValue;

        /// <summary>
        /// A keyboard Escape was spent closing a popup, and TMP has yet to read that press's own KeyDown. TMP
        /// reads Escape from IMGUI, not from the Input System, and the KeyDown can come a frame after the
        /// callback the chain ran in — a same-frame test misses it, and the edit the press only closed a list
        /// over is cancelled and rolled back. The field being edited takes the next Escape KeyDown as the one
        /// owed (<see cref="SettleOwedEscape"/>).
        /// </summary>
        internal static bool EscapeOwed =>
            s_owedEscapeFrame != int.MinValue && Time.frameCount - s_owedEscapeFrame <= OwedEscapeFrames;

        /// <summary>The owed KeyDown was taken, or its press is over.</summary>
        internal static void SettleOwedEscape() => s_owedEscapeFrame = int.MinValue;

        /// <summary>The frame a press was spent on anything — what <see cref="UI.EscapeConsumedThisFrame"/> reads.</summary>
        internal static int ConsumedFrame { get; private set; } = int.MinValue;

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
            if (s_lastResult) ConsumedFrame = Time.frameCount;
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
                // The gamepad's buttons have no IMGUI KeyDown to come.
                if (key == EscapeKey.Escape) s_owedEscapeFrame = Time.frameCount;
                PopupChain.Close(top, PopupCloseReason.Escape);
                return true;
            }

            if (key == EscapeKey.CancelButton) return InputField.AnyEditing;
            if (UI.Modal.TryHandleEscape()) return true;
            if (UI.Router.TryHandleEscape()) return true;

            // TMP cancels an edit on Escape (and on B, through the input module's Cancel) in the EventSystem's
            // update, after this; game code reading its own back key needs to know now. Not on Start.
            return key == EscapeKey.Escape && InputField.AnyEditing;
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

        /// <summary>
        /// <see cref="PopupConsumedFrame"/> and <see cref="EscapeOwed"/> outlive a <c>[Test]</c>'s frozen frame
        /// count otherwise.
        /// </summary>
        internal static void ForgetPopupConsumedFrameForTests()
        {
            PopupConsumedFrame = int.MinValue;
            SettleOwedEscape();
        }

        /// <summary>
        /// Domain reload off: frame numbers start over in the next Play session, and the last session's
        /// press would otherwise be "consumed" again when its frame comes round.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForPlaySession()
        {
            s_lastPress = int.MinValue;
            s_lastResult = false;
            PopupConsumedFrame = int.MinValue;
            ConsumedFrame = int.MinValue;
            SettleOwedEscape();
            InputField.ForgetEditing();
        }

        internal static void ResetForTests()
        {
            ResetForPlaySession();
            s_nextTestPress = -1;
        }
    }
}
