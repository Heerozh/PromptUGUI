using System;
using LitMotion;
using PromptUGUI.Application;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Drives a single <see cref="Graphic"/> from the owning <see cref="IStateSource"/>'s
    /// <see cref="InteractState"/> stream, on two layers that never mix:
    /// <list type="bullet">
    /// <item>the <b>fill</b> — <c>absolute ?? selectionBase</c>, where <c>selectionBase</c> is the
    /// selected base (Tab/Toggle <c>selectedColor</c>) while the source is selected, else the base
    /// colour. Only the control's <c>targetGraphic</c> has one; it is <see cref="Graphic.color"/> on
    /// an Image and the panel's fill on a procedural surface.</item>
    /// <item>the <b>multiplier</b> — <c>modulate ?? white</c>, on the graphic's
    /// <see cref="CanvasTint">CanvasRenderer colour</see>. Every reactor has one: the targetGraphic and
    /// each fan-out descendant.</item>
    /// </list>
    /// What reaches the screen is the product, which the batcher forms.
    /// </summary>
    /// <remarks>
    /// <para>A fan-out reactor never writes <see cref="Graphic.color"/>: that colour belongs to whoever
    /// set it — the descendant's own <c>color=</c>, a Variant, a theme, or code — and keeping a copy of
    /// it here (multiplied, written back on every state change) is how a hover used to undo all of
    /// them.</para>
    /// <para>The fill's base comes from the owning control's live <c>color=</c> declaration, pushed in
    /// through <see cref="Configure"/> and, for a write from code, <see cref="SetBase"/>. It is NOT
    /// re-read off the graphic: mid-hover the graphic shows the hover ABSOLUTE, and promoting that would
    /// bake it in for good. Peeking the graphic stays the fallback, taken once, for a control that
    /// declares no colour (its built-in bg).</para>
    /// <para>Base/absolute/selected colours may be gradients (landed via <see cref="ColorApplier"/>); a
    /// fill transition with a gradient endpoint snaps instead of fading (no Color-lerp for a vertex
    /// gradient). Modulates are solid, so the multiplier always fades.</para>
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

        // Interaction feedback runs on the UI's clock, not the game's: a pause menu at timeScale 0
        // still has to answer a hover (uGUI's own ColorTint ignores timeScale for the same reason).
        private static readonly IMotionScheduler Clock = MotionScheduler.UpdateIgnoreTimeScale;

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
        private MotionHandle _fillMotion;
        private MotionHandle _tintMotion;

        private void EnsureInit()
        {
            if (_graphic != null) return;
            _graphic = GetComponent<Graphic>();
            _panel = _graphic as ProceduralPanel;
            // Stamped once, on first init = the build frame. A Tab declared isOn="true" must show its
            // selectedColor on frame 1 instead of fading into it. See BornFrame.
            if (_graphic != null) _bornFrame = BornFrame.Capture();

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
        /// null and the first peek stands.
        /// </summary>
        /// <param name="ownsFill">
        /// True for the control's <c>targetGraphic</c>, whose base / absolutes / selected base ARE its
        /// fill. False for a fan-out reactor on a descendant: that graphic's colour belongs to its own
        /// control and is never written here — only the multiplier lands on it.
        /// </param>
        public void Configure(StateColorSet absolutes, StateColorSet modulates, float fade,
            ColorSpec? selectedBase = null, bool selected = false, ColorSpec? authoredBase = null,
            bool ownsFill = true)
        {
            _ownsFill = ownsFill;
            // The declaration wins over the pixels. Marking it captured also skips the fallback peek.
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
                Paint(_source.Current, instant: false);
        }

        /// <summary>
        /// A new base written from code (<c>btn.Color = …</c>) between attribute passes. The control's
        /// setter has already put it on the graphic; this makes it the colour the fill returns to, and
        /// re-asserts the current state over it — a hovered control keeps showing its hoverColor.
        /// </summary>
        internal void SetBase(in ColorSpec spec)
        {
            _baseColor = spec;
            _baseCaptured = true;
            if (_source != null && _graphic != null) PaintFill(StateOf(_source.Current), instant: true);
        }

        /// <summary>
        /// Stops driving this graphic: the fill is left exactly as it is, the multiplier goes back to
        /// identity — nothing else would ever clear it.
        ///
        /// <para>Called when the control's <c>targetGraphic</c> moves elsewhere — a procedural
        /// surface taking over from the Image it retires — and when every state colour is gone (a theme
        /// dropped them; uGUI's ColorTint takes the CanvasRenderer colour back). A reactor still
        /// subscribed would write its old values over the new owner's on the next hover.</para>
        ///
        /// <para>A later <see cref="Configure"/> re-attaches, so a switch back is symmetric.</para>
        /// </summary>
        internal void Detach()
        {
            if (_fillMotion.IsActive()) _fillMotion.TryCancel();
            if (_tintMotion.IsActive()) _tintMotion.TryCancel();
            _sub?.Dispose();
            _sub = null;
            _source = null;
            if (_graphic != null) CanvasTint.Set(_graphic, Color.white);
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
            if (_source != null) Paint(_source.Current, instant: false);
        }

        private Color MultiplierFor(InteractState state) => _modulates.For(state)?.Start ?? Color.white;

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

        // Focus reuses the hover visual (spec §4.3). The composite already folds Focused→Normal in
        // Pointer mode, so this only fires for an actually-directional-focused control.
        private static InteractState StateOf(InteractState state)
            => state == InteractState.Focused ? InteractState.Hover : state;

        private void OnState(InteractState state) => Paint(state, instant: false);

        private void Paint(InteractState state, bool instant)
        {
            if (_graphic == null) return;
            state = StateOf(state);
            // A change in the born frame (before the first rendered frame — e.g. a modal Configure
            // hook) snaps, so the control shows its final state on frame 1 instead of fading in from
            // its base. See BornFrame.
            instant |= TestForceInstant || _fade <= 0f || BornFrame.IsCurrent(_bornFrame);
            PaintFill(state, instant);
            PaintTint(state, instant);
        }

        private void PaintFill(InteractState state, bool instant)
        {
            if (!_ownsFill) return;
            // The fallback base is taken the first time this reactor paints a fill, not when it is
            // installed: a graphic can start out as a fan-out descendant and be promoted to target
            // later (a procedural panel taking over), and only now is its fill the control's.
            if (!_baseCaptured)
            {
                _baseColor = ColorApplier.Peek(_graphic);
                _baseCaptured = true;
            }

            var target = BaseFor(state);
            if (_fillMotion.IsActive()) _fillMotion.TryCancel();

            // A procedural surface keeps its look in its MATERIAL, shared by every panel with the
            // same style so they batch. The fill is a material parameter: tweening it would mint a
            // material per frame through ProceduralMaterialCache, so it snaps — state changes are
            // discrete, and the cache sees one entry per state. (The multiplier still fades.)
            if (_panel != null)
            {
                _panel.SetFill(target);
                _panel.FlushParams();
                return;
            }

            var current = ColorApplier.Peek(_graphic);
            // Already there — the usual case for a modulate-only state, whose fill is the base.
            if (current == target) return;
            // Gradients snap: there's no Color-lerp for a vertex gradient. Solid↔solid (non-transparent)
            // fades. Mirrors the CrossesTransparency snap precedent.
            if (instant || target.IsGradient || current.IsGradient
                || CrossesTransparency(current.Start, target.Start))
            {
                ColorApplier.Apply(_graphic, target);
                return;
            }

            // _graphic 可能在 tween 结束前被销毁（如 Carousel 指示点在 locale/Theme/resize 触发的
            // ReSolve 中被 RebuildIndicator 重建）。LitMotion 的逐帧回调靠 Unity 隐式 bool 判空跳过已
            // 销毁的目标，避免写已销毁对象抛 MissingReferenceException（宿主 OnDestroy 的 TryCancel 在
            // Play 模式延迟销毁时存在竞态，不足以独力兜底）。
            _fillMotion = LMotion.Create(_graphic.color, target.Start, _fade)
                .WithScheduler(Clock)
                .Bind(_graphic, static (c, g) => { if (g) g.color = c; });
        }

        private void PaintTint(InteractState state, bool instant)
        {
            var target = MultiplierFor(state);
            if (_tintMotion.IsActive()) _tintMotion.TryCancel();

            var current = CanvasTint.Get(_graphic);
            if (current == target)
            {
                // Still written: a TMP sub-mesh made since the last write may not have it yet.
                CanvasTint.Set(_graphic, target);
                return;
            }
            if (instant || CrossesTransparency(current, target))
            {
                CanvasTint.Set(_graphic, target);
                return;
            }

            // Same destroyed-target guard as the fill above.
            _tintMotion = LMotion.Create(current, target, _fade)
                .WithScheduler(Clock)
                .Bind(_graphic, static (c, g) => { if (g) CanvasTint.Set(g, c); });
        }

        private void OnDestroy()
        {
            if (_fillMotion.IsActive()) _fillMotion.TryCancel();
            if (_tintMotion.IsActive()) _tintMotion.TryCancel();
            _sub?.Dispose();
            _sub = null;
        }
    }
}
