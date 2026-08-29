using System;
using System.Collections.Generic;
using LitMotion;
using LitMotion.Extensions;
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
        private CompositeDisposable _activationSubs;

        private float _iconSize = 24f;
        private float _arrowSize = 16f;
        private float _gap = 6f;
        private float _popupWidth;                 // 0 = auto (max of handle width and content)
        private float _popupGap = 4f;
        private float _transition = DefaultTransition;
        private MotionHandle _fadeMotion;
        private MotionHandle _slideMotion;
        private MotionHandle _arrowMotion;
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

        /// <summary>Panel width. Unset (or 0) sizes it to the wider of the handle and its content.</summary>
        [UIAttr, Preserve]
        public float PopupWidth { set { _popupWidth = value; PlacePopup(); } }

        /// <summary>Space between handle and panel, on whichever side the panel drops.</summary>
        [UIAttr, Preserve]
        public float PopupGap { set { _popupGap = value; PlacePopup(); } }

        /// <summary>Default open / close duration when the author writes none.</summary>
        internal const float DefaultTransition = 0.15f;

        /// <summary>The resolved open / close duration, in seconds. 0 = no animation.</summary>
        internal float TransitionSeconds => _transition;

        /// <summary>
        /// Open / close duration: <c>"0.15s"</c> / <c>"150ms"</c> / a bare number of seconds;
        /// <c>0</c> snaps. The panel is an internal node, so an author cannot wrap it in an
        /// <c>&lt;Animation&gt;</c> — this is the hook for its own entrance (TM-D13). For animating
        /// the rows, use <c>&lt;Animation on="expand"&gt;</c> around each <c>&lt;Tab&gt;</c>.
        /// </summary>
        [UIAttr, Preserve]
        public string Transition
        {
            set
            {
                if (string.IsNullOrEmpty(value)) { _transition = DefaultTransition; return; }
                try
                {
                    _transition = Mathf.Max(0f, AnimationSpec.ParseSeconds(value));
                }
                catch (FormatException)
                {
                    Debug.LogWarning(
                        $"<TabMenu id='{Id}'> transition='{value}' is not a duration " +
                        $"('0.15s' / '150ms' / '0.15'); using {DefaultTransition}s.");
                    _transition = DefaultTransition;
                }
            }
        }

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
            => _core.BindItems(source, bind, BeforeRebuild, AfterRebuild);

        // A row is built by AddComponent-ing a TMP onto a fresh GameObject. Do that under an
        // inactive parent — which is exactly what a collapsed popup is — and the TMP never runs
        // Awake, so it reports a preferred size of 0 for the rest of its life and the row collapses.
        // Activating for the duration of the rebuild and switching back within the same frame costs
        // nothing visually (nothing renders between the two) and gets every row measured.
        private bool _tempActivatedForRebuild;

        private void BeforeRebuild()
        {
            if (_popup == null || _popup.gameObject.activeSelf) return;
            _popup.gameObject.SetActive(true);
            _tempActivatedForRebuild = true;
        }

        private void AfterRebuild()
        {
            WireActivationSubscriptions();
            RefreshCaption();

            if (_tempActivatedForRebuild)
            {
                _tempActivatedForRebuild = false;
                _popup.gameObject.SetActive(false);
                return;
            }
            PlacePopup();
        }

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

        // ── Expand / collapse ──────────────────────────────────────────────────────────────

        /// <summary>Name of the full-screen click catcher, on the root canvas while a menu is open.</summary>
        internal const string BlockerName = "__TabMenuBlocker";

        /// <summary>
        /// How far above the root canvas the panel sorts. The catcher sits one below it, so both
        /// clear the page without reaching the modal band (<c>UI.Modal.SortingOrderBase</c> = 1000).
        /// </summary>
        internal static int PopupSortingOffset = 2;

        // At most one menu is open anywhere: the catcher covers the screen, so a second one could
        // not be reached, and Escape needs a single unambiguous target (TM-D9 / TM-D16).
        private static TabMenu s_expanded;

        internal static bool HasExpandedMenu => s_expanded != null;

        private GameObject _blocker;
        private readonly Subject<Unit> _expandedSubject = new();
        private readonly Subject<Unit> _collapsedSubject = new();

        public bool IsExpanded { get; private set; }

        public Observable<Unit> OnExpanded => _expandedSubject;

        public Observable<Unit> OnCollapsed => _collapsedSubject;

        public void Toggle()
        {
            if (IsExpanded) Collapse(); else Expand();
        }

        public void Expand()
        {
            if (IsExpanded || !Interactable) return;
            if (GameObject == null || !GameObject.activeInHierarchy) return;

            if (s_expanded != null && s_expanded != this) s_expanded.Collapse();
            s_expanded = this;
            IsExpanded = true;

            _popup.gameObject.SetActive(true);

            var rootCanvas = RootCanvas();
            var baseOrder = rootCanvas != null ? rootCanvas.sortingOrder : 0;
            _popupCanvas.overrideSorting = true;
            _popupCanvas.sortingOrder = baseOrder + PopupSortingOffset;

            EnsureBlocker(rootCanvas, baseOrder + PopupSortingOffset - 1);
            PlacePopup();
            PlayTransition(expanding: true);

            _expandedSubject.OnNext(Unit.Default);
        }

        public void Collapse()
        {
            if (!IsExpanded) return;
            IsExpanded = false;
            if (s_expanded == this) s_expanded = null;

            if (_blocker != null) _blocker.SetActive(false);
            PlayTransition(expanding: false);

            _collapsedSubject.OnNext(Unit.Default);
        }

        /// <summary>
        /// Fades and slides the panel in or out and spins the caret, then — on the way out —
        /// deactivates the panel once the motion lands.
        /// </summary>
        /// <remarks>
        /// Outside play mode the motions are skipped and the end state written directly: LitMotion
        /// ticks on the player loop, so an EditMode caller would otherwise be left with a panel
        /// stuck at alpha 0 that never deactivates.
        /// </remarks>
        private void PlayTransition(bool expanding)
        {
            CancelMotions();

            var slideFrom = SlideOffset();
            var restPosition = _popup.anchoredPosition;

            if (_transition <= 0f || !UnityEngine.Application.isPlaying)
            {
                _popupCg.alpha = expanding ? 1f : 0f;
                _popup.anchoredPosition = restPosition;
                SetArrowAngle(expanding ? 180f : 0f);
                if (!expanding) DeactivatePopup();
                return;
            }

            var fromAlpha = expanding ? 0f : _popupCg.alpha;
            var toAlpha = expanding ? 1f : 0f;
            _popupCg.alpha = fromAlpha;

            _fadeMotion = LMotion.Create(fromAlpha, toAlpha, _transition)
                .WithEase(Ease.OutCubic)
                .Bind(_popupCg, static (v, cg) => { if (cg) cg.alpha = v; })
                .AddTo(_popup.gameObject);

            var from = expanding ? restPosition + slideFrom : restPosition;
            var to = expanding ? restPosition : restPosition + slideFrom;
            _popup.anchoredPosition = from;
            _slideMotion = LMotion.Create(from, to, _transition)
                .WithEase(Ease.OutCubic)
                .Bind(_popup, static (v, rt) => { if (rt) rt.anchoredPosition = v; })
                .AddTo(_popup.gameObject);

            var fromAngle = CurrentArrowAngle();
            var toAngle = expanding ? 180f : 0f;
            _arrowMotion = LMotion.Create(fromAngle, toAngle, _transition)
                .WithEase(Ease.OutCubic)
                .Bind(this, static (v, self) => self.SetArrowAngle(v))
                .AddTo(_popup.gameObject);

            if (!expanding)
            {
                // Only the collapse owns the deactivation, and only if it actually finishes: a
                // re-expand cancels this handle first, so a cancelled close cannot switch off a
                // panel the user just re-opened.
                var captured = _fadeMotion;
                _fadeMotion.GetAwaiter().OnCompleted(() =>
                {
                    if (!IsExpanded && captured.Equals(_fadeMotion)) DeactivatePopup();
                });
            }
        }

        // Enters from the side it is anchored to, so the panel appears to unfold out of the handle.
        private Vector2 SlideOffset()
        {
            const float Distance = 8f;
            return _popup.pivot.y > 0.5f ? new Vector2(0f, Distance) : new Vector2(0f, -Distance);
        }

        private float CurrentArrowAngle()
            => _arrow != null ? Mathf.Repeat(_arrow.rectTransform.localEulerAngles.z, 360f) : 0f;

        private void SetArrowAngle(float degrees)
        {
            if (_arrow == null) return;
            var e = _arrow.rectTransform.localEulerAngles;
            _arrow.rectTransform.localEulerAngles = new Vector3(e.x, e.y, degrees);
        }

        private void CancelMotions()
        {
            if (_fadeMotion.IsActive()) _fadeMotion.TryCancel();
            if (_slideMotion.IsActive()) _slideMotion.TryCancel();
            if (_arrowMotion.IsActive()) _arrowMotion.TryCancel();
        }

        private void DeactivatePopup()
        {
            if (_popup == null) return;
            _popupCanvas.overrideSorting = false;
            _popup.gameObject.SetActive(false);
        }

        private Canvas RootCanvas()
        {
            var canvas = GameObject.GetComponentInParent<Canvas>(true);
            return canvas != null ? canvas.rootCanvas : null;
        }

        // The catcher hangs off the ROOT canvas, not the menu: it has to cover the whole screen, and
        // a child of the handle could never do that without inheriting its clipping and position.
        private void EnsureBlocker(Canvas rootCanvas, int sortingOrder)
        {
            if (_blocker == null)
            {
                var host = rootCanvas != null ? rootCanvas.transform : GameObject.transform.root;
                _blocker = new GameObject(BlockerName, typeof(RectTransform)) { layer = GameObject.layer };
                var rt = (RectTransform)_blocker.transform;
                rt.SetParent(host, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                var img = _blocker.AddComponent<UnityImage>();
                img.color = new Color(0f, 0f, 0f, 0f);
                img.raycastTarget = true;

                _blocker.AddComponent<Canvas>().overrideSorting = true;
                _blocker.AddComponent<GraphicRaycaster>();

                var btn = _blocker.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                // Invisible and keyboard-unreachable: it exists for the pointer only, and letting
                // directional navigation land on it would strand the focus on nothing.
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.onClick.AddListener(Collapse);
            }
            _blocker.transform.SetAsLastSibling();
            _blocker.GetComponent<Canvas>().sortingOrder = sortingOrder;
            _blocker.SetActive(true);
        }

        /// <summary>
        /// Sizes the panel and hangs it off the handle, flipping or clamping it back inside the
        /// canvas when it would otherwise overflow. See <see cref="PopupPlacer"/> for the rules.
        /// </summary>
        private void PlacePopup()
        {
            if (_popup == null || !IsExpanded) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var width = _popupWidth > 0f
                ? _popupWidth
                : Mathf.Max(RectTransform.rect.width, LayoutUtility.GetPreferredWidth(_content));
            var height = LayoutUtility.GetPreferredHeight(_content);
            _popup.sizeDelta = new Vector2(width, height);

            var rootCanvas = RootCanvas();
            var placement = rootCanvas != null
                ? PopupPlacer.Solve(RectInCanvas(RectTransform, rootCanvas),
                                    new Vector2(width, height),
                                    ((RectTransform)rootCanvas.transform).rect,
                                    _popupGap)
                // No canvas to measure against (a detached test rig): keep the default drop-down.
                : new PopupPlacement(new Vector2(0f, 0f), new Vector2(0f, 1f),
                                     new Vector2(0f, -_popupGap), false);

            _popup.anchorMin = placement.Anchor;
            _popup.anchorMax = placement.Anchor;
            _popup.pivot = placement.Pivot;
            _popup.anchoredPosition = placement.AnchoredPosition;
        }

        private static Rect RectInCanvas(RectTransform rt, Canvas canvas)
        {
            var canvasRt = (RectTransform)canvas.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var min = canvasRt.InverseTransformPoint(corners[0]);   // bottom-left
            var max = canvasRt.InverseTransformPoint(corners[2]);   // top-right
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────────────

        internal override void OnAfterApply()
        {
            base.OnAfterApply();

            _core.CollectStatic(Children);
            _core.SyncInitialSelection();
            _core.WireTabSubscriptions();
            _selectionSub ??= _core.SelectionChanged.Subscribe(_ => OnSelectionSettled());
            WireActivationSubscriptions();
            RefreshCaption();

            // A variant / theme pass that hides or disables the menu must not leave an orphaned
            // panel floating over the page.
            if (IsExpanded && (!Interactable || !GameObject.activeInHierarchy)) Collapse();
            else PlacePopup();

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

        /// <summary>
        /// Mirrors <see cref="Btn.Interactable"/>: driving it from code greys the handle through the
        /// underlying Button as well as the base CanvasGroup — and shuts an open menu, which the
        /// user can no longer dismiss by clicking a row.
        /// </summary>
        public override bool Interactable
        {
            get => base.Interactable;
            set
            {
                base.Interactable = value;
                if (_btn != null) _btn.interactable = value;
                if (!value) Collapse();
            }
        }

        public override void Dispose()
        {
            UI.Locale.Changed -= ApplyFont;
            if (s_expanded == this) s_expanded = null;
            IsExpanded = false;
            // The catcher is parented to the root canvas, outside this control's subtree, so the
            // Screen teardown that destroys everything else would leave it behind.
            if (_blocker != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_blocker);
                else UnityEngine.Object.DestroyImmediate(_blocker);
                _blocker = null;
            }
            CancelMotions();
            RepointCaptionSource(null);
            _selectionSub?.Dispose();
            _activationSubs?.Dispose();
            _expandedSubject.Dispose();
            _collapsedSubject.Dispose();
            _core.Dispose();
            base.Dispose();
        }

        private void OnSelectionSettled()
        {
            RefreshCaption();
            Collapse();
        }

        // Picking a row closes the menu — including a re-pick of the row already selected, which
        // never reaches SelectionChanged (uGUI swallows it, see PuiToggle.OnClicked).
        private void WireActivationSubscriptions()
        {
            _activationSubs?.Dispose();
            _activationSubs = new CompositeDisposable();
            for (int i = 0; i < _core.Tabs.Count; i++)
                _core.Tabs[i].OnActivated.Subscribe(_ => Collapse()).AddTo(_activationSubs);
        }
    }
}
