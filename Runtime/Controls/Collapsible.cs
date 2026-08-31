using LitMotion;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// An inline fold: a header bar that stays put and a body that opens and closes under it,
    /// pushing whatever follows down and pulling it back up (spec
    /// <c>2026-08-31-collapsible-design.md</c>).
    ///
    /// <para><b>Not a <see cref="TabMenu"/>.</b> That one is a popup — its own canvas, a full-screen
    /// click catcher, one open at a time, closes on choose. This is a panel that lives in the page:
    /// everything around it stays clickable, several can be open at once (unless a <c>group</c> says
    /// otherwise), and folding it re-flows the layout rather than floating over it.</para>
    ///
    /// <para><b>Its height is never authored.</b> The panel is exactly its header plus its body, so
    /// <c>height=</c> / <c>size=</c> are a parse error (<c>PUI-COLLAPSIBLE-HEIGHT</c>) and the
    /// vertical axis is force-hugged. Width is the author's business, as usual.</para>
    ///
    /// <para>The header is a <c>&lt;Btn&gt;</c>-shaped surface of its own: it broadcasts interaction
    /// state, takes the per-state colours, and is what directional navigation focuses. Its content is
    /// either the built-in caption (<c>text</c> / <c>icon</c>) or an author-supplied
    /// <c>&lt;Header&gt;</c> subtree; the caret is drawn by the library either way.</para>
    /// </summary>
    public sealed class Collapsible : ProceduralControl, IExpandable, IHugContent
    {
        /// <summary>Default open / close duration when the author writes none.</summary>
        internal const float DefaultTransition = 0.2f;

        private const float DefaultHeaderHeight = 44f;
        private const float CaptionPadX = 12f;
        private const float CaptionGap = 8f;
        private const float CaptionIconSize = 24f;
        private const float CaptionArrowSize = 16f;
        private const float DefaultFontSize = 14f;

        private VerticalLayoutGroup _rootLayout;

        private RectTransform _header;
        private UnityImage _headerBg;
        private LayoutElement _headerLe;
        private PuiButton _btn;
        private CaptionBuilder _caption;
        private RectTransform _headerHost;      // lazy: only when a <Header> supplies content

        private RectTransform _body;
        private CanvasGroup _bodyCg;
        private HugElement _bodyBox;
        private RectTransform _content;
        private VerticalLayoutGroup _contentLayout;
        private ScrollRect _scroll;             // lazy: only with maxHeight

        private float _headerHeight = DefaultHeaderHeight;
        private float _maxHeight;               // 0 = uncapped
        private float _transition = DefaultTransition;
        private string _groupName;
        private RectTransform _offsetHolder;

        private bool _expandedDeclared = true;
        private bool _initialised;
        private bool _pendingDeclaredChange;

        // Set only while a fold is animating: it overrides the body's "as tall as my content" answer
        // with the current tween value. Null the rest of the time, which is what makes an open panel
        // follow its content live — a row hidden, a row added by BindItems, a longer translation all
        // re-flow on the next layout pass with nothing to notify.
        private float? _boxOverride;

        private MotionHandle _heightMotion;
        private MotionHandle _fadeMotion;
        private MotionHandle _arrowMotion;

        private readonly Subject<Unit> _expandedSubject = new();
        private readonly Subject<Unit> _collapsedSubject = new();
        private readonly Subject<bool> _toggledSubject = new();

        // State colours / offsets, replayed through the installers on every pass like <Btn>.
        private string _hoverColor, _pressedColor, _disabledColor;
        private string _hoverModulate, _pressedModulate, _disabledModulate;
        private string _headerColorSpec;
        private Vector2? _pressedOffset;

        // The whole panel draws — header and body are one surface, and headerColor= layers on top.
        private protected override GameObject SurfaceHost => GameObject;

        // The Selectable lives on the header, not on the surface's node, so the surface has no
        // targetGraphic to follow (same shape as TabMenu, whose surface is the popup).
        private protected override Selectable SurfaceSelectable => null;

        /// <summary>Author children (other than <c>&lt;Header&gt;</c>) go into the body column.</summary>
        protected internal override Transform ChildHostTransform => _content;

        /// <summary>Where <c>&lt;Header&gt;</c>'s children are instantiated. Built on first ask.</summary>
        internal RectTransform HeaderHost
        {
            get
            {
                if (_headerHost != null) return _headerHost;
                _headerHost = ProceduralBuilders.AddChild(_header, "Host");
                LayoutHeaderHost();
                return _headerHost;
            }
        }

        // The root's own VerticalLayoutGroup already publishes header + body to a parent group, so a
        // bare hug inside a stack needs no HugElement (FND §1.4.3).
        protected internal override bool SelfReportsContentSize => true;

        // The panel's height IS its content; ApplyCommon injects the hug rather than making the
        // author write it (and PUI-COLLAPSIBLE-HEIGHT rejects the author writing anything else).
        protected internal override bool ForcesHugHeight => true;

        float IHugContent.ContentSize(int axis)
            => axis == 0 ? _rootLayout.preferredWidth : _rootLayout.preferredHeight;

        public override void OnAttached()
        {
            var marker = GameObject.AddComponent<ExpandableMarker>();
            marker.Owner = this;

            var bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(bg);

            _rootLayout = GameObject.GetComponent<VerticalLayoutGroup>()
                          ?? GameObject.AddComponent<VerticalLayoutGroup>();
            _rootLayout.childControlWidth = true;
            _rootLayout.childControlHeight = true;
            // Header and body span the panel — a fold whose bar is narrower than its own background
            // is not a thing. (Cross-axis force-expand, exactly like TabMenu's popup rows.)
            _rootLayout.childForceExpandWidth = true;
            _rootLayout.childForceExpandHeight = false;
            _rootLayout.spacing = 0f;

            BuildHeader();
            BuildBody();
        }

        private void BuildHeader()
        {
            _header = ProceduralBuilders.AddChild(RectTransform, "Header");
            _headerBg = _header.gameObject.AddComponent<UnityImage>();
            // Transparent by default: the header is part of the panel's own surface unless the
            // author gives it a skin of its own (headerColor / headerSprite).
            _headerBg.color = new Color(0f, 0f, 0f, 0f);
            _headerBg.raycastTarget = true;

            _headerLe = _header.gameObject.AddComponent<LayoutElement>();
            ApplyHeaderHeight();

            _btn = _header.gameObject.AddComponent<PuiButton>();
            _btn.targetGraphic = _headerBg;
            _btn.onClick.AddListener(Toggle);

            // Pinned mode: the caret hugs the right edge and the label fills what is left — a header
            // bar spans its panel, so there is nothing to hug (CaptionBuilder's class note).
            _caption = new CaptionBuilder(_header, arrowAtRight: true,
                padX: CaptionPadX, gap: CaptionGap, iconSize: CaptionIconSize,
                arrowSize: CaptionArrowSize, fontSize: DefaultFontSize);
        }

        private void BuildBody()
        {
            _body = ProceduralBuilders.AddChild(RectTransform, "Body");
            _bodyCg = _body.gameObject.AddComponent<CanvasGroup>();
            // The body publishes its height the same way a hug axis does — recomputed inside every
            // layout pass from the content, clamped by maxHeight — with the tween writing an
            // override while it runs. A plain LayoutElement would freeze whatever the last fold
            // wrote and stop following the rows.
            _bodyBox = _body.gameObject.AddComponent<HugElement>();
            _bodyBox.SetAxis(1, true, float.NegativeInfinity, float.PositiveInfinity, BodyBox);
            // Without this the body is a dead end in both layout walks: a row that dirties itself
            // would never reach the root column, and the content's fitter would never run in the
            // panel's own pass. See LayoutLink.
            _body.gameObject.AddComponent<LayoutLink>();
            // The mask exists from the start but only clips while the box is short of the content —
            // a mask that is switched off stops breaking batching.
            var mask = _body.gameObject.AddComponent<RectMask2D>();
            mask.enabled = false;

            // Top-anchored and sized by its own fitter, so the rows keep their full height while the
            // body's box animates over them: that is what gives the fold something to reveal (and,
            // with maxHeight, something to scroll).
            _content = ProceduralBuilders.AddChild(_body, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            _content.anchoredPosition = Vector2.zero;

            _contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            _contentLayout.childControlWidth = true;
            _contentLayout.childControlHeight = true;
            _contentLayout.childForceExpandWidth = true;
            _contentLayout.childForceExpandHeight = false;

            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // ── Attributes: caption ────────────────────────────────────────────────────────────

        [UIAttr, Preserve]
        public string Text
        {
            set => _caption.SetText(value);
        }

        internal override string PeekDefaultText() => _caption?.Label?.text;

        [UIAttr("fontSize"), Preserve]
        public float FontSize
        {
            set => _caption.SetFontSize(value);
        }

        /// <summary>Header text colour; distinct from <c>color</c>, which fills the panel.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => LabelColorApplier.Apply(_caption.Label, value);
        }

        [UIAttr, Preserve]
        public string Font
        {
            set => _caption.FontType = value;
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Icon
        {
            set { _caption.SetIconSprite(UI.ResolveSprite(value)); LayoutHeaderHost(); }
        }

        [UIAttr(IsColor = true), Preserve]
        public string IconColor
        {
            set => ColorApplier.Apply(_caption.Icon, UI.Theme.ResolveSpec(value));
        }

        /// <summary>The caret. <c>""</c> hides it — a sprite-less Image renders a solid block.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Arrow
        {
            set { _caption.SetArrowSprite(UI.ResolveSprite(value)); LayoutHeaderHost(); }
        }

        [UIAttr(IsColor = true), Preserve]
        public string ArrowColor
        {
            set => ColorApplier.Apply(_caption.Arrow, UI.Theme.ResolveSpec(value));
        }

        [UIAttr, Preserve]
        public float ArrowSize
        {
            set { _caption.ArrowSize = value; LayoutHeaderHost(); }
        }

        // ── Attributes: header bar ─────────────────────────────────────────────────────────

        [UIAttr, Preserve]
        public float HeaderHeight
        {
            set { _headerHeight = value; ApplyHeaderHeight(); }
        }

        /// <summary>The header bar's own fill, drawn over the panel's surface. Default: transparent.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string HeaderColor
        {
            set
            {
                _headerColorSpec = value;
                ColorApplier.Apply(_headerBg, UI.Theme.ResolveSpec(value));
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string HeaderSprite
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    _headerBg.sprite = null;
                    _headerBg.type = UnityImage.Type.Simple;
                    return;
                }
                var sprite = UI.ResolveSprite(value);
                if (sprite == null) return;
                _headerBg.sprite = sprite;
                _headerBg.type = ProceduralBuilders.DeriveType(sprite);
            }
        }

        /// <summary>Absolute header bg colour while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Absolute header bg colour while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Absolute header bg colour while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
        /// <summary>Relative multiplier over the header subtree while Hover. Never reaches the body.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverModulate { set => _hoverModulate = value; }
        [UIAttr(IsColor = true), Preserve] public string PressedModulate { set => _pressedModulate = value; }
        [UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }

        /// <summary>Header content offset while Pressed (Unity sign: negative y = down).</summary>
        [UIAttr, Preserve]
        public string PressedOffset { set => _pressedOffset = StateOffsetSet.Parse(value); }

        // ── Attributes: panel ──────────────────────────────────────────────────────────────

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                var bg = GameObject.GetComponent<UnityImage>();
                if (bg != null) ColorApplier.Apply(bg, spec);
                Surface.SetFill(spec);
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set
            {
                var bg = GameObject.GetComponent<UnityImage>();
                if (bg == null) return;
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    bg.sprite = null;
                    bg.type = UnityImage.Type.Simple;
                    return;
                }
                var sprite = UI.ResolveSprite(value);
                if (sprite == null) return;
                bg.sprite = sprite;
                bg.type = ProceduralBuilders.DeriveType(sprite);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set
            {
                var bg = GameObject.GetComponent<UnityImage>();
                if (bg != null) ImageTint.Apply(bg, value);
            }
        }

        // ── Attributes: body ───────────────────────────────────────────────────────────────

        /// <summary>Gap between body rows.</summary>
        [UIAttr, Preserve]
        public float Spacing { set => _contentLayout.spacing = value; }

        /// <summary>Padding inside the body column (same shorthand as <c>&lt;VStack padding&gt;</c>).</summary>
        [UIAttr, Preserve]
        public string Padding
        {
            set => _contentLayout.padding = PaddingParser.Parse(value, _contentLayout.padding);
        }

        /// <summary>Cap on the body's height; past it the body scrolls. 0 / unset = uncapped.</summary>
        [UIAttr, Preserve]
        public float MaxHeight
        {
            set
            {
                _maxHeight = value;
                SyncScrollRect();
                MarkBodyDirty();
            }
        }

        /// <summary>Open / close duration. <c>0</c> = no animation.</summary>
        [UIAttr, Preserve]
        public string Transition
        {
            set
            {
                if (string.IsNullOrEmpty(value)) { _transition = DefaultTransition; return; }
                try
                {
                    _transition = Mathf.Max(0f, AnimationSpec.ParseSeconds(value));
                }
                catch (System.FormatException)
                {
                    Debug.LogWarning(
                        $"<Collapsible id='{Id}'> transition='{value}' is not a duration " +
                        "(e.g. '0.2s', '200ms', '0.2'). Falling back to the default.");
                    _transition = DefaultTransition;
                }
            }
        }

        internal float TransitionSeconds => _transition;

        /// <summary>Accordion key: within one Screen, opening a member closes the others.</summary>
        [UIAttr, Preserve]
        public string Group
        {
            set
            {
                var next = string.IsNullOrEmpty(value) ? null : value;
                if (next == _groupName) return;
                Groups()?.Remove(_groupName, this);
                _groupName = next;
                Groups()?.Add(_groupName, this);
            }
        }

        internal string GroupName => _groupName;

        /// <summary>Initial open state; runtime-owned once the user has folded it.</summary>
        [UIAttr, Preserve]
        public bool Expanded
        {
            get => IsExpanded;
            set
            {
                _expandedDeclared = value;
                if (_initialised && value != IsExpanded) _pendingDeclaredChange = true;
            }
        }

        internal override string PeekRuntimeState() => IsExpanded ? "true" : "false";

        // ── Open / close ───────────────────────────────────────────────────────────────────

        public bool IsExpanded { get; private set; } = true;

        public Observable<Unit> OnExpanded => _expandedSubject;
        public Observable<Unit> OnCollapsed => _collapsedSubject;

        /// <summary>Emits the new state on every open / close.</summary>
        public Observable<bool> OnToggled => _toggledSubject;

        /// <summary>The header bar's interaction state (Normal / Hover / Pressed / Disabled).</summary>
        public Observable<InteractState> OnState => _btn.OnState;

        /// <summary>
        /// The gesture — what the header's click and Submit run. Blocked while the control is not
        /// interactable, which is what <c>interactable="false"</c> means: the affordance is off.
        /// <see cref="Expand"/> / <see cref="Collapse"/> stay open to code, so a panel can still be
        /// driven while its header is locked.
        /// </summary>
        public void Toggle()
        {
            if (!Interactable) return;
            if (IsExpanded) Collapse(); else Expand();
        }

        public void Expand()
        {
            if (IsExpanded) return;

            Groups()?.NotifyExpanding(_groupName, this);

            IsExpanded = true;
            PlayTransition(expanding: true);
            _expandedSubject.OnNext(Unit.Default);
            _toggledSubject.OnNext(true);
        }

        public void Collapse()
        {
            if (!IsExpanded) return;

            IsExpanded = false;
            PlayTransition(expanding: false);
            _collapsedSubject.OnNext(Unit.Default);
            _toggledSubject.OnNext(false);
        }

        /// <summary>
        /// Runs the three channels of the fold — body height, body fade, caret turn — from wherever
        /// they currently are to the ends this direction implies.
        /// </summary>
        /// <remarks>
        /// Reading the current value as the from-value is what makes an interrupted fold reverse
        /// smoothly instead of snapping to the far end and starting over. Outside play mode (and at
        /// <c>transition="0"</c>) the end state is written directly: LitMotion ticks on the player
        /// loop, so an EditMode caller would otherwise be left with a body stuck at height 0 that
        /// never re-activates.
        /// </remarks>
        private void PlayTransition(bool expanding)
        {
            CancelMotions();

            // Measuring needs the rows alive (a TMP built under an inactive parent measures 0
            // forever — InactiveMeasure), and an opening body has to be active before the content
            // can be measured at all.
            if (expanding) _content.gameObject.SetActive(true);

            var target = expanding ? CappedContent(RevealDriver.Measure(_content, 1)) : 0f;

            if (_transition <= 0f || !UnityEngine.Application.isPlaying)
            {
                _boxOverride = null;   // hand the axis back to "as tall as the content"
                MarkBodyDirty();
                _bodyCg.alpha = expanding ? 1f : 0f;
                _caption.ArrowRotation = expanding ? 0f : 180f;
                if (!expanding) _content.gameObject.SetActive(false);
                UpdateClip();
                return;
            }

            var fromBox = _boxOverride ?? BodyBox(1);
            _boxOverride = fromBox;
            _heightMotion = LMotion.Create(fromBox, target, _transition)
                .WithEase(Ease.OutCubic)
                .Bind(this, static (v, self) =>
                {
                    self._boxOverride = v;
                    self.MarkBodyDirty();
                    self.UpdateClip();
                })
                .AddTo(_body.gameObject);

            _fadeMotion = LMotion.Create(_bodyCg.alpha, expanding ? 1f : 0f, _transition)
                .WithEase(Ease.OutCubic)
                .Bind(_bodyCg, static (v, cg) => { if (cg) cg.alpha = v; })
                .AddTo(_body.gameObject);

            _arrowMotion = LMotion.Create(_caption.ArrowRotation, expanding ? 0f : 180f, _transition)
                .WithEase(Ease.OutCubic)
                .Bind(this, static (v, self) => self._caption.ArrowRotation = v)
                .AddTo(_body.gameObject);

            // Only a fold that actually FINISHES releases the override (and, closing, switches the
            // rows off) — and only if it is still the current one: folding back mid-transition
            // cancels this handle first, so a cancelled close cannot switch off a body the user
            // just re-opened.
            var captured = _heightMotion;
            var wasExpanding = expanding;
            _heightMotion.GetAwaiter().OnCompleted(() =>
            {
                if (!captured.Equals(_heightMotion)) return;
                _boxOverride = null;
                MarkBodyDirty();
                if (!wasExpanding && !IsExpanded) _content.gameObject.SetActive(false);
                UpdateClip();
            });
        }

        /// <summary>
        /// What the body publishes to the root column: the tween's value while one is running,
        /// otherwise the content's own height (capped) when open and nothing when closed.
        /// </summary>
        private float BodyBox(int axis)
        {
            if (_boxOverride.HasValue) return _boxOverride.Value;
            return IsExpanded ? CappedContent(ContentHeight()) : 0f;
        }

        // Layout-time read: uGUI has already计算过 the content column bottom-up by the time a parent
        // asks, so this is both fresh and free. The heavier RevealDriver.Measure (activate + force a
        // rebuild) is only for picking a tween's target, outside the pass.
        private float ContentHeight()
            => _content != null ? LayoutUtility.GetPreferredSize(_content, 1) : 0f;

        private float CappedContent(float content)
            => _maxHeight > 0f ? Mathf.Min(content, _maxHeight) : content;

        private void MarkBodyDirty()
        {
            if (_body != null) LayoutRebuilder.MarkLayoutForRebuild(_body);
        }

        /// <summary>Clips only while the box is short of the content — capped or mid-fold.</summary>
        private void UpdateClip()
        {
            if (_body == null) return;
            var mask = _body.GetComponent<RectMask2D>();
            if (mask != null) mask.enabled = BodyBox(1) < ContentHeight() - 0.01f;
        }

        private void CancelMotions()
        {
            if (_heightMotion.IsActive()) _heightMotion.TryCancel();
            if (_fadeMotion.IsActive()) _fadeMotion.TryCancel();
            if (_arrowMotion.IsActive()) _arrowMotion.TryCancel();
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────────────

        internal override void OnAfterApply()
        {
            base.OnAfterApply();

            _btn.interactable = Interactable;
            _offsetHolder = StateOffsetInstaller.Install(
                _header.gameObject, _offsetHolder, new StateOffsetSet(_pressedOffset, null));

            var abs = StateColorSet.ResolveAbsolutes(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.ResolveModulates(
                _hoverModulate, _pressedModulate, null, StateColorSet.NoneToNull(_disabledModulate));
            // Rooted at the header: the per-state multiplier is about the bar the pointer is over,
            // and fanning it into the body would tint content that is not part of the control's
            // affordance (spec §4.4).
            var reactor = StateTintInstaller.Install(_header.gameObject, _btn, Children, abs, mod,
                authoredBase: HeaderBase());
            _btn.transition = reactor != null
                ? Selectable.Transition.None
                : Selectable.Transition.ColorTint;
            if (string.IsNullOrWhiteSpace(_disabledColor)
                && string.IsNullOrWhiteSpace(_disabledModulate))
                DisabledGrayscaleInstaller.Install(_header.gameObject, _btn, Children);

            LayoutHeaderHost();
            SyncScrollRect();
            SyncHeaderPreferredWidth();

            if (!_initialised)
            {
                _initialised = true;
                IsExpanded = _expandedDeclared;
                EstablishInitialState();
                return;
            }

            if (_pendingDeclaredChange)
            {
                _pendingDeclaredChange = false;
                if (_expandedDeclared) Expand(); else Collapse();
                return;
            }

            // An ordinary pass (theme, locale, resize, a variant that is not ours) changes nothing
            // about the fold — the body publishes its own height every layout pass — but the cap and
            // the caret zone may have moved, so the clip is re-derived.
            UpdateClip();
        }

        /// <summary>
        /// Writes the open / closed look for the very first pass — no transition, because a panel
        /// that opens already folded should not animate itself open at Screen open.
        /// </summary>
        private void EstablishInitialState()
        {
            _boxOverride = null;
            MarkBodyDirty();

            if (IsExpanded)
            {
                _bodyCg.alpha = 1f;
                _caption.ArrowRotation = 0f;
                UpdateClip();
                return;
            }

            _bodyCg.alpha = 0f;
            _caption.ArrowRotation = 180f;
            UpdateClip();
            // Deactivate only after Open()'s measuring pass: a TMP added to an already-inactive
            // GameObject never runs Awake and mis-measures forever. Same deferral Tab.bind uses.
            var owner = UI.OwnerScreenOf(this);
            if (owner != null) owner.DeferDuringOpen(HideBodyIfCollapsed);
            else HideBodyIfCollapsed();
        }

        private void HideBodyIfCollapsed()
        {
            if (!IsExpanded && _content != null) _content.gameObject.SetActive(false);
        }

        private void ApplyHeaderHeight()
        {
            if (_headerLe == null) return;
            _headerLe.preferredHeight = _headerHeight;
            _headerLe.minHeight = _headerHeight;
            _headerLe.flexibleHeight = 0f;
        }

        // Lets the root group report a真 content width, so width="hug" and the free-positioning
        // native fallback both land on the caption rather than on zero.
        private void SyncHeaderPreferredWidth()
        {
            if (_headerLe == null || _caption == null) return;
            _headerLe.preferredWidth = _caption.ContentWidth();
        }

        /// <summary>Keeps the <c>&lt;Header&gt;</c> host clear of the caret's zone.</summary>
        private void LayoutHeaderHost()
        {
            if (_headerHost == null) return;
            _headerHost.anchorMin = Vector2.zero;
            _headerHost.anchorMax = Vector2.one;
            _headerHost.pivot = new Vector2(0.5f, 0.5f);
            _headerHost.offsetMin = Vector2.zero;
            _headerHost.offsetMax = new Vector2(-_caption.ArrowZoneWidth, 0f);
        }

        private void SyncScrollRect()
        {
            if (_body == null) return;

            if (_maxHeight <= 0f)
            {
                if (_scroll != null) _scroll.enabled = false;
                return;
            }

            if (_scroll == null)
            {
                _scroll = _body.gameObject.AddComponent<ScrollRect>();
                _scroll.viewport = _body;
                _scroll.content = _content;
                _scroll.horizontal = false;
                _scroll.vertical = true;
                _scroll.movementType = ScrollRect.MovementType.Elastic;
                _scroll.elasticity = 0.1f;
                _scroll.inertia = true;
                _scroll.scrollSensitivity = 20f;
            }
            _scroll.enabled = true;
        }

        private ColorSpec? HeaderBase()
            => string.IsNullOrWhiteSpace(_headerColorSpec)
                ? null
                : UI.Theme.ResolveSpec(_headerColorSpec);

        private CollapsibleGroupRegistry Groups() => UI.OwnerScreenOf(this)?.CollapsibleGroups;

        /// <summary>
        /// Width from the caption, height from the header — the vertical axis is force-hugged, so
        /// this height is never actually consulted; it is here so the pair is well-formed.
        /// </summary>
        public override Vector2? GetNativeSize()
            => new Vector2(_caption != null ? _caption.ContentWidth() : 0f, _headerHeight);

        public override void Dispose()
        {
            CancelMotions();
            Groups()?.Remove(_groupName, this);
            _expandedSubject.Dispose();
            _collapsedSubject.Dispose();
            _toggledSubject.Dispose();
            base.Dispose();
        }
    }
}
