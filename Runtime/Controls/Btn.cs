using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

        // Raw (unresolved) *Color attribute values. Resolved against UI.Theme in OnAfterApply
        // (same resolver Color uses) so a Variant changing a token re-resolves on ReSolve.
        private string _hoverColor;
        private string _pressedColor;
        private string _disabledColor;

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
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        /// <summary>
        /// Broadcasts this Btn's uGUI interaction state (Normal / Hover / Pressed / Disabled).
        /// Descendants and C# can subscribe to react to press / hover beyond the single
        /// <c>targetGraphic</c> that Selectable's ColorTint drives. The underlying
        /// <see cref="ReactiveProperty{T}"/> replays the current value (Normal at start) to
        /// new subscribers.
        /// </summary>
        public Observable<BtnState> OnState => _btn.OnState;

        /// <summary>
        /// Bridges the common <c>interactable</c> attribute (already applied by
        /// <see cref="ControlAttributeApplier"/> via <c>ApplyCommon</c> → base
        /// <see cref="Control.Interactable"/>, CanvasGroup-backed) to the underlying
        /// <see cref="UnityEngine.UI.Button"/>. Setting <c>Button.interactable = false</c>
        /// synchronously runs <c>DoStateTransition(Disabled)</c>, so <see cref="OnState"/>
        /// emits <see cref="BtnState.Disabled"/>. Runs after every <c>ApplyCommon</c>
        /// (initial apply + each Variant ReSolve), composing with — not replacing — the
        /// CanvasGroup behaviour.
        /// </summary>
        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _btn.interactable = Interactable;
            ApplyStateTint();
        }

        /// <summary>
        /// If any <c>*Color</c> multiplier is set: switch the bg off uGUI ColorTint (so the
        /// reactors become the single source of truth) and install / refresh a
        /// <see cref="StateTintReactor"/> on the bg plus every descendant <see cref="Graphic"/>
        /// (not descending into a nested <see cref="Btn"/>, and skipping any control with
        /// <c>stateReact="false"</c>). If no <c>*Color</c> is set, leave the default ColorTint
        /// behaviour untouched (back-compat) — no reactors. Idempotent: re-runs on every Variant
        /// ReSolve, adding a reactor only where one is missing then re-<c>Configure</c>-ing all
        /// (the reactor captures its base colour once, so re-Configure never re-captures).
        /// </summary>
        private void ApplyStateTint()
        {
            var hasAny = !string.IsNullOrEmpty(_hoverColor)
                         || !string.IsNullOrEmpty(_pressedColor)
                         || !string.IsNullOrEmpty(_disabledColor);
            if (!hasAny) return;

            _btn.transition = Selectable.Transition.None;

            Color? hover = string.IsNullOrEmpty(_hoverColor) ? null : UI.Theme.Resolve(_hoverColor);
            Color? pressed = string.IsNullOrEmpty(_pressedColor) ? null : UI.Theme.Resolve(_pressedColor);
            Color? disabled = string.IsNullOrEmpty(_disabledColor) ? null : UI.Theme.Resolve(_disabledColor);

            // Collect GameObjects to exclude: any opted-out control's GameObject and any
            // GameObject inside a nested <Btn> subtree (a deeper Btn owns its own graphics).
            var blocked = new HashSet<GameObject>();
            foreach (var child in Children)
                CollectBlocked(child as Control, blocked);

            // Fan out to the bg + every descendant Graphic except blocked ones. Walking the
            // transform subtree (not just the control tree) catches non-control graphics like
            // the Btn's auto-label. The bg lives on this Btn's own GO and is always tinted.
            foreach (var g in GameObject.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                if (blocked.Contains(g.gameObject)) continue;
                InstallReactor(g, hover, pressed, disabled);
            }
        }

        private static void CollectBlocked(Control control, HashSet<GameObject> blocked)
        {
            if (control == null) return;
            var optedOut = !control.StateReact;
            var nestedBtn = control is Btn;
            if (optedOut || nestedBtn)
            {
                // Block this control's whole transform subtree (its graphics + descendants).
                if (control.GameObject != null)
                {
                    foreach (var g in control.GameObject.GetComponentsInChildren<Graphic>(includeInactive: true))
                        blocked.Add(g.gameObject);
                    blocked.Add(control.GameObject);
                }
                return; // already covered the whole subtree
            }

            foreach (var child in control.Children)
                CollectBlocked(child as Control, blocked);
        }

        private static void InstallReactor(Graphic graphic, Color? hover, Color? pressed, Color? disabled)
        {
            if (graphic == null) return;
            var reactor = graphic.GetComponent<StateTintReactor>()
                          ?? graphic.gameObject.AddComponent<StateTintReactor>();
            reactor.Configure(hover, pressed, disabled, StateTintReactor.DefaultFade);
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
            if (_autoLabel == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _autoLabel.font = asset;
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

        /// <summary>Tint multiplier applied to the Btn's bg + descendant graphics while Hover.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string HoverColor { set => _hoverColor = value; }

        /// <summary>Tint multiplier applied while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string PressedColor { set => _pressedColor = value; }

        /// <summary>Tint multiplier applied while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string DisabledColor { set => _disabledColor = value; }

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
            _click.Dispose();
            base.Dispose();
        }
    }
}
