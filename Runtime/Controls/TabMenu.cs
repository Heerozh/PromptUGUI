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
    public sealed class TabMenu : ProceduralControl, IExpandable
    {
        // Caption metrics. Fixed like Btn's/Tab's padding constants: the tunable part of the
        // caption is what the author writes (fontSize / iconSize / gap), not its inset.
        private const float PadX = 4f;
        private const float PadY = 6f;
        private const float MinTapHeight = 44f;
        private const float DefaultFontSize = 24f;

        private UnityImage _hit;          // transparent, raycastTarget=true: the handle's click area
        private CaptionBuilder _caption;  // [icon] label [caret], inline mode — shared with <Collapsible>
        private RectTransform _popup;
        private RectTransform _content;
        private UnityImage _popupBg;
        private PopupCore _popupCore;     // the panel's Canvas / catcher / Escape / focus cage / placement
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

        private float _popupWidth;                 // 0 = auto (max of handle width and content)
        private float _popupGap = 4f;
        private MotionHandle _arrowMotion;

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
            var marker = GameObject.AddComponent<ExpandableMarker>();
            marker.Owner = this;

            _hit = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _hit.color = new Color(0f, 0f, 0f, 0f);
            _hit.raycastTarget = true;

            _group = GameObject.AddComponent<ToggleGroup>();
            _group.allowSwitchOff = false;
            _core.Group = _group;

            BuildCaption();
            BuildPopup();

            _btn = GameObject.GetComponent<PuiButton>() ?? GameObject.AddComponent<PuiButton>();
            _btn.targetGraphic = _caption.Label;
            _btn.onClick.AddListener(Toggle);

            UI.Locale.Changed += ApplyFont;
        }

        // Inline mode: the caret trails the text, and the handle hugs the row (GetNativeSize).
        private void BuildCaption()
            => _caption = new CaptionBuilder(RectTransform, arrowAtRight: false,
                padX: PadX, gap: 6f, iconSize: 24f, arrowSize: 16f, fontSize: DefaultFontSize);

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

            // The core adds the panel's CanvasGroup / Canvas / Raycaster now, not on expand. Its
            // overriding Canvas is also what frees the panel from an ancestor mask, so a TabMenu inside
            // a <ScrollList> still drops a full, unclipped menu. A menu: the catcher eats the outside
            // press, the focus is caged in the panel, and gamepad B closes it too.
            _popupCore = new PopupCore(this, _popup, PopupDismiss.Blocker, cageFocus: true, cancelButtonCloses: true)
            {
                Measure = MeasurePopup,
                FocusPick = PickFocus,
                TransitionSeconds = DefaultTransition,
            };
            _popupCore.Closed += OnPopupClosed;

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
            set => _caption.SetFontSize(value);
        }

        /// <summary>Caption text colour; distinct from <c>color</c>, which fills the popup panel.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => LabelColorApplier.Apply(_caption.Label, value);
        }

        [UIAttr, Preserve]
        public string Font
        {
            set => _caption.FontType = value;
        }

        /// <summary>Caption icon edge length. The slot takes no space when the selected tab has no icon.</summary>
        [UIAttr, Preserve]
        public float IconSize { set => _caption.IconSize = value; }

        /// <summary>Gap between caption icon, label and caret.</summary>
        [UIAttr, Preserve]
        public float Gap { set => _caption.Gap = value; }

        /// <summary>Caret edge length.</summary>
        [UIAttr, Preserve]
        public float ArrowSize { set => _caption.ArrowSize = value; }

        /// <summary>The caret. <c>""</c> hides it — a sprite-less Image renders a solid block.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Arrow
        {
            set => _caption.SetArrowSprite(UI.ResolveSprite(value));
        }

        [UIAttr(IsColor = true), Preserve]
        public string ArrowColor
        {
            set => ColorApplier.Apply(_caption.Arrow, UI.Theme.ResolveSpec(value));
        }

        /// <summary>The popup panel's fill — <em>not</em> the collapsed handle's (TM-D3).</summary>
        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                ColorApplier.Apply(_popupBg, spec);
                Surface.SetFill(spec);
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

        /// <summary>
        /// Makes "no tab selected" a legal resting state — same contract as
        /// <see cref="TabBar.AllowSwitchOff"/>. The collapsed handle then shows an empty caption
        /// (and hugs just its caret) until something is picked.
        /// </summary>
        [UIAttr, Preserve]
        public bool AllowSwitchOff { set => _core.AllowSwitchOff = value; }

        /// <summary>Before any child tab exists — see <see cref="TabBar.PreConfigureGroup"/>.</summary>
        internal void PreConfigureGroup(string allowSwitchOff)
            => _core.AllowSwitchOff = bool.TryParse(allowSwitchOff, out var on) && on;

        /// <summary>Panel width. Unset (or 0) sizes it to the wider of the handle and its content.</summary>
        [UIAttr, Preserve]
        public float PopupWidth { set { _popupWidth = value; PlacePopup(); } }

        /// <summary>Space between handle and panel, on whichever side the panel drops.</summary>
        [UIAttr, Preserve]
        public float PopupGap { set { _popupGap = value; PlacePopup(); } }

        /// <summary>Default open / close duration when the author writes none.</summary>
        internal const float DefaultTransition = 0.15f;

        /// <summary>The resolved open / close duration, in seconds. 0 = no animation.</summary>
        internal float TransitionSeconds => _popupCore.TransitionSeconds;

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
                if (string.IsNullOrEmpty(value)) { _popupCore.TransitionSeconds = DefaultTransition; return; }
                try
                {
                    _popupCore.TransitionSeconds = Mathf.Max(0f, AnimationSpec.ParseSeconds(value));
                }
                catch (FormatException)
                {
                    UILog.Warn(this,
                        $"<TabMenu id='{Id}'> transition='{value}' is not a duration " +
                        $"('0.15s' / '150ms' / '0.15'); using {DefaultTransition}s.");
                    _popupCore.TransitionSeconds = DefaultTransition;
                }
            }
        }

        // ── Selection (delegated to TabGroupCore) ──────────────────────────────────────────

        public int Count => _core.Tabs.Count;

        public int SelectedIndex => _core.SelectedIndex;

        public Tab SelectedTab => _core.SelectedTab;

        public Tab GetAt(int index) => _core.Tabs[index];

        /// <summary>
        /// Deselects every tab, blanking the caption and hiding every bound page. Same contract as
        /// <see cref="TabBar.ClearSelection"/>; <see cref="OnSelectionChanged"/> emits <c>null</c>.
        /// </summary>
        public void ClearSelection() => _core.ClearSelection();

        /// <summary><see cref="ClearSelection()"/>, optionally without animation — see <see cref="TabBar.ClearSelection(bool)"/>.</summary>
        public void ClearSelection(bool animate)
        {
            if (animate) _core.ClearSelection();
            else InstantState.Run(_core.ClearSelection);
        }

        public Observable<Tab> OnSelectionChanged => _core.SelectionChanged;

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<Tab, T> bind)
            => BindItems<T, Tab>(source, bind);

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind) where TSlot : class, IControl
            => _core.BindItems(source, bind, BeforeRebuild, AfterRebuild);

        public IDisposable BindItems<T, TKey>(
            Observable<IReadOnlyList<T>> source,
            Action<Tab, T> bind,
            Func<T, TKey> key)
            => BindItems<T, Tab, TKey>(source, bind, key);

        public IDisposable BindItems<T, TSlot, TKey>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind,
            Func<T, TKey> key) where TSlot : class, IControl
            => _core.BindItems(source, bind, key, BeforeRebuild, AfterRebuild);

        // A row is built by AddComponent-ing a TMP onto a fresh GameObject, and a TMP built under an
        // inactive parent — which is exactly what a collapsed popup is — never measures itself again
        // (see InactiveMeasure). The core switches the panel on for the rebuild and off again straight
        // after — or, when the menu is open, re-places it around its new rows.
        private IDisposable _rebuildScope;

        private void BeforeRebuild()
        {
            if (_popup == null) return;
            _rebuildScope = _popupCore.BeginRebuild();
        }

        private void AfterRebuild()
        {
            WireActivationSubscriptions();
            RefreshCaption();

            var scope = _rebuildScope;
            _rebuildScope = null;
            scope?.Dispose();
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

            _caption.SetIconSprite(tab != null ? tab.CaptionIcon : null);
            _caption.SetText(tab != null ? tab.CaptionText ?? "" : "");
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
        /// The collapsed handle hugs its caption — deliberately the opposite of
        /// <c>&lt;Dropdown&gt;</c>, whose fixed width keeps a form field from twitching as options
        /// change. A channel switcher wants the caret to sit right after the name.
        /// </summary>
        public override Vector2? GetNativeSize()
        {
            // The caption is filled in OnAfterApply, which runs AFTER ApplyCommon measures us — so on
            // the very first pass the label is still empty and measuring it would hand the layout a
            // handle just wide enough for the caret. Peek at the tabs instead: they are children, so
            // the DFS post-order apply has already given them their text and isOn.
            var label = _caption.Label;
            var (text, hasIcon) = label != null && !string.IsNullOrEmpty(label.text)
                ? (label.text, _caption.Icon != null && _caption.Icon.enabled)
                : PeekSelectedContent();

            var size = _caption.MeasureText(text);
            return new Vector2(_caption.ContentWidth(text, hasIcon),
                               Mathf.Max(MinTapHeight, size.y + PadY * 2f));
        }

        /// <summary>
        /// What the caption is about to show, read straight off the tabs. Mirrors the group's
        /// auto-select rule (first tab when none is <c>isOn</c>) so the measurement matches whatever
        /// <see cref="RefreshCaption"/> lands on moments later — including the case where that rule
        /// is off (<c>allowSwitchOff</c> / a cleared selection), where the handle shows nothing and
        /// must therefore reserve nothing.
        /// </summary>
        private (string Text, bool HasIcon) PeekSelectedContent()
        {
            Tab first = null;
            foreach (var child in Children)
            {
                var tab = child as Tab ?? TabGroupCore.FindTabIn(child);
                if (tab == null) continue;
                first ??= tab;
                if (tab.IsOn) return (tab.CaptionText, tab.CaptionIcon != null);
            }
            return first != null && _core.AutoSelectsFirst
                ? (first.CaptionText, first.CaptionIcon != null)
                : (null, false);
        }

        private void ApplyFont() => _caption.ApplyFont();

        // ── Expand / collapse ──────────────────────────────────────────────────────────────

        // True only while Expand() is bringing the panel up. Activating the popup subtree makes uGUI
        // revalidate the ToggleGroup on enable, which re-announces the already-selected tab — and
        // "a tab was selected" is otherwise the signal to close. Without this guard a menu closes
        // itself in the same call that opened it.
        private bool _expanding;
        private readonly Subject<Unit> _expandedSubject = new();
        private readonly Subject<Unit> _collapsedSubject = new();

        public bool IsExpanded => _popupCore != null && _popupCore.IsOpen;

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

            // At most one menu is open anywhere (TM-D9): opening goes through PopupChain, which closes
            // any other popup first.
            _expanding = true;
            try
            {
                _popupCore.Open();
            }
            finally
            {
                _expanding = false;
            }

            PlayArrow(expanding: true);
            _expandedSubject.OnNext(Unit.Default);
        }

        public void Collapse()
        {
            if (!IsExpanded) return;
            _popupCore.Close(PopupCloseReason.Owner);
        }

        // Every close lands here, whoever closed it: a picked row, Escape, the catcher, a higher layer,
        // another menu opening, the Screen starting to close.
        private void OnPopupClosed(PopupCloseReason reason)
        {
            PlayArrow(expanding: false);
            _collapsedSubject.OnNext(Unit.Default);
        }

        /// <summary>
        /// Spins the caret alongside the core's fade and slide, over the same duration. Outside play
        /// mode the end state is written directly: LitMotion ticks on the player loop.
        /// </summary>
        private void PlayArrow(bool expanding)
        {
            if (_arrowMotion.IsActive()) _arrowMotion.TryCancel();

            var seconds = _popupCore.TransitionSeconds;
            if (seconds <= 0f || !UnityEngine.Application.isPlaying)
            {
                SetArrowFlip(expanding ? 1f : 0f);
                return;
            }

            _arrowMotion = LMotion.Create(CurrentArrowFlip(), expanding ? 1f : 0f, seconds)
                .WithEase(Ease.OutCubic)
                .Bind(this, static (v, self) => self.SetArrowFlip(v))
                .AddTo(_popup.gameObject);
        }

        /// <summary>How far through the turn the caret is: 0 = pointing down, 1 = pointing up.</summary>
        private float CurrentArrowFlip() => _caption != null ? _caption.ArrowRotation / 180f : 0f;

        /// <summary>
        /// Turns the caret 180° through its <b>mesh</b>, not its transform.
        /// </summary>
        /// <remarks>
        /// A transform turn (<c>localEulerAngles.z</c>) is wrong here: rotation happens about the
        /// pivot, and the caret's pivot is its LEFT edge (that is what lets
        /// <see cref="CaptionBuilder.Layout"/> place it by its left side), so turning it swings the
        /// whole glyph to the left of where it was placed and the caret visibly jumps sideways on
        /// every open. <see cref="RotateFlipEffect"/> rotates the vertices about the rect's CENTRE
        /// and never touches the transform, so a square caret turns in place. (This used to be a
        /// negative-y <c>localScale</c> for the same reason; the mesh effect replaced it when
        /// <c>&lt;Collapsible&gt;</c> needed the same caret — one mechanism, spec §6.)
        /// </remarks>
        private void SetArrowFlip(float t)
        {
            if (_caption == null) return;
            _caption.ArrowRotation = t * 180f;
        }

        // ── Placement ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The panel's size: <c>popupWidth</c>, else the wider of the handle and its rows; as tall as
        /// the rows. The core hangs it off the handle and flips or clamps it (<see cref="PopupPlacer"/>).
        /// </summary>
        private PopupLayout MeasurePopup()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var width = _popupWidth > 0f
                ? _popupWidth
                : Mathf.Max(RectTransform.rect.width, LayoutUtility.GetPreferredWidth(_content));
            var height = LayoutUtility.GetPreferredHeight(_content);
            return new PopupLayout(new Vector2(width, height), _popupGap);
        }

        private void PlacePopup() => _popupCore?.Reposition();

        // Opening a menu lands the focus on what is currently chosen, not on row 0 blindly.
        private GameObject PickFocus() =>
            SelectedTab != null && SelectedTab.GameObject != null ? SelectedTab.GameObject : null;

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

            // A variant / theme pass that hides or disables the menu must not leave an orphaned panel
            // floating over the page; otherwise the panel is re-placed. A collapsed panel stays active
            // through Open()'s measuring pass (a TMP added by AddComponent on an inactive GameObject
            // never runs Awake and mis-measures) and is switched off after it — the deferral Tab.bind
            // uses for an unselected page.
            _popupCore.OnHostAfterApply();
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
            // Leaves the chain and destroys the catcher, which hangs off the root canvas outside this
            // control's subtree — the Screen teardown that destroys everything else would leave it.
            if (_popupCore != null)
            {
                _popupCore.Closed -= OnPopupClosed;
                _popupCore.Dispose();
            }
            if (_arrowMotion.IsActive()) _arrowMotion.TryCancel();
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
            // Not while opening: see _expanding. Picking a row — by click or from code — is what
            // closes a menu, and reactivating the panel is neither.
            if (!_expanding) Collapse();
        }

        // Picking a row closes the menu — including a re-pick of the row already selected, which
        // never reaches SelectionChanged (uGUI swallows it, see PuiToggle.OnClicked).
        private void WireActivationSubscriptions()
        {
            _activationSubs?.Dispose();
            _activationSubs = new CompositeDisposable();
            for (int i = 0; i < _core.Tabs.Count; i++)
                _core.Tabs[i].OnActivated.Subscribe(_ => { if (!_expanding) Collapse(); }).AddTo(_activationSubs);
        }
    }
}
