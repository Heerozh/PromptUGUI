using PromptUGUI.Application.Navigation;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>Convenience alias — identical to <see cref="Navigation.Enable"/>.</summary>
        public static void UseGamepadNavigation() => Navigation.Enable();

        public static partial class Navigation
        {
            public enum NavMode { Pointer, Directional }

            /// <summary>Pointer ↔ Directional 中枢信号（spec §3）。仅在 Enable 后由 NavigationController 驱动；
            /// 内部 settable 供控制器与 EditMode 测试设定。</summary>
            internal static NavMode Mode { get; set; } = NavMode.Pointer;
            internal static bool IsDirectional => Mode == NavMode.Directional;

            internal static bool IsEnabled { get; private set; }
            internal static NavigationController Controller { get; private set; }

            public static void Enable()
            {
#if ENABLE_INPUT_SYSTEM
                if (IsEnabled) return;
                IsEnabled = true;
                var es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (es == null)
                {
                    var go = new UnityEngine.GameObject("EventSystem",
                        typeof(UnityEngine.EventSystems.EventSystem),
                        typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                    es = go.GetComponent<UnityEngine.EventSystems.EventSystem>();
                }
                else if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null
                         && es.GetComponent<UnityEngine.EventSystems.BaseInputModule>() == null)
                {
                    es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                }
                Controller = es.gameObject.GetComponent<NavigationController>()
                             ?? es.gameObject.AddComponent<NavigationController>();
#else
                if (_warnedNoInputSystem) return;
                _warnedNoInputSystem = true;
                UnityEngine.Debug.LogWarning("[PromptUGUI] UI.UseGamepadNavigation() requires the New Input System package; gamepad/keyboard navigation is disabled.");
#endif
            }

#if !ENABLE_INPUT_SYSTEM
            private static bool _warnedNoInputSystem;
#endif

            /// <summary>控制器调用：上次输入来自指针 → 翻 Pointer，并在翻转时重绘选中控件焦点态。</summary>
            internal static void NotePointerInput() => SetMode(NavMode.Pointer);
            internal static void NoteDirectionalInput() => SetMode(NavMode.Directional);

            private static void SetMode(NavMode m)
            {
                if (Mode == m) return;
                Mode = m;
                RepokeSelected();
            }

            private static void RepokeSelected()
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null) return;
                var src = go.GetComponent<PromptUGUI.Controls.Internal.IStateSource>();
                src?.RefreshState();
            }

            /// <summary>全局默认光标节点（Task 8 替换为懒加载内置光标；当前返回 null）。</summary>
            internal static IR.ElementNode DefaultCursorNode => null;

            internal static void ResetForTestsInternal()
            {
                Mode = NavMode.Pointer;
                if (Controller != null) UnityEngine.Object.DestroyImmediate(Controller);
                Controller = null;
                IsEnabled = false;
#if !ENABLE_INPUT_SYSTEM
                _warnedNoInputSystem = false;
#endif
            }
        }
    }
}
