using System;
using LitMotion;
using PromptUGUI.Application;
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
    /// The base (authored) colour comes from the owning control's live <c>color=</c> declaration,
    /// pushed in through <see cref="Configure"/>. It is NOT re-read off the graphic: on a
    /// re-<see cref="Configure"/> (a Variant / theme / resize ReSolve) the graphic may be showing a
    /// TINT — the control is hovered — and promoting that would bake the hover colour in for good.
    /// Peeking the graphic stays the fallback, captured once, for a graphic nobody authored a colour
    /// for (a control's built-in bg, or a descendant that only carries the modulate fan-out).
    /// A state with no absolute and no modulate returns the graphic to its base colour. Base/absolute/selected colours may be gradients (landed via
    /// <see cref="ColorApplier"/>); a transition with a gradient endpoint snaps instead of fading
    /// (no Color-lerp for a vertex gradient — see <c>OnState</c>). Modulates stay solid.
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
        private ProceduralPanel _panel;     // non-null ⇒ the target draws procedurally
        private bool _baseCaptured;
        private int _bornFrame = int.MinValue;
        private ColorSpec _baseColor = ColorSpec.Solid(Color.white);
        private ColorSpec? _selectedBase;   // base while the source is selected (Tab/Toggle isOn); null ⇒ none
        private bool _selected;             // pushed by the owning control via SetSelected
        private bool _ownsFill = true;      // false ⇒ fan-out reactor on a descendant: multiplier only

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
            _panel = _graphic as ProceduralPanel;
            if (_graphic != null)
            {
                // Fallback capture: only for a graphic whose owner pushed no authored colour.
                if (!_baseCaptured)
                {
                    _baseColor = ColorApplier.Peek(_graphic);
                    _baseCaptured = true;
                }
                // Stamped unconditionally, and NOT inside the capture above — a control that
                // declares color= arrives with _baseCaptured already true, and hanging the stamp off
                // that would leave _bornFrame unset. The born-frame gate would then never fire and a
                // Tab declared isOn="true" would tween toward its selectedColor instead of showing
                // it on frame 1. This runs once: EnsureInit returns early once _graphic is set.
                _bornFrame = BornFrame.Capture();   // first init = the build frame
            }

            // includeInactive: the source control may be on a TabBar-bound page that is hidden
            // (SetActive(false)) at Open — without this the *Modulate fan-out would silently never
            // subscribe (EnsureInit is guarded, so it would stay dead even after the page is shown).
            _source = GetComponentInParent<IStateSource>(true);
            if (_source != null)
                _sub = _source.OnState.Subscribe(OnState);
        }

        /// <summary>
        /// (Re)set the per-state absolute overrides + relative multipliers + fade, and the authored
        /// base. Safe to call repeatedly (Variant / theme / resize ReSolve): pass the control's
        /// current <c>color=</c> as <paramref name="authoredBase"/> and the base follows it; pass
        /// null and the first-init Peek stands.
        /// </summary>
        /// <param name="ownsFill">
        /// True for the control's <c>targetGraphic</c>, whose base / absolutes / selected base ARE its
        /// fill. False for a fan-out reactor on a descendant: that graphic's fill (a child
        /// <c>&lt;Frame color=&gt;</c>, or none at all for a hollow border Frame) belongs to its own
        /// control and is never rewritten here — only the multiplier lands on it.
        /// </param>
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade,
            ColorSpec? selectedBase = null, bool selected = false, ColorSpec? authoredBase = null,
            bool ownsFill = true)
        {
            _ownsFill = ownsFill;
            // The declaration wins over the pixels. Marking it captured also makes EnsureInit skip
            // its Peek on first init — the authored value is already the right answer there.
            if (authoredBase.HasValue)
            {
                _baseColor = authoredBase.Value;
                _baseCaptured = true;
            }

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
            // Re-attach after a Detach: EnsureInit returns early once _graphic is known, so the
            // subscription it normally makes would never be remade.
            if (!firstInit && _source == null)
            {
                _source = GetComponentInParent<IStateSource>(true);
                if (_source != null) _sub = _source.OnState.Subscribe(OnState);
            }

            // On a *re*-Configure (Variant / Theme / window-resize ReSolve) the OnState subscription
            // does NOT replay and the broadcaster's state value is unchanged — yet ControlAttributeApplier
            // has just reset the graphic to its authored base colour. Without an explicit repaint the
            // state tint (e.g. a Selected tab's selectedColor) silently vanishes on the next resize.
            // First install is already painted by the subscription replay above, so only repaint here.
            if (!firstInit && _source != null)
                OnState(_source.Current);
        }

        /// <summary>
        /// Stops driving this graphic and leaves it exactly as it is.
        ///
        /// <para>Called when the control's <c>targetGraphic</c> moves elsewhere — a procedural
        /// surface taking over from the Image it retires. The reactor that used to own the Image is
        /// still subscribed to the state stream, and on the next hover it writes the old theme's
        /// colour back over the retirement: the Image reappears at full alpha in the previous skin's
        /// colour, drawn as a hard rectangle behind the rounded surface. Un-reproducible if you open
        /// straight into the second skin, which is why it survived until someone switched themes.</para>
        ///
        /// <para>A later <see cref="Configure"/> re-attaches, so a switch back is symmetric.</para>
        /// </summary>
        internal void Detach()
        {
            if (_handle.IsActive()) _handle.TryCancel();
            _sub?.Dispose();
            _sub = null;
            _source = null;
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

        private Color MultiplierFor(InteractState state) => _modulates.For(state)?.Top ?? Color.white;

        private ColorSpec BaseFor(InteractState state)
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
            // Focus reuses the hover visual (spec §4.3). The composite already folds Focused→Normal in
            // Pointer mode, so this only fires for an actually-directional-focused control.
            if (state == InteractState.Focused) state = InteractState.Hover;

            if (_panel != null)
            {
                ApplyToPanel(state);
                return;
            }

            var target = BaseFor(state).Multiply(MultiplierFor(state));   // premultiply modulate into both stops

            if (_handle.IsActive()) _handle.TryCancel();

            var current = ColorApplier.Peek(_graphic);
            // Gradients snap: there's no Color-lerp for a vertex gradient. Solid↔solid (non-transparent)
            // keeps the existing fade. Mirrors the CrossesTransparency snap precedent. A change in the
            // born frame (before the first rendered frame — e.g. a modal Configure hook) also snaps, so
            // the control shows its final state on frame 1 instead of fading in from its base. See BornFrame.
            if (TestForceInstant || _fade <= 0f
                || target.IsGradient || current.IsGradient
                || CrossesTransparency(current.Top, target.Top)
                || BornFrame.IsCurrent(_bornFrame))
            {
                ColorApplier.Apply(_graphic, target);
                return;
            }

            // _graphic 可能在 tween 结束前被销毁（如 Carousel 指示点在 locale/Theme/resize 触发的
            // ReSolve 中被 RebuildIndicator 重建）。LitMotion 的逐帧回调靠 Unity 隐式 bool 判空跳过已
            // 销毁的目标，避免写已销毁对象抛 MissingReferenceException（宿主 OnDestroy 的 TryCancel 在
            // Play 模式延迟销毁时存在竞态，不足以独力兜底）。
            _handle = LMotion.Create(_graphic.color, target.Top, _fade)
                .Bind(_graphic, static (c, g) => { if (g) g.color = c; });
        }

        /// <summary>
        /// A procedural surface splits the two halves instead of premultiplying them, because they
        /// land in two different places.
        ///
        /// <para>The panel's authored look lives in its MATERIAL and <c>Graphic.color</c> is a
        /// multiplier layered on top (<c>col *= IN.color</c> in the shader) — the split that lets
        /// every panel sharing a style share one material and keep batching. Premultiplying the way
        /// an Image needs would apply the base twice: once as the fill, once as the vertex tint, so
        /// <c>color="#3366ff"</c> renders as its own square. And an "absolute" hoverColor written to
        /// a multiplier channel is not absolute at all — it darkens whatever is underneath, which on
        /// glass means tinting the blurred backdrop rather than the pane.</para>
        ///
        /// <para>A fan-out reactor on a DESCENDANT panel (<c>_ownsFill</c> false) leaves the fill
        /// alone entirely: the child's colour is the child's, and absolutes never fan out. Writing
        /// the peeked base there painted every accent bar and hollow bracket Frame inside a
        /// <c>&lt;Btn pressedModulate&gt;</c> opaque white.</para>
        ///
        /// <para>So: absolutes drive the fill, modulates stay on the vertex colour. The one thing
        /// lost is the fade on an absolute change — the fill is a material parameter, and tweening it
        /// per frame would mint a material per frame through <c>ProceduralMaterialCache</c>. State
        /// changes are discrete and infrequent, so the cache sees one entry per state, not one per
        /// frame. Modulates still fade, since they are pure vertex colour.</para>
        /// </summary>
        private void ApplyToPanel(InteractState state)
        {
            if (_handle.IsActive()) _handle.TryCancel();

            if (_ownsFill)
            {
                _panel.SetFill(BaseFor(state));
                _panel.FlushParams();
            }

            var multiplier = MultiplierFor(state);
            var current = _graphic.color;
            if (TestForceInstant || _fade <= 0f
                || CrossesTransparency(current, multiplier)
                || BornFrame.IsCurrent(_bornFrame))
            {
                _graphic.color = multiplier;
                return;
            }

            _handle = LMotion.Create(current, multiplier, _fade)
                .Bind(_graphic, static (c, g) => { if (g) g.color = c; });
        }

        private void OnDestroy()
        {
            if (_handle.IsActive()) _handle.TryCancel();
            _sub?.Dispose();
            _sub = null;
        }
    }
}
