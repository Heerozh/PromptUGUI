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
        // Text-effect / target attrs added in Tasks 11-12.

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

        private TMP_Text ResolveTextTarget() => null;  // Tasks 11-12 implement

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
