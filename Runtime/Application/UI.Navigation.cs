namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Navigation
        {
            public enum NavMode { Pointer, Directional }

            /// <summary>Pointer ↔ Directional 中枢信号（spec §3）。仅在 Enable 后由 NavigationController 驱动；
            /// 内部 settable 供控制器与 EditMode 测试设定。</summary>
            internal static NavMode Mode { get; set; } = NavMode.Pointer;
            internal static bool IsDirectional => Mode == NavMode.Directional;

            internal static void ResetForTestsInternal()
            {
                Mode = NavMode.Pointer;
            }
        }
    }
}
