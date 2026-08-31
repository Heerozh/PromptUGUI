using R3;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A control that opens and closes — <see cref="TabMenu"/> (a popup panel) and
    /// <see cref="Collapsible"/> (an inline fold). It is what <c>on="expand"</c> /
    /// <c>on="collapse"</c> listen to, so a row animating itself into view does not care which of
    /// the two it happens to live in (spec 2026-08-31-collapsible-design §4.5).
    /// </summary>
    internal interface IExpandable
    {
        public bool IsExpanded { get; }

        /// <summary>Fires when opening <em>starts</em> — the content is already active and measured,
        /// so a row's entrance animation runs alongside the container's own transition.</summary>
        public Observable<Unit> OnExpanded { get; }

        public Observable<Unit> OnCollapsed { get; }
    }

    /// <summary>
    /// A component-shaped handle back to the <see cref="IExpandable"/> that owns this GameObject.
    ///
    /// <para>Controls here are plain C# objects, so a <c>&lt;Trigger on="expand"&gt;</c> nested
    /// inside one cannot reach it with a <c>GetComponentInParent</c> walk. This marker is what that
    /// walk finds — the same trick <c>IStateSource</c> uses for <c>state-*</c> triggers, which
    /// resolve upward for the same reason.</para>
    /// </summary>
    internal sealed class ExpandableMarker : MonoBehaviour
    {
        internal IExpandable Owner;
    }
}
