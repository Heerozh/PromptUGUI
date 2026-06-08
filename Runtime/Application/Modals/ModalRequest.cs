using System;
using PromptUGUI.Application;

namespace PromptUGUI.Application.Modals
{
    public abstract class ModalRequest<TResult>
    {
        public abstract string XmlSrc { get; }

        /// <summary>
        /// Optional post-bind customization hook. Invoked once by
        /// <see cref="ModalEntry{TResult}.RunBind"/> AFTER <see cref="Bind"/>, with the live modal
        /// <see cref="IScreen"/>. Lets callers reach any control (disable the OK <c>Btn</c>, wire
        /// field validation, restyle, …) without subclassing. Runs after Bind so it overrides
        /// builtin wiring rather than being overwritten by it. The builtin <c>MessageBox.Open</c> /
        /// <c>InputBox.Open</c> helpers expose it as a <c>configure</c> parameter.
        /// </summary>
        public Action<IScreen> Configure;

        public abstract void Bind(IScreen screen, Action<TResult> close);

        public virtual bool TryEscape(out TResult result)
        {
            result = default;
            return false;
        }
    }
}
