using System;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;

namespace PromptUGUI.Controls
{
    public class Trigger : Control
    {
        private readonly Subject<Unit> _fire = new();
        public Observable<Unit> OnFire => _fire;

        private TriggerSpec _spec;
        private IDisposable _sourceSub;
        private bool _subscribed;

        [UIAttr("on")]
        public string On { set => _spec = TriggerSpec.Parse(value); }

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

        public override void Dispose()
        {
            _sourceSub?.Dispose();
            _fire.Dispose();
            base.Dispose();
        }
    }
}
