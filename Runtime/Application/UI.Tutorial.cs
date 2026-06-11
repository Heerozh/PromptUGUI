using System;
using PromptUGUI.Application.Modals;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static class Tutorial
        {
            public static string XmlSrc { get; set; } = "PromptUGUI/Tutorial/TutorialOverlay.ui";
            public static int SortingOrder { get; set; } = 3000;   // > Toast(2000) > Modal(1000)
            public static string MaskColor { get; set; } = "#000000B0";

            private static Func<string, int> _load;
            private static Action<string, int> _save;
            public static void UseProgressStore(Func<string, int> load, Action<string, int> save)
            { _load = load; _save = save; }

            internal static TutorialFlow Active;
            private static TutorialOverlayView _view;
            private static string _overlayKey;
            private static readonly Func<string, bool> _rejectAll = _ => false;

            public static bool IsActive => Active != null;
            internal static bool IsBlockingInput => _view != null && _view.IsBlockingStep;

            internal static async Awaitable<TutorialOverlayView> EnsureOverlay()
            {
                if (_view != null) return _view;
                await ModalDocCache.EnsureLoaded(XmlSrc);
                var (screen, key) = UI.OpenModalScreen(XmlSrc);
                _overlayKey = key;
                var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
                _view = screen.RootGameObject.AddComponent<TutorialOverlayView>();
                _view.Init(screen);
                return _view;
            }

            internal static void DestroyOverlay()
            {
                if (_overlayKey != null) UI.CloseModalScreen(_overlayKey);
                _overlayKey = null; _view = null;
            }

            /// <summary>
            /// 跑一段引导:body 内一步一 await。id 用于断点续(load/save 委托)。
            /// 整段独占(重入抛 InvalidOperationException);try/finally 保证 guard 注销 + overlay 销毁。
            /// </summary>
            public static async Awaitable Run(string id, Func<TutorialFlow, Awaitable> body)
            {
                if (id == null) throw new ArgumentNullException(nameof(id));
                if (body == null) throw new ArgumentNullException(nameof(body));
                if (Active != null)
                    throw new InvalidOperationException("UI.Tutorial.Run: a tutorial is already running");

                int resume = _load?.Invoke(id) ?? 0;
                var flow = new TutorialFlow(id, resume, _save);
                Active = flow;
                Router.AddGuard(_rejectAll);
                try
                {
                    await body(flow);
                    _save?.Invoke(id, int.MaxValue);   // 整段完成哨兵
                }
                finally
                {
                    Router.RemoveGuard(_rejectAll);
                    DestroyOverlay();
                    Active = null;
                }
            }

            internal static void ResetForTestsInternal()
            { Active = null; _view = null; _overlayKey = null; _load = null; _save = null; }

            // —— 测试钩子 —— //
            internal static TutorialFlow BeginSessionForTests()
            { var f = new TutorialFlow("test", 0, null); Active = f; return f; }
            internal static void TickForTests(float dt) => _view?.Tick(dt);
            internal static TutorialOverlayView ViewForTests => _view;
        }
    }
}
