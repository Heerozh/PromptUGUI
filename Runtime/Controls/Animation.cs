using LitMotion;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public sealed class Animation : Trigger
    {
        private RectTransform _offsetProxy;
        private CanvasGroup _cg;
        private MotionHandle[] _current;
        private readonly AnimationSpec _animSpec = new AnimationSpec();
        private AnimationSpec.AnimationSnapshot _lastApplied;

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
                    _revealAtLargerEnd = IsLarger(_animSpec.RevealFrom, _animSpec.RevealTo);
                    _revealInitialized = true;
                }
                ApplyRevealBox(_revealBox);
                RevealDriver.SetClip(RevealHost.gameObject, !_revealAtLargerEnd);
            }

            base.OnAfterApply();  // Trigger handles initial Fire / subscriptions
        }

        // ── reveal (FND §2.4) ────────────────────────────────────────────────────────────

        private float _revealBox;
        private bool _revealInitialized;
        private bool _revealAtLargerEnd;

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

        /// <summary>
        /// Which endpoint is the open one. <c>hug</c> counts as the larger side without measuring:
        /// the clip only has to be right, and content bigger than a partial reveal is what the
        /// channel is for. Measuring here would cost a layout rebuild on every ReSolve.
        /// </summary>
        private static bool IsLarger(Internal.RevealValue a, Internal.RevealValue b)
            => a.IsHug ? !b.IsHug : (!b.IsHug && a.Px > b.Px);

        /// <summary>Current reveal box — read by the driver so a fire starts from where we are.</summary>
        internal float RevealBox => _revealBox;

        internal void SetRevealBox(float value)
        {
            _revealBox = value;
            ApplyRevealBox(value);
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

        protected override void OnTriggerFired()
        {
            CancelCurrent();
            _current = AnimationDriver.Play(_animSpec, _offsetProxy, _cg, ResolveTextTarget());
        }

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
            base.Dispose();
        }
    }
}
