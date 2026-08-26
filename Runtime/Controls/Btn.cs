using System;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Btn : Control, IPointerEventSource
    {
        private UnityImage _bg;
        private PuiButton _btn;
        private TMP_Text _autoLabel;
        private string _fontType = "default";
        private readonly Subject<Unit> _click = new();
        private PointerEventRelay _pointerRelay;
        private Sprite _pressedSprite;
        private bool _pressedSpriteAuthored;
        // bg 在无 state override 时的 Image.Type：默认皮肤=Tiled，作者 sprite= 后由 AutoSlice 决定。
        private UnityImage.Type _baseType;
        private Sprite _disabledSprite;
        private IDisposable _stateSpriteSub;

        // Absolute per-state bg colours (set targetGraphic). Resolved in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _disabledColor;
        // Relative per-state multipliers (fan out to bg + descendants). Resolved in OnAfterApply.
        private string _hoverModulate;
        private string _pressedModulate;
        private string _disabledModulate;
        // Press-offset: content-holder shift while Pressed. _offsetHolder lazily created by
        // StateOffsetInstaller (full-stretch wrapper around the content; bg stays on this GO).
        private Vector2? _pressedOffset;
        private RectTransform _offsetHolder;

        private const float HorizontalPadding = 16f;
        private const float VerticalPadding = 6f;
        private const float MinTapHeight = 44f;
        private const float DefaultIconBtnWidth = 80f;

        private PointerEventRelay EnsureRelay()
            => _pointerRelay ??= GameObject.AddComponent<PointerEventRelay>();

        public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
        public Observable<Unit> OnPointerExit => EnsureRelay().OnPointerExit;
        public Observable<Unit> OnPointerDown => EnsureRelay().OnPointerDown;

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = PromptUGUI.Controls.Internal.ProceduralBuilders.DefaultBtnColor;
            PromptUGUI.Controls.Internal.ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _baseType = _bg.type;
            _btn = GameObject.GetComponent<PuiButton>() ?? GameObject.AddComponent<PuiButton>();
            _btn.targetGraphic = _bg;
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
            _stateSpriteSub = _btn.OnState.Subscribe(ApplyStateSprite);
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        /// <summary>
        /// Broadcasts this Btn's uGUI interaction state (Normal / Hover / Pressed / Disabled).
        /// Descendants and C# can subscribe to react to press / hover beyond the single
        /// <c>targetGraphic</c> that Selectable's ColorTint drives. The underlying
        /// <see cref="ReactiveProperty{T}"/> replays the current value (Normal at start) to
        /// new subscribers.
        /// </summary>
        public Observable<InteractState> OnState => _btn.OnState;

        // Children parent into the press-offset holder when it exists (so Add blocks land inside too),
        // else directly onto this GO (identical to the default Control behaviour — no holder, no cost).
        protected internal override Transform ChildHostTransform
            => _offsetHolder != null ? _offsetHolder : RectTransform;

        /// <summary>
        /// Runtime override so setting <see cref="Interactable"/> from code (e.g. a modal
        /// <c>Configure</c> hook gating the OK button) also drives the underlying
        /// <see cref="UnityEngine.UI.Button"/> — greying it out + emitting
        /// <see cref="InteractState.Disabled"/> — not just the base CanvasGroup. Mirrors the
        /// XML-attr path (<see cref="OnAfterApply"/>), so code-applied and Variant-applied
        /// disables look identical. The CanvasGroup side (from base) still blocks raycasts.
        /// </summary>
        public override bool Interactable
        {
            get => base.Interactable;
            set
            {
                base.Interactable = value;
                if (_btn != null) _btn.interactable = value;
            }
        }

        /// <summary>
        /// Bridges the common <c>interactable</c> attribute (already applied by
        /// <see cref="ControlAttributeApplier"/> via <c>ApplyCommon</c> → base
        /// <see cref="Control.Interactable"/>, CanvasGroup-backed) to the underlying
        /// <see cref="UnityEngine.UI.Button"/>. Setting <c>Button.interactable = false</c>
        /// synchronously runs <c>DoStateTransition(Disabled)</c>, so <see cref="OnState"/>
        /// emits <see cref="InteractState.Disabled"/>. Runs after every <c>ApplyCommon</c>
        /// (initial apply + each Variant ReSolve), composing with — not replacing — the
        /// CanvasGroup behaviour.
        /// </summary>
        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _btn.interactable = Interactable;
            _offsetHolder = StateOffsetInstaller.Install(GameObject, _offsetHolder, new StateOffsetSet(_pressedOffset, null));
            var abs = StateColorSet.ResolveAbsolutes(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, null, StateColorSet.NoneToNull(_disabledModulate));
            var bgReactor = StateTintInstaller.Install(GameObject, _btn, Children, abs, mod,
                authoredBase: AuthoredBase());
            // A pressed/disabled sprite is itself a state visual: drop uGUI's built-in ColorTint so
            // the swapped image isn't double-darkened. COMPUTED, not set one-way — clearing the
            // sprite (a Variant flip, a theme switch) has to hand feedback back to ColorTint, or the
            // Btn is left with none at all and no attribute can restore it.
            _btn.transition =
                bgReactor != null || _pressedSprite != null || _disabledSprite != null
                    ? UnityEngine.UI.Selectable.Transition.None
                    : UnityEngine.UI.Selectable.Transition.ColorTint;
            // 默认禁用外观：作者未声明任何 disabled* 时整控件去色。与 transition 无关（ColorTint/None 皆可）。
            if (string.IsNullOrWhiteSpace(_disabledColor)
                && string.IsNullOrWhiteSpace(_disabledModulate)
                && _disabledSprite == null)
                DisabledGrayscaleInstaller.Install(GameObject, _btn, Children);
        }

        private TMP_Text EnsureLabel()
        {
            if (_autoLabel != null) return _autoLabel;
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(GameObject.transform, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _autoLabel = go.AddComponent<TextMeshProUGUI>();
            _autoLabel.alignment = TextAlignmentOptions.Center;
            _autoLabel.raycastTarget = false;
            _autoLabel.fontSize = 24;  // 默认 prefab Button label 字号；AddText 默认 14 是 Toggle/Dropdown 字号
            _autoLabel.color = PromptUGUI.Controls.Internal.ProceduralBuilders.DefaultLabelColor;
            ApplyFont();
            return _autoLabel;
        }

        private void ApplyFont()
        {
            FontApplier.Apply(_autoLabel, _fontType);
        }

        [UIAttr, Preserve]
        public string Text
        {
            set
            {
                if (string.IsNullOrEmpty(value) && _autoLabel == null) return;
                EnsureLabel().text = value ?? "";
            }
        }

        internal override string PeekDefaultText() => _autoLabel != null ? _autoLabel.text : null;

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
        public int FontSize
        {
            set => EnsureLabel().fontSize = value;
        }

        /// <summary>Label text color (theme token / hex / CSS / gradient / <c>/alpha</c>).
        /// Distinct from <see cref="Color"/>, which is the background. Empty = default ink.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => Internal.LabelColorApplier.Apply(EnsureLabel(), value);
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                // Held, not just applied: StateTintInstaller needs the DECLARATION to hand the state
                // reactor its base. Reading the graphic back instead would pick up whatever tint the
                // reactor last painted. See StateTintReactor's remarks.
                _color = value;
                Internal.ColorApplier.Apply(_bg, UI.Theme.ResolveSpec(value));
            }
        }

        private string _color;

        /// <summary>What <c>color=</c> currently declares, or null when the author declared none —
        /// then the reactor keeps the control's own built-in bg colour as its base.</summary>
        private ColorSpec? AuthoredBase()
            => string.IsNullOrWhiteSpace(_color) ? (ColorSpec?)null : UI.Theme.ResolveSpec(_color);

        /// <summary>Absolute bg colour while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Absolute bg colour while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Absolute bg colour while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
        /// <summary>Relative colour multiplier (fans out to subtree) while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverModulate { set => _hoverModulate = value; }
        /// <summary>Relative colour multiplier while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedModulate { set => _pressedModulate = value; }
        /// <summary>Relative colour multiplier while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }

        /// <summary>Content offset (pixels, Unity sign: negative y = down) while Pressed. <c>""</c> / <c>none</c> = none.</summary>
        [UIAttr, Preserve] public string PressedOffset { set => _pressedOffset = StateOffsetSet.Parse(value); }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set
            {
                _bg.sprite = UI.ResolveSprite(value);
                // 作者 sprite 按 border 选 Sliced/Simple（默认钉木框的 Tiled 只属于内置皮肤）；
                // 透明 normal（""）不动 type，留给 ApplyStateSprite 按 override 调。
                PromptUGUI.Controls.Internal.ProceduralBuilders.AutoSlice(_bg);
                _baseType = _bg.type;
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string PressedSprite
        {
            set
            {
                _pressedSpriteAuthored = true;
                // "" / "none" => no pressed swap (mirrors Tab.selectedSprite). Otherwise resolve
                // through the same path as `sprite`; a Variant ReSolve re-invokes this setter.
                _pressedSprite = string.IsNullOrEmpty(value) || value == "none"
                    ? null
                    : UI.ResolveSprite(value);
                // Re-evaluate for the live state so a Variant-driven change takes effect immediately.
                ApplyStateSprite(_btn.Current);
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string DisabledSprite
        {
            set
            {
                // Same contract as pressedSprite: "" / "none" => no swap. Shown while the Btn is
                // disabled (interactable=false / runtime Interactable=false).
                _disabledSprite = string.IsNullOrEmpty(value) || value == "none"
                    ? null
                    : UI.ResolveSprite(value);
                ApplyStateSprite(_btn.Current);
            }
        }

        // Swaps the bg's overrideSprite (never its authored `sprite`) so revert is overrideSprite=null.
        // Priority Disabled > Pressed (states are mutually exclusive); authored bg.sprite shows otherwise.
        // Pressed falls back to the built-in pressed skin when the author customized nothing
        // (keeps ColorTint — only AUTHORED pressed/disabled sprites flip transition=None in OnAfterApply).
        // overrideSprite 与 type 共用一个 Image：AUTHORED override 按自己的 border 选 Sliced/Simple
        // （内置 pressed 兜底沿用基础 type=Tiled），松开后回落 _baseType。
        private void ApplyStateSprite(InteractState state)
        {
            var authored = state == InteractState.Disabled ? _disabledSprite
                         : state == InteractState.Pressed ? _pressedSprite
                         : null;
            _bg.overrideSprite = authored
                ?? (state == InteractState.Pressed ? DefaultPressedSprite() : null);
            _bg.type = authored != null
                ? PromptUGUI.Controls.Internal.ProceduralBuilders.DeriveType(authored)
                : _baseType;
        }

        private Sprite DefaultPressedSprite()
        {
            // authored ''/none 也算 opt-out（此时 _pressedSprite 同为 null，仅此标志能区分"从未写过"）
            if (_pressedSpriteAuthored) return null;
            var round = PromptUGUI.Controls.Internal.ProceduralBuilders.GetDefaultSprite(
                PromptUGUI.Controls.Internal.ProceduralBuilders.SpriteRoundedRect);
            if (round == null || _bg.sprite != round) return null;   // 作者换过 sprite= → 让位
            return PromptUGUI.Controls.Internal.ProceduralBuilders.GetDefaultSprite(
                PromptUGUI.Controls.Internal.ProceduralBuilders.SpritePressed);
        }

        public override Vector2? GetNativeSize()
        {
            if (_autoLabel != null && !string.IsNullOrEmpty(_autoLabel.text))
            {
                // Unconstrained natural size — NOT _autoLabel.preferredHeight, which TMP measures at
                // the live label-rect width. On a ReSolve that width is the previous solve's value, so
                // a grown label would wrap and inflate the height. Mirrors Text.GetNativeSize.
                var pref = _autoLabel.GetPreferredValues(_autoLabel.text);
                var w = pref.x + HorizontalPadding * 2f;
                var h = Mathf.Max(MinTapHeight, pref.y + VerticalPadding * 2f);
                return new Vector2(w, h);
            }
            return new Vector2(DefaultIconBtnWidth, MinTapHeight);
        }

        public Observable<Unit> OnClick => _click;

        internal void SimulateClick() => _click.OnNext(Unit.Default);

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _stateSpriteSub?.Dispose();
            _click.Dispose();
            base.Dispose();
        }
    }
}
