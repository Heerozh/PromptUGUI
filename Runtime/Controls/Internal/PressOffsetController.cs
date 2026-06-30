using System;
using R3;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Drives a content-holder's <see cref="RectTransform.anchoredPosition"/> from the owning
    /// <see cref="IStateSource"/>'s <see cref="InteractState"/> stream. On each state it snaps the
    /// holder to <c>offsets.For(state)</c> — instant, no tween (physical-button feel, pixel-perfect;
    /// authored offsets are integer pixels). One per control (lives on the holder), unlike the
    /// per-graphic <see cref="StateTintReactor"/>.
    /// </summary>
    internal sealed class PressOffsetController : MonoBehaviour
    {
        private RectTransform _holder;
        private StateOffsetSet _offsets;
        private IStateSource _source;
        private IDisposable _sub;

        /// <summary>
        /// (Re)set the per-state offsets. Safe to call repeatedly (Variant ReSolve). Assigns offsets
        /// BEFORE the first subscription so the synchronous replay of the source's current state sees
        /// them (mirrors <see cref="StateTintReactor.Configure"/>).
        /// </summary>
        public void Configure(StateOffsetSet offsets)
        {
            _offsets = offsets;
            var firstInit = _holder == null;
            EnsureInit();
            // On a re-Configure the subscription does NOT replay; repaint the current state explicitly.
            if (!firstInit && _source != null) OnState(_source.Current);
        }

        private void EnsureInit()
        {
            if (_holder != null) return;
            _holder = (RectTransform)transform;
            // includeInactive: the source control may sit on a hidden (SetActive(false)) page at Open.
            _source = GetComponentInParent<IStateSource>(true);
            if (_source != null)
                _sub = _source.OnState.Subscribe(OnState);   // replays Current synchronously
        }

        private void OnState(InteractState state)
        {
            if (_holder != null)
                _holder.anchoredPosition = _offsets.For(state);
        }

        private void OnDestroy()
        {
            _sub?.Dispose();
            _sub = null;
        }
    }
}
