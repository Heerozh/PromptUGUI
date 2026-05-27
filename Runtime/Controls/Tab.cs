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
            var lrt = _label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            ApplyFont();

            var bar = FindAncestorTabBar();
            if (bar == null)
                Debug.LogWarning($"Tab '{Id}' has no <TabBar> ancestor; mutual exclusion disabled.");
            else
                _toggle.group = bar.InternalToggleGroup;

            _toggle.onValueChanged.AddListener(v =>
            {
                _changed.OnNext(v);
                if (v) _selected.OnNext(Unit.Default);
            });
            UI.Locale.Changed += ApplyFont;
        }

        private TabBar FindAncestorTabBar()
        {
            // TabBar is a POCO Control (not a Component), so we can't GetComponent it.
            // Walk transform ancestors and look each GameObject up via the owning Screen's
            // NodeMap to find the Control instance.
            var screen = UI.OwnerScreenOf(this);
            if (screen == null) return null;
            var t = RectTransform.parent;
            while (t != null)
            {
                foreach (var c in screen.NodeMap.Values)
                {
                    if (c is TabBar bar && c.GameObject == t.gameObject) return bar;
                }
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
