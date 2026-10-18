using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Swallows a press on the blank parts of a <c>&lt;Suggestions&gt;</c> panel (spec 2026-10-18
    /// §5.4). Without it the press bubbles up to the input field the panel hangs under, and
    /// <c>TMP_InputField.OnPointerDown</c> moves the caret or starts a text selection. It answers
    /// click as well: the input module resolves the click handler separately, and one that only
    /// took the down would still deliver the click to <c>TMP_InputField.OnPointerClick</c>.
    /// </summary>
    internal sealed class PointerSink : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public void OnPointerDown(PointerEventData eventData) { }

        public void OnPointerClick(PointerEventData eventData) { }
    }
}
