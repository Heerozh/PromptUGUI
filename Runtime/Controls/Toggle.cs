using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Controls
{
    public sealed class Toggle : Control
    {
        private UnityImage _bg;
        private UnityImage _checkmark;
        private UnityToggle _toggle;
        private TMP_Text _label;
        private string _fontType = "default";
        private string _groupName;
        private readonly Subject<bool> _changed = new();

        public override void OnAttached()
        {
            _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();

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

            _toggle.onValueChanged.AddListener(v => _changed.OnNext(v));
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_label == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _label.font = asset;
        }

        [UIAttr]
        public string Text
        {
            set
            {
                if (_label != null) _label.text = value ?? "";
            }
        }

        [UIAttr]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
            }
        }

        [UIAttr]
        public string Sprite
        {
            set
            {
                if (string.IsNullOrEmpty(value)) { _checkmark.sprite = null; return; }
                _checkmark.sprite = Resources.Load<Sprite>(value);
            }
        }

        [UIAttr]
        public bool IsOn
        {
            get => _toggle.isOn;
            set => _toggle.isOn = value;
        }

        [UIAttr]
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

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            base.Dispose();
        }
    }
}
