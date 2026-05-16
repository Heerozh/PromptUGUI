using System;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public class Trigger : Control
    {
        private readonly Subject<Unit> _fire = new();
        public Observable<Unit> OnFire => _fire;

        // Trigger / Animation 是装饰器型 wrapper，自身无视觉。当作者没在 wrapper 上写 size 时，
        // 把唯一直接子节点的 resolved size 当作自身 native size 暴露出来——这样父
        // V/HStack 看到的是「内容尺寸」而不是 0×0 槽位 (loading-dots 类布局)。Web 端 CSS
        // 默认 box model 就是 fit-content，这里复刻同一约定。歧义场景 (0/多子节点、子节点
        // 自己也未定尺寸) 返回 null，回退到「无 LayoutElement / sizeDelta=0」的原行为。
        // ScreenInstantiator 把 ControlAttributeApplier.Apply (含 ApplyCommon → GetNativeSize)
        // 放在子树递归之后，所以这里读到的 child.sizeDelta 已是子节点 ApplyCommon 算出的最终值。
        public override Vector2? GetNativeSize()
        {
            if (Children.Count != 1) return null;
            var child = Children[0] as Control;
            if (child == null) return null;

            var native = child.GetNativeSize();
            if (native.HasValue) return native;

            var sd = child.RectTransform.sizeDelta;
            if (sd.x <= 0f || sd.y <= 0f) return null;
            return sd;
        }

        private TriggerSpec _spec;
        private IDisposable _sourceSub;
        private bool _subscribed;

        [UIAttr("on")]
        public string On { set => _spec = TriggerSpec.Parse(value); }

        internal Internal.TriggerKind TriggerKind => _spec?.Kind ?? Internal.TriggerKind.Open;

        internal override void OnAfterApply()
        {
            if (_subscribed) return;
            _subscribed = true;
            _spec ??= new TriggerSpec { Kind = TriggerKind.Open };
            InitTriggerSubscription();
        }

        protected virtual void InitTriggerSubscription()
        {
            switch (_spec.Kind)
            {
                case TriggerKind.Open:
                case TriggerKind.Loop:
                    Fire();
                    break;
                case TriggerKind.Click:
                    SubscribeClick();
                    break;
                case TriggerKind.HoverEnter:
                case TriggerKind.HoverExit:
                case TriggerKind.Press:
                    SubscribePointer(_spec.Kind);
                    break;
                case TriggerKind.Manual:
                    // no auto-subscribe; awaiting Fire()
                    break;
            }
        }

        public void Fire()
        {
            OnTriggerFired();
            _fire.OnNext(Unit.Default);
        }

        protected virtual void OnTriggerFired() { }

        private void SubscribeClick()
        {
            var btn = Internal.TriggerSourceResolver.FindBtn(this, _spec.SourceId);
            _sourceSub = btn.OnClick.Subscribe(_ => Fire());
        }

        private void SubscribePointer(TriggerKind kind)
        {
            var src = Internal.TriggerSourceResolver.FindPointerSource(this, _spec.SourceId);
            var stream = kind switch
            {
                TriggerKind.HoverEnter => src.OnPointerEnter,
                TriggerKind.HoverExit => src.OnPointerExit,
                TriggerKind.Press => src.OnPointerDown,
                _ => throw new InvalidOperationException("unreachable"),
            };
            _sourceSub = stream.Subscribe(_ => Fire());
        }

        public override void Dispose()
        {
            _sourceSub?.Dispose();
            _fire.Dispose();
            base.Dispose();
        }
    }
}
