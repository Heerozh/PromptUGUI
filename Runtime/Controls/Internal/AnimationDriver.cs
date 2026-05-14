using System.Collections.Generic;
using LitMotion;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    internal static class AnimationDriver
    {
        public static MotionHandle[] Play(
            AnimationSpec spec,
            RectTransform offsetProxy,
            CanvasGroup canvasGroup,
            TMP_Text textTarget)
        {
            var handles = new List<MotionHandle>();
            var ease = ToEase(spec.Easing);

            switch (spec.Family)
            {
                case AnimationFamily.Preset:
                    // Task 9 expands preset → low-level invocations
                    break;

                case AnimationFamily.LowLevel:
                    if (spec.HasTranslate)
                        handles.Add(LMotion.Create(spec.TranslateFrom, spec.TranslateTo, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay)
                            .Bind(offsetProxy, (v, rt) => rt.anchoredPosition = v));
                    if (spec.HasScale)
                        handles.Add(LMotion.Create(
                                new Vector3(spec.ScaleFrom.x, spec.ScaleFrom.y, 1f),
                                new Vector3(spec.ScaleTo.x, spec.ScaleTo.y, 1f),
                                spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay)
                            .Bind(offsetProxy, (v, rt) => rt.localScale = v));
                    if (spec.HasRotate)
                        handles.Add(LMotion.Create(spec.RotateFrom, spec.RotateTo, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay)
                            .Bind(offsetProxy, (v, rt) => rt.localEulerAngles = new Vector3(0, 0, v)));
                    if (spec.HasFade)
                        handles.Add(LMotion.Create(spec.FadeFrom, spec.FadeTo, spec.Duration)
                            .WithEase(ease).WithDelay(spec.Delay)
                            .Bind(canvasGroup, (v, cg) => cg.alpha = v));
                    break;

                case AnimationFamily.Text:
                    // Task 11 / 12 fill in text effects
                    break;
            }

            return handles.ToArray();
        }

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
