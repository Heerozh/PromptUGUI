using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// The suggestion list of an <c>&lt;InputField&gt;</c> (spec 2026-10-18 §5): a part element the
    /// field adopts, whose own GameObject is the floating panel. It hangs under the field — so a press
    /// anywhere in it walks up to the field's <c>ISelectHandler</c> and the field keeps editing — and
    /// a <see cref="PopupCore"/> lifts it above the page without a click catcher or a focus cage.
    ///
    /// <para>Rows come from <c>itemTemplate</c> through <c>BindItems</c>; the field and the host's
    /// code decide when it shows. Outside an <c>&lt;InputField&gt;</c> it stays inert.</para>
    /// </summary>
    public sealed class Suggestions : ProceduralControl, IScrollbarHost
    {
        internal const float DefaultMaxHeight = 320f;
        internal const float DefaultGap = 4f;
        internal const float DefaultTransition = 0.1f;

        private UnityImage _bg;
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private VerticalLayoutGroup _layout;
        private Scrollbar _bar;
        private bool _ownsBar;

        private InputField _field;
        private PopupCore _core;

        private float _maxHeight = DefaultMaxHeight;
        private float _gap = DefaultGap;
        private float _popupWidth;   // 0 = the field's width
        private bool _preferAbove;
        private bool _alignEnd;
        private float _transition = DefaultTransition;
        private string _itemTemplate;
        private Func<RectTransform, IControl> _factory;

        // One per item of the last push: a shell the template is instantiated under (spec §5.5).
        private sealed class Row
        {
            public RectTransform Shell;
            public FocuslessPress Press;
            public IControl Control;
        }

        private readonly List<Row> _rows = new();
        private readonly List<Row> _prevRows = new();
        private IItemBinding _binding;
        private int[] _remap;
        private string _rowsTemplate;
        private RectTransform _pool;
        private bool _warnedSelectable;
        private bool _inPush;
        private Action _queuedPush;
        private bool _disposed;

        // Visibility (spec §6.1): open ⟺ editing ∧ armed ∧ Count > 0 ∧ the field usable.
        private bool _armed;
        private bool _keepArmedOnClose;   // only an empty push closes without disarming
        private bool? _openSide;          // the side chosen on open (true = above); pushes keep it
        private int _highlight = -1;
        private bool _editEndPending;     // the edit ended while a row was held down (SG-D15)

        private readonly Subject<Unit> _opened = new();
        private readonly Subject<Unit> _closed = new();
        private readonly Subject<int> _highlightChanged = new();
        private readonly Subject<SuggestionSubmit> _submit = new();

        // The panel is what draws, and it is this control's own node.
        private protected override GameObject SurfaceHost => GameObject;

        // Placed by its field, never a slot in anyone's layout (spec §5.2).
        protected internal override bool ParticipatesInLayout => false;

        // Whatever ApplyCommon writes is overwritten by the placement on open; stretch is the least
        // surprising interim value (same as <Scrollbar> / <Decor>).
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(AnchorVertical.Stretch, AnchorHorizontal.Stretch);

        /// <summary>Whether the panel is showing.</summary>
        public bool IsOpen => _core != null && _core.IsOpen;

        internal InputField Field => _field;
        internal float MaxHeightValue => _maxHeight;
        internal float GapValue => _gap;
        internal bool PrefersAbove => _preferAbove;
        internal float TransitionSeconds => _transition;
        internal string ItemTemplateName => _itemTemplate;
        internal RectTransform Content => _content;
        internal RectTransform Viewport => _viewport;

        public override void OnAttached()
        {
            // The Image first: Surface captures the host's Image on first access.
            _bg = GameObject.GetComponent<UnityImage>();
            if (_bg == null) _bg = GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _bg.raycastTarget = true;

            _scroll = GameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;

            // Rectangular clip (spec §6.8): round panels keep their rows off the corners with padding.
            _viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.pivot = new Vector2(0f, 1f);
            _viewport.offsetMin = Vector2.zero;
            _viewport.offsetMax = Vector2.zero;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _content = ProceduralBuilders.AddChild(_viewport, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            _content.anchoredPosition = Vector2.zero;

            _layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = true;
            _layout.childForceExpandHeight = false;
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = _viewport;
            _scroll.content = _content;

            GameObject.AddComponent<PointerSink>();
        }

        /// <summary>
        /// Called by the <c>&lt;InputField&gt;</c> that adopted it: from now on the field is the
        /// popup's host — its place in the hierarchy decides ancestry and its root canvas the sorting.
        /// </summary>
        internal void AttachField(InputField field)
        {
            _field = field;
            // No catcher (a press outside ends the edit, and that closes it), no cage (the field keeps
            // the focus); gamepad B closes it like any popup.
            _core = new PopupCore(field, RectTransform, PopupDismiss.Owner, cageFocus: false, cancelButtonCloses: true)
            {
                TransitionSeconds = _transition,
                Measure = Measure,
            };
            _core.Opened += OnCoreOpened;
            _core.Closed += OnCoreClosed;
            field.EditingChanged += OnEditingChanged;
        }

        /// <summary>
        /// The field's end-of-apply step: close if it is now hidden, disabled or read-only, else re-place.
        /// </summary>
        internal void OnFieldAfterApply()
        {
            if (_core == null) return;
            _core.OnHostAfterApply();
            if (IsOpen && _field.IsReadOnly) Close();
        }

        // ── Showing (spec §6.1) ────────────────────────────────────────────────────────────

        /// <summary>Opening starts — after the panel is placed.</summary>
        public Observable<Unit> OnOpened => _opened;

        /// <summary>The panel closed, whoever closed it.</summary>
        public Observable<Unit> OnClosed => _closed;

        /// <summary>
        /// Arms the list: it shows while the field is editing and there is something to show — for
        /// a list that should appear on focus (recent searches) rather than on the first keystroke.
        /// </summary>
        public void Open()
        {
            _armed = true;
            Evaluate();
        }

        /// <summary>Closes the list and disarms it: it stays closed until the user types again or <see cref="Open"/>.</summary>
        public void Close()
        {
            _armed = false;
            _core?.Close(PopupCloseReason.Owner);
        }

        /// <summary>The user changed the text: arm, and show if there is something to show.</summary>
        internal void NotifyUserInput()
        {
            _armed = true;
            Evaluate();
        }

        private bool CanShow =>
            _core != null && _armed && _rows.Count > 0 && (_field.IsEditing || _editEndPending)
            && _field.PeekInteractable && !_field.IsReadOnly && _field.GameObject.activeInHierarchy;

        private void Evaluate()
        {
            if (_core == null) return;
            if (CanShow)
            {
                if (!_core.IsOpen) _core.Open();
                return;
            }
            if (!_core.IsOpen) return;
            // Nothing to show while still editing and armed: close, and let the next non-empty push re-open.
            _keepArmedOnClose = _armed && _rows.Count == 0 && _field.IsEditing;
            _core.Close(PopupCloseReason.Owner);
        }

        private void OnEditingChanged(bool editing)
        {
            if (editing) return;   // entering edit mode does not arm: last round's rows stay hidden
            // On Android a tap on the game view may hide the keyboard — ending the edit — before the tap on
            // a row is released. That press decides (SG-D15): see OnRowReleased.
            if (IsOpen && _rows.Exists(r => r.Press.IsPressed))
            {
                _editEndPending = true;
                return;
            }
            _armed = false;
            _core?.Close(PopupCloseReason.Owner);
        }

        private void OnRowReleased(Row row, bool clicks)
        {
            if (!_editEndPending) return;
            _editEndPending = false;
            var index = _rows.IndexOf(row);
            if (clicks && IsOpen && index >= 0)
            {
                CommitChosen(index);   // the click that follows finds the list closed and does nothing
                return;
            }
            _armed = false;
            _core?.Close(PopupCloseReason.Owner);
        }

        private void OnCoreOpened()
        {
            SetHighlight(-1);
            _opened.OnNext(Unit.Default);
        }

        private void OnCoreClosed(PopupCloseReason reason)
        {
            if (!_keepArmedOnClose) _armed = false;
            _keepArmedOnClose = false;
            _editEndPending = false;
            _openSide = null;
            SetHighlight(-1);
            // Covered by a modal, a routed page, the Loading overlay: typing on into a field the user can
            // no longer see would only push the list up again (spec §6.10).
            if (reason == PopupCloseReason.Covered) _field?.EndEditing();
            _closed.OnNext(Unit.Default);
        }

        // ── Placement (spec §6.8) ──────────────────────────────────────────────────────────

        private PopupLayout Measure()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var contentHeight = LayoutUtility.GetPreferredHeight(_content);
            var width = _popupWidth > 0f ? _popupWidth : _field.RectTransform.rect.width;

            var canvas = _field.GameObject.GetComponentInParent<Canvas>(true);
            var root = canvas != null ? canvas.rootCanvas : null;
            if (root == null)
            {
                var height = _maxHeight > 0f ? Mathf.Min(contentHeight, _maxHeight) : contentHeight;
                return new PopupLayout(new Vector2(width, height), _gap, _preferAbove, alignEnd: _alignEnd);
            }

            var bounds = PopupBounds.For(root);
            var field = PopupCore.RectInCanvas(_field.RectTransform, root);
            // The field and the bounds are root-canvas units; the rows, the cap and the gap are written in the
            // panel's and the field's own, which a scaled ancestor shrinks. Solved in canvas units, the height
            // comes back in the panel's.
            var sizeScale = PopupCore.CanvasScale(RectTransform, root).y;
            var gapScale = PopupCore.CanvasScale(_field.RectTransform, root).y;
            var (h, above) = SuggestionsPlacement.Solve(field, bounds, contentHeight * sizeScale, _maxHeight * sizeScale,
                                                        _gap * gapScale, _openSide ?? _preferAbove,
                                                        locked: _openSide.HasValue);
            _openSide = above;
            return new PopupLayout(new Vector2(width, h / sizeScale), _gap, above, bounds, _alignEnd);
        }

        // ── Highlight and submit (spec §6.2 / §6.4) ────────────────────────────────────────

        /// <summary>The keyboard / gamepad highlight; −1 = none. Ignored while the list is closed, or on a row
        /// that cannot be chosen.</summary>
        public int HighlightedIndex
        {
            get => _highlight;
            set
            {
                if (!IsOpen) return;
                var index = Mathf.Clamp(value, -1, _rows.Count - 1);
                if (index >= 0 && !Pickable(index)) return;
                SetHighlight(index);
            }
        }

        // A row its bind left interactable (spec 2026-10-19-suggestions-width-pickable §4.2).
        private bool Pickable(int index) => _rows[index].Press.Interactable;

        /// <summary>The highlight moved; −1 = back to none.</summary>
        public Observable<int> OnHighlightChanged => _highlightChanged;

        /// <summary>
        /// Every submit of the field (spec §6.4): a chosen suggestion carries its index, a submit of the
        /// typed text −1. With a list, this one stream covers them all.
        /// </summary>
        public Observable<SuggestionSubmit> OnSubmit => _submit;

        /// <summary>
        /// One step through "none → 0 → … → n−1 → none" (<paramref name="delta"/> = +1) or back (−1), over the
        /// rows that cannot be chosen. With none to choose it lands on "none" — the arrows do nothing.
        /// </summary>
        internal void MoveHighlight(int delta)
        {
            if (!IsOpen || _rows.Count == 0) return;
            var states = _rows.Count + 1;
            var next = _highlight;
            for (var step = 0; step < states; step++)
            {
                next = ((next + 1 + delta) % states + states) % states - 1;
                if (next < 0 || Pickable(next)) break;
            }
            SetHighlight(next);
        }

        private void SetHighlight(int index)
        {
            if (index == _highlight) return;
            if (_highlight >= 0 && _highlight < _rows.Count) _rows[_highlight].Press.SetOn(false);
            _highlight = index;
            if (index >= 0)
            {
                _rows[index].Press.SetOn(true);
                EnsureVisible(index);
            }
            _highlightChanged.OnNext(index);
        }

        /// <summary>Item <paramref name="index"/> was chosen: close, report it, end the edit.</summary>
        internal void CommitChosen(int index)
        {
            if (_field == null || index < 0 || index >= _rows.Count || !Pickable(index)) return;
            var text = _field.TextValue;
            Close();
            _submit.OnNext(new SuggestionSubmit(text, index));
            _field.EndEditing();
        }

        /// <summary>The typed text was submitted (the field has already raised its own <c>OnSubmit</c>).</summary>
        internal void CommitTyped(string text)
        {
            Close();
            _submit.OnNext(new SuggestionSubmit(text, -1));
        }

        private void OnRowClicked(Row row)
        {
            if (!IsOpen) return;
            var index = _rows.IndexOf(row);
            if (index >= 0) CommitChosen(index);
        }

        /// <summary>Scrolls the content just enough to show row <paramref name="index"/> whole.</summary>
        private void EnsureVisible(int index)
        {
            var shell = _rows[index].Shell;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            // Content's pivot is its top edge: local y = 0 there, negative going down.
            var top = -(shell.localPosition.y + shell.rect.yMax);
            var bottom = -(shell.localPosition.y + shell.rect.yMin);
            var scroll = RevealOffset(top, bottom, _viewport.rect.height, _content.anchoredPosition.y,
                                      _content.rect.height);
            if (!Mathf.Approximately(scroll, _content.anchoredPosition.y))
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, scroll);
        }

        /// <summary>
        /// The scroll offset (content top above the viewport top, ≥ 0) that brings the span
        /// [<paramref name="top"/>, <paramref name="bottom"/>] (from the content's top) fully into a
        /// viewport <paramref name="viewportHeight"/> tall, moving as little as possible.
        /// </summary>
        internal static float RevealOffset(float top, float bottom, float viewportHeight, float scroll,
                                           float contentHeight)
        {
            if (top < scroll) scroll = top;
            else if (bottom > scroll + viewportHeight) scroll = bottom - viewportHeight;
            return Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentHeight - viewportHeight));
        }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            // No authored <Scrollbar> came with the children — build the stock one now.
            EnsureDefaultScrollbar();
            // ApplyCommon has just written geometry, and hidden= may have switched the node on: the panel
            // is shown only while open (spec §5.2). After Screen.Open's measuring pass when one is running.
            if (IsOpen)
            {
                _core.Reposition();
                return;
            }
            var owner = UI.OwnerScreenOf(this);
            if (owner != null) owner.DeferDuringOpen(HideIfClosed);
            else HideIfClosed();
        }

        private void HideIfClosed()
        {
            if (!IsOpen && GameObject != null) GameObject.SetActive(false);
        }

        // ── Attributes ─────────────────────────────────────────────────────────────────────

        /// <summary>The row template, resolved like <c>&lt;ScrollList itemTemplate&gt;</c>.</summary>
        [UIAttr, Preserve]
        public string ItemTemplate
        {
            set
            {
                _itemTemplate = string.IsNullOrEmpty(value) ? null : value;
                _factory = ResolveFactory(_itemTemplate);
            }
        }

        private Func<RectTransform, IControl> ResolveFactory(string name)
        {
            if (name == null) return null;
            // Template first, then a registered Control — the resolver ScrollList / Carousel share.
            if (TemplateFactoryResolver.TryResolve(UI.OwnerScreenOf(this), name, $"itemTemplate='{name}'", out var factory))
                return factory;
            throw new ParseException(
                $"<Suggestions itemTemplate='{name}'>: tag is neither a registered Control nor a Template");
        }

        /// <summary>The panel's height cap; taller content scrolls. <c>0</c> = only the room available.</summary>
        [UIAttr, Preserve]
        public float MaxHeight
        {
            set => _maxHeight = NonNegative("maxHeight", value, DefaultMaxHeight);
        }

        /// <summary>Space between the field and the panel.</summary>
        [UIAttr, Preserve]
        public float Gap
        {
            set => _gap = NonNegative("gap", value, DefaultGap);
        }

        /// <summary>
        /// Which side it prefers — <c>below</c> (default) or <c>above</c>, flipped when that side lacks room — and,
        /// after a dash, which edges line up with the field's when it is wider: <c>start</c> (default, the left
        /// ones) or <c>end</c> (the right ones; it grows left). Spec 2026-10-19-suggestions-width-pickable §4.1.
        /// </summary>
        [UIAttr, Preserve]
        public string Placement
        {
            set
            {
                var v = string.IsNullOrEmpty(value) ? "below" : value.Trim();
                var dash = v.IndexOf('-');
                var side = dash < 0 ? v : v.Substring(0, dash);
                var align = dash < 0 ? "start" : v.Substring(dash + 1);
                if ((side != "below" && side != "above") || (align != "start" && align != "end"))
                    throw new ArgumentException(
                        $"Suggestions.placement='{value}' invalid; expected below|above, optionally -start|-end");
                _preferAbove = side == "above";
                _alignEnd = align == "end";
            }
        }

        /// <summary>The panel's width; <c>0</c> = the field's. Narrower than the field is allowed.</summary>
        [UIAttr, Preserve]
        public float PopupWidth
        {
            set => _popupWidth = NonNegative("popupWidth", value, 0f);
        }

        /// <summary>Padding inside the panel (same shorthand as <c>&lt;TabMenu padding&gt;</c>).</summary>
        [UIAttr, Preserve]
        public string Padding
        {
            set => _layout.padding = PaddingParser.Parse(value, _layout.padding);
        }

        /// <summary>Gap between rows.</summary>
        [UIAttr, Preserve]
        public float Spacing { set => _layout.spacing = value; }

        /// <summary>Open / close duration: <c>"0.1s"</c> / <c>"100ms"</c> / seconds; <c>0</c> snaps.</summary>
        [UIAttr, Preserve]
        public string Transition
        {
            set
            {
                _transition = DefaultTransition;
                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        _transition = Mathf.Max(0f, AnimationSpec.ParseSeconds(value));
                    }
                    catch (FormatException)
                    {
                        UILog.Warn(this,
                            $"[{SuggestionsRules.ValueCode}] <Suggestions id='{Id}'> transition='{value}' is not a " +
                            $"duration ('0.1s' / '100ms' / '0.1'); using {DefaultTransition}s.");
                    }
                }
                if (_core != null) _core.TransitionSeconds = _transition;
            }
        }

        /// <summary>The panel's fill.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                ColorApplier.Apply(_bg, spec);
                Surface.SetFill(spec);
            }
        }

        /// <summary>The panel's background sprite. <c>""</c> / <c>none</c> = flat colour.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    _bg.sprite = null;
                    _bg.type = UnityImage.Type.Simple;
                    return;
                }
                var sprite = UI.ResolveSprite(value);
                if (sprite == null) return;
                _bg.sprite = sprite;
                _bg.type = ProceduralBuilders.DeriveType(sprite);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        private float NonNegative(string attr, float value, float fallback)
        {
            if (value >= 0f) return value;
            UILog.Warn(this,
                $"[{SuggestionsRules.ValueCode}] <Suggestions id='{Id}'> {attr}='{value}' must be 0 or more; " +
                $"using {fallback}.");
            return fallback;
        }

        // ── Rows ───────────────────────────────────────────────────────────────────────────

        /// <summary>Items in the last push.</summary>
        public int Count => _rows.Count;

        /// <summary>
        /// Rows by position: item j is bound onto row j, rows are reused across pushes and released of
        /// their <c>.AddTo(row)</c> subscriptions before they are bound again — the
        /// <c>ScrollList.BindItems</c> contract (spec §6.9). Every row is re-bound on every push.
        /// </summary>
        public IDisposable BindItems<T, TSlot>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind)
            where TSlot : class, IControl =>
            Subscribe(source, new PositionalBinding<T, TSlot>(bind));

        public IDisposable BindItems<T>(Observable<IReadOnlyList<T>> source, Action<IControl, T> bind) =>
            BindItems<T, IControl>(source, bind);

        /// <summary>
        /// Rows follow their items by <paramref name="key"/>: a reorder only moves rows. A null or
        /// duplicate key rejects the whole push (logged through R3; the list keeps the previous one).
        /// </summary>
        public IDisposable BindItems<T, TSlot, TKey>(Observable<IReadOnlyList<T>> source, Action<TSlot, T> bind,
                                                     Func<T, TKey> key)
            where TSlot : class, IControl =>
            Subscribe(source, new KeyedBinding<T, TSlot, TKey>(bind, key ?? throw new ArgumentNullException(nameof(key))));

        public IDisposable BindItems<T, TKey>(Observable<IReadOnlyList<T>> source, Action<IControl, T> bind,
                                              Func<T, TKey> key) =>
            BindItems<T, IControl, TKey>(source, bind, key);

        private IDisposable Subscribe<T, TSlot>(Observable<IReadOnlyList<T>> source, ItemBinding<T, TSlot> binding)
            where TSlot : class, IControl =>
            source.Subscribe(items => OnPush(binding, items ?? Array.Empty<T>()));

        private void OnPush<T, TSlot>(ItemBinding<T, TSlot> binding, IReadOnlyList<T> items)
            where TSlot : class, IControl
        {
            // A host that forgot .AddTo(screen): the list is gone, the stream is not.
            if (_disposed) return;
            if (_inPush)
            {
                // A push from inside a bind callback: the latest one runs once this one is done.
                _queuedPush = () => OnPush(binding, items);
                return;
            }
            if (_factory == null)
                throw new InvalidOperationException(
                    $"<Suggestions id='{Id}'>: itemTemplate must be set before BindItems is called");

            // Validation (null / duplicate key) throws here, before anything has changed.
            binding.Accept(items, _binding, _rows.Count, ref _remap);
            _binding = binding;

            // A push resets the highlight (spec §6.1). Unlit on the row object itself: a keyed push may
            // move that row to another index.
            var hadHighlight = _highlight != -1;
            if (_highlight >= 0 && _highlight < _rows.Count) _rows[_highlight].Press.SetOn(false);
            _highlight = -1;

            _inPush = true;
            try
            {
                // Closed, the panel is inactive and rows built in it would not measure (TM-D12): the
                // core switches it on for the rebuild. Open, it re-places the panel around them.
                using (_core?.BeginRebuild())
                    Rebuild(binding, items.Count);
            }
            finally
            {
                _inPush = false;
            }

            if (hadHighlight) _highlightChanged.OnNext(-1);
            // Open on a non-empty push when armed; an empty one closes without disarming.
            Evaluate();

            var queued = _queuedPush;
            _queuedPush = null;
            queued?.Invoke();
        }

        /// <summary>
        /// Structure first, then bind (as <c>ScrollList.Rebuild</c>): item j takes the row
        /// <c>_remap[j]</c> names, rows nobody takes are retired out of Content, siblings are pinned to
        /// data order — and only then is every row bound, so a bind callback sees the final list.
        /// </summary>
        private void Rebuild(IItemBinding binding, int count)
        {
            var full = _rowsTemplate != _itemTemplate;
            _rowsTemplate = _itemTemplate;

            _prevRows.Clear();
            _prevRows.AddRange(_rows);
            _rows.Clear();
            for (var j = 0; j < count; j++)
            {
                var from = full ? -1 : _remap[j];
                Row row = null;
                if (from >= 0 && from < _prevRows.Count && _prevRows[from] != null
                    && _prevRows[from].Shell != null && _prevRows[from].Control.GameObject != null)
                {
                    row = _prevRows[from];
                    _prevRows[from] = null;
                    if (row.Control is Control c) c.ReleaseSubscriptions();
                }
                _rows.Add(row ?? NewRow());
            }
            foreach (var old in _prevRows)
                if (old != null) Retire(old);
            _prevRows.Clear();

            for (var j = 0; j < _rows.Count; j++)
                if (_rows[j].Shell.GetSiblingIndex() != j) _rows[j].Shell.SetSiblingIndex(j);

            for (var j = 0; j < count; j++)
            {
                var row = _rows[j];
                // Every row starts pickable; a bind that sets Interactable = false marks a message row ("No
                // match", "Loading…") — shown in state-disabled, never highlighted or chosen.
                var control = row.Control as Control;
                if (control != null && !control.PeekInteractable) control.Interactable = true;
                if (!binding.TryBind(row.Control, j))
                    throw new InvalidCastException(
                        $"itemTemplate='{_itemTemplate}' instantiated {row.Control.GetType().Name}, " +
                        $"but BindItems expected {binding.SlotType.Name}");
                row.Press.Interactable = control == null || control.PeekInteractable;
            }
        }

        /// <summary>
        /// The shell is built before the template: it carries the row's <see cref="FocuslessPress"/>,
        /// which the template's <c>&lt;Show on="state-*"&gt;</c> blocks look up the parent chain for
        /// while they are being applied — inside the factory call. Its layout group makes the template
        /// root a layout-group child (<c>height</c> applies), and the catcher makes the whole row take
        /// the pointer, gaps between the template's own graphics included.
        /// </summary>
        private Row NewRow()
        {
            var shell = ProceduralBuilders.AddChild(_content, "Row");
            shell.gameObject.AddComponent<HitCatcher>();
            var press = shell.gameObject.AddComponent<FocuslessPress>();
            var vlg = shell.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var row = new Row { Shell = shell, Press = press };
            press.OnClicked.Subscribe(_ => OnRowClicked(row));
            press.Released += clicks => OnRowReleased(row, clicks);
            row.Control = _factory(shell);

            if (!_warnedSelectable && shell.GetComponentInChildren<Selectable>(true) != null)
            {
                _warnedSelectable = true;
                UILog.Warn(this,
                    $"[{SuggestionsRules.RowSelectableCode}] <Suggestions id='{Id}'> itemTemplate='{_itemTemplate}': " +
                    "a row contains a Selectable (<Btn> / <Toggle> / <InputField> …). Pressing it takes the " +
                    "selection from the field and ends the edit; give the row its looks with " +
                    "<Show on=\"state-*\"> instead.");
            }
            return row;
        }

        /// <summary>
        /// Out of Content first, then disposed: in Play mode <c>Destroy</c> waits for the end of the frame,
        /// and until then the row would still be laid out and still hold a sibling index.
        /// </summary>
        private void Retire(Row row)
        {
            if (row.Shell != null) row.Shell.SetParent(EnsurePool(), worldPositionStays: false);
            row.Control?.Dispose();
            if (row.Shell == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(row.Shell.gameObject);
            else UnityEngine.Object.DestroyImmediate(row.Shell.gameObject);
        }

        private RectTransform EnsurePool()
        {
            if (_pool != null) return _pool;
            _pool = ProceduralBuilders.AddChild(RectTransform, "Pool");
            // Disabled, so a retired row parked here for the rest of the frame is never laid out.
            _pool.gameObject.AddComponent<VerticalLayoutGroup>().enabled = false;
            _pool.gameObject.SetActive(false);
            return _pool;
        }

        // ── The scrollbar (IScrollbarHost) ─────────────────────────────────────────────────

        // uGUI's expand mode needs the bar to be a direct child of the ScrollRect's transform: the panel.
        RectTransform IScrollbarHost.ScrollbarHost => RectTransform;

        void IScrollbarHost.AdoptScrollbar(Scrollbar bar)
        {
            if (_bar != null && _bar != bar)
            {
                UILog.Warn(bar,
                    $"[{ScrollbarRules.DuplicateCode}] <Suggestions id='{Id}'>: a second <Scrollbar> " +
                    $"(id='{bar.Id}') — a host takes one bar; the first in document order is used and this " +
                    "one is parked inactive.");
                bar.GameObject.SetActive(false);
                return;
            }
            _bar = bar;
            _ownsBar = false;
            bar.AttachHost(this);
            WireScrollbar();
        }

        void IScrollbarHost.OnScrollbarChanged(Scrollbar bar)
        {
            if (bar == _bar) WireScrollbar();
        }

        private void EnsureDefaultScrollbar()
        {
            if (_bar != null) return;
            var go = new GameObject(Scrollbar.NodeName, typeof(RectTransform));
            go.transform.SetParent(RectTransform, worldPositionStays: false);
            var bar = new Scrollbar();
            bar.AttachTo(go);
            _bar = bar;
            _ownsBar = true;
            bar.AttachHost(this);
            WireScrollbar();
        }

        /// <summary>
        /// Always vertical. Besides the ScrollRect wiring (as <c>&lt;Dropdown&gt;</c> does it), the bar
        /// is display-only (spec §5.4): uGUI's Scrollbar is a Selectable, and pressing it would deselect
        /// the field being edited — so no part of it takes a raycast, and it is off the navigation graph.
        /// Scrolling is by dragging the rows or the wheel.
        /// </summary>
        private void WireScrollbar()
        {
            if (_bar == null || _scroll == null) return;
            _bar.Orient(vertical: true);
            if (_scroll.verticalScrollbar != _bar.Bar) _scroll.verticalScrollbar = _bar.Bar;
            _scroll.verticalScrollbarVisibility = _bar.IsOverlay
                ? ScrollRect.ScrollbarVisibility.AutoHide
                : ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _scroll.verticalScrollbarSpacing = _bar.ResolvedSpacing;
            _viewport.sizeDelta = new Vector2(_bar.IsOverlay ? 0f : -(_bar.CurrentThickness + _bar.ResolvedSpacing), 0f);

            _bar.Bar.navigation = new Navigation { mode = Navigation.Mode.None };
            foreach (var g in _bar.GameObject.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
        }

        public override void Dispose()
        {
            _disposed = true;
            if (_field != null) _field.EditingChanged -= OnEditingChanged;
            foreach (var row in _rows) row.Control?.Dispose();
            _rows.Clear();
            if (_core != null)
            {
                _core.Opened -= OnCoreOpened;
                _core.Closed -= OnCoreClosed;
                _core.Dispose();
                _core = null;
            }
            _opened.Dispose();
            _closed.Dispose();
            _highlightChanged.Dispose();
            _submit.Dispose();
            if (_ownsBar) _bar?.Dispose();
            base.Dispose();
        }
    }
}
