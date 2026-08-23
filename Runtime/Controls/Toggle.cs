using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Toggle : Control
    {
        private UnityImage _bg;
        private StateTintReactor _bgReactor;
        private UnityImage _checkmark;
        private PuiToggle _toggle;
        private TMP_Text _label;
        private string _fontType = "default";
        private string _groupName;
        private readonly Subject<bool> _changed = new();

        // Absolute per-state bg colours (set targetGraphic). Resolved in OnAfterApply.
        private string _hoverColor;
        private string _pressedColor;
        private string _selectedColor;
        private string _disabledColor;
        // Relative per-state multipliers (fan out to bg + descendants). Resolved in OnAfterApply.
        private string _hoverModulate;
        private string _pressedModulate;
        private string _selectedModulate;
        private string _disabledModulate;
        // Press/select content-offset (see StateOffsetInstaller). _offsetHolder lazily created.
        private Vector2? _pressedOffset;
        private Vector2? _selectedOffset;
        private RectTransform _offsetHolder;

        // Bound to OnAttached layout — changing these without changing OnAttached breaks the formula.
        // CheckmarkZoneWidth = Background sizeDelta.x (20) + 3px gap = Label offsetMin.x (23)
        // RightPadding       = -Label offsetMax.x (5)
        private const float CheckmarkZoneWidth = 23f;
        private const float RightPadding = 5f;
        private const float VerticalPadding = 6f;
        private const float MinTapHeight = 44f;
        private const float DefaultIconOnlySize = 44f;

        public override void OnAttached()
        {
            _toggle = GameObject.GetComponent<PuiToggle>() ?? GameObject.AddComponent<PuiToggle>();

            // Background：左侧垂直居中 20x20 box。
            // 默认 prefab 是 (0,1) 锚到 top-left + 20x20 + pos(10,-10)，因为 Toggle 固定 20 高刚好满；
            // PromptUGUI 里 Toggle 经常被 VStack 拉高，必须用 left-middle 锚点才能让 checkmark
            // 始终跟 label 视觉同行。这是对 prefab 的有意偏离 (M5.1 跟随式调整)。
            var bgRt = ProceduralBuilders.AddChild(RectTransform, "Background");
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(0f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta = new Vector2(20f, 20f);
            bgRt.anchoredPosition = new Vector2(10f, 0f);
            _bg = bgRt.gameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _toggle.targetGraphic = _bg;

            // Checkmark：放在 Background 内部，居中 20x20 simple sprite
            _checkmark = ProceduralBuilders.AddImage(bgRt, "Checkmark", raycast: false);
            _checkmark.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _checkmark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _checkmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
            _checkmark.rectTransform.anchoredPosition = Vector2.zero;
            _checkmark.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(_checkmark, ProceduralBuilders.SpriteCheckmark);
            _toggle.graphic = _checkmark;
            _toggle.InitStateBroadcast();

            // Label：从 Background 右边开始水平 stretch，垂直填满；raycastTarget=true 让整条 toggle 都能点击
            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.alignment = TextAlignmentOptions.Left;
            _label.raycastTarget = true;
            var labelRt = _label.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            // offsetMin.x=23：Background 占 0-20，留 3px gap (跟默认 prefab Label 实际可视区一致)
            // offsetMax.x=-5：右侧 5px padding
            // Y 全 stretch (0/0)：让 TMP 垂直居中渲染时跟 checkmark 同行
            labelRt.offsetMin = new Vector2(23f, 0f);
            labelRt.offsetMax = new Vector2(-5f, 0f);

            ApplyFont();

            _toggle.onValueChanged.AddListener(v => { _changed.OnNext(v); _bgReactor?.SetSelected(v); });
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            FontApplier.Apply(_label, _fontType);
        }

        [UIAttr, Preserve]
        public string Text
        {
            set
            {
                if (_label != null) _label.text = value ?? "";
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
                ApplyFont();
            }
        }

        /// <summary>Label text color (theme token / hex / CSS / gradient / <c>/alpha</c>);
        /// distinct from the background <c>color</c>. Empty = default ink.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => LabelColorApplier.Apply(_label, value);
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => Internal.ColorApplier.Apply(_bg, UI.Theme.ResolveSpec(value));
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        /// <summary>
        /// 勾选框（20×20 box）的 sprite —— 与 <c>color</c> 指向同一层，跟其它所有控件一致。
        /// 注意这是行为修正：它过去指向 checkmark，跟 <c>color</c> 指的不是一层。
        /// 想换对勾图形用 <c>checkmark</c>。
        /// </summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _bg.sprite = UI.ResolveSprite(value);
        }

        /// <summary>对勾图形的 sprite。<c>""</c> = 无图（纯色方块）。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Checkmark
        {
            set => _checkmark.sprite = UI.ResolveSprite(value);
        }

        /// <summary>对勾颜色；支持 token / <c>/alpha</c> / 渐变。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string CheckmarkColor
        {
            set => Internal.ColorApplier.Apply(_checkmark, UI.Theme.ResolveSpec(value));
        }

        [UIAttr, Preserve]
        public bool IsOn
        {
            get => _toggle.isOn;
            set => _toggle.isOn = value;
        }

        [UIAttr, Preserve]
        public string Group
        {
            set
            {
                _groupName = value;
                if (string.IsNullOrEmpty(value)) { _toggle.group = null; return; }
                var screen = PromptUGUI.Application.UI.OwnerScreenOf(this);
                _toggle.group = screen?.ToggleGroups.GetOrCreate(value);
            }
        }

        public Observable<bool> OnValueChanged => _changed;

        /// <summary>Absolute bg colour while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverColor { set => _hoverColor = value; }
        /// <summary>Absolute bg colour while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedColor { set => _pressedColor = value; }
        /// <summary>Absolute bg colour while checked (isOn) at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedColor { set => _selectedColor = value; }
        /// <summary>Absolute bg colour while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledColor { set => _disabledColor = value; }
        /// <summary>Relative colour multiplier (fans out to subtree) while Hover.</summary>
        [UIAttr(IsColor = true), Preserve] public string HoverModulate { set => _hoverModulate = value; }
        /// <summary>Relative colour multiplier while Pressed.</summary>
        [UIAttr(IsColor = true), Preserve] public string PressedModulate { set => _pressedModulate = value; }
        /// <summary>Relative colour multiplier while checked (isOn) at rest.</summary>
        [UIAttr(IsColor = true), Preserve] public string SelectedModulate { set => _selectedModulate = value; }
        /// <summary>Relative colour multiplier while Disabled.</summary>
        [UIAttr(IsColor = true), Preserve] public string DisabledModulate { set => _disabledModulate = value; }

        /// <summary>Content offset (px, Unity sign: negative y = down) while Pressed. <c>""</c>/<c>none</c>=none.</summary>
        [UIAttr, Preserve] public string PressedOffset { set => _pressedOffset = StateOffsetSet.Parse(value); }
        /// <summary>Content offset held while Selected (isOn). Composes with pressedOffset (Pressed wins).</summary>
        [UIAttr, Preserve] public string SelectedOffset { set => _selectedOffset = StateOffsetSet.Parse(value); }

        /// <summary>Broadcasts the Toggle's interaction state. Selected = checked (isOn) and at rest.</summary>
        public Observable<InteractState> OnState => _toggle.OnState;

        /// <summary>
        /// Runtime override so setting <see cref="Interactable"/> from code also drives the
        /// underlying Toggle — greying it + emitting <see cref="InteractState.Disabled"/> — not
        /// just the base CanvasGroup. Mirrors the XML-attr path (<see cref="OnAfterApply"/>) and
        /// <c>Btn.Interactable</c>, so code- and Variant-applied disables look identical.
        /// </summary>
        public override bool Interactable
        {
            get => base.Interactable;
            set
            {
                base.Interactable = value;
                if (_toggle != null) _toggle.interactable = value;
            }
        }

        protected internal override Transform ChildHostTransform
            => _offsetHolder != null ? _offsetHolder : RectTransform;

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _toggle.interactable = Interactable;
            _offsetHolder = StateOffsetInstaller.Install(GameObject, _offsetHolder, new StateOffsetSet(_pressedOffset, _selectedOffset));
            // selectedColor is the selection-aware BASE (not a Selected-state absolute); selectedModulate
            // stays the Selected multiplier. Toggle keeps its Checkmark overlay unchanged.
            var abs = StateColorSet.ResolveAbsolutes(_hoverColor, _pressedColor, null, _disabledColor);
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, _selectedModulate, StateColorSet.NoneToNull(_disabledModulate));
            ColorSpec? selectedBase = string.IsNullOrWhiteSpace(_selectedColor)
                ? (ColorSpec?)null
                : UI.Theme.ResolveSpec(_selectedColor);
            _bgReactor = StateTintInstaller.Install(GameObject, _toggle, Children, abs, mod, selectedBase, IsOn);
            // 默认禁用外观：作者未声明任何 disabled* 时整控件去色。
            if (string.IsNullOrWhiteSpace(_disabledColor) && string.IsNullOrWhiteSpace(_disabledModulate))
                DisabledGrayscaleInstaller.Install(GameObject, _toggle, Children);
        }

        public override Vector2? GetNativeSize()
        {
            if (_label != null && !string.IsNullOrEmpty(_label.text))
            {
                // Unconstrained natural size — NOT _label.preferredHeight, which TMP measures at the
                // live label-rect width. On a ReSolve that width is the previous solve's value, so a
                // grown label would wrap and inflate the height. Mirrors Text.GetNativeSize.
                var pref = _label.GetPreferredValues(_label.text);
                var w = pref.x + CheckmarkZoneWidth + RightPadding;
                var h = Mathf.Max(MinTapHeight, pref.y + VerticalPadding * 2f);
                return new Vector2(w, h);
            }
            return new Vector2(DefaultIconOnlySize, DefaultIconOnlySize);
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            base.Dispose();
        }
    }
}
