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
    /// <c>baseColor * multiplier[state]</c> (component-wise) — the uGUI ColorTint behaviour,
    /// but fanned out per-graphic instead of a single <c>targetGraphic</c>.
    /// </summary>
    /// <remarks>
    /// The base (authored) colour is captured ONCE on first init and never re-captured: a
    /// re-<see cref="Configure"/> (e.g. a Variant ReSolve changing a multiplier) must not promote
    /// the currently-tinted colour into the new base. Multipliers default to white (identity), so
    /// a state with no explicit multiplier returns the graphic to its base colour.
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

        private Color _hover = Color.white;
        private Color _pressed = Color.white;
        private Color _selected = Color.white;
        private Color _disabled = Color.white;
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
        /// (Re)set the per-state multipliers + fade. A null multiplier keeps Normal (white =
        /// identity) for that state. Safe to call repeatedly (Variant ReSolve): the base colour
        /// stays captured from the first init.
        /// </summary>
        public void Configure(Color? hover, Color? pressed, Color? selected, Color? disabled, float fade)
        {
            EnsureInit();
            _hover = hover ?? Color.white;
            _pressed = pressed ?? Color.white;
            _selected = selected ?? Color.white;
            _disabled = disabled ?? Color.white;
            _fade = fade;
        }

        private Color MultiplierFor(InteractState state) => state switch
        {
            InteractState.Hover => _hover,
            InteractState.Pressed => _pressed,
            InteractState.Selected => _selected,
            InteractState.Disabled => _disabled,
            _ => Color.white,
        };

        private void OnState(InteractState state)
        {
            if (_graphic == null) return;
            var target = _baseColor * MultiplierFor(state);

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
