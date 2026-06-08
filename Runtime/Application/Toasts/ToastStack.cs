using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// 一组同位置 toast 的目标偏移（纯函数）。heights 按到达顺序 oldest→newest：
    /// newest 落基准 basePos，每条旧的沿 dir 被顶开"所有比它新的高度+spacing"之和。
    /// </summary>
    internal static class ToastStack
    {
        internal static Vector2[] ComputeTargets(
            IReadOnlyList<float> heights, float spacing, Vector2 dir, Vector2 basePos)
        {
            int n = heights.Count;
            var result = new Vector2[n];
            float cum = 0f;
            for (int i = n - 1; i >= 0; i--)   // 从 newest 往回累加
            {
                result[i] = basePos + dir * cum;
                cum += heights[i] + spacing;
            }
            return result;
        }
    }
}
