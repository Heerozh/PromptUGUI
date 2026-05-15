using System;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    // Non-generic queue entry interface — lets UI.Modal.cs work without referencing
    // the generic ModalRequest<TResult> type, which Unity's Roslyn fails to resolve
    // across namespace boundaries in partial-class files.
    internal interface IModalEntry
    {
        public string XmlSrc { get; }
        public void RunBind(IScreen screen, Action onClose);
        public bool TryEscape(Action wakePump);
        public void Cancel(Exception ex);
        public bool Resolved { get; }
    }

    internal sealed class ModalEntry<TResult> : IModalEntry
    {
        private readonly ModalRequest<TResult> _request;
        private readonly AwaitableCompletionSource<TResult> _tcs = new();

        public bool Resolved { get; private set; }
        public string XmlSrc => _request.XmlSrc;
        public Awaitable<TResult> Awaitable => _tcs.Awaitable;

        private ModalEntry(ModalRequest<TResult> request) { _request = request; }

        /// <summary>
        /// Creates a <see cref="ModalEntry{TResult}"/> for <paramref name="request"/> and
        /// returns both the queue-compatible <see cref="IModalEntry"/> and the caller's
        /// <see cref="Awaitable{TResult}"/> in one call.
        /// </summary>
        internal static (IModalEntry entry, Awaitable<TResult> awaitable) Create(
            ModalRequest<TResult> request)
        {
            var e = new ModalEntry<TResult>(request);
            return (e, e._tcs.Awaitable);
        }

        public void RunBind(IScreen screen, Action onClose)
        {
            _request.Bind(screen, result =>
            {
                if (Resolved) return;
                Resolved = true;
                _tcs.TrySetResult(result);
                onClose?.Invoke();
            });
        }

        public bool TryEscape(Action wakePump)
        {
            if (Resolved) return false;
            if (!_request.TryEscape(out var r)) return false;
            Resolved = true;
            _tcs.TrySetResult(r);
            wakePump?.Invoke();
            return true;
        }

        public void Cancel(Exception ex)
        {
            if (Resolved) return;
            Resolved = true;
            _tcs.TrySetException(ex);
        }
    }
}
