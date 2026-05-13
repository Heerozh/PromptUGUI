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
        private Rect _lastSafe;
        private Vector2 _lastScreenSize;
        private bool _hasApplied;

        private void OnEnable()
        {
            _rt = transform as RectTransform;
            Apply();
        }

        private void Update()
        {
            // 跟 Unity 官方 SafeArea 示例对齐：每帧 poll，只在 safeArea / 分辨率
            // 真的变了时写。不订阅 OnRectTransformDimensionsChange —— 那条路径会跟
            // ApplyCommon / RectTransform setter 内部反向求解形成写入循环（已观测：
            // anchor 改写后 Unity 让 offset 留下亚像素到 ~0.65px 的残值，再触发回写，
            // 卡在 var screen = UI.Open(...) 的 InstantiateRecursive 阶段）。
            var safe = SafeAreaOverride != null ? SafeAreaOverride() : Screen.safeArea;
            var screenSize = ScreenSizeOverride != null
                ? ScreenSizeOverride()
                : new Vector2(Screen.width, Screen.height);

            if (!_hasApplied || safe != _lastSafe || screenSize != _lastScreenSize)
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

            _lastSafe = safe;
            _lastScreenSize = screenSize;
            _hasApplied = true;

            var aMin = new Vector2(safe.xMin / screenSize.x, safe.yMin / screenSize.y);
            var aMax = new Vector2(safe.xMax / screenSize.x, safe.yMax / screenSize.y);

            _rt.anchorMin = aMin;
            _rt.anchorMax = aMax;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
