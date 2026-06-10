using System;
using PromptUGUI.Application.Toasts;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// 轻量提示文字子系统（spec §3）。独立于模态：不进 dialog 栈、不挡输入、定时自淡出。
        /// </summary>
        public static class Toast
        {
            public static string XmlSrc { get; set; } = "PromptUGUI/Toast.ui";   // 带 .ui 后缀
            public static int SortingOrder { get; set; } = 2000;                  // 须高于 Modal(1000)
            public static ToastPosition DefaultPosition { get; set; } = ToastPosition.Bottom;
            public static ToastStackMode DefaultStackMode { get; set; } = ToastStackMode.Stacked;
            public static int MaxVisible { get; set; } = 5;

            public static float FadeInSeconds { get; set; } = 0.2f;
            public static float FadeOutSeconds { get; set; } = 0.4f;
            public static float Spacing { get; set; } = 12f;
            public static float EdgeInset { get; set; } = 120f;
            public static Vector2 Padding { get; set; } = new(24f, 12f);   // content 比文字大出的边距
            public static float HoldBase { get; set; } = 1.0f;
            public static float HoldPerChar { get; set; } = 0.06f;
            public static float HoldMin { get; set; } = 1.5f;
            public static float HoldMax { get; set; } = 5.0f;

            // canonical：preset / Vector2(隐式) / ToastPosition.At(...)
            // color：色值 token（"red" / "#ff0000" / 主题色 / "red/0.5" alpha 后缀），null=保留模板默认色。
            // 它是 configure 之前应用的语法糖 → configure 仍能最后覆盖整条文字色。
            public static void Show(string text, ToastPosition position = default,
                ToastStackMode mode = ToastStackMode.Default, float holdSeconds = 0f,
                Action<IScreen> configure = null, string color = null)
            {
                if (position.IsUnspecified) position = DefaultPosition;
                if (mode == ToastStackMode.Default)
                    mode = DefaultStackMode == ToastStackMode.Default ? ToastStackMode.Stacked : DefaultStackMode;
                float hold = ToastDuration.Compute(text, holdSeconds < 0f ? 0f : holdSeconds,
                    HoldBase, HoldPerChar, HoldMin, HoldMax);
                ToastOverlay.Show(new ToastOverlay.ToastEntry
                {
                    Text = text,
                    Position = position,
                    Mode = mode,
                    Hold = hold,
                    Configure = configure,
                    Color = color,
                });
            }

            // 控件路径字符串（"<screenName>/<idPath>"）
            public static void Show(string text, string controlPath,
                ToastStackMode mode = ToastStackMode.Default, float holdSeconds = 0f,
                Action<IScreen> configure = null, string color = null)
                => Show(text, ToastPosition.At(controlPath), mode, holdSeconds, configure, color);

            // 控件引用（专用重载，因 IControl 不能隐式转 ToastPosition — CS0552）
            public static void Show(string text, IControl control,
                ToastStackMode mode = ToastStackMode.Default, float holdSeconds = 0f,
                Action<IScreen> configure = null, string color = null)
                => Show(text, ToastPosition.At(control), mode, holdSeconds, configure, color);
        }
    }
}
