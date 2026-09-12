using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The <see cref="TMP_Dropdown"/> behind <c>&lt;Dropdown&gt;</c>. TMP clones the whole popup
    /// Template on every Show, and <c>Object.Instantiate</c> only carries serialized state — the
    /// <see cref="ProceduralPanel"/>s inside (a <c>&lt;Scrollbar radius=&gt;</c>'s track and handle)
    /// come out blank. The one override here copies their parameters onto the clone
    /// (2026-09-12 spec §5.5), the way <see cref="PuiButton"/> / <see cref="PuiToggle"/> hook the
    /// other uGUI components.
    /// </summary>
    internal sealed class PuiDropdown : TMP_Dropdown
    {
        protected override GameObject CreateDropdownList(GameObject template)
        {
            var clone = base.CreateDropdownList(template);
            ProceduralPanel.CopyStateToClone(template.transform, clone.transform);
            return clone;
        }

        /// <summary>The override, callable without a Canvas / EventSystem: EditMode tests only.</summary>
        internal GameObject CloneListForTests() => CreateDropdownList(template.gameObject);
    }
}
