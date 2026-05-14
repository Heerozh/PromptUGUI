using R3;

namespace PromptUGUI.Controls.Internal
{
    internal interface IPointerEventSource
    {
        public Observable<Unit> OnPointerEnter { get; }
        public Observable<Unit> OnPointerExit { get; }
        public Observable<Unit> OnPointerDown { get; }
    }
}
