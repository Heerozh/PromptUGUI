using System;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The per-frame side of a <see cref="PopupCore"/>, on its panel (spec 2026-10-19 §5.1). Play mode
    /// only: the host's follow-up after the frame's motion (a <c>&lt;Popover&gt;</c> tracking its anchor)
    /// runs in <c>LateUpdate</c>. Ordered early so that, within a frame, a popup decides about a press
    /// before the EventSystem hands that press on.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    internal sealed class PopupPanelGuard : MonoBehaviour
    {
        internal PopupCore Core;

        /// <summary>The host's per-frame callback while open (a Popover's <c>Follow</c>).</summary>
        internal Action LateTick;

        // Switched off while open — an ancestor hidden from code. Closing from here is refused by Unity
        // (the hierarchy is mid-deactivation), so the chain prunes it next frame (spec 2026-10-19 §5.2).
        private void OnDisable()
        {
            if (!UnityEngine.Application.isPlaying || Core == null || !Core.IsOpen) return;
            PopupChain.SchedulePrune();
        }

        // Destroyed while open — its Screen torn down without UI.Close (a scene change, leaving Play mode):
        // off the chain now, so nothing later tries to close a panel that is gone.
        private void OnDestroy()
        {
            if (Core != null && Core.IsOpen) Core.Abandon();
        }

        // Presses outside the open pass-through popups, decided before the EventSystem passes them on
        // (spec 2026-10-19 §5.4). The probe runs once per frame however many popups call it.
        private void Update()
        {
            if (!UnityEngine.Application.isPlaying || Core == null || !Core.IsOpen) return;
            if (Core.Dismiss != PopupDismiss.PassThrough) return;
            OutsidePressProbe.Tick();
        }

        private void LateUpdate()
        {
            if (!UnityEngine.Application.isPlaying || Core == null || !Core.IsOpen) return;
            LateTick?.Invoke();
        }
    }
}
