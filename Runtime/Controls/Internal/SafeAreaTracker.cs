using System;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    [DisallowMultipleComponent]
    internal sealed class SafeAreaTracker : MonoBehaviour
    {
        // 仅测试注入：默认 null → 走真实 Screen.safeArea / Screen.width|height
        internal static Func<Rect> SafeAreaOverride;
        internal static Func<Vector2> ScreenSizeOverride;

        private RectTransform _rt;

        private void OnEnable()
        {
            _rt = transform as RectTransform;
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_rt == null) return;
            Apply();
        }

        internal void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) return;

            var safe = SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;
            var screenSize = ScreenSizeOverride != null
                ? ScreenSizeOverride()
                : new Vector2(Screen.width, Screen.height);

            if (screenSize.x <= 0f || screenSize.y <= 0f) return;

            var aMin = new Vector2(safe.xMin / screenSize.x, safe.yMin / screenSize.y);
            var aMax = new Vector2(safe.xMax / screenSize.x, safe.yMax / screenSize.y);

            // 写之前比较一次：避免 OnRectTransformDimensionsChange → Apply → 写 RectTransform →
            // 再次触发 OnRectTransformDimensionsChange 的回环。
            //
            // 必须用宽容差：Unity 的 Vector2.== 阈值是 sqrMagnitude < 1e-10（约边长 1e-5），
            // 但 anchor 改写后 RectTransform 上 offset 会留下 ~7.5e-5 的 float 残留 ——
            // 比 Vector2.== 阈值大两个量级，比较直接相等会永远写、永远循环。
            // 用按维度的绝对容差：anchor 是 [0,1] 分数，0.0001 容差对应 1920 屏上 < 0.2 像素；
            // offset 是像素量纲，0.5 容差正好亚像素。
            if (NotApprox(_rt.anchorMin, aMin, kAnchorEpsilon)) _rt.anchorMin = aMin;
            if (NotApprox(_rt.anchorMax, aMax, kAnchorEpsilon)) _rt.anchorMax = aMax;
            if (NotApprox(_rt.offsetMin, Vector2.zero, kOffsetEpsilon)) _rt.offsetMin = Vector2.zero;
            if (NotApprox(_rt.offsetMax, Vector2.zero, kOffsetEpsilon)) _rt.offsetMax = Vector2.zero;
        }

        private const float kAnchorEpsilon = 1e-4f;
        private const float kOffsetEpsilon = 0.5f;

        private static bool NotApprox(Vector2 a, Vector2 b, float eps)
        {
            return Mathf.Abs(a.x - b.x) > eps || Mathf.Abs(a.y - b.y) > eps;
        }
    }
}
