using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Switches an inactive subtree on for the length of a measurement and back off afterwards.
    ///
    /// <para>A TMP added by <c>AddComponent</c> under an inactive GameObject never runs
    /// <c>Awake</c>, and from then on reports a preferred size of 0 for the rest of its life. Any
    /// control that builds rows while they are hidden — <see cref="TabMenu"/>'s collapsed popup,
    /// <see cref="Collapsible"/>'s collapsed body, <see cref="RevealDriver"/>'s content measurement —
    /// therefore has to activate before measuring. Nothing renders between the two calls (they
    /// happen inside one frame), so this is invisible.</para>
    /// </summary>
    internal static class InactiveMeasure
    {
        /// <summary>Activates <paramref name="go"/> if it was inactive. Returns whether it did.</summary>
        public static bool ActivateIfNeeded(GameObject go)
        {
            if (go == null || go.activeSelf) return false;
            go.SetActive(true);
            return true;
        }

        /// <summary>
        /// Undoes <see cref="ActivateIfNeeded"/>. Pass back exactly what it returned — an object that
        /// was already active must not be switched off.
        /// </summary>
        public static void Restore(GameObject go, bool activated)
        {
            if (go == null || !activated) return;
            go.SetActive(false);
        }
    }
}
