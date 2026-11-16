using LitMotion;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The default "picked up" look — a 1.03 scale eased in over 0.12s and eased back out on release —
    /// for a lifted thing that has no <c>on="lift"</c> hook of its own (spec 2026-09-16 §4.3, shared with
    /// drag-and-drop 2026-11-16 §5.4). Relative to the scale the thing had when it was lifted, so an
    /// authored <c>scale</c> is never flattened to 1.
    ///
    /// <para>One instance per driver, reused across sessions. A new lift while the previous one is still
    /// easing back first lands that tween on its destination — its target's base must not be read
    /// mid-way, and a tween left behind on another row would stop wherever it was cut off.</para>
    /// </summary>
    internal sealed class LiftLook
    {
        internal const float Scale = 1.03f;
        internal const float Duration = 0.12f;

        private RectTransform _rt;
        private Vector3 _base;
        private bool _on;
        private MotionHandle _tween;
        private Vector3 _tweenTo;

        internal bool IsOn => _on;

        internal void Apply(RectTransform rt)
        {
            Settle();
            _rt = rt;
            _base = rt.localScale;
            _on = true;
            TweenTo(new Vector3(_base.x * Scale, _base.y * Scale, _base.z));
        }

        /// <summary>Back to the scale it was lifted at — eased, or at once (a cancel restores instantly).</summary>
        internal void Undo(bool instant)
        {
            if (!_on) return;
            _on = false;
            if (_rt == null) return;
            if (instant)
            {
                Kill();
                _rt.localScale = _base;
                return;
            }
            TweenTo(_base);
        }

        /// <summary>A running tween ends on its destination now.</summary>
        internal void Settle()
        {
            if (!_tween.IsActive()) return;
            _tween.TryCancel();
            if (_rt != null) _rt.localScale = _tweenTo;
        }

        internal void Kill()
        {
            if (_tween.IsActive()) _tween.TryCancel();
        }

        private void TweenTo(Vector3 target)
        {
            Kill();
            _tweenTo = target;
            if (!UnityEngine.Application.isPlaying)
            {
                _rt.localScale = target;
                return;
            }
            _tween = LMotion.Create(_rt.localScale, target, Duration)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(_rt, static (v, r) => { if (r != null) r.localScale = v; });
        }
    }
}
