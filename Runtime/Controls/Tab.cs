using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Controls
{
    public sealed class Tab : Control
    {
        private UnityImage _bg;
        private UnityEngine.Sprite _selectedSprite;   // resolved selectedSprite, swapped onto _bg.overrideSprite while IsOn
        private StateTintReactor _bgReactor;
        private UnityImage _icon;
        private TMP_Text _label;
        private PuiToggle _toggle;
        private string _fontType = "default";
        private string _bindId;
        private bool _bindResolved;
        private Frame _boundFrame;
        private readonly Subject<bool> _changed = new();
        private readonly Subject<Unit> _selected = new();

        // Absolute per-state bg colours (set _bg). Resolved in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _selectedColor;
        private string _disabledColor;
        // Relative per-state multipliers (fan out to _bg + descendants). Resolved in OnAfterApply.
        private string _hoverModulate;
        private string _pressedModulate;
        private string _selectedModulate;
        private string _disabledModulate;

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultBtnColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            _toggle = GameObject.GetComponent<PuiToggle>() ?? GameObject.AddComponent<PuiToggle>();
            _toggle.targetGraphic = _bg;
            _toggle.transition = Selectable.Transition.ColorTint;
            _toggle.InitStateBroadcast();

            var group = FindAncestorToggleGroup();
            if (group == null)
                Debug.LogWarning($"Tab '{Id}' has no <TabBar> ancestor; mutual exclusion disabled.");
            else
                _toggle.group = group;

            _toggle.onValueChanged.AddListener(OnIsOnChanged);
            UI.Locale.Changed += ApplyFont;
        }

        private TMP_Text EnsureLabel()
        {
            if (_label != null) return _label;
            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.alignment = TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            _label.fontSize = 24;
            _label.text = "";
            var lrt = _label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = _icon != null ? new Vector2(32f, 0f) : Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            ApplyFont();
            return _label;
        }

        private void OnIsOnChanged(bool isOn)
        {
            _changed.OnNext(isOn);
            if (isOn) _selected.OnNext(Unit.Default);
            ApplyBindFrame(isOn);
            ApplySelectedSprite();
            _bgReactor?.SetSelected(isOn);
        }

        private void ApplyBindFrame(bool isOn)
        {
            if (_bindId == null && !_bindResolved) return;
            if (!_bindResolved)
            {
                try { _boundFrame = UI.OwnerScreenOf(this)?.Get<Frame>(_bindId); }
                catch { _boundFrame = null; }
                if (_boundFrame == null)
                    Debug.LogWarning($"Tab.bind='{_bindId}' did not resolve to a Frame; ignoring.");
                _bindResolved = true;
                _bindId = null;     // prevent re-warn
            }
            if (_boundFrame != null) _boundFrame.GameObject.SetActive(isOn);
        }

        internal void ForceSyncBindFrame(bool isOn) => ApplyBindFrame(isOn);

        private ToggleGroup FindAncestorToggleGroup()
        {
            // OnAttached runs before Screen._nodeMap is populated, so we can't look up
            // the TabBar control by GameObject yet. TabBar.OnAttached has already added
            // its ToggleGroup component to its own GameObject (parent created first
            // during DFS instantiation), so a transform-ancestor GetComponent walk
            // finds it directly without depending on _nodeMap.
            var t = RectTransform.parent;
            while (t != null)
            {
                var g = t.GetComponent<UnityEngine.UI.ToggleGroup>();
                if (g != null) return g;
                t = t.parent;
            }
            return null;
        }

        private void ApplyFont()
        {
            FontApplier.Apply(_label, _fontType);
        }

        [UIAttr, Preserve]
        public bool IsOn
        {
            get => _toggle != null && _toggle.isOn;
            set { if (_toggle != null) _toggle.isOn = value; }
        }

        [UIAttr, Preserve]
        public string Bind
        {
            set => _bindId = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        [UIAttr, Preserve]
        public string Text
        {
            set
            {
                if (string.IsNullOrEmpty(value) && _label == null) return;
                EnsureLabel().text = value ?? "";
            }
        }

        internal override string PeekDefaultText() => _label != null ? _label.text : null;

        internal override string PeekRuntimeState() => IsOn ? "true" : "false";

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                if (_label != null) ApplyFont();
            }
        }

        [UIAttr("fontSize"), Preserve]
        public int FontSize
        {
            set => EnsureLabel().fontSize = value;
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Icon
        {
            set
            {
                if (_icon == null)
                {
                    _icon = ProceduralBuilders.AddImage(RectTransform, "Icon", raycast: false);
                    var rt = _icon.rectTransform;
                    rt.anchorMin = new Vector2(0f, 0.5f);
                    rt.anchorMax = new Vector2(0f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(24f, 24f);
                    rt.anchoredPosition = new Vector2(16f, 0f);     // 4px gap from left edge then center of 24
                    // Shift label right to make room for icon — only if label exists.
                    // If text is applied later, EnsureLabel() reads _icon != null and shifts itself.
                    if (_label != null) _label.rectTransform.offsetMin = new Vector2(32f, 0f);
                }
                _icon.sprite = UI.ResolveSprite(value);
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set
            {
                // sprite="" / sprite="none" → drop the built-in default bg sprite, leaving a
                // plain color-filled rect. Mirrors Btn/Toggle/Image, which null out the same way.
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    _bg.sprite = null;
                    _bg.type = UnityImage.Type.Simple;
                    return;
                }
                ApplyBgSprite(UI.ResolveSprite(value));
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string SelectedSprite
        {
            set
            {
                // "" / "none" → no selected sprite (no swap). No default overlay to clear.
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    _selectedSprite = null;
                    ApplySelectedSprite();
                    return;
                }
                _selectedSprite = UI.ResolveSprite(value);
                // Mirror <Btn pressedSprite>: the swapped sprite IS the selected feedback, so take the
                // bg off uGUI's built-in ColorTint to avoid double-tinting it.
                _toggle.transition = Selectable.Transition.None;
                ApplySelectedSprite();
            }
        }

        // Show the selected sprite by overriding the bg's displayed sprite while IsOn — the authored
        // `sprite` (_bg.sprite) is never touched. Keyed on IsOn (persistent), so hover/press of the
        // selected tab (transient state only) never disturb it.
        private void ApplySelectedSprite()
        {
            if (_bg == null) return;
            var showSelected = IsOn && _selectedSprite != null;
            _bg.overrideSprite = showSelected ? _selectedSprite : null;
            // overrideSprite shares the Image's single `type` field with `sprite`, so re-derive
            // 9-slice vs simple from whichever sprite is now displayed (the override when selected,
            // else the authored bg sprite) — otherwise a bordered selectedSprite over a Simple-typed
            // normal bg (e.g. sprite="") would render un-sliced. Mirrors ApplyBgSprite's rule.
            var shown = showSelected ? _selectedSprite : _bg.sprite;
            _bg.type = shown != null && shown.border != Vector4.zero
                ? UnityImage.Type.Sliced
                : UnityImage.Type.Simple;
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _bg.color = UI.Theme.Resolve(value);
        }

        /// <summary>
        /// Blend mode for how <see cref="Color"/> combines with the bg sprite — same
        /// <c>multiply</c> / <c>linear</c> (Linear Light) choice as <see cref="Btn"/> / <see cref="Image"/>.
        /// Applies to <c>_bg</c> (where <see cref="Color"/> lands); since <c>selectedSprite</c> swaps the
        /// bg's own <c>overrideSprite</c>, the selected sprite is tinted too. Orthogonal to the
        /// state-colour reactors (which drive <c>color</c>, not material).
        /// </summary>
        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        private void ApplyBgSprite(UnityEngine.Sprite sprite)
        {
            if (sprite == null) return;
            _bg.sprite = sprite;
            _bg.type = sprite.border != Vector4.zero ? UnityImage.Type.Sliced : UnityImage.Type.Simple;
        }

        /// <summary>Absolute bg colour while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Absolute bg colour while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Absolute bg colour while this Tab is the active (isOn) one at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedColor { set => _selectedColor = value; }
        /// <summary>Absolute bg colour while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
        /// <summary>Relative colour multiplier (fans out to subtree) while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverModulate { set => _hoverModulate = value; }
        /// <summary>Relative colour multiplier while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedModulate { set => _pressedModulate = value; }
        /// <summary>Relative colour multiplier while active (isOn) at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedModulate { set => _selectedModulate = value; }
        /// <summary>Relative colour multiplier while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }

        /// <summary>Broadcasts the Tab's interaction state. Selected = this Tab is the active (isOn) one at rest.</summary>
        public Observable<InteractState> OnState => _toggle.OnState;

        public Observable<bool> OnValueChanged => _changed;
        public Observable<Unit> OnSelected => _selected;

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _toggle.interactable = Interactable;
            // selectedColor is the selection-aware BASE (not a Selected-state absolute), so pass null
            // for the Selected absolute; selectedModulate stays the Selected multiplier.
            var abs = StateColorSet.Resolve(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.Resolve(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
            Color? selectedBase = string.IsNullOrWhiteSpace(_selectedColor)
                ? (Color?)null
                : UI.Theme.Resolve(_selectedColor);
            _bgReactor = StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod, selectedBase, IsOn);
            if (_selectedSprite != null) _toggle.transition = Selectable.Transition.None;
            ApplySelectedSprite();
        }

        public override void Dispose()
        {
            UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            _selected.Dispose();
            base.Dispose();
        }
    }
}
