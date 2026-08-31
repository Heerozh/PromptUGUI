using System.Collections.Generic;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// What the <c>reveal</c> channel needs from whoever owns the animated box. Implemented by
    /// <c>&lt;Animation&gt;</c>; <c>&lt;Collapsible&gt;</c> drives its body through the same shape.
    /// </summary>
    internal interface IRevealTarget
    {
        /// <summary>The box being shown right now — a reversal starts from here, not from an endpoint.</summary>
        public float RevealBox { get; }

        public void SetRevealBox(float value);

        /// <summary>Resolves an endpoint, measuring the content when it is <c>hug</c>.</summary>
        public float ResolveReveal(RevealValue value);

        public void SetRevealClip(bool on);

        /// <summary>The motion landed on an endpoint: <paramref name="reversed"/> tells which one.</summary>
        public void OnRevealSettled(bool reversed);
    }

    /// <summary>Everything a spec can drive. Filled by the control, read by the driver.</summary>
    internal struct AnimationContext
    {
        public RectTransform Proxy;
        public CanvasGroup Cg;
        public TMP_Text Text;
        public IRevealTarget Reveal;
    }

    internal static class AnimationDriver
    {
        /// <summary>
        /// Builds the motions for one fire. <paramref name="reverse"/> swaps the endpoints; the start
        /// value is then read from wherever each channel currently is, so an interrupted animation
        /// turns around from where it was rather than snapping to an end (spec
        /// 2026-08-31-hug-reveal-flip-checked-design §2.4.5).
        ///
        /// <para>Reading the current value is enabled only for animations that can actually reverse
        /// (<c>reverse-on=</c>) or that own a box (<c>reveal</c>). Everything else keeps the historic
        /// "write from, then tween" behaviour exactly — a re-fired <c>slidein-left</c> still restarts
        /// from off-screen.</para>
        /// </summary>
        public static MotionHandle[] Play(AnimationSpec spec, in AnimationContext ctx, bool reverse = false)
        {
            var handles = new List<MotionHandle>();
            var ease = ToEase(spec.Easing);
            var fromCurrent = spec.ReverseOn != null || spec.HasReveal;
            var loops = LoopCountOf(spec);
            var loopType = LoopTypeOf(spec);

            switch (spec.Family)
            {
                case AnimationFamily.Preset:
                    // ExpandPreset mutates Has*/From/To. Run it on a copy so the authored
                    // spec — which Screen.ReSolve re-Validates on every theme/locale/variant/
                    // resize change — is never polluted with the expanded low-level flags.
                    // Otherwise a played preset reads as "preset + low-level" and trips the
                    // mutual-exclusion check in AnimationSpec.Validate.
                    spec = spec.Clone();
                    spec.ExpandPreset();
                    ease = ToEase(spec.Easing);
                    loops = LoopCountOf(spec);
                    loopType = LoopTypeOf(spec);
                    goto case AnimationFamily.LowLevel;

                case AnimationFamily.LowLevel:
                    if (spec.HasTranslate)
                    {
                        var current = ctx.Proxy.anchoredPosition;
                        var from = fromCurrent ? current : (reverse ? spec.TranslateTo : spec.TranslateFrom);
                        var to = reverse ? spec.TranslateFrom : spec.TranslateTo;
                        // .AddTo ties the motion's lifetime to the target GameObject so it is
                        // auto-cancelled when the GO is destroyed outside the Screen.Close()/Dispose()
                        // path (async scene load aborted, scene unload, editor stop). Without it the
                        // handle keeps ticking in the global MotionDispatcher and writes to a
                        // destroyed component → MissingReferenceException.
                        handles.Add(LMotion.Create(from, to, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay).WithLoops(loops, loopType)
                            .Bind(ctx.Proxy, (v, rt) => rt.anchoredPosition = v)
                            .AddTo(ctx.Proxy.gameObject));
                    }
                    if (spec.HasScale)
                    {
                        var specFrom = new Vector3(spec.ScaleFrom.x, spec.ScaleFrom.y, 1f);
                        var specTo = new Vector3(spec.ScaleTo.x, spec.ScaleTo.y, 1f);
                        var from = fromCurrent ? ctx.Proxy.localScale : (reverse ? specTo : specFrom);
                        var to = reverse ? specFrom : specTo;
                        handles.Add(LMotion.Create(from, to, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay).WithLoops(loops, loopType)
                            .Bind(ctx.Proxy, (v, rt) => rt.localScale = v)
                            .AddTo(ctx.Proxy.gameObject));
                    }
                    if (spec.HasRotate)
                    {
                        var from = fromCurrent
                            ? ctx.Proxy.localEulerAngles.z
                            : (reverse ? spec.RotateTo : spec.RotateFrom);
                        var to = reverse ? spec.RotateFrom : spec.RotateTo;
                        handles.Add(LMotion.Create(from, to, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay).WithLoops(loops, loopType)
                            .Bind(ctx.Proxy, (v, rt) => rt.localEulerAngles = new Vector3(0, 0, v))
                            .AddTo(ctx.Proxy.gameObject));
                    }
                    if (spec.HasFade)
                    {
                        var from = fromCurrent ? ctx.Cg.alpha : (reverse ? spec.FadeTo : spec.FadeFrom);
                        var to = reverse ? spec.FadeFrom : spec.FadeTo;
                        handles.Add(LMotion.Create(from, to, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay).WithLoops(loops, loopType)
                            .Bind(ctx.Cg, (v, cg) => cg.alpha = v)
                            .AddTo(ctx.Cg.gameObject));
                    }
                    if (spec.HasReveal && ctx.Reveal != null)
                    {
                        // Endpoints are resolved per fire, never cached: 'hug' has to see the content
                        // as it is now (new rows, a locale switch), and the start is wherever the box
                        // currently sits so an interrupted open turns straight around.
                        var target = ctx.Reveal.ResolveReveal(reverse ? spec.RevealFrom : spec.RevealTo);
                        var from = ctx.Reveal.RevealBox;
                        var reveal = ctx.Reveal;
                        reveal.SetRevealClip(true);
                        var handle = LMotion.Create(from, target, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay).WithLoops(loops, loopType)
                            .Bind(v => reveal.SetRevealBox(v))
                            .AddTo(ctx.Proxy.gameObject);
                        var captured = reverse;
                        handle.GetAwaiter().OnCompleted(() => reveal.OnRevealSettled(captured));
                        handles.Add(handle);
                    }
                    break;

                case AnimationFamily.Text:
                    if (spec.HasCount)
                    {
                        if (ctx.Text == null)
                            throw new System.InvalidOperationException(
                                "<Animation count=...> requires a Text target (in subtree or via target=\"@id\")");
                        handles.Add(LMotion.Create(spec.CountFrom, spec.CountTo, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay).WithLoops(loops, loopType)
                            .BindToText(ctx.Text, spec.Format)
                            .AddTo(ctx.Text.gameObject));
                    }
                    if (spec.HasCharColor)
                    {
                        if (ctx.Text == null)
                            throw new System.InvalidOperationException(
                                "<Animation char-color=...> requires a Text target");
                        // Force the mesh to populate textInfo so characterCount is correct.
                        ctx.Text.ForceMeshUpdate();
                        var count = ctx.Text.textInfo.characterCount;
                        for (int i = 0; i < count; i++)
                        {
                            var charIdx = i;
                            var perCharDelay = spec.Delay + spec.CharStaggerSec * i;
                            handles.Add(LMotion.Create(spec.CharColorFrom, spec.CharColorTo, spec.Duration)
                                .WithEase(ease).WithDelay(perCharDelay).WithLoops(loops, loopType)
                                .BindToTMPCharColor(ctx.Text, charIdx)
                                .AddTo(ctx.Text.gameObject));
                        }
                    }
                    break;
            }

            return handles.ToArray();
        }

        // LoopMode.None maps to LitMotion's own default (one pass, Restart), so every channel can
        // call WithLoops unconditionally instead of repeating a three-way switch.
        private static int LoopCountOf(AnimationSpec spec) => spec.LoopMode switch
        {
            LoopMode.Yoyo => -1,
            LoopMode.Restart => -1,
            LoopMode.Count => spec.LoopCount,
            _ => 1,
        };

        private static LoopType LoopTypeOf(AnimationSpec spec) =>
            spec.LoopMode == LoopMode.Yoyo ? LoopType.Yoyo : LoopType.Restart;

        private static Ease ToEase(EasingKind k) => k switch
        {
            EasingKind.Linear => Ease.Linear,
            EasingKind.InCubic => Ease.InCubic,
            EasingKind.OutCubic => Ease.OutCubic,
            EasingKind.InOutCubic => Ease.InOutCubic,
            EasingKind.InQuad => Ease.InQuad,
            EasingKind.OutQuad => Ease.OutQuad,
            EasingKind.InOutQuad => Ease.InOutQuad,
            EasingKind.InQuart => Ease.InQuart,
            EasingKind.OutQuart => Ease.OutQuart,
            EasingKind.InOutQuart => Ease.InOutQuart,
            EasingKind.InQuint => Ease.InQuint,
            EasingKind.OutQuint => Ease.OutQuint,
            EasingKind.InOutQuint => Ease.InOutQuint,
            EasingKind.OutBack => Ease.OutBack,
            EasingKind.OutElastic => Ease.OutElastic,
            EasingKind.OutBounce => Ease.OutBounce,
            _ => Ease.OutCubic,
        };
    }
}
