using System;
using System.Globalization;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// A cooldown sweep (spec 2026-10-13): a dark veil over a skill icon that the sweep edge
    /// uncovers, or — with <c>thickness</c> — an arc around it. The control PB-D6 of the Progress
    /// spec set aside: the sweep is radial, and the control keeps its own clock and says when it is
    /// ready, which a display-only <c>&lt;Progress&gt;</c> never does.
    ///
    /// <para>The FILL is the primary surface, as on <c>&lt;Progress&gt;</c>. A colour fill is always
    /// the SDF surface — <c>Image</c> ignores <c>Type.Filled</c> without a sprite, so the SDF is the
    /// only place a colour can be swept — and <c>value</c> becomes a sector cut inside it
    /// (<see cref="ProceduralPanel.SetRadialCut"/>): the whole shape is cut, so a square icon is swept
    /// corners and all, and the glow / border / haze follow the cut edges. A bitmap fill sweeps
    /// through <c>Image.Filled</c> + <c>Radial360</c> instead.</para>
    ///
    /// <para>Fixed layers <c>Bg</c> (the uncut track) → <c>Fill</c> → <c>Label</c> (the countdown),
    /// built once and only ever toggled. Nothing catches a click: a Cooldown sits on a
    /// <c>&lt;Btn&gt;</c>. No glass — a glass veil samples the scene behind the canvas, not the icon
    /// under it (§5.10).</para>
    ///
    /// <para>The clock (§5.6): <see cref="Start(float, float)"/> records when the cooldown ends and
    /// <see cref="CooldownClock"/> advances it once a frame — also while it is hidden — until it goes
    /// ready. Every way the remaining fraction changes (the clock, <see cref="Stop"/>, a pushed
    /// <see cref="Value"/>) meets in one place, which is where a flip between cooling and ready
    /// becomes <see cref="OnCooling"/> / <see cref="OnReady"/>.</para>
    /// </summary>
    public sealed class Cooldown : ProceduralControl
    {
        /// <summary>The fill when none is authored: a dark veil, so a bare Cooldown is a usable overlay.</summary>
        internal static readonly Color DefaultFillColor = new(0f, 0f, 0f, 0.6f);

        private UnityImage _bg;
        private UnityImage _fill;
        private TMP_Text _label;

        // Whether a sprite / colour was authored for the layer; ReconcileLayers derives visibility
        // from them, and _fillSprite decides between the SDF sweep and Image.Filled.
        private bool _bgSprite, _bgColor, _fillSprite;

        private float _value;
        private float _originTurns;
        private UnityImage.Origin360 _origin360 = UnityImage.Origin360.Top;
        private bool _clockwise = true;
        private bool _drawElapsed;
        private float _thickness;
        private bool _roundCap;
        private bool _warnedGlass;

        // The clock: when a running cooldown ends, on which clock, out of how long.
        private bool _running;
        private bool _scaled = true;
        private float _total;
        private float _end;

        private readonly Subject<Unit> _ready = new();
        private readonly Subject<Unit> _cooling = new();
        private bool _disposed;

        // The countdown label.
        private bool _countdown;
        private float _tenthsBelow = 1f;
        private string _fontType = "default";
        private string _labelText = "";
        private Func<float, string> _formatter;

        private protected override GameObject SurfaceHost => _fill.gameObject;

        /// <summary>
        /// The REMAINING fraction: 1 = the cooldown has just started, 0 = ready. Clamped; NaN is 0.
        /// Setting it stops the clock — this is the push-a-value spelling, the same as
        /// <c>&lt;Progress&gt;</c>'s.
        /// </summary>
        [UIAttr, Preserve]
        public float Value
        {
            get => _value;
            set
            {
                StopClock();
                SetFraction(float.IsNaN(value) ? 0f : Mathf.Clamp01(value));
            }
        }

        /// <summary>
        /// While the clock runs this never equals an XML literal, so a Variant ReSolve can never
        /// write <c>value=</c> back over a running cooldown — not even in the frame it was started,
        /// when the fraction is still exactly 1.
        /// </summary>
        internal override string PeekRuntimeState() =>
            _running ? "running" : _value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Runs a full cooldown of <paramref name="total"/> seconds.</summary>
        public void Start(float total) => Start(total, total);

        /// <summary>
        /// Runs a cooldown of <paramref name="total"/> seconds that has <paramref name="remaining"/>
        /// left — what a reconnect or a server sync calls, as often as it likes: a cooldown that is
        /// already running is re-timed, not restarted, and does not say <see cref="OnCooling"/>
        /// again. <paramref name="remaining"/> beyond <paramref name="total"/> is clamped; nothing
        /// left (or no total) is <see cref="Stop"/>.
        /// </summary>
        public void Start(float total, float remaining)
        {
            if (!(total > 0f) || !(remaining > 0f))
            {
                Stop();
                return;
            }
            remaining = Mathf.Min(remaining, total);
            _total = total;
            _end = CooldownClock.Now(_scaled) + remaining;
            _running = true;
            CooldownClock.Add(this);
            SetFraction(remaining / total);
        }

        /// <summary>Ready now: stops the clock and clears the sweep.</summary>
        public void Stop()
        {
            StopClock();
            SetFraction(0f);
        }

        /// <summary>Whether anything is left to wait for — <c>Value &gt; 0</c>, clock or no clock.</summary>
        public bool IsCooling => _value > 0f;

        /// <summary>Whether the clock is driving the sweep (as opposed to a pushed <see cref="Value"/>).</summary>
        public bool IsRunning => _running;

        /// <summary>The running cooldown's length in seconds; 0 when the clock is not running.</summary>
        public float Total => _running ? _total : 0f;

        /// <summary>Seconds left on the running cooldown; 0 when the clock is not running.</summary>
        public float Remaining => _running ? Mathf.Max(0f, _end - CooldownClock.Now(_scaled)) : 0f;

        /// <summary>
        /// The cooldown has run out (remaining went from above 0 to 0) — by the clock, by
        /// <see cref="Stop"/> or by a pushed <see cref="Value"/>. A UI event: what a skill may do is
        /// the game's own state to decide.
        /// </summary>
        public Observable<Unit> OnReady => _ready;

        /// <summary>A cooldown has begun (remaining went from 0 to above 0).</summary>
        public Observable<Unit> OnCooling => _cooling;

        /// <summary>
        /// Writes the countdown text from the seconds left; null restores the default
        /// (<see cref="FormatRemaining"/>). For a localised spelling such as "2m".
        /// </summary>
        public Func<float, string> CountdownFormatter
        {
            set
            {
                _formatter = value;
                RefreshLabel();
            }
        }

        /// <summary>
        /// The clock: <c>scaled</c> (the default — a paused game pauses its cooldowns) or
        /// <c>unscaled</c>. Switching mid-run keeps the seconds that are left.
        /// </summary>
        [UIAttr("time"), Preserve]
        public string TimeMode
        {
            set
            {
                var scaled = (value ?? "").Trim() switch
                {
                    "" or "scaled" => true,
                    "unscaled" => false,
                    _ => throw new ParseException($"time=\"{value}\": expected scaled / unscaled"),
                };
                if (scaled == _scaled) return;
                if (_running)
                {
                    var left = _end - CooldownClock.Now(_scaled);
                    _scaled = scaled;
                    _end = CooldownClock.Now(_scaled) + left;
                }
                else _scaled = scaled;
            }
        }

        /// <summary>Shows the seconds left in the middle while the clock runs.</summary>
        [UIAttr, Preserve]
        public bool Countdown
        {
            set
            {
                _countdown = value;
                _label.gameObject.SetActive(value);
                RefreshLabel();
            }
        }

        /// <summary>Below this many seconds the countdown shows tenths; 0 = never.</summary>
        [UIAttr, Preserve]
        public float TenthsBelow
        {
            set
            {
                if (float.IsNaN(value) || value < 0f)
                    throw new ParseException($"tenthsBelow=\"{value}\": expected a number of seconds ≥ 0");
                _tenthsBelow = value;
                RefreshLabel();
            }
        }

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        [UIAttr("fontSize"), Preserve]
        public float FontSize
        {
            set => _label.fontSize = value;
        }

        /// <summary>Countdown colour (token / hex / gradient / <c>/alpha</c>); white by default.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => LabelColorApplier.Apply(_label, value);
        }

        private void ApplyFont() => FontApplier.Apply(_label, _fontType);

        private void StopClock()
        {
            if (!_running) return;
            _running = false;
            CooldownClock.Remove(this);
        }

        /// <summary>One frame of the clock (<see cref="CooldownClock"/>): sweep on, or go ready.</summary>
        internal void Advance()
        {
            // A GameObject destroyed without Dispose (a scene unloaded under an open Screen) has no
            // layers left to draw on.
            if (!_running || GameObject == null)
            {
                _running = false;
                CooldownClock.Remove(this);
                return;
            }
            var left = _end - CooldownClock.Now(_scaled);
            if (left > 0f)
            {
                SetFraction(left / _total);
                return;
            }
            StopClock();
            SetFraction(0f);
        }

        /// <summary>
        /// The one place the remaining fraction changes. Hidden or not it is cheap — a hidden panel
        /// only stores the cut and draws it when shown — and it is where a flip between cooling and
        /// ready becomes an event.
        /// </summary>
        private void SetFraction(float fraction)
        {
            var wasCooling = _value > 0f;
            _value = fraction;
            ReconcileFill();
            RefreshLabel();

            var cooling = _value > 0f;
            if (cooling == wasCooling || _disposed) return;
            if (cooling) _cooling.OnNext(Unit.Default);
            else _ready.OnNext(Unit.Default);
        }

        /// <summary>
        /// The countdown, written only when the text changes — a TMP rebuild is far dearer than the
        /// sweep's four vertices, and the text changes once a second (ten times a second for tenths).
        /// </summary>
        private void RefreshLabel()
        {
            if (_label == null) return;
            var text = _countdown && _running ? Format(Remaining) : "";
            if (text == _labelText) return;
            _labelText = text;
            _label.text = text;
            LabelWritesForTests++;
        }

        internal int LabelWritesForTests { get; private set; }

        private string Format(float seconds) =>
            _formatter != null ? _formatter(seconds) ?? "" : FormatRemaining(seconds, _tenthsBelow);

        /// <summary>
        /// The default countdown (§5.7), always rounded UP so the label reaches 0 exactly when the
        /// cooldown does: tenths below <paramref name="tenthsBelow"/> seconds (<c>0.43</c> → "0.5"),
        /// whole seconds below a minute (<c>7.2</c> → "8"), <c>m:ss</c> from there (<c>89.4</c> →
        /// "1:30"). Digits only, so no translation is involved.
        /// </summary>
        internal static string FormatRemaining(float seconds, float tenthsBelow)
        {
            if (!(seconds > 0f)) return "";
            // The epsilon keeps a value that is exactly on a step (7 s, 0.4 s) from rounding past it.
            const float Epsilon = 1e-4f;
            // …and the floor keeps a last sliver of a frame from showing a 0 that is not ready yet.
            if (seconds < tenthsBelow)
            {
                var tenths = Mathf.Max(1f, Mathf.Ceil(seconds * 10f - Epsilon)) / 10f;
                return tenths.ToString("0.0", CultureInfo.InvariantCulture);
            }
            var whole = Mathf.Max(1, (int)Mathf.Ceil(seconds - Epsilon));
            if (whole < 60) return whole.ToString(CultureInfo.InvariantCulture);
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                   (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Where the sweep edge starts (and ends): <c>top</c> / <c>right</c> / <c>bottom</c> / <c>left</c>.</summary>
        [UIAttr, Preserve]
        public string Origin
        {
            set
            {
                (_originTurns, _origin360) = (value ?? "").Trim() switch
                {
                    "" or "top" => (0f, UnityImage.Origin360.Top),
                    "right" => (0.25f, UnityImage.Origin360.Right),
                    "bottom" => (0.5f, UnityImage.Origin360.Bottom),
                    "left" => (0.75f, UnityImage.Origin360.Left),
                    _ => throw new ParseException(
                        $"origin=\"{value}\": expected top / right / bottom / left"),
                };
                ReconcileFill();
            }
        }

        /// <summary>The way the sweep edge travels while the cooldown runs.</summary>
        [UIAttr, Preserve]
        public bool Clockwise
        {
            set
            {
                _clockwise = value;
                ReconcileFill();
            }
        }

        /// <summary>
        /// Which part is drawn: <c>remaining</c> (the part not yet swept — a veil that shrinks, the
        /// default) or <c>elapsed</c> (the part already swept — an arc that grows).
        /// </summary>
        [UIAttr, Preserve]
        public string Draw
        {
            set
            {
                _drawElapsed = (value ?? "").Trim() switch
                {
                    "" or "remaining" => false,
                    "elapsed" => true,
                    _ => throw new ParseException($"draw=\"{value}\": expected remaining / elapsed"),
                };
                ReconcileFill();
            }
        }

        /// <summary>A ring this many px wide along the shape's edge instead of the solid shape; the track follows.</summary>
        [UIAttr, Preserve]
        public string Thickness
        {
            set
            {
                _thickness = ProceduralValueParser.Pixels(value, "thickness");
                ApplyRing();
                ReconcileFill();
            }
        }

        /// <summary>
        /// The ends of a ring: <c>flat</c> or <c>round</c>. Round ends are exact only on a circular
        /// ring; any other shape is drawn with flat ends and a warning (§5.2).
        /// </summary>
        [UIAttr, Preserve]
        public string Cap
        {
            set
            {
                _roundCap = (value ?? "").Trim() switch
                {
                    "" or "flat" => false,
                    "round" => true,
                    _ => throw new ParseException($"cap=\"{value}\": expected flat / round"),
                };
                ReconcileFill();
            }
        }

        /// <summary>Fill bitmap — swept by <c>Image.Filled</c>. <c>""</c> / <c>none</c> = no bitmap: the SDF draws the fill.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Fill
        {
            set
            {
                _fillSprite = !(string.IsNullOrEmpty(value) || value == "none");
                _fill.sprite = _fillSprite ? UI.ResolveSprite(value) : null;
                ReconcileFill();
            }
        }

        /// <summary>Fill colour: token / <c>/alpha</c> / gradient. Defaults to a dark veil.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string FillColor
        {
            set
            {
                var spec = string.IsNullOrWhiteSpace(value)
                    ? ColorSpec.Solid(DefaultFillColor)
                    : UI.Theme.ResolveSpec(value);
                ColorApplier.Apply(_fill, spec);
                Surface.SetFill(spec);
            }
        }

        /// <summary>The track bitmap: the whole (uncut) shape under the fill.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Bg
        {
            set
            {
                _bgSprite = !(string.IsNullOrEmpty(value) || value == "none");
                _bg.sprite = _bgSprite ? UI.ResolveSprite(value) : null;
                if (_bgSprite) ProceduralBuilders.AutoSlice(_bg);
                ReconcileLayers();
            }
        }

        /// <summary>The track colour. <c>""</c> turns a colour track back off.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string BgColor
        {
            set
            {
                _bgColor = !(string.IsNullOrWhiteSpace(value) || value == "none");
                if (_bgColor)
                {
                    var spec = UI.Theme.ResolveSpec(value);
                    ColorApplier.Apply(_bg, spec);
                    BgSurface.SetFill(spec);
                }
                ReconcileLayers();
            }
        }

        // The colour track's own surface: it takes the shape (radius) and the ring, never a cut.
        private ProceduralSurface _bgSurface;
        private ProceduralSurface BgSurface => _bgSurface ??= AddInnerSurface(_bg.gameObject);

        /// <summary>
        /// Held until <see cref="OnAfterApply"/>, as on <c>&lt;Progress&gt;</c>: the shape goes to the
        /// fill and to a colour track, and whether the fill is a bitmap is only known once every
        /// setter of the pass has run.
        /// </summary>
        private protected override void DeclareRadius(RadiusSpec radius) { }

        internal override void OnAfterApply()
        {
            RouteShape();
            RefuseGlass();
            base.OnAfterApply();
            ApplyRing();
            ProceduralBuilders.AutoSlice(_bg);
            ReconcileLayers();
            ReconcileFill();
        }

        /// <summary>
        /// The fill's surface is declared whenever the fill is not a bitmap — with or without a
        /// radius — because only the SDF can sweep a colour. A colour track goes procedural when it
        /// has a shape to take (a radius or a ring); a bitmap track keeps its baked shape.
        /// </summary>
        private void RouteShape()
        {
            var radius = DeclaredRadius;
            if (!_fillSprite || radius.HasValue)
            {
                var r = radius ?? RadiusSpec.Zero;
                Surface.Declare(p => p.SetRadius(r));
            }
            if (_bgColor && !_bgSprite && (radius.HasValue || _thickness > 0f))
            {
                var r = radius ?? RadiusSpec.Zero;
                BgSurface.Declare(p => p.SetRadius(r));
            }
        }

        /// <summary>
        /// §5.10: glass is never drawn. The declaration has already written the panel; turning it
        /// back off before the surface reconciles keeps the fill on the opaque shader, which is the
        /// one that can sweep.
        /// </summary>
        private void RefuseGlass()
        {
            var panel = SurfacePanelOrNull;
            if (panel == null || !panel.IsGlass) return;
            panel.SetGlass(false);
            if (_warnedGlass) return;
            _warnedGlass = true;
            UILog.Warn(this,
                "PromptUGUI: <Cooldown glass> is not supported — glass samples the scene behind the " +
                "canvas, not the icon under the veil. The fill is drawn opaque (PUI-CD-GLASS).");
        }

        private void ApplyRing()
        {
            if (SurfaceIsDrawing) SurfacePanelOrNull.SetRingWidth(_thickness);
            if (_bgSurface != null && _bgSurface.IsActive) _bgSurface.Panel.SetRingWidth(_thickness);
        }

        /// <summary>A layer is shown when a sprite or a colour is authored for it — computed, so <c>""</c> turns it off again.</summary>
        private void ReconcileLayers()
        {
            _bg.gameObject.SetActive(_bgSprite || _bgColor);
        }

        /// <summary>
        /// Folds <c>origin</c> / <c>clockwise</c> / <c>draw</c> and the remaining fraction into what
        /// the fill draws (§5.1). The sweep edge has travelled <c>s = 1 − r</c> of a turn from the
        /// origin; <c>remaining</c> is the part <c>[s, 1]</c> ahead of it, <c>elapsed</c> the part
        /// <c>[0, s]</c> behind it, both measured the way the edge travels. A sector is symmetric
        /// about its centre line, so the SDF needs only that centre and the half-angle; the bitmap
        /// path hands Unity an origin, a direction and an amount.
        /// </summary>
        private void ReconcileFill()
        {
            if (_fill == null) return;
            var drawn = _drawElapsed ? 1f - _value : _value;

            if (SurfaceIsDrawing)
            {
                var swept = 1f - _value;
                var centre = _drawElapsed ? swept * 0.5f : (swept + 1f) * 0.5f;
                var turns = _originTurns + (_clockwise ? centre : -centre);
                SurfacePanelOrNull.SetRadialCut(turns, drawn * Mathf.PI, _roundCap && _thickness > 0f);
                return;
            }

            // Back on the bitmap path (a theme switch): the parked panel forgets the sweep, and the
            // radial switch with it, so it comes back clean if the next skin asks for it again.
            SurfacePanelOrNull?.ClearCut();

            _fill.type = UnityImage.Type.Filled;
            _fill.fillMethod = UnityImage.FillMethod.Radial360;
            _fill.fillOrigin = (int)_origin360;
            // `remaining` runs back from the origin against the sweep; `elapsed` runs with it.
            _fill.fillClockwise = _drawElapsed ? _clockwise : !_clockwise;
            _fill.fillAmount = drawn;
        }

        public override Vector2? GetNativeSize() => new Vector2(64f, 64f);

        public override void OnAttached()
        {
            var bgRt = ProceduralBuilders.AddChild(RectTransform, "Bg");
            bgRt.gameObject.SetActive(false);
            _bg = bgRt.gameObject.AddComponent<UnityImage>();
            _bg.raycastTarget = false;

            _fill = ProceduralBuilders.AddImage(RectTransform, "Fill", raycast: false);
            _fill.color = DefaultFillColor;

            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.fontSize = 24;
            _label.color = Color.white;   // it sits on a dark veil
            _label.text = "";
            _label.gameObject.SetActive(false);
            ApplyFont();
            UI.Locale.Changed += ApplyFont;
        }

        public override void Dispose()
        {
            _disposed = true;
            StopClock();
            UI.Locale.Changed -= ApplyFont;
            _ready.Dispose();
            _cooling.Dispose();
            base.Dispose();
        }
    }
}
