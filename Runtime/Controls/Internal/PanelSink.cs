using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Keeps the pointer on a <c>&lt;Popover&gt;</c>'s panel (spec 2026-10-19 §6): a press, click, drag or
    /// wheel on its blank area stops here instead of bubbling to whatever the popover was declared under
    /// — a list row it lives in is not dragged, a <c>&lt;Btn&gt;</c> it lives in is not clicked. Controls
    /// inside the panel still get their own events first: this is only the last handler on the way up.
    /// </summary>
    internal sealed class PanelSink : MonoBehaviour,
        IPointerDownHandler, IPointerClickHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IScrollHandler
    {
        public void OnPointerDown(PointerEventData eventData) { }

        public void OnPointerClick(PointerEventData eventData) { }

        public void OnInitializePotentialDrag(PointerEventData eventData) { }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData) { }

        public void OnScroll(PointerEventData eventData) { }
    }
}
