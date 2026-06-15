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
            }
            base.OnAfterApply();  // Trigger handles initial Fire / subscriptions
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
