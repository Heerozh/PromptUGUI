using LitMotion;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public sealed class Animation : Trigger, Internal.IRevealTarget
    {
        private RectTransform _offsetProxy;
        private CanvasGroup _cg;
        private MotionHandle[] _current;
        private readonly AnimationSpec _animSpec = new AnimationSpec();
        private AnimationSpec.AnimationSnapshot _lastApplied;
        private System.IDisposable _reverseSub;
        private readonly R3.Subject<R3.Unit> _reverse = new();

        /// <summary>Fires every time this animation is played backwards (<c>reverse-on=</c> or <see cref="Reverse"/>).</summary>
        public R3.Observable<R3.Unit> OnReverse => _reverse;

        protected internal override Transform ChildHostTransform => _offsetProxy;

        [UIAttr("type"), Preserve] public string TypeAttr { set => _animSpec.SetType(value); }
        [UIAttr("translate"), Preserve] public string TranslateAttr { set => _animSpec.SetTranslate(value); }
        [UIAttr("scale"), Preserve] public string ScaleAttr { set => _animSpec.SetScale(value); }
        [UIAttr("rotate"), Preserve] public string RotateAttr { set => _animSpec.SetRotate(value); }
        [UIAttr("fade"), Preserve] public string FadeAttr { set => _animSpec.SetFade(value); }
        [UIAttr("duration"), Preserve] public string DurationAttr { set => _animSpec.SetDuration(value); }
        [UIAttr("delay"), Preserve] public string DelayAttr { set => _animSpec.SetDelay(value); }
        [UIAttr("easing"), Preserve] public string EasingAttr { set => _animSpec.SetEasing(value); }
        [UIAttr("loop"), Preserve] public string LoopAttr { set => _animSpec.SetLoop(value); }
        [UIAttr("count"), Preserve] public string CountAttr { set => _animSpec.SetCount(value); }
        [UIAttr("format"), Preserve] public string FormatAttr { set => _animSpec.SetFormat(value); }
        [UIAttr("target"), Preserve] public string TargetAttr { set => _animSpec.SetTarget(value); }
        [UIAttr("char-color"), Preserve] public string CharColorAttr { set => _animSpec.SetCharColor(value); }
        [UIAttr("char-stagger"), Preserve] public string CharStaggerAttr { set => _animSpec.SetCharStagger(value); }

        /// <summary>Family D: <c>y</c> = grow/shrink the height, <c>x</c> = the width (FND §2.3).</summary>
        [UIAttr("reveal"), Preserve] public string RevealAttr { set => _animSpec.SetReveal(value); }
        [UIAttr("reveal-from"), Preserve] public string RevealFromAttr { set => _animSpec.SetRevealFrom(value); }
        [UIAttr("reveal-to"), Preserve] public string RevealToAttr { set => _animSpec.SetRevealTo(value); }

        /// <summary>The event that plays this animation backwards, from wherever it is (FND §2.4.5).</summary>
        [UIAttr("reverse-on"), Preserve] public string ReverseOnAttr { set => _animSpec.SetReverseOn(value); }

        public override void OnAttached()
        {
            var go = new GameObject("_offsetProxy", typeof(RectTransform));
            go.transform.SetParent(RectTransform, worldPositionStays: false);
            _offsetProxy = (RectTransform)go.transform;
            _offsetProxy.anchorMin = Vector2.zero;
            _offsetProxy.anchorMax = Vector2.one;
            _offsetProxy.offsetMin = Vector2.zero;
            _offsetProxy.offsetMax = Vector2.zero;
            _offsetProxy.pivot = new Vector2(0.5f, 0.5f);
            // Note: CanvasGroup is added by the base Control.Interactable setter during ApplyCommon.
            // We retrieve it in OnAfterApply (after ApplyCommon has run) to avoid duplicate-component error.
        }

        internal override void OnAfterApply()
        {
            // on="loop" implies yoyo unless user explicitly set loop=
            if (TriggerKind == PromptUGUI.Controls.Internal.TriggerKind.Loop
                && _animSpec.LoopMode == PromptUGUI.Controls.Internal.LoopMode.None)
            {
                _animSpec.LoopMode = PromptUGUI.Controls.Internal.LoopMode.Yoyo;
            }
            // Retrieve the CanvasGroup that ApplyCommon already added via Control.Interactable.
            _cg = GameObject.GetComponent<CanvasGroup>();
            if (_cg == null) _cg = GameObject.AddComponent<CanvasGroup>();
            _animSpec.Validate();
            var snap = _animSpec.Snapshot();
            if (!snap.Equals(_lastApplied))
            {
                CancelCurrent();
                _lastApplied = snap;
                _revealInitialized = false;   // new endpoints → re-establish the resting box
            }

            // Reveal's resting state is reveal-from, NOT identity: "hidden until something opens it"
            // is what the channel means, and an on="expand@..." subtree has no business being
            // visible before the expand (FND §2.4.4). Established BEFORE base.OnAfterApply, because
            // that is where an on="open" trigger fires — it must start from the resting box.
            //
            // Re-asserted on every pass: ApplyCommon has just reset the geometry this owns, so
            // without this a Variant flip or a resize would snap a half-open panel shut.
            if (_animSpec.HasReveal)
            {
                if (!_revealInitialized)
                {
                    _revealBox = ResolveReveal(_animSpec.RevealFrom);
                    _revealShowsEverything = _animSpec.RevealFrom.IsHug;
                    _revealInitialized = true;
                }
                ApplyRevealBox(_revealBox);
                RevealDriver.SetClip(RevealHost.gameObject, !_revealShowsEverything);
            }

            base.OnAfterApply();  // Trigger handles initial Fire / subscriptions
        }

        // ── reveal (FND §2.4) ────────────────────────────────────────────────────────────

        private float _revealBox;
        private bool _revealInitialized;

        /// <summary>
        /// Whether the box currently shows the whole content — true only at a <c>hug</c> endpoint,
        /// which is the one value that means "exactly as big as what is inside". A numeric endpoint
        /// may or may not cover the content, so the clip stays on for those: being wrong there would
        /// spill the overflow across the siblings.
        /// </summary>
        private bool _revealShowsEverything;

        /// <summary>The node whose size the reveal owns — the layout wrapper when there is one.</summary>
        private RectTransform RevealHost => LayoutHost;

        /// <summary>The single authored child (PUI-REVEAL-SINGLE-CHILD guarantees there is one).</summary>
        private RectTransform RevealChild =>
            _offsetProxy != null && _offsetProxy.childCount > 0
                ? (RectTransform)_offsetProxy.GetChild(0)
                : null;

        private bool RevealInLayoutGroup =>
            RevealHost.parent != null
            && RevealHost.parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>() != null;

        internal float ResolveReveal(Internal.RevealValue v)
            => v.IsHug ? RevealDriver.Measure(RevealChild, _animSpec.RevealAxis) : v.Px;

        private void ApplyRevealBox(float value)
            => RevealDriver.ApplyBox(RevealHost, _animSpec.RevealAxis, value, RevealInLayoutGroup);

        // ── IRevealTarget ────────────────────────────────────────────────────────────────

        /// <summary>Current reveal box — read by the driver so a fire starts from where we are.</summary>
        float Internal.IRevealTarget.RevealBox => _revealBox;

        void Internal.IRevealTarget.SetRevealBox(float value)
        {
            _revealBox = value;
            ApplyRevealBox(value);
        }

        float Internal.IRevealTarget.ResolveReveal(Internal.RevealValue value) => ResolveReveal(value);

        void Internal.IRevealTarget.SetRevealClip(bool on)
            => RevealDriver.SetClip(RevealHost.gameObject, on);

        void Internal.IRevealTarget.OnRevealSettled(bool reversed)
        {
            // Landed on an endpoint: remember whether it shows everything, so the next ReSolve
            // re-asserts the same clip state, and drop the mask when nothing is hidden any more
            // (a live RectMask2D breaks batching for the whole subtree).
            var landed = reversed ? _animSpec.RevealFrom : _animSpec.RevealTo;
            _revealShowsEverything = landed.IsHug;
            RevealDriver.SetClip(RevealHost.gameObject, !_revealShowsEverything);
        }

        public override Vector2? GetNativeSize()
        {
            var native = base.GetNativeSize();
            if (!_animSpec.HasReveal || !_revealInitialized || !native.HasValue) return native;
            // Report the animating box, not the child's full size: a parent group must reserve what
            // is actually shown right now.
            var size = native.Value;
            size[_animSpec.RevealAxis] = _revealBox;
            return size;
        }

        protected override void InitTriggerSubscription()
        {
            base.InitTriggerSubscription();
            if (_animSpec.ReverseOn != null)
                _reverseSub = SubscribeSpec(_animSpec.ReverseOn, Reverse, () => SnapTo(reverse: true));
        }

        /// <summary>
        /// A <c>checked</c> / <c>unchecked</c> trigger whose control is ALREADY in that state at open
        /// establishes the end state instead of animating into it (FND-D10) — no chevron spinning on
        /// frame 1, no panel sliding open behind the loading screen.
        /// </summary>
        protected override void OnTriggerFiredInitial() => SnapTo(reverse: false);

        private void SnapTo(bool reverse)
        {
            // The text family writes a string, so there is no "end state" to establish cheaply —
            // let it run normally.
            if (_animSpec.Family == Internal.AnimationFamily.Text)
            {
                if (reverse) Reverse();
                else Fire();
                return;
            }

            CancelCurrent();
            AnimationDriver.WriteEndState(_animSpec, Context(), reverse);
            if (reverse) _reverse.OnNext(R3.Unit.Default);
            else RaiseFireOnly();
        }

        protected override void OnTriggerFired()
        {
            CancelCurrent();
            _current = AnimationDriver.Play(_animSpec, Context(), reverse: false);
        }

        /// <summary>
        /// Plays this animation backwards from wherever it currently is. Called by
        /// <c>reverse-on=</c>; also the C# entry point for <c>reverse-on="manual"</c>.
        /// </summary>
        public void Reverse()
        {
            CancelCurrent();
            _current = AnimationDriver.Play(_animSpec, Context(), reverse: true);
            _reverse.OnNext(R3.Unit.Default);
        }

        private Internal.AnimationContext Context() => new Internal.AnimationContext
        {
            Proxy = _offsetProxy,
            Cg = _cg,
            Text = ResolveTextTarget(),
            Reveal = this,
        };

        private TMP_Text ResolveTextTarget()
        {
            if (_animSpec.Family != Internal.AnimationFamily.Text) return null;
            if (!string.IsNullOrEmpty(_animSpec.TargetId))
            {
                var screen = UI.OwnerScreenOf(this)
                    ?? throw new System.InvalidOperationException(
                        $"<Animation target=\"@{_animSpec.TargetId}\">: owner Screen not found");
                // Use screen.Get<Text> first (works after Screen._byId is populated).
                // If called during instantiation (on="open" fires inside InstantiateInto
                // before _byId is populated), fall back to transform-tree lookup by name —
                // GameObject names match element ids (ScreenInstantiator assigns go.name = node.Id).
                try
                {
                    return screen.Get<Text>(_animSpec.TargetId).TmpComponent;
                }
                catch (System.Collections.Generic.KeyNotFoundException)
                {
                    // Fallback: during on="open" instantiation, _byId not yet populated.
                    // Find by GameObject name (ids are assigned as go.name by ScreenInstantiator).
                    return FindTmpInTree(screen.RootGameObject.transform, _animSpec.TargetId)
                        ?? throw new System.InvalidOperationException(
                            $"<Animation target=\"@{_animSpec.TargetId}\">: id '{_animSpec.TargetId}' not found in screen");
                }
            }
            return Internal.AnimationTargetResolver.FindTextInSubtree(this);
        }

        private static TMP_Text FindTmpInTree(Transform root, string name)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name)
                {
                    var tmp = child.GetComponent<TMP_Text>();
                    if (tmp != null) return tmp;
                }
                var found = FindTmpInTree(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void CancelCurrent()
        {
            if (_current == null) return;
            foreach (var h in _current) if (h.IsActive()) h.TryCancel();
            _current = null;
        }

        public override void Dispose()
        {
            CancelCurrent();
            _reverseSub?.Dispose();
            _reverse.Dispose();
            base.Dispose();
        }
    }
}
