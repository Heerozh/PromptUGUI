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
        private UnityToggle _toggle;
        private string _fontType = "default";
        private readonly Subject<bool> _changed = new();
        private readonly Subject<Unit> _selected = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultBtnColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
            _toggle.targetGraphic = _bg;
            _toggle.transition = Selectable.Transition.ColorTint;

            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.alignment = TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            _label.fontSize = 24;
            _label.text = "";
            var lrt = _label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            ApplyFont();

            var group = FindAncestorToggleGroup();
            if (group == null)
                Debug.LogWarning($"Tab '{Id}' has no <TabBar> ancestor; mutual exclusion disabled.");
            else
                _toggle.group = group;

            _toggle.onValueChanged.AddListener(v =>
            {
                _changed.OnNext(v);
                if (v) _selected.OnNext(Unit.Default);
            });
            UI.Locale.Changed += ApplyFont;
        }

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
        public string Text
        {
            set
            {
                if (_label != null) _label.text = value ?? "";
            }
        }

        internal override string PeekDefaultText() => _label != null ? _label.text : null;

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
            set { if (_label != null) _label.fontSize = value; }
        }

        public Observable<bool> OnValueChanged => _changed;
        public Observable<Unit> OnSelected => _selected;

        public override void Dispose()
        {
            UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            _selected.Dispose();
            base.Dispose();
        }
    }
}
