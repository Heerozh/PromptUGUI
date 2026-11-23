using System;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    // Non-generic queue entry interface — lets UI.Modal.cs work without referencing
    // the generic ModalRequest<TResult> type.
    internal interface IModalEntry
    {
        public string XmlSrc { get; }
        public void RunBind(IScreen screen, Action onClose);
        public bool TryEscape(Action wakePump);
        public void Cancel(Exception ex);
        public bool Resolved { get; }
        /// <summary>The registration on the caller's CancellationToken: disposed when this entry settles.</summary>
        public void HoldTokenRegistration(System.Threading.CancellationTokenRegistration registration);
    }

    // How many token registrations dialogs hold right now (test seam: a long-lived token must not collect them).
    internal static class ModalTokenRegistrations
    {
        internal static int Live;
    }

    internal sealed class ModalEntry<TResult> : IModalEntry
    {
        private readonly ModalRequest<TResult> _request;
        private readonly AwaitableCompletionSource<TResult> _tcs = new();

        public bool Resolved { get; private set; }
        public string XmlSrc => _request.XmlSrc;
        public Awaitable<TResult> Awaitable => _tcs.Awaitable;

        private ModalEntry(ModalRequest<TResult> request) { _request = request; }

        private System.Threading.CancellationTokenRegistration _tokenRegistration;
        private bool _holdsToken;

        public void HoldTokenRegistration(System.Threading.CancellationTokenRegistration registration)
        {
            // Already settled: a token cancelled before the dialog was asked for runs its callback inside Register.
            if (Resolved)
            {
                registration.Dispose();
                return;
            }
            _tokenRegistration = registration;
            _holdsToken = true;
            ModalTokenRegistrations.Live++;
        }

        // Settled, whichever way: a long-lived token keeps no callback for a dialog that is gone.
        private void ReleaseToken()
        {
            if (!_holdsToken) return;
            _holdsToken = false;
            var registration = _tokenRegistration;
            _tokenRegistration = default;
            ModalTokenRegistrations.Live--;
            registration.Dispose();
        }

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
                ReleaseToken();
                _tcs.TrySetResult(result);
                onClose?.Invoke();
            });
            // Caller customization layers on top of (after) the request's own Bind.
            _request.Configure?.Invoke(screen);
        }

        public bool TryEscape(Action wakePump)
        {
            if (Resolved) return false;
            if (!_request.TryEscape(out var r)) return false;
            Resolved = true;
            ReleaseToken();
            _tcs.TrySetResult(r);
            wakePump?.Invoke();
            return true;
        }

        public void Cancel(Exception ex)
        {
            if (Resolved) return;
            Resolved = true;
            ReleaseToken();
            _tcs.TrySetException(ex);
        }
    }
}
