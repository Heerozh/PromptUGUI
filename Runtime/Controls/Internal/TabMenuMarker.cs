using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A component-shaped handle back to the <see cref="TabMenu"/> that owns this GameObject.
    ///
    /// <para><c>TabMenu</c>, like every other control here, is a plain C# object, so a
    /// <c>&lt;Trigger on="expand"&gt;</c> nested inside the popup cannot reach it with a
    /// <c>GetComponentInParent</c> walk. This marker is what that walk finds — the same trick
    /// <c>IStateSource</c> uses for <c>state-*</c> triggers, which resolve upward for the same
    /// reason.</para>
    /// </summary>
    internal sealed class TabMenuMarker : MonoBehaviour
    {
        internal TabMenu Owner;
    }
}
