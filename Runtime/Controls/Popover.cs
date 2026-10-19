using System;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Layout;
using PromptUGUI.Lint;
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

        // Open state: what it hangs from, and the side chosen when it opened (kept while open).
        private IControl _anchor;
        private PopupSide? _lockedSide;
        private bool _opening;

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

        /// <summary>Opens beside <paramref name="anchor"/> — any control of the same Screen.</summary>
        public void Open(IControl anchor)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (IsOpen && ReferenceEquals(anchor, _anchor)) return;
            if (IsOpen) Close();
            if (!CanOpenOn(anchor)) return;
            OpenCore(anchor);
        }

        public void Close()
        {
            if (!IsOpen) return;
            _core.Close(PopupCloseReason.Owner);
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
            _core.OnHostAfterApply();
        }

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

        private bool CanOpenOn(IControl anchor)
        {
            if (_core == null || GameObject == null) return false;
            if (!PeekInteractable) return false;
            var parent = RectTransform.parent;
            if (parent == null || !parent.gameObject.activeInHierarchy) return false;
            var go = anchor.GameObject;
            if (go == null || !go.activeInHierarchy) return false;
            return true;
        }

        private void OpenCore(IControl anchor)
        {
            _anchor = anchor;
            _lockedSide = null;
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
            _opened.OnNext(Unit.Default);
            _expanded.OnNext(Unit.Default);
        }

        private void OnCoreClosed(PopupCloseReason reason)
        {
            _lockedSide = null;
            _closed.OnNext(Map(reason));
            _collapsed.OnNext(Unit.Default);
        }

        private static PopoverCloseReason Map(PopupCloseReason reason)
        {
            switch (reason)
            {
                case PopupCloseReason.Escape: return PopoverCloseReason.Escape;
                case PopupCloseReason.OutsidePress: return PopoverCloseReason.OutsidePress;
                case PopupCloseReason.Covered: return PopoverCloseReason.Covered;
                case PopupCloseReason.HostGone: return PopoverCloseReason.HostGone;
                default: return PopoverCloseReason.Code;
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
            var anchorRect = AnchorRectInCanvas(root);
            var size = rt.rect.size * ScaleToCanvas(rt, root);
            var placement = PopupPlacer.SolveSide(anchorRect, size, bounds, _gap, _prefer, _align, _lockedSide);
            _lockedSide = placement.Side;
            return PopupLayout.Free(placement.Panel, placement.Side, _align);
        }

        private Rect AnchorRectInCanvas(Canvas root)
        {
            var art = _anchor?.RectTransform;
            if (art == null) return default;
            art.GetWorldCorners(Corners);
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
            if (_core != null)
            {
                _core.Closed -= OnCoreClosed;
                _core.Dispose();
            }
            _opened.Dispose();
            _closed.Dispose();
            _expanded.Dispose();
            _collapsed.Dispose();
            base.Dispose();
        }
    }
}
