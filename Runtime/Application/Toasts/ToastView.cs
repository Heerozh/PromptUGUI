using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// 挂在 toast Screen root 上的单条生命周期驱动：淡入→停留→淡出→通知管理器移除。
    /// 用 <see cref="Time.unscaledDeltaTime"/>（游戏暂停 timeScale=0 时 toast 仍能淡出）。
    /// 每帧把 content 节点平滑 lerp 到管理器分配的堆叠目标位（位置回收）。
    /// 用 UIBehaviour（与 CarouselView 一致）以可靠收到 Unity 生命周期回调。
    /// </summary>
    internal sealed class ToastView : UIBehaviour
    {
        private enum Phase { FadeIn, Hold, FadeOut, Done }

        private CanvasGroup _cg;
        private RectTransform _content;
        private float _fadeIn, _fadeOut, _hold;
        private Action<ToastView> _onComplete;

        private Phase _phase;
        private float _t;
        private Vector2 _target;
        private bool _hasTarget;
        private const float ReflowTau = 0.08f;   // 位置平滑时间常数（越小越快贴目标）

        internal void Init(CanvasGroup cg, RectTransform content,
            float fadeIn, float hold, float fadeOut, Action<ToastView> onComplete)
        {
            _cg = cg; _content = content;
            _fadeIn = Mathf.Max(1e-4f, fadeIn);
            _fadeOut = Mathf.Max(1e-4f, fadeOut);
            _hold = Mathf.Max(0f, hold);
            _onComplete = onComplete;
            _phase = Phase.FadeIn; _t = 0f;
            if (_cg != null) _cg.alpha = 0f;
        }

        /// <param name="snap">true=立刻就位（最新一条落基准，无滑动）；false=平滑 lerp（被顶开/回收）。</param>
        internal void SetTarget(Vector2 target, bool snap)
        {
            _target = target; _hasTarget = true;
            if (snap && _content != null) _content.anchoredPosition = target;
        }

        internal bool IsEvicting => _phase == Phase.FadeOut || _phase == Phase.Done;

        // 管理器分配的堆叠目标位（materialize/reflow 时即确定，不依赖 Update）。供测试断言。
        internal Vector2 CurrentTarget => _target;

        /// <summary>MaxVisible 超额：立刻切到淡出，快速挤走。</summary>
        internal void Evict()
        {
            if (_phase == Phase.FadeOut || _phase == Phase.Done) return;
            _phase = Phase.FadeOut; _t = 0f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_hasTarget && _content != null)
            {
                float k = 1f - Mathf.Exp(-dt / ReflowTau);
                _content.anchoredPosition = Vector2.Lerp(_content.anchoredPosition, _target, k);
            }

            _t += dt;
            switch (_phase)
            {
                case Phase.FadeIn:
                    if (_cg != null) _cg.alpha = Mathf.Clamp01(_t / _fadeIn);
                    if (_t >= _fadeIn) { if (_cg != null) _cg.alpha = 1f; _phase = Phase.Hold; _t = 0f; }
                    break;
                case Phase.Hold:
                    if (_t >= _hold) { _phase = Phase.FadeOut; _t = 0f; }
                    break;
                case Phase.FadeOut:
                    if (_cg != null) _cg.alpha = 1f - Mathf.Clamp01(_t / _fadeOut);
                    if (_t >= _fadeOut)
                    {
                        if (_cg != null) _cg.alpha = 0f;
                        _phase = Phase.Done;
                        _onComplete?.Invoke(this);
                    }
                    break;
            }
        }
    }
}
