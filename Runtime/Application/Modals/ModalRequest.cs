using System;
using PromptUGUI.Application;

namespace PromptUGUI.Application.Modals
{
    public abstract class ModalRequest<TResult>
    {
        public abstract string XmlSrc { get; }

        public abstract void Bind(IScreen screen, Action<TResult> close);

        public virtual bool TryEscape(out TResult result)
        {
            result = default;
            return false;
        }
    }
}
