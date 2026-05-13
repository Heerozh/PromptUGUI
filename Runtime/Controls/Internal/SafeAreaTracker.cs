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

            _rt.anchorMin = aMin;
            _rt.anchorMax = aMax;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
