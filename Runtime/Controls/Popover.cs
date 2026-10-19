using System;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls.Internal;
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
    /// A floating panel anchored to any control (spec 2026-10-19): declared anywhere in a Screen, opened
    /// by its <c>on="click@id"</c> trigger or from code, placed beside its anchor on the preferred side
    /// (flipped when that helps, kept inside the safe area), and closed by a press outside, Esc / B, a
    /// layer covering it, or its anchor going away.
    ///
    /// <para>The node itself is the panel: a procedural surface that stacks its children like a
    /// <c>&lt;VStack&gt;</c> and sizes itself (<see cref="PopoverFitter"/>). It never takes part in a
    /// parent's layout and owns its geometry (<see cref="Control.OwnsGeometry"/>); a
    /// <see cref="PopupCore"/> lifts it, places it and runs the popup chain's rules.</para>
    /// </summary>
    public sealed class Popover : ProceduralControl, IExpandable
    {
        internal const float DefaultGap = 4f;
        internal const float DefaultTransition = 0.15f;

        private UnityImage _bg;
        private VerticalLayoutGroup _layout;
        private PopoverFitter _fitter;
        private PopupCore _core;

        private SizeSpec _size;
        private PopupSide _prefer = PopupSide.Bottom;
        private PopupAlign _align = PopupAlign.Start;
        private float _gap = DefaultGap;
        private float _transition = DefaultTransition;

        // Open state: what it hangs from (a control, or a screen point for OpenAt), and the side chosen
        // when it opened (kept while open).
        private IControl _anchor;
        private Vector2? _point;
        private PopupSide? _lockedSide;
        private bool _opening;

        // on="click@id": the raw value, the value last resolved, and the trigger it found.
        private string _onRaw = "manual";
        private string _resolvedOn = "manual";
        private Btn _trigger;
        private IDisposable _triggerSub;

        // What a close the core reports as Owner really was: our Close(), or a click on the trigger.
        private PopoverCloseReason? _closeAs;

        private readonly Subject<Unit> _opened = new();
        private readonly Subject<PopoverCloseReason> _closed = new();
        private readonly Subject<Unit> _expanded = new();
        private readonly Subject<Unit> _collapsed = new();

        private static readonly Vector3[] Corners = new Vector3[4];

        protected internal override bool ParticipatesInLayout => false;

        internal override bool OwnsGeometry => true;

        // The panel is the node itself: radius / glass / <Decor> all land on it (same as TM-D3).
        private protected override GameObject SurfaceHost => GameObject;

        internal float GapValue => _gap;
        internal float TransitionSeconds => _transition;
        internal PopupCore Core => _core;

        // ── Public surface ─────────────────────────────────────────────────────────────────

        public bool IsOpen => _core != null && _core.IsOpen;

        /// <summary>What it is anchored to; null while closed.</summary>
        public IControl Anchor => IsOpen ? _anchor : null;

        /// <summary>Opening starts: the content is active, measured and placed.</summary>
        public Observable<Unit> OnOpened => _opened;

        /// <summary>Closing starts, with the reason (spec §4.4).</summary>
        public Observable<PopoverCloseReason> OnClosed => _closed;

        bool IExpandable.IsExpanded => IsOpen;
        Observable<Unit> IExpandable.OnExpanded => _expanded;
        Observable<Unit> IExpandable.OnCollapsed => _collapsed;

        /// <summary>Opens at its <c>on="click@id"</c> trigger.</summary>
        public void Open() => Open(RequireTrigger());

        /// <summary>
        /// Opens beside <paramref name="anchor"/> — any control of the same Screen. Already open there:
        /// nothing; open elsewhere: closed (<see cref="PopoverCloseReason.Code"/>) and reopened here. Does
        /// nothing when it cannot open (§4.3); check <see cref="IsOpen"/>.
        /// </summary>
        public void Open(IControl anchor)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            ValidateAnchor(anchor);
            if (IsOpen && ReferenceEquals(anchor, _anchor)) return;
            if (IsOpen) Close();
            if (!CanOpen(anchor)) return;
            _point = null;
            OpenCore(anchor);
        }

        /// <summary>Opens at a point in screen pixels (<c>PointerEventData.position</c>) — a context menu.</summary>
        public void OpenAt(Vector2 screenPosition)
        {
            if (IsOpen) Close();
            if (!CanOpen(null)) return;
            _point = screenPosition;
            OpenCore(null);
        }

        /// <summary>Its <c>on="click@id"</c> trigger's toggle, from code.</summary>
        public void Toggle() => Toggle(RequireTrigger());

        /// <summary>Open on <paramref name="anchor"/>, or close if it is already open there.</summary>
        public void Toggle(IControl anchor)
        {
            if (IsOpen && ReferenceEquals(anchor, _anchor)) Close();
            else Open(anchor);
        }

        public void Close() => CloseAs(PopoverCloseReason.Code);

        /// <summary>Reads <c>!IsOpen</c>. Setting true closes it; setting false does nothing — opening needs
        /// an anchor (spec §4.11). XML <c>hidden=</c> is ignored.</summary>
        public override bool Hidden
        {
            get => !IsOpen;
            set
            {
                if (value) Close();
            }
        }

        /// <summary>Not interactable: it cannot open, and an open one closes.</summary>
        public override bool Interactable
        {
            get => base.Interactable;
            set
            {
                base.Interactable = value;
                if (!value) Close();
            }
        }

        // ── Construction ───────────────────────────────────────────────────────────────────

        public override void OnAttached()
        {
            var rt = RectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            // The Image first: the procedural surface captures the host's Image on first access.
            _bg = GameObject.GetComponent<UnityImage>();
            if (_bg == null) _bg = GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _bg.raycastTarget = true;

            var marker = GameObject.AddComponent<ExpandableMarker>();
            marker.Owner = this;

            // Same group setup as <VStack>: children sized by their layout elements, never stretched.
            _layout = GameObject.AddComponent<VerticalLayoutGroup>();
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = false;
            _layout.childForceExpandHeight = false;
            _layout.childAlignment = TextAnchor.UpperCenter;

            _fitter = GameObject.AddComponent<PopoverFitter>();
            _fitter.Owner = this;
            GameObject.AddComponent<PanelSink>();

            _core = new PopupCore(this, rt, PopupDismiss.PassThrough, cageFocus: true, cancelButtonCloses: true)
            {
                Measure = Measure,
                TransitionSeconds = _transition,
                Anchor = AnchorTransform,
                RestoreTo = RestoreTarget,
                RestoreWhenUncaged = true,
                LateTick = Follow,
            };
            _core.Closed += OnCoreClosed;
        }

        internal override void ApplyOwnSize(SizeSpec sizeSpec)
        {
            if (sizeSpec.IsFlexibleWidth || sizeSpec.IsFlexibleHeight
                || sizeSpec.IsFractionalWidth || sizeSpec.IsFractionalHeight
                || sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)
                throw new ArgumentException(
                    $"[{PopoverRules.SizeCode}] <Popover id='{Id}'>: width / height / size take a number, 'hug' or " +
                    "'clamp(min, hug, max)' — a popover sits in no layout and has no parent size to share, so " +
                    "'stretch', '%' and 'native' mean nothing here.");
            _size = sizeSpec;
            _fitter?.SetDirty();
        }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            // Hidden / disabled while open → closed; otherwise re-placed. Closed → switched off, after
            // Screen.Open's measuring pass when one is running (TM-D12).
            _closeAs = PopoverCloseReason.Code;
            try
            {
                _core.OnHostAfterApply();
            }
            finally
            {
                _closeAs = null;
            }
            if (_onRaw != _resolvedOn) ScheduleTriggerResolve();
        }

        // ── on="click@id" (spec §4.3) ──────────────────────────────────────────────────────

        /// <summary><c>click@&lt;id&gt;</c> — a <c>&lt;Btn&gt;</c> that toggles it and is its anchor — or <c>manual</c>.</summary>
        [UIAttr("on"), Preserve]
        public string On
        {
            set
            {
                var v = string.IsNullOrEmpty(value) ? "manual" : value.Trim();
                const string click = "click@";
                var ok = v == "manual"
                         || (v.StartsWith(click, StringComparison.Ordinal) && v.Length > click.Length
                             && v.IndexOf('@', click.Length) < 0);
                if (!ok)
                    throw new ArgumentException(
                        $"[{PopoverRules.OnCode}] <Popover id='{Id}'> on='{value}': a popover opens on " +
                        "'click@<id>' (a <Btn> outside it, which also becomes its anchor) or 'manual' (from code).");
                _onRaw = v;
            }
        }

        // Resolved once whatever is being built is complete: the trigger may be declared after the popover,
        // or later in the same template row (spec §5.6).
        private void ScheduleTriggerResolve()
        {
            var wanted = _onRaw;
            _resolvedOn = wanted;
            void Resolve()
            {
                if (_onRaw != wanted || _core == null) return;   // superseded, or disposed meanwhile
                _triggerSub?.Dispose();
                _triggerSub = null;
                _trigger = null;
                if (wanted == "manual") return;
                Btn btn;
                try
                {
                    btn = TriggerSourceResolver.FindTriggerBtn(this, wanted.Substring("click@".Length), "Popover");
                }
                catch (Exception ex) when (!(ex is ParseException))
                {
                    throw ControlAttributeApplier.WrapForNode(SourceNode, ex);
                }
                _trigger = btn;
                _triggerSub = btn.OnClick.Subscribe(_ => ToggleFromTrigger(btn));
            }

            var owner = UI.OwnerScreenOf(this);
            if (owner != null) owner.DeferDuringOpen(() => ScreenInstantiator.DeferAfterBuild(Resolve));
            else ScreenInstantiator.DeferAfterBuild(Resolve);
        }

        private void ToggleFromTrigger(Btn trigger)
        {
            if (IsOpen && ReferenceEquals(_anchor, trigger)) CloseAs(PopoverCloseReason.Trigger);
            else Open(trigger);
        }

        private Btn RequireTrigger()
            => _trigger ?? throw new InvalidOperationException(
                $"<Popover id='{Id}'> has no on=\"click@<id>\" trigger: open it with Open(anchor) or OpenAt(point).");

        // ── Attributes ─────────────────────────────────────────────────────────────────────

        /// <summary><c>top|bottom|left|right</c>, optionally <c>-start|-center|-end</c>.</summary>
        [UIAttr, Preserve]
        public string Placement
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _prefer = PopupSide.Bottom;
                    _align = PopupAlign.Start;
                    return;
                }
                (_prefer, _align) = PopupPlacer.ParsePlacement(value);
            }
        }

        /// <summary>Space between the anchor and the panel. A string so a non-number warns instead of failing.</summary>
        [UIAttr, Preserve]
        public string Gap
        {
            set
            {
                _gap = DefaultGap;
                if (string.IsNullOrEmpty(value)) return;
                if (float.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0f)
                {
                    _gap = v;
                    return;
                }
                UILog.Warn(this,
                    $"[{PopoverRules.ValueCode}] <Popover id='{Id}'> gap='{value}' must be a number of 0 or more; " +
                    $"using {DefaultGap}.");
            }
        }

        /// <summary><c>pass</c> (default): a press outside closes it and still lands; <c>block</c>: a catcher eats it.</summary>
        [UIAttr, Preserve]
        public string Dismiss
        {
            set
            {
                PopupDismiss mode;
                switch (string.IsNullOrEmpty(value) ? "pass" : value.Trim())
                {
                    case "pass": mode = PopupDismiss.PassThrough; break;
                    case "block": mode = PopupDismiss.Blocker; break;
                    default:
                        throw new ArgumentException($"Popover.dismiss='{value}' invalid; expected pass|block");
                }
                _core.Dismiss = mode;
            }
        }

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
                            $"[{PopoverRules.ValueCode}] <Popover id='{Id}'> transition='{value}' is not a " +
                            $"duration ('0.15s' / '150ms' / '0.15'); using {DefaultTransition}s.");
                    }
                }
                _core.TransitionSeconds = _transition;
            }
        }

        [UIAttr, Preserve]
        public string Padding
        {
            set
            {
                VStack.ParseTRBL(value, out var t, out var r, out var b, out var l);
                _layout.padding = new RectOffset(l, r, t, b);
            }
        }

        [UIAttr, Preserve]
        public float Spacing { set => _layout.spacing = value; }

        [UIAttr, Preserve]
        public string ChildAlign
        {
            set => _layout.childAlignment = string.IsNullOrEmpty(value) ? TextAnchor.UpperCenter : VStack.ParseChildAlign(value);
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

        // ── Open / close ───────────────────────────────────────────────────────────────────

        private void ValidateAnchor(IControl anchor)
        {
            var go = anchor.GameObject;
            if (go != null && GameObject != null && go.transform.IsChildOf(GameObject.transform))
                throw new ArgumentException($"<Popover id='{Id}'>: its anchor cannot be inside the popover itself.",
                    nameof(anchor));
            var mine = UI.OwnerScreenOf(this);
            var theirs = UI.OwnerScreenOf(anchor);
            if (mine != null && theirs != null && !ReferenceEquals(mine, theirs))
                throw new ArgumentException(
                    $"<Popover id='{Id}'>: its anchor belongs to Screen '{theirs.Name}', not '{mine.Name}' — a popover " +
                    "opens beside controls of its own Screen.", nameof(anchor));
        }

        // Quietly refused (spec §4.3): not interactable, nowhere to hang from, or its layer is covered —
        // a dialog or the Loading overlay is over it, so it would draw on top of what covers it.
        private bool CanOpen(IControl anchor)
        {
            if (_core == null || GameObject == null) return false;
            if (!PeekInteractable) return false;
            var parent = RectTransform.parent;
            if (parent == null || !parent.gameObject.activeInHierarchy) return false;
            if (anchor != null)
            {
                var go = anchor.GameObject;
                if (go == null || !go.activeInHierarchy || !AnchorInteractable(anchor)) return false;
            }
            var root = RootCanvas();
            var order = root != null ? root.sortingOrder : 0;
            if (order < EscapeChain.TopModalOrder()) return false;
            if (LoadingOverlay.ActiveCount > 0 && order < Loading.SortingOrder) return false;
            return true;
        }

        // Not IControl.Interactable: that getter adds a CanvasGroup (spec §4.6).
        private static bool AnchorInteractable(IControl anchor)
        {
            var go = anchor.GameObject;
            if (go == null) return false;
            var selectable = go.GetComponent<Selectable>();
            if (selectable != null) return selectable.IsInteractable();
            return !(anchor is Control c) || c.PeekInteractable;
        }

        private void CloseAs(PopoverCloseReason reason)
        {
            if (!IsOpen) return;
            _closeAs = reason;
            try
            {
                _core.Close(PopupCloseReason.Owner);
            }
            finally
            {
                _closeAs = null;
            }
        }

        private void OpenCore(IControl anchor)
        {
            _anchor = anchor;
            _lockedSide = null;
            _selectedAtOpen = CurrentSelection();
            _opening = true;
            try
            {
                // Measure before placing: the content may have changed while the panel was off.
                GameObject.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);
                _core.Open();
            }
            finally
            {
                _opening = false;
            }
            if (!IsOpen) return;
            RememberPlacement();
            _opened.OnNext(Unit.Default);
            _expanded.OnNext(Unit.Default);
        }

        // ── Following (spec §4.6) ──────────────────────────────────────────────────────────

        // What the last placement was made against: the anchor's rect, the bounds, the safe area.
        private Rect _lastAnchorRect;
        private Rect _lastBounds;
        private Rect _lastSafe;
        private GameObject _selectedAtOpen;

        /// <summary>Tests: the panel's <c>LateUpdate</c> step (EditMode runs none).</summary>
        internal void TickForTests() => Follow();

        /// <summary>
        /// Every frame while open (after the frame's motion): close when the anchor went away, re-place when
        /// it or the bounds moved. A new safe area (the screen turned, the window resized) re-picks the side;
        /// a soft keyboard only re-clamps it on the side chosen at open.
        /// </summary>
        private void Follow()
        {
            if (!IsOpen) return;
            if (!_point.HasValue && (!AnchorAlive() || AnchorClippedOut()))
            {
                _core.Close(PopupCloseReason.AnchorLost);
                return;
            }
            var root = RootCanvas();
            if (root == null) return;
            var safe = PopupBounds.SafeFor(root);
            var bounds = PopupBounds.For(root);
            var anchor = _point.HasValue ? default : AnchorRectInCanvas(root);
            if (safe != _lastSafe) _lockedSide = null;
            else if (anchor == _lastAnchorRect && bounds == _lastBounds) return;
            _core.Reposition();
            RememberPlacement();
        }

        private void RememberPlacement()
        {
            var root = RootCanvas();
            if (root == null) return;
            _lastSafe = PopupBounds.SafeFor(root);
            _lastBounds = PopupBounds.For(root);
            _lastAnchorRect = _point.HasValue ? default : AnchorRectInCanvas(root);
        }

        private bool AnchorAlive()
        {
            var go = _anchor?.GameObject;
            return go != null && go.activeInHierarchy && AnchorInteractable(_anchor);
        }

        // Entirely outside what clips it: every RectMask2D / Mask above the anchor, up to the first canvas
        // that sorts on its own — the way MaskUtilities stops — so an anchor inside another popover's
        // panel is not judged by the list that panel happens to hang in.
        private bool AnchorClippedOut()
        {
            var root = RootCanvas();
            var art = _anchor?.RectTransform;
            if (root == null || art == null) return false;
            var rect = AnchorRectInCanvas(root);
            for (var t = art.parent; t != null; t = t.parent)
            {
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null && (canvas.overrideSorting || canvas.isRootCanvas)) break;
                var clips = (t.GetComponent<RectMask2D>() is RectMask2D m2 && m2.isActiveAndEnabled)
                            || (t.GetComponent<Mask>() is Mask m && m.isActiveAndEnabled);
                if (!clips) continue;
                if (!rect.Overlaps(RectInCanvas((RectTransform)t, root))) return true;
            }
            return false;
        }

        private static GameObject CurrentSelection()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            return es != null ? es.currentSelectedGameObject : null;
        }

        // Where the focus goes when a selection is left in the closing panel (spec §4.8): the anchor, unless
        // it is gone or no longer shows what it showed (AnchorLost / HostGone); else what was selected when
        // it opened; else nowhere.
        private GameObject RestoreTarget(PopupCloseReason reason)
        {
            if (reason != PopupCloseReason.AnchorLost && reason != PopupCloseReason.HostGone && _anchor != null)
            {
                var go = _anchor.GameObject;
                var sel = go != null ? go.GetComponent<Selectable>() : null;
                if (sel != null && go.activeInHierarchy && sel.IsInteractable()) return go;
            }
            return _selectedAtOpen != null && _selectedAtOpen.activeInHierarchy ? _selectedAtOpen : null;
        }

        private void OnCoreClosed(PopupCloseReason reason)
        {
            _lockedSide = null;
            _closed.OnNext(Map(reason));
            _collapsed.OnNext(Unit.Default);
        }

        private PopoverCloseReason Map(PopupCloseReason reason)
        {
            switch (reason)
            {
                case PopupCloseReason.Escape: return PopoverCloseReason.Escape;
                case PopupCloseReason.OutsidePress: return PopoverCloseReason.OutsidePress;
                case PopupCloseReason.Covered: return PopoverCloseReason.Covered;
                case PopupCloseReason.HostGone: return PopoverCloseReason.HostGone;
                case PopupCloseReason.AnchorLost: return PopoverCloseReason.AnchorLost;
                case PopupCloseReason.Replaced: return PopoverCloseReason.Replaced;
                default: return _closeAs ?? PopoverCloseReason.Code;
            }
        }

        private Transform AnchorTransform() => _anchor?.RectTransform;

        // ── Placement and size ─────────────────────────────────────────────────────────────

        private Canvas RootCanvas()
        {
            var parent = RectTransform.parent;
            if (parent == null) return null;
            var canvas = parent.GetComponentInParent<Canvas>(true);
            return canvas != null ? canvas.rootCanvas : null;
        }

        private PopupLayout Measure()
        {
            var root = RootCanvas();
            var rt = RectTransform;
            if (root == null) return new PopupLayout(rt.rect.size, _gap);

            var bounds = PopupBounds.For(root);
            var anchorRect = _point.HasValue ? PointInCanvas(root, _point.Value) : AnchorRectInCanvas(root);
            var size = rt.rect.size * ScaleToCanvas(rt, root);
            var placement = PopupPlacer.SolveSide(anchorRect, size, bounds, _gap, _prefer, _align, _lockedSide);
            _lockedSide = placement.Side;
            return PopupLayout.Free(placement.Panel, placement.Side, _align);
        }

        private static Rect PointInCanvas(Canvas root, Vector2 screenPosition)
        {
            var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.transform, screenPosition,
                cam, out var local);
            return new Rect(local, Vector2.zero);
        }

        private Rect AnchorRectInCanvas(Canvas root)
        {
            // The anchor may have been destroyed since the last frame (a fitter-driven reposition).
            var art = _anchor != null && _anchor.GameObject != null ? _anchor.RectTransform : null;
            return art == null ? default : RectInCanvas(art, root);
        }

        private static Rect RectInCanvas(RectTransform rt, Canvas root)
        {
            rt.GetWorldCorners(Corners);
            var canvasRt = (RectTransform)root.transform;
            var min = canvasRt.InverseTransformPoint(Corners[0]);
            var max = canvasRt.InverseTransformPoint(Corners[2]);
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        // How many root-canvas units one unit of the panel's own space is.
        private static float ScaleToCanvas(RectTransform rt, Canvas root)
        {
            var rootScale = root.transform.lossyScale.x;
            return rootScale > 0f ? rt.lossyScale.x / rootScale : 1f;
        }

        /// <summary>
        /// One axis of the panel's size, from its stack's content: <c>hug</c> (and an omitted axis) is the
        /// content, a number is itself, either clamped by <c>clamp(min, …, max)</c>. True when it changed.
        /// </summary>
        internal bool FitAxis(int axis)
        {
            if (_layout == null) return false;
            var content = axis == 0 ? _layout.preferredWidth : _layout.preferredHeight;
            float size;
            if (axis == 0 ? !_size.HasWidth : !_size.HasHeight)
            {
                size = content;
            }
            else
            {
                var hug = axis == 0 ? _size.IsHugWidth : _size.IsHugHeight;
                var value = hug ? content : (axis == 0 ? _size.Width : _size.Height);
                var min = axis == 0 ? _size.MinWidth : _size.MinHeight;
                var max = axis == 0 ? _size.MaxWidth : _size.MaxHeight;
                size = Mathf.Clamp(value, min, max);
            }
            size = Mathf.Max(0f, size);

            var rt = RectTransform;
            var delta = rt.sizeDelta;
            if (Mathf.Abs(delta[axis] - size) < 0.001f) return false;
            delta[axis] = size;
            rt.sizeDelta = delta;
            return true;
        }

        /// <summary>After a size change while open, re-clamp on the side chosen at open.</summary>
        internal void AfterFit(bool changed)
        {
            if (!changed || _opening || !IsOpen) return;
            _core.Reposition();
        }

        // ── Teardown ───────────────────────────────────────────────────────────────────────

        public override void Dispose()
        {
            _triggerSub?.Dispose();
            _triggerSub = null;
            if (_core != null)
            {
                // The core leaves the chain silently; whoever listens still hears it went (spec §4.11).
                if (_core.IsOpen)
                {
                    _closed.OnNext(PopoverCloseReason.HostGone);
                    _collapsed.OnNext(Unit.Default);
                }
                _core.Closed -= OnCoreClosed;
                _core.Dispose();
                _core = null;
            }
            _opened.Dispose();
            _closed.Dispose();
            _expanded.Dispose();
            _collapsed.Dispose();
            base.Dispose();
        }
    }
}
