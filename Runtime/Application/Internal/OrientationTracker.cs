using System;
using UnityEngine;

namespace PromptUGUI.Application.Internal
{
    /// <summary>
    /// 全局单例 MonoBehaviour，每帧根据 Screen.width/height 切换 `portrait`/
    /// `landscape` 这两个 reserved variant。EditMode 测试通过
    /// <see cref="ScreenSizeOverride"/> 注入屏幕尺寸并直接调用 <see cref="Apply"/>。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class OrientationTracker : MonoBehaviour
    {
        // 仅测试注入：默认 null → 走真实 Screen.width/height
        internal static Func<Vector2> ScreenSizeOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[PromptUGUI] OrientationTracker")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            DontDestroyOnLoad(go);
            go.AddComponent<OrientationTracker>();
        }

        private void OnEnable() => Apply();
        private void Update() => Apply();

        /// <summary>
        /// 读屏幕尺寸→推 isPortrait→交给 <c>UI.Orientation.Set</c>。
        /// 屏幕尺寸为 0（编辑器启动早期 / dock 隐藏 Game View）时跳过，不写。
        /// `UI.Orientation.AutoTrack=false` 时也跳过——用户自管。
        /// </summary>
        internal static void Apply()
        {
            if (!UI.Orientation.AutoTrack) return;
            Vector2 size = ScreenSizeOverride != null
                ? ScreenSizeOverride()
                : new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height);
            if (size.x <= 0f || size.y <= 0f) return;
            // 等宽高视为 landscape：与 `Screen.ApplyCanvasScaler` 里
            // `size.x >= size.y` 锁宽的判定保持一致。
            UI.Orientation.Set(size.y > size.x);
        }
    }
}
