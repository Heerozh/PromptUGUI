using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// On each row of an open <see cref="PuiDropdown"/> list: TMP selects the current row when it shows the list, so the
    /// input module's Cancel (Esc, gamepad B) goes to that row's <c>DropdownItem.OnCancel</c>, never to the dropdown.
    /// <c>ExecuteEvents</c> calls every cancel handler on the object — this one runs after TMP has hidden the list and
    /// tells the dropdown that the close was the user's (spec 2026-11-23-ui-sfx-cues §7.5, P24).
    /// </summary>
    internal sealed class SfxCancelRelay : MonoBehaviour, ICancelHandler
    {
        private PuiDropdown _dropdown;

        internal void Init(PuiDropdown dropdown) => _dropdown = dropdown;

        public void OnCancel(BaseEventData eventData)
        {
            if (_dropdown != null) _dropdown.CueUserCloseJustNow();
        }
    }
}
