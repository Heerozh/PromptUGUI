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
    /// <c>(absolute ?? selectionBase) × (modulate ?? white)</c>, where <c>selectionBase</c> is the
    /// selected base (Tab/Toggle <c>selectedColor</c>) while the source is selected, else the captured
    /// base colour. Absolutes and the selected base are applied only to the control's
    /// <c>targetGraphic</c>; modulates fan out to every descendant graphic.
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
        private Color? _selectedBase;       // base while the source is selected (Tab/Toggle isOn); null ⇒ none
        private bool _selected;             // pushed by the owning control via SetSelected

        private StateColorSet _absolutes;   // per-state ABSOLUTE base override (targetGraphic only)
        private StateColorSet _modulates;   // per-state relative MULTIPLIER (null entry = white identity)
        private float _fade = DefaultFade;

        private IStateSource _source;
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

            // includeInactive: the source control may be on a TabBar-bound page that is hidden
            // (SetActive(false)) at Open — without this the *Modulate fan-out would silently never
            // subscribe (EnsureInit is guarded, so it would stay dead even after the page is shown).
            _source = GetComponentInParent<IStateSource>(true);
            if (_source != null)
                _sub = _source.OnState.Subscribe(OnState);
        }

        /// <summary>
        /// (Re)set the per-state absolute overrides + relative multipliers + fade. Safe to call
        /// repeatedly (Variant ReSolve): the base colour stays captured from the first init.
        /// </summary>
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade, Color? selectedBase = null, bool selected = false)
        {
            // Assign the colour sets BEFORE EnsureInit subscribes: the OnState subscription replays
            // the source's current state synchronously, so if the control is already in a non-Normal
            // state at first install (e.g. a Tab declared isOn="true", shown Selected at Open and never
            // re-toggled) that first replay must see the colours — otherwise it paints the base colour
            // and, with no later state change to correct it, the state colour never appears.
            _absolutes = absolutes;
            _modulates = modulates;
            _fade = fade;
            _selectedBase = selectedBase;
            _selected = selected;

            var firstInit = _graphic == null;
            EnsureInit();

            // On a *re*-Configure (Variant / Theme / window-resize ReSolve) the OnState subscription
            // does NOT replay and the broadcaster's state value is unchanged — yet ControlAttributeApplier
            // has just reset the graphic to its authored base colour. Without an explicit repaint the
            // state tint (e.g. a Selected tab's selectedColor) silently vanishes on the next resize.
            // First install is already painted by the subscription replay above, so only repaint here.
            if (!firstInit && _source != null)
                OnState(_source.Current);
        }

        /// <summary>
        /// Pushed by the owning Tab/Toggle on every isOn change (and re-asserted on ReSolve): selects
        /// the selection-aware base. Repaints the current state so a selected control at rest shows
        /// its selected base immediately. Read as a push (not from the broadcaster) because the
        /// broadcaster suppresses Selected under a transient state and does not re-emit on isOn-only
        /// changes.
        /// </summary>
        public void SetSelected(bool on)
        {
            _selected = on;
            if (_source != null) OnState(_source.Current);
        }

        private Color MultiplierFor(InteractState state) => _modulates.For(state) ?? Color.white;

        private Color BaseFor(InteractState state)
            => _absolutes.For(state)
               ?? ((_selected && _selectedBase.HasValue) ? _selectedBase.Value : _baseColor);

        /// <summary>
        /// True when a colour transition has a fully-transparent endpoint. Such a transition must
        /// SNAP, not tween: a straight RGBA lerp between a transparent colour and an opaque one drags
        /// RGB through black (a visible flicker — e.g. a transparent Tab fading into its selectedColor
        /// on select). Opaque ↔ opaque transitions (hover / press feedback) still fade.
        /// </summary>
        internal static bool CrossesTransparency(Color from, Color to) => from.a <= 0f || to.a <= 0f;

        private void OnState(InteractState state)
        {
            if (_graphic == null) return;
            var target = BaseFor(state) * MultiplierFor(state);

            if (_handle.IsActive()) _handle.TryCancel();

            if (TestForceInstant || _fade <= 0f || CrossesTransparency(_graphic.color, target))
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
