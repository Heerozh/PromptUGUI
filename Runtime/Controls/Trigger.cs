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

        private protected TriggerSpec _spec;
        private protected IDisposable _sourceSub;
        private bool _subscribed;

        [UIAttr("on"), Preserve]
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
            _sourceSub = SubscribeSpec(_spec, Fire, OnTriggerFiredInitial);
        }

        /// <summary>
        /// What a <c>checked</c> / <c>unchecked</c> trigger does when the control is ALREADY in that
        /// state as the Screen opens. Firing normally is right for a <c>&lt;Trigger&gt;</c>;
        /// <c>&lt;Animation&gt;</c> overrides it to establish the end state without animating —
        /// a header authored <c>isOn="true"</c> must not spin its chevron on frame 1 (FND-D10).
        /// </summary>
        protected virtual void OnTriggerFiredInitial() => Fire();

        /// <summary>Raises <see cref="OnFire"/> without running <see cref="OnTriggerFired"/>.</summary>
        private protected void RaiseFireOnly() => _fire.OnNext(Unit.Default);

        /// <summary>
        /// Wires one <see cref="TriggerSpec"/> to one callback and hands back the subscription.
        /// Factored out of <see cref="InitTriggerSubscription"/> so <c>&lt;Animation&gt;</c> can
        /// subscribe a second spec — its <c>reverse-on=</c> — through exactly the same resolution
        /// rules (spec 2026-08-31-hug-reveal-flip-checked-design §2.4.6).
        /// </summary>
        private protected IDisposable SubscribeSpec(TriggerSpec spec, Action onFire, Action onInitial = null)
        {
            onInitial ??= onFire;
            switch (spec.Kind)
            {
                case TriggerKind.Checked:
                case TriggerKind.Unchecked:
                    {
                        // Persistent state, not the transient state-* machine: hovering a checked
                        // Toggle must not take the block away (FND §4.4).
                        var want = spec.Kind == TriggerKind.Checked;
                        var toggle = Internal.TriggerSourceResolver.FindToggleSource(this, spec.SourceId);
                        if (toggle.IsOn == want) onInitial();
                        return toggle.OnValueChanged.Subscribe(v =>
                        {
                            if (v != want) return;
                            // A control's isOn= attribute is applied AFTER its children have
                            // subscribed (attributes go bottom-up), so an authored isOn="true"
                            // arrives here as an edge, not as the subscribe-time state. Anything
                            // that lands while the Screen is still opening is still "how it
                            // starts" and must establish, not animate.
                            if (PromptUGUI.Application.UI.OwnerScreenOf(this)?.IsOpening == true) onInitial();
                            else onFire();
                        });
                    }
                case TriggerKind.Open:
                case TriggerKind.Loop:
                    onFire();
                    return null;
                case TriggerKind.Click:
                    {
                        var btn = Internal.TriggerSourceResolver.FindBtn(this, spec.SourceId);
                        return btn.OnClick.Subscribe(_ => onFire());
                    }
                case TriggerKind.HoverEnter:
                case TriggerKind.HoverExit:
                case TriggerKind.Press:
                    {
                        var src = Internal.TriggerSourceResolver.FindPointerSource(this, spec.SourceId);
                        var stream = spec.Kind switch
                        {
                            TriggerKind.HoverEnter => src.OnPointerEnter,
                            TriggerKind.HoverExit => src.OnPointerExit,
                            _ => src.OnPointerDown,
                        };
                        return stream.Subscribe(_ => onFire());
                    }
                case TriggerKind.StateNormal:
                case TriggerKind.StateHover:
                case TriggerKind.StatePressed:
                case TriggerKind.StateSelected:
                case TriggerKind.StateDisabled:
                    {
                        var pui = Internal.TriggerSourceResolver.FindStateSource(this, spec.SourceId);
                        var target = spec.Kind switch
                        {
                            TriggerKind.StateNormal => InteractState.Normal,
                            TriggerKind.StateHover => InteractState.Hover,
                            TriggerKind.StatePressed => InteractState.Pressed,
                            TriggerKind.StateSelected => InteractState.Selected,
                            _ => InteractState.Disabled,
                        };
                        // OnState replays the current value on subscribe, so a trigger whose target
                        // matches the control's current state fires once at open.
                        return pui.OnState.Subscribe(s =>
                        {
                            if (s == target) onFire();
                        });
                    }
                case TriggerKind.Expand:
                case TriggerKind.Collapse:
                    {
                        var menu = Internal.TriggerSourceResolver.FindTabMenu(this, spec.SourceId);
                        var stream = spec.Kind == TriggerKind.Expand ? menu.OnExpanded : menu.OnCollapsed;
                        return stream.Subscribe(_ => onFire());
                    }
                default:
                    // Manual: no auto-subscribe; awaiting Fire()
                    return null;
            }
        }

        public void Fire()
        {
            OnTriggerFired();
            _fire.OnNext(Unit.Default);
        }

        protected virtual void OnTriggerFired() { }

        public override void Dispose()
        {
            _sourceSub?.Dispose();
            _fire.Dispose();
            base.Dispose();
        }
    }
}
