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
        private UnityImage _overlay;
        private UnityImage _icon;
        private TMP_Text _label;
        private PuiToggle _toggle;
        private string _fontType = "default";
        private string _bindId;
        private bool _bindResolved;
        private Frame _boundFrame;
        private readonly Subject<bool> _changed = new();
        private readonly Subject<Unit> _selected = new();

        // Raw (unresolved) *Color attribute values. Resolved against UI.Theme in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _selectedColor;
        private string _disabledColor;

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
            if (_label == null) return;
            var settings = PromptUGUISettings.Instance;
            var locale = UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _label.font = asset;
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
                if (string.IsNullOrEmpty(value)) return;
                ApplyBgSprite(UI.ResolveSprite(value));
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string SelectedSprite
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                EnsureOverlay(UI.ResolveSprite(value));
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _bg.color = UI.Theme.Resolve(value);
        }

        private void ApplyBgSprite(UnityEngine.Sprite sprite)
        {
            if (sprite == null) return;
            _bg.sprite = sprite;
            _bg.type = sprite.border != Vector4.zero ? UnityImage.Type.Sliced : UnityImage.Type.Simple;
        }

        private void EnsureOverlay(UnityEngine.Sprite selectedSprite)
        {
            if (_overlay == null)
            {
                _overlay = ProceduralBuilders.AddImage(RectTransform, "Overlay", raycast: false);
                _overlay.rectTransform.SetSiblingIndex(0);   // draw under Label / Icon
                var rt = _overlay.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                _toggle.graphic = _overlay;
                _toggle.toggleTransition = UnityToggle.ToggleTransition.None;   // instant; TB-D5
            }
            if (selectedSprite == null) return;
            _overlay.sprite = selectedSprite;
            _overlay.type = selectedSprite.border != Vector4.zero
                ? UnityImage.Type.Sliced
                : UnityImage.Type.Simple;
        }

        /// <summary>Tint multiplier applied to the Tab's bg + descendant graphics while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Tint multiplier applied while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Tint multiplier applied while this Tab is the active (isOn) one at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedColor { set => _selectedColor = value; }
        /// <summary>Tint multiplier applied while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }

        /// <summary>Broadcasts the Tab's interaction state. Selected = this Tab is the active (isOn) one at rest.</summary>
        public Observable<InteractState> OnState => _toggle.OnState;

        public Observable<bool> OnValueChanged => _changed;
        public Observable<Unit> OnSelected => _selected;

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _toggle.interactable = Interactable;
            StateTintInstaller.Install(GameObject, _toggle, Children,
                _hoverColor, _pressedColor, _selectedColor, _disabledColor);
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
