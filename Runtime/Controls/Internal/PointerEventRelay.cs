using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    internal sealed class PointerEventRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private readonly Subject<Unit> _enter = new();
        private readonly Subject<Unit> _exit = new();
        private readonly Subject<Unit> _down = new();

        public Observable<Unit> OnPointerEnter => _enter;
        public Observable<Unit> OnPointerExit => _exit;
        public Observable<Unit> OnPointerDown => _down;

        void IPointerEnterHandler.OnPointerEnter(PointerEventData e) => _enter.OnNext(Unit.Default);
        void IPointerExitHandler.OnPointerExit(PointerEventData e) => _exit.OnNext(Unit.Default);
        void IPointerDownHandler.OnPointerDown(PointerEventData e) => _down.OnNext(Unit.Default);

        private void OnDestroy()
        {
            _enter.Dispose();
            _exit.Dispose();
            _down.Dispose();
        }
    }
}
