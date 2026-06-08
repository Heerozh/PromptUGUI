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
        private IDisposable _pressedSpriteSub;

        // Absolute per-state bg colours (set targetGraphic). Resolved in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _disabledColor;
        // Relative per-state multipliers (fan out to bg + descendants). Resolved in OnAfterApply.
        private string _hoverModulate;
        private string _pressedModulate;
        private string _disabledModulate;

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
            _btn = GameObject.GetComponent<PuiButton>() ?? GameObject.AddComponent<PuiButton>();
            _btn.targetGraphic = _bg;
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
            _pressedSpriteSub = _btn.OnState.Subscribe(ApplyPressedSpriteForState);
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
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, null, _disabledModulate);
            StateTintInstaller.Install(GameObject, _btn, Children, abs, mod);
            // A pressedSprite is itself a state visual: drop uGUI's built-in ColorTint so the
            // swapped pressed image isn't double-darkened. Set-only, matching the state-colour path
            // (StateTintInstaller flips transition to None when any *Color / *Modulate is present).
            if (_pressedSprite != null)
                _btn.transition = UnityEngine.UI.Selectable.Transition.None;
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

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _bg.color = UI.Theme.Resolve(value);
        }

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

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _bg.sprite = UI.ResolveSprite(value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string PressedSprite
        {
            set
            {
                // "" / "none" => no pressed swap (mirrors Tab.selectedSprite). Otherwise resolve
                // through the same path as `sprite`; a Variant ReSolve re-invokes this setter.
                _pressedSprite = string.IsNullOrEmpty(value) || value == "none"
                    ? null
                    : UI.ResolveSprite(value);
                // Re-evaluate for the live state so a Variant-driven change takes effect immediately.
                ApplyPressedSpriteForState(_btn.Current);
            }
        }

        // Swaps the bg's overrideSprite (never its authored `sprite`) so revert is overrideSprite=null.
        private void ApplyPressedSpriteForState(InteractState state)
            => _bg.overrideSprite = state == InteractState.Pressed ? _pressedSprite : null;

        public override Vector2? GetNativeSize()
        {
            if (_autoLabel != null && !string.IsNullOrEmpty(_autoLabel.text))
            {
                _autoLabel.ForceMeshUpdate();
                var w = _autoLabel.preferredWidth + HorizontalPadding * 2f;
                var h = Mathf.Max(MinTapHeight, _autoLabel.preferredHeight + VerticalPadding * 2f);
                return new Vector2(w, h);
            }
            return new Vector2(DefaultIconBtnWidth, MinTapHeight);
        }

        public Observable<Unit> OnClick => _click;

        internal void SimulateClick() => _click.OnNext(Unit.Default);

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _pressedSpriteSub?.Dispose();
            _click.Dispose();
            base.Dispose();
        }
    }
}
