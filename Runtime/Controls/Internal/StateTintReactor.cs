using System;
using LitMotion;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Drives a single <see cref="Graphic"/>'s colour from the owning <see cref="IStateSource"/>'s
    /// <see cref="InteractState"/> stream. On each state it tweens the graphic toward
    /// <c>(absolute ?? baseColor) × (modulate ?? white)</c>. Absolutes are applied only to the
    /// control's <c>targetGraphic</c>; modulates fan out to every descendant graphic.
    /// </summary>
    /// <remarks>
    /// The base (authored) colour is captured ONCE on first init and never re-captured: a
    /// re-<see cref="Configure"/> (e.g. a Variant ReSolve) must not promote the currently-tinted
    /// colour into the new base. A state with no absolute and no modulate returns the graphic to
    /// its base colour.
    /// </remarks>
    internal sealed class StateTintReactor : MonoBehaviour
    {
        /// <summary>uGUI Selectable default colour fade duration.</summary>
        internal const float DefaultFade = 0.1f;

        /// <summary>
        /// Test seam: when true, every tint is applied synchronously (fade treated as 0) so
        /// EditMode tests can assert the final colour without a frame loop. Production default
        /// is the per-instance <see cref="_fade"/> (0.1f). Never set outside tests.
        /// </summary>
        internal static bool TestForceInstant;

        private Graphic _graphic;
        private bool _baseCaptured;
        private Color _baseColor = Color.white;

        private StateColorSet _absolutes;   // per-state ABSOLUTE base override (targetGraphic only)
        private StateColorSet _modulates;   // per-state relative MULTIPLIER (null entry = white identity)
        private float _fade = DefaultFade;

        private IDisposable _sub;
        private MotionHandle _handle;

        private void EnsureInit()
        {
            if (_graphic != null) return;
            _graphic = GetComponent<Graphic>();
            if (_graphic != null && !_baseCaptured)
            {
                _baseColor = _graphic.color;
                _baseCaptured = true;
            }

            var source = GetComponentInParent<IStateSource>();
            if (source != null)
                _sub = source.OnState.Subscribe(OnState);
        }

        /// <summary>
        /// (Re)set the per-state absolute overrides + relative multipliers + fade. Safe to call
        /// repeatedly (Variant ReSolve): the base colour stays captured from the first init.
        /// </summary>
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade)
        {
            EnsureInit();
            _absolutes = absolutes;
            _modulates = modulates;
            _fade = fade;
        }

        private Color MultiplierFor(InteractState state) => _modulates.For(state) ?? Color.white;
        private Color BaseFor(InteractState state) => _absolutes.For(state) ?? _baseColor;

        private void OnState(InteractState state)
        {
            if (_graphic == null) return;
            var target = BaseFor(state) * MultiplierFor(state);

            if (_handle.IsActive()) _handle.TryCancel();

            if (TestForceInstant || _fade <= 0f)
            {
                _graphic.color = target;
                return;
            }

            _handle = LMotion.Create(_graphic.color, target, _fade)
                .Bind(_graphic, static (c, g) => g.color = c);
        }

        private void OnDestroy()
        {
            if (_handle.IsActive()) _handle.TryCancel();
            _sub?.Dispose();
            _sub = null;
        }
    }
}
