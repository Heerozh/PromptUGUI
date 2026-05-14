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
        private readonly AnimationSpec _spec = new AnimationSpec();
        private AnimationSpec.AnimationSnapshot _lastApplied;

        protected internal override Transform ChildHostTransform => _offsetProxy;

        [UIAttr("type")] public string TypeAttr { set => _spec.SetType(value); }
        [UIAttr("translate")] public string TranslateAttr { set => _spec.SetTranslate(value); }
        [UIAttr("scale")] public string ScaleAttr { set => _spec.SetScale(value); }
        [UIAttr("rotate")] public string RotateAttr { set => _spec.SetRotate(value); }
        [UIAttr("fade")] public string FadeAttr { set => _spec.SetFade(value); }
        [UIAttr("duration")] public string DurationAttr { set => _spec.SetDuration(value); }
        [UIAttr("delay")] public string DelayAttr { set => _spec.SetDelay(value); }
        [UIAttr("easing")] public string EasingAttr { set => _spec.SetEasing(value); }
        [UIAttr("loop")] public string LoopAttr { set => _spec.SetLoop(value); }
        [UIAttr("count")] public string CountAttr { set => _spec.SetCount(value); }
        [UIAttr("format")] public string FormatAttr { set => _spec.SetFormat(value); }
        [UIAttr("target")] public string TargetAttr { set => _spec.SetTarget(value); }
        [UIAttr("char-color")] public string CharColorAttr { set => _spec.SetCharColor(value); }
        [UIAttr("char-stagger")] public string CharStaggerAttr { set => _spec.SetCharStagger(value); }

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
                && _spec.LoopMode == PromptUGUI.Controls.Internal.LoopMode.None)
            {
                _spec.LoopMode = PromptUGUI.Controls.Internal.LoopMode.Yoyo;
            }
            // Retrieve the CanvasGroup that ApplyCommon already added via Control.Interactable.
            _cg = GameObject.GetComponent<CanvasGroup>();
            if (_cg == null) _cg = GameObject.AddComponent<CanvasGroup>();
            _spec.Validate();
            var snap = _spec.Snapshot();
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
            _current = AnimationDriver.Play(_spec, _offsetProxy, _cg, ResolveTextTarget());
        }

        private TMP_Text ResolveTextTarget()
        {
            if (_spec.Family != Internal.AnimationFamily.Text) return null;
            if (!string.IsNullOrEmpty(_spec.TargetId))
            {
                var screen = UI.OwnerScreenOf(this)
                    ?? throw new System.InvalidOperationException(
                        $"<Animation target=\"@{_spec.TargetId}\">: owner Screen not found");
                // Use screen.Get<Text> first (works after Screen._byId is populated).
                // If called during instantiation (on="open" fires inside InstantiateInto
                // before _byId is populated), fall back to transform-tree lookup by name —
                // GameObject names match element ids (ScreenInstantiator assigns go.name = node.Id).
                try
                {
                    return screen.Get<Text>(_spec.TargetId).TmpComponent;
                }
                catch (System.Collections.Generic.KeyNotFoundException)
                {
                    // Fallback: during on="open" instantiation, _byId not yet populated.
                    // Find by GameObject name (ids are assigned as go.name by ScreenInstantiator).
                    return FindTmpInTree(screen.RootGameObject.transform, _spec.TargetId)
                        ?? throw new System.InvalidOperationException(
                            $"<Animation target=\"@{_spec.TargetId}\">: id '{_spec.TargetId}' not found in screen");
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
