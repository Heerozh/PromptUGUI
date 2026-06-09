using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>停留时长公式（纯函数，无逐帧依赖）。hold = clamp(min, base + 字数*perChar, max)。</summary>
    internal static class ToastDuration
    {
        internal static float Compute(string text, float holdOverride,
            float baseSec, float perChar, float min, float max)
        {
            if (holdOverride > 0f) return holdOverride;
            int chars = text?.Length ?? 0;     // 原始字符串长度作代理（<sprite> 标记会略拉长，可接受）
            return Mathf.Clamp(baseSec + chars * perChar, min, max);
        }
    }
}
