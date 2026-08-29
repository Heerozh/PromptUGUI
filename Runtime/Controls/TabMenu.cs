using System;
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
    /// <summary>
    /// A tab group folded into a popup: collapsed it shows the selected tab's icon + text plus a
    /// caret; expanded it drops a panel of <c>&lt;Tab&gt;</c> rows.
    ///
    /// <para>Its children <em>are</em> tabs, so everything <see cref="Tab"/> already does —
    /// <c>bind=</c>, <c>isOn</c>, per-state colours, <c>selectedSprite</c>, procedural surfaces,
    /// <c>&lt;Show on="state-*"&gt;</c>, <c>BindItems</c> — works unchanged here. The selection
    /// semantics themselves are <see cref="TabGroupCore"/>, shared verbatim with
    /// <see cref="TabBar"/>; this control owns only the presentation.</para>
    ///
    /// <para><b>The procedural surface is the popup panel, not the handle</b> (spec TM-D3):
    /// <c>radius</c> / <c>glass</c> / <c>color</c> / <c>&lt;Decor&gt;</c> all describe the menu that
    /// drops down, and the collapsed handle is transparent by design. Wrap it in a
    /// <c>&lt;Frame&gt;</c> to give the handle a background — the same way <c>&lt;TabBar&gt;</c>
    /// gets a bar background.</para>
    /// </summary>
    public sealed class TabMenu : ProceduralControl
    {
        // Caption metrics. Fixed like Btn's/Tab's padding constants: the tunable part of the
        // caption is what the author writes (fontSize / iconSize / gap), not its inset.
        private const float PadX = 4f;
        private const float PadY = 6f;
        private const float MinTapHeight = 44f;
        private const float DefaultFontSize = 24f;

        private UnityImage _hit;          // transparent, raycastTarget=true: the handle's click area
        private UnityImage _icon;
        private UnityImage _arrow;
        private TMP_Text _label;
        private RectTransform _popup;
        private RectTransform _content;
        private UnityImage _popupBg;
        private CanvasGroup _popupCg;
        private Canvas _popupCanvas;
        private VerticalLayoutGroup _layout;
        private PuiButton _btn;
        private ToggleGroup _group;

        private readonly TabGroupCore _core;

        // The tab whose ContentChanged we currently mirror. Re-pointed on every selection change so
        // a caption tracks renames of the selected tab only.
        private Tab _captionSource;
        private Action _captionSourceHandler;
        private IDisposable _selectionSub;

        private float _iconSize = 24f;
        private float _arrowSize = 16f;
        private float _gap = 6f;
        private string _fontType = "default";

        public TabMenu()
        {
            _core = new TabGroupCore(this, () => _content);
        }

        // The panel is what draws; the handle deliberately has no surface of its own (TM-D3).
        private protected override GameObject SurfaceHost => _popup.gameObject;

        // No Selectable: the panel is not the thing that hovers or presses. Handle state colours are
        // out of scope for v1 (TM-D18), so nothing needs its targetGraphic followed.
        private protected override Selectable SurfaceSelectable => null;

        // Authored children — tabs, their Template/Animation wrappers, <Decor> — belong to the menu.
        protected internal override Transform ChildHostTransform => _content;

        public override void OnAttached()
        {
            var marker = GameObject.AddComponent<TabMenuMarker>();
            marker.Owner = this;

            _hit = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _hit.color = new Color(0f, 0f, 0f, 0f);
            _hit.raycastTarget = true;

            _group = GameObject.AddComponent<ToggleGroup>();
            _group.allowSwitchOff = false;

            BuildCaption();
            BuildPopup();

            _btn = GameObject.GetComponent<PuiButton>() ?? GameObject.AddComponent<PuiButton>();
            _btn.targetGraphic = _label;
            _btn.onClick.AddListener(Toggle);

            UI.Locale.Changed += ApplyFont;
        }

        private void BuildCaption()
        {
            _icon = ProceduralBuilders.AddImage(RectTransform, "Icon", raycast: false);
            _icon.enabled = false;
            var irt = _icon.rectTransform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);

            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            _label.alignment = TextAlignmentOptions.Left;
            _label.raycastTarget = false;
            _label.fontSize = DefaultFontSize;
            _label.text = "";
            var lrt = _label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);

            _arrow = ProceduralBuilders.AddImage(RectTransform, "Arrow", raycast: false);
            _arrow.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(_arrow, ProceduralBuilders.SpriteCaret);
            var art = _arrow.rectTransform;
            art.anchorMin = new Vector2(0f, 0.5f);
            art.anchorMax = new Vector2(0f, 0.5f);
            art.pivot = new Vector2(0f, 0.5f);

            ApplyFont();
        }

        private void BuildPopup()
        {
            // Anchored to the handle's bottom-left corner, growing down. PlacePopup re-derives this
            // (and may flip it upward) once there is a laid-out canvas to measure against.
            _popup = ProceduralBuilders.AddChild(RectTransform, "Popup");
            _popup.anchorMin = new Vector2(0f, 0f);
            _popup.anchorMax = new Vector2(0f, 0f);
            _popup.pivot = new Vector2(0f, 1f);

            _popupBg = _popup.gameObject.AddComponent<UnityImage>();
            _popupBg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_popupBg);

            _popupCg = _popup.gameObject.AddComponent<CanvasGroup>();

            // Added now, disabled: a Canvas + Raycaster appearing on expand would force a rebuild in
            // the frame the menu opens. overrideSorting is what lifts the panel above the page AND
            // what breaks it out of an ancestor mask (MaskUtilities stops at an overriding Canvas),
            // so a TabMenu inside a <ScrollList> still drops a full, unclipped menu.
            _popupCanvas = _popup.gameObject.AddComponent<Canvas>();
            _popupCanvas.overrideSorting = false;
            _popup.gameObject.AddComponent<GraphicRaycaster>();

            _content = ProceduralBuilders.AddChild(_popup, "Content");
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;

            _layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            // Mirrors TabBar's sizing contract, with one deliberate difference: menu rows span the
            // panel (childForceExpandWidth), because a column of ragged-width rows is not a menu.
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = true;
            _layout.childForceExpandHeight = false;
        }

        // ── Attributes ─────────────────────────────────────────────────────────────────────

        [UIAttr("fontSize"), Preserve]
        public float FontSize
        {
            set { _label.fontSize = value; LayoutCaption(); }
        }

        /// <summary>Caption text colour; distinct from <c>color</c>, which fills the popup panel.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => LabelColorApplier.Apply(_label, value);
        }

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        /// <summary>Caption icon edge length. The slot takes no space when the selected tab has no icon.</summary>
        [UIAttr, Preserve]
        public float IconSize { set { _iconSize = value; LayoutCaption(); } }

        /// <summary>Gap between caption icon, label and caret.</summary>
        [UIAttr, Preserve]
        public float Gap { set { _gap = value; LayoutCaption(); } }

        /// <summary>Caret edge length.</summary>
        [UIAttr, Preserve]
        public float ArrowSize { set { _arrowSize = value; LayoutCaption(); } }

        /// <summary>The caret. <c>""</c> hides it — a sprite-less Image renders a solid block.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Arrow
        {
            set
            {
                var sprite = UI.ResolveSprite(value);
                _arrow.sprite = sprite;
                _arrow.enabled = sprite != null;
                LayoutCaption();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string ArrowColor
        {
            set => ColorApplier.Apply(_arrow, UI.Theme.ResolveSpec(value));
        }

        /// <summary>The popup panel's fill — <em>not</em> the collapsed handle's (TM-D3).</summary>
        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                ColorApplier.Apply(_popupBg, spec);
                Surface.SetFill(spec.Top, spec.Bottom);
            }
        }

        /// <summary>The popup panel's background sprite. <c>""</c> / <c>none</c> = flat colour.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    _popupBg.sprite = null;
                    _popupBg.type = UnityImage.Type.Simple;
                    return;
                }
                var sprite = UI.ResolveSprite(value);
                if (sprite == null) return;
                _popupBg.sprite = sprite;
                _popupBg.type = ProceduralBuilders.DeriveType(sprite);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_popupBg, value);
        }

        /// <summary>Padding inside the popup panel (same shorthand as <c>&lt;TabBar padding&gt;</c>).</summary>
        [UIAttr, Preserve]
        public string Padding
        {
            set => _layout.padding = PaddingParser.Parse(value, _layout.padding);
        }

        /// <summary>Gap between menu rows.</summary>
        [UIAttr, Preserve]
        public float Spacing { set => _layout.spacing = value; }

        [UIAttr, Preserve]
        public string ItemTemplate { set => _core.ItemTemplate = value; }

        // ── Selection (delegated to TabGroupCore) ──────────────────────────────────────────

        public int Count => _core.Tabs.Count;

        public int SelectedIndex => _core.SelectedIndex;

        public Tab SelectedTab => _core.SelectedTab;

        public Tab GetAt(int index) => _core.Tabs[index];

        public Observable<Tab> OnSelectionChanged => _core.SelectionChanged;

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<Tab, T> bind)
            => BindItems<T, Tab>(source, bind);

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind) where TSlot : class, IControl
            => _core.BindItems(source, bind);

        // ── Caption ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-reads the selected tab's icon + text into the caption. Called on every selection
        /// change, on a content change of the selected tab, and after each attribute pass — a
        /// caller should not normally need it.
        /// </summary>
        public void RefreshCaption()
        {
            var tab = SelectedTab;
            RepointCaptionSource(tab);

            _label.text = tab != null ? tab.CaptionText ?? "" : "";
            var sprite = tab != null ? tab.CaptionIcon : null;
            _icon.sprite = sprite;
            _icon.enabled = sprite != null;

            LayoutCaption();
        }

        // Only the SELECTED tab's renames reach the caption, so exactly one subscription is live at
        // a time and it moves with the selection.
        private void RepointCaptionSource(Tab tab)
        {
            if (ReferenceEquals(_captionSource, tab)) return;
            if (_captionSource != null && _captionSourceHandler != null)
                _captionSource.ContentChanged -= _captionSourceHandler;
            _captionSource = tab;
            if (tab == null) return;
            _captionSourceHandler ??= RefreshCaption;
            tab.ContentChanged += _captionSourceHandler;
        }

        /// <summary>
        /// Places icon / label / caret in a row, left to right, each slot collapsing to nothing when
        /// it has no content. Hand-placed rather than run through a HorizontalLayoutGroup so
        /// <see cref="GetNativeSize"/> can state the same geometry as a closed-form expression.
        /// </summary>
        private void LayoutCaption()
        {
            var x = PadX;
            if (_icon != null && _icon.enabled)
            {
                _icon.rectTransform.sizeDelta = new Vector2(_iconSize, _iconSize);
                _icon.rectTransform.anchoredPosition = new Vector2(x, 0f);
                x += _iconSize + _gap;
            }

            var textWidth = LabelWidth();
            _label.rectTransform.sizeDelta = new Vector2(textWidth, _label.rectTransform.sizeDelta.y);
            _label.rectTransform.anchoredPosition = new Vector2(x, 0f);
            x += textWidth;

            if (_arrow != null && _arrow.enabled)
            {
                x += _gap;
                _arrow.rectTransform.sizeDelta = new Vector2(_arrowSize, _arrowSize);
                _arrow.rectTransform.anchoredPosition = new Vector2(x, 0f);
            }
        }

        // Unconstrained natural width — NOT preferredWidth, which TMP measures at the live rect and
        // would feed the previous solve's value back on a ReSolve. Mirrors Btn / Tab / Text.
        private float LabelWidth()
            => string.IsNullOrEmpty(_label.text) ? 0f : _label.GetPreferredValues(_label.text).x;

        private float LabelHeight()
            => string.IsNullOrEmpty(_label.text) ? 0f : _label.GetPreferredValues(_label.text).y;

        /// <summary>
        /// The collapsed handle hugs its caption — deliberately the opposite of
        /// <c>&lt;Dropdown&gt;</c>, whose fixed width keeps a form field from twitching as options
        /// change. A channel switcher wants the caret to sit right after the name.
        /// </summary>
        public override Vector2? GetNativeSize()
        {
            var w = PadX * 2f + LabelWidth();
            if (_icon != null && _icon.enabled) w += _iconSize + _gap;
            if (_arrow != null && _arrow.enabled) w += _gap + _arrowSize;
            return new Vector2(w, Mathf.Max(MinTapHeight, LabelHeight() + PadY * 2f));
        }

        private void ApplyFont() => FontApplier.Apply(_label, _fontType);

        // ── Expand / collapse (Task 5 wires the real behaviour) ────────────────────────────

        public bool IsExpanded { get; private set; }

        public void Toggle()
        {
            if (IsExpanded) Collapse(); else Expand();
        }

        public void Expand()
        {
        }

        public void Collapse()
        {
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────────────

        internal override void OnAfterApply()
        {
            base.OnAfterApply();

            _core.CollectStatic(Children);
            _core.SyncInitialSelection();
            _core.WireTabSubscriptions();
            _selectionSub ??= _core.SelectionChanged.Subscribe(_ => RefreshCaption());
            RefreshCaption();

            // The popup must stay active through Open()'s measuring pass: a TMP added by
            // AddComponent on an inactive GameObject never runs Awake and mis-measures its
            // preferred size. Same deferral Tab.bind uses for an unselected page.
            if (!IsExpanded)
            {
                var owner = UI.OwnerScreenOf(this);
                if (owner != null) owner.DeferDuringOpen(HidePopupIfCollapsed);
                else HidePopupIfCollapsed();
            }
        }

        private void HidePopupIfCollapsed()
        {
            if (!IsExpanded && _popup != null) _popup.gameObject.SetActive(false);
        }

        public override void Dispose()
        {
            UI.Locale.Changed -= ApplyFont;
            RepointCaptionSource(null);
            _selectionSub?.Dispose();
            _core.Dispose();
            base.Dispose();
        }
    }
}
