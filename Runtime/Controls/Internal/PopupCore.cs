using System;
using LitMotion;
using LitMotion.Extensions;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>What a host tells <see cref="PopupCore"/> about its panel each time it is placed.</summary>
    internal readonly struct PopupLayout
    {
        public readonly Vector2 Size;
        public readonly float Gap;
        public readonly bool PreferAbove;

        /// <summary>Where the panel may go, in the root canvas's local space; null = the whole canvas.</summary>
        public readonly Rect? Bounds;

        public PopupLayout(Vector2 size, float gap, bool preferAbove = false, Rect? bounds = null)
        {
            Size = size;
            Gap = gap;
            PreferAbove = preferAbove;
            Bounds = bounds;
        }
    }

    /// <summary>
    /// The popup machinery that used to be private to <see cref="TabMenu"/> (spec 2026-10-18 §4.1),
    /// shared by every control that drops a panel over the page: lifting the panel above its host's
    /// root canvas, the optional full-screen catcher, the Escape listener, the directional-focus cage,
    /// placement, the open / close transition and what is put back on close.
    ///
    /// <para>Not a control: the host builds the panel and its content, owns the panel's surface, and
    /// decides when to open and close — the same split <see cref="TabGroupCore"/> has with
    /// <see cref="TabBar"/> / <see cref="TabMenu"/>. The panel stays in the host's subtree (TM-D8):
    /// an overriding nested Canvas lifts it and frees it from ancestor masks.</para>
    /// </summary>
    internal sealed class PopupCore : IPopupEntry, IDisposable
    {
        /// <summary>Name of the full-screen click catcher, on the root canvas while a popup is open.</summary>
        internal const string BlockerName = "__PopupBlocker";

        private const float SlideDistance = 8f;

        private readonly Control _host;
        private readonly RectTransform _panel;
        private readonly PopupDismiss _dismiss;
        private readonly bool _cageFocus;
        private readonly bool _cancelButtonCloses;
        private readonly Canvas _canvas;
        private readonly CanvasGroup _group;

        private GameObject _blocker;
        private ModalEscapeListener _escapeListener;
        private MotionHandle _fadeMotion;
        private MotionHandle _slideMotion;

        // What the cage replaced, put back on close (spec §4.3): the global cage and this Screen's own,
        // separately — a Screen that was never confined must not inherit the global one.
        private bool _caged;
        private GameObject _prevContainmentRoot;
        private GameObject _prevScreenConfine;

        public PopupCore(Control host, RectTransform panel, PopupDismiss dismiss, bool cageFocus, bool cancelButtonCloses)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _panel = panel ? panel : throw new ArgumentNullException(nameof(panel));
            _dismiss = dismiss;
            _cageFocus = cageFocus;
            _cancelButtonCloses = cancelButtonCloses;

            // Explicit null checks, not `??`: in the Editor a missing component comes back as a fake null
            // object that `??` takes for a real one.
            var go = panel.gameObject;
            _group = go.GetComponent<CanvasGroup>();
            if (_group == null) _group = go.AddComponent<CanvasGroup>();

            // Added now, not overriding: a Canvas + Raycaster appearing on open would force a rebuild in
            // the frame the popup opens. overrideSorting is what lifts the panel above the page AND what
            // breaks it out of an ancestor mask (MaskUtilities stops at an overriding Canvas).
            _canvas = go.GetComponent<Canvas>();
            if (_canvas == null) _canvas = go.AddComponent<Canvas>();
            _canvas.overrideSorting = false;
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
        }

        public bool IsOpen { get; private set; }

        /// <summary>Open / close duration in seconds; 0 snaps.</summary>
        public float TransitionSeconds { get; set; }

        /// <summary>The panel's size and placement inputs, asked for on every placement.</summary>
        public Func<PopupLayout> Measure { get; set; }

        /// <summary>Where the caged focus lands on open; null falls back to the first focusable in the panel.</summary>
        public Func<GameObject> FocusPick { get; set; }

        /// <summary>Fires when opening starts — the panel is active, sorted and placed.</summary>
        public event Action Opened;

        /// <summary>Fires whenever the popup closes, whoever closed it.</summary>
        public event Action<PopupCloseReason> Closed;

        public GameObject HostGameObject => _host.GameObject;

        public RectTransform Panel => _panel;

        public int HostRootOrder
        {
            get
            {
                var root = RootCanvas();
                return root != null ? root.sortingOrder : 0;
            }
        }

        // ── Open / close ───────────────────────────────────────────────────────────────────

        public void Open()
        {
            if (IsOpen || _panel == null) return;
            IsOpen = true;
            PopupChain.Push(this);

            _panel.gameObject.SetActive(true);
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = PopupChain.PanelOrder(this);
            if (_dismiss == PopupDismiss.Blocker) EnsureBlocker(RootCanvas(), PopupChain.BlockerOrder(this));

            Reposition();
            PushNavigation();
            EnableEscapeListener(true);
            PlayTransition(opening: true);

            Opened?.Invoke();
        }

        public void Close(PopupCloseReason reason = PopupCloseReason.Owner)
        {
            if (!IsOpen) return;
            if (PopupChain.Contains(this)) PopupChain.Close(this, reason);
            else CloseCore(reason);
        }

        void IPopupEntry.CloseFromChain(PopupCloseReason reason) => CloseCore(reason);

        private void CloseCore(PopupCloseReason reason)
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (_blocker != null) _blocker.SetActive(false);
            EnableEscapeListener(false);
            PopNavigation(reason);
            PlayTransition(opening: false);

            Closed?.Invoke(reason);
        }

        /// <summary>
        /// Re-reads the host's layout and hangs the panel off the host again, flipping or clamping it
        /// back inside the bounds (<see cref="PopupPlacer"/>). A no-op while closed.
        /// </summary>
        public void Reposition()
        {
            if (_panel == null || !IsOpen) return;

            var layout = Measure != null ? Measure() : new PopupLayout(_panel.rect.size, 0f);
            _panel.sizeDelta = layout.Size;

            var root = RootCanvas();
            var placement = root != null
                ? PopupPlacer.Solve(RectInCanvas(_host.RectTransform, root), layout.Size,
                                    layout.Bounds ?? ((RectTransform)root.transform).rect,
                                    layout.Gap, layout.PreferAbove)
                // No canvas to measure against (a detached test rig): keep the default drop-down.
                : new PopupPlacement(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, -layout.Gap), false);

            _panel.anchorMin = placement.Anchor;
            _panel.anchorMax = placement.Anchor;
            _panel.pivot = placement.Pivot;
            _panel.anchoredPosition = placement.AnchoredPosition;
        }

        /// <summary>
        /// The host's end-of-apply step (after an attribute pass, a Variant / theme ReSolve): a host
        /// that is now hidden or disabled must not leave an orphaned panel floating over the page, so
        /// it closes as if the host had asked; otherwise the panel is re-placed. A closed panel is
        /// deactivated — after <c>Screen.Open</c>'s measuring pass when one is running (TM-D12).
        /// </summary>
        public void OnHostAfterApply()
        {
            if (IsOpen && (!_host.PeekInteractable || !_host.GameObject.activeInHierarchy))
                Close(PopupCloseReason.Owner);
            else
                Reposition();

            if (IsOpen) return;
            var owner = UI.OwnerScreenOf(_host);
            if (owner != null) owner.DeferDuringOpen(HideIfClosed);
            else HideIfClosed();
        }

        private void HideIfClosed()
        {
            if (!IsOpen && _panel != null) _panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rows built under a closed — inactive — panel never measure themselves: a TMP added by
        /// AddComponent on an inactive GameObject skips Awake (<see cref="InactiveMeasure"/>). Wrap a
        /// rebuild in this: the panel is switched on for the scope and off again straight after, or,
        /// when it is open, re-placed around its new content.
        /// </summary>
        public IDisposable BeginRebuild()
        {
            var activated = _panel != null && InactiveMeasure.ActivateIfNeeded(_panel.gameObject);
            return new RebuildScope(this, activated);
        }

        private sealed class RebuildScope : IDisposable
        {
            private PopupCore _core;
            private readonly bool _activated;

            public RebuildScope(PopupCore core, bool activated)
            {
                _core = core;
                _activated = activated;
            }

            public void Dispose()
            {
                var core = _core;
                if (core == null) return;
                _core = null;
                if (_activated) InactiveMeasure.Restore(core._panel.gameObject, true);
                else core.Reposition();
            }
        }

        // ── Transition ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fades and slides the panel in or out, then — on the way out — deactivates it once the
        /// motion lands. Outside play mode the end state is written directly: LitMotion ticks on the
        /// player loop, so an EditMode caller would otherwise be left with a panel stuck at alpha 0.
        /// </summary>
        private void PlayTransition(bool opening)
        {
            CancelMotions();

            var slideFrom = SlideOffset();
            var rest = _panel.anchoredPosition;

            if (TransitionSeconds <= 0f || !UnityEngine.Application.isPlaying)
            {
                _group.alpha = opening ? 1f : 0f;
                _panel.anchoredPosition = rest;
                if (!opening) DeactivatePanel();
                return;
            }

            var fromAlpha = opening ? 0f : _group.alpha;
            var toAlpha = opening ? 1f : 0f;
            _group.alpha = fromAlpha;
            _fadeMotion = LMotion.Create(fromAlpha, toAlpha, TransitionSeconds)
                .WithEase(Ease.OutCubic)
                .Bind(_group, static (v, cg) => { if (cg) cg.alpha = v; })
                .AddTo(_panel.gameObject);

            var from = opening ? rest + slideFrom : rest;
            var to = opening ? rest : rest + slideFrom;
            _panel.anchoredPosition = from;
            _slideMotion = LMotion.Create(from, to, TransitionSeconds)
                .WithEase(Ease.OutCubic)
                .Bind(_panel, static (v, rt) => { if (rt) rt.anchoredPosition = v; })
                .AddTo(_panel.gameObject);

            if (!opening)
            {
                // Only the close owns the deactivation, and only if it actually finishes: a re-open
                // cancels this handle first, so a cancelled close cannot switch off a reopened panel.
                var captured = _fadeMotion;
                _fadeMotion.GetAwaiter().OnCompleted(() =>
                {
                    if (!IsOpen && captured.Equals(_fadeMotion)) DeactivatePanel();
                });
            }
        }

        // Enters from the side it is anchored to, so the panel appears to unfold out of the host.
        private Vector2 SlideOffset() =>
            _panel.pivot.y > 0.5f ? new Vector2(0f, SlideDistance) : new Vector2(0f, -SlideDistance);

        private void CancelMotions()
        {
            if (_fadeMotion.IsActive()) _fadeMotion.TryCancel();
            if (_slideMotion.IsActive()) _slideMotion.TryCancel();
        }

        private void DeactivatePanel()
        {
            if (_panel == null) return;
            _canvas.overrideSorting = false;
            _panel.gameObject.SetActive(false);
        }

        // ── Directional navigation (spec §4.3) ─────────────────────────────────────────────

        /// <summary>Cages the focus inside the open panel and lands it on <see cref="FocusPick"/>.</summary>
        private void PushNavigation()
        {
            if (!_cageFocus || !UI.Navigation.IsEnabled) return;
            _caged = true;

            // ContainmentRoot is the authority — EnforceContainment reads it every frame, and a caller
            // can set it without going through a Screen (as UI.Modal does).
            _prevContainmentRoot = UI.Navigation.ContainmentRoot;
            var screen = UI.OwnerScreenOf(_host);
            _prevScreenConfine = screen?.NavConfineRoot;
            screen?.ConfineNavigationTo(_panel.gameObject);
            UI.Navigation.ContainmentRoot = _panel.gameObject;

            var es = FindEventSystem();
            if (es == null) return;
            var pick = FocusPick?.Invoke();
            if (pick == null) pick = UI.Navigation.FirstFocusableUnder(_panel.gameObject);
            if (pick != null) es.SetSelectedGameObject(pick);
        }

        /// <summary>
        /// Puts back only what is still this popup's (spec §4.3): a cage another layer has replaced
        /// meanwhile stays, and a focus that has already moved outside the panel is not stolen back.
        /// </summary>
        private void PopNavigation(PopupCloseReason reason)
        {
            if (!_caged) return;
            _caged = false;

            var panelGo = _panel != null ? _panel.gameObject : null;
            var screen = UI.OwnerScreenOf(_host);
            if (screen != null && panelGo != null && screen.NavConfineRoot == panelGo)
                screen.ConfineNavigationTo(_prevScreenConfine);
            if (panelGo != null && UI.Navigation.ContainmentRoot == panelGo)
                UI.Navigation.ContainmentRoot = _prevContainmentRoot;
            _prevContainmentRoot = null;
            _prevScreenConfine = null;

            var es = FindEventSystem();
            if (es == null) return;
            var selected = es.currentSelectedGameObject;
            if (selected != null && (panelGo == null || !selected.transform.IsChildOf(panelGo.transform))) return;
            if (reason == PopupCloseReason.HostGone)
            {
                if (selected != null) es.SetSelectedGameObject(null);
                return;
            }
            if (_host.GameObject != null) es.SetSelectedGameObject(_host.GameObject);
        }

        // EventSystem.current is null in EditMode; mirror Screen.FindEventSystem's fallback.
        private static UnityEngine.EventSystems.EventSystem FindEventSystem()
            => UnityEngine.EventSystems.EventSystem.current
               ?? UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();

        // ── Escape ─────────────────────────────────────────────────────────────────────────

        private void EnableEscapeListener(bool on)
        {
            if (!on)
            {
                if (_escapeListener != null) _escapeListener.enabled = false;
                return;
            }
            if (_escapeListener == null)
            {
                _escapeListener = _panel.gameObject.AddComponent<ModalEscapeListener>();
                // Unlike a modal, a popup may be dismissed with the gamepad's Cancel button too.
                _escapeListener.AlsoCancelButton = _cancelButtonCloses;
                _escapeListener.OnEscape = () =>
                {
                    PopupChain.NoteEscapeConsumed();
                    Close(PopupCloseReason.Escape);
                };
            }
            _escapeListener.enabled = true;
        }

        // ── Catcher ────────────────────────────────────────────────────────────────────────

        private Canvas RootCanvas()
        {
            var go = _host.GameObject;
            if (go == null) return null;
            var canvas = go.GetComponentInParent<Canvas>(true);
            return canvas != null ? canvas.rootCanvas : null;
        }

        // The catcher hangs off the ROOT canvas, not the host: it has to cover the whole screen, and a
        // child of the host could never do that without inheriting its clipping and position.
        private void EnsureBlocker(Canvas rootCanvas, int sortingOrder)
        {
            if (_blocker == null)
            {
                var host = rootCanvas != null ? rootCanvas.transform : _host.GameObject.transform.root;
                _blocker = new GameObject(BlockerName, typeof(RectTransform)) { layer = _host.GameObject.layer };
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
                btn.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                btn.onClick.AddListener(() => Close(PopupCloseReason.OutsidePress));
            }
            _blocker.transform.SetAsLastSibling();
            _blocker.GetComponent<Canvas>().sortingOrder = sortingOrder;
            _blocker.SetActive(true);
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

        /// <summary>
        /// Leaves the chain without closing anything else, and destroys the catcher — it is parented to
        /// the root canvas, outside the host's subtree, so the teardown that destroys everything else
        /// would leave it behind.
        /// </summary>
        public void Dispose()
        {
            PopupChain.Remove(this);
            IsOpen = false;
            if (_blocker != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_blocker);
                else UnityEngine.Object.DestroyImmediate(_blocker);
                _blocker = null;
            }
            CancelMotions();
            if (_caged && _panel != null && UI.Navigation.ContainmentRoot == _panel.gameObject)
                UI.Navigation.ContainmentRoot = _prevContainmentRoot;
            _caged = false;
        }
    }
}
