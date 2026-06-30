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

            public static bool IsEnabled { get; private set; }
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

            /// <summary>
            /// 控件 <c>OnSubmit</c> 的唤醒门：导航启用且当前为 Pointer 模式（焦点光标隐藏）时，
            /// 把这次确认解释为「先唤回光标」而非「点击隐藏焦点」——翻 Directional 让光标重现，
            /// 返回 true 让调用方吞掉本次 Submit。其余情况返回 false（照常点击）。
            /// 见 spec 2026-06-30-nav-hidden-submit-wake。
            /// </summary>
            internal static bool TryWakeOnSubmit()
            {
                if (!IsEnabled) return false;
                if (IsDirectional) return false;
                NoteDirectionalInput();
                return true;
            }

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

            private static IR.ElementNode _defaultCursorNode;
            private static bool _defaultCursorLoaded;

            /// <summary>自定义全局光标 src 键（null = 内置）。非 null 时由调用方 SourceResolver 懒加载（v1 占位）。</summary>
            public static string DefaultCursorSrc { get; set; }

            /// <summary>全局默认光标节点：懒加载内置 FocusCursor.ui.xml，缓存后返回。</summary>
            internal static IR.ElementNode DefaultCursorNode
            {
                get
                {
                    if (_defaultCursorLoaded) return _defaultCursorNode;
                    _defaultCursorLoaded = true;
                    var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(
                        "PromptUGUI/Navigation/FocusCursor.ui");
                    if (ta == null) return null;
                    var doc = Parser.UIDocumentParser.Parse(ta.text);
                    foreach (var sc in doc.Screens)
                        if (sc.FocusCursor != null) { _defaultCursorNode = sc.FocusCursor; break; }
                    return _defaultCursorNode;
                }
            }

            // ── Selection containment + focus recovery ────────────────────────────────────
            // ContainmentRoot non-null (a modal is open) = EventSystem selection must stay within
            // that subtree. Enforced every frame from NavigationController.Update.
            // Uses only EventSystem + Selectable; no InputSystem types → no #if needed here.

            internal static UnityEngine.GameObject ContainmentRoot { get; set; }

            // Last live focus seen while enforcing — the anchor a full-screen page re-acquires when a
            // pointer click on the background / a non-Selectable child clears the EventSystem selection
            // (uGUI deselects) and the user then resumes with directional input. Without recovery the
            // directional InputModule has no current selection to move from and navigation goes dead.
            private static UnityEngine.GameObject _lastFocused;

            internal static void EnforceContainment()
            {
                // EventSystem.current is null in EditMode; mirror Screen.FindEventSystem fallback.
                var es = UnityEngine.EventSystems.EventSystem.current
                         ?? UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (es == null) return;
                var sel = es.currentSelectedGameObject;
                var root = ContainmentRoot;

                if (root != null)
                {
                    // Modal focus trap: selection must stay within the modal subtree.
                    if (sel != null && sel.transform.IsChildOf(root.transform)) { Remember(sel); return; }
                    var pick = FirstFocusableUnder(root);
                    if (pick != null) { es.SetSelectedGameObject(pick); _lastFocused = pick; }
                    return;
                }

                // Full-screen page: remember the live focus; when it is lost (click on the background)
                // and the user is driving with directional input, recall it so navigation never goes
                // dead. In Pointer mode the deselect is intentional (cursor hidden) — leave it cleared.
                if (sel != null) { Remember(sel); return; }
                if (!IsDirectional) return;
                if (IsFocusable(_lastFocused)) es.SetSelectedGameObject(_lastFocused);
            }

            internal static void EnforceContainmentForTests() => EnforceContainment();

            private static void Remember(UnityEngine.GameObject go)
            {
                if (IsFocusable(go)) _lastFocused = go;
            }

            internal static UnityEngine.GameObject FirstFocusableUnder(UnityEngine.GameObject root)
            {
                var all = root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false);
                foreach (var s in all)
                    if (IsFocusable(s)) return s.gameObject;
                return null;
            }

            private static bool IsFocusable(UnityEngine.GameObject go) =>
                go != null && IsFocusable(go.GetComponent<UnityEngine.UI.Selectable>());

            private static bool IsFocusable(UnityEngine.UI.Selectable s) =>
                s != null && s.IsActive() && s.IsInteractable()
                && s.navigation.mode != UnityEngine.UI.Navigation.Mode.None;

            // ─────────────────────────────────────────────────────────────────────────────

            internal static void ResetForTestsInternal()
            {
                Mode = NavMode.Pointer;
                if (Controller != null) UnityEngine.Object.DestroyImmediate(Controller);
                Controller = null;
                IsEnabled = false;
                ContainmentRoot = null;
                _lastFocused = null;
                _defaultCursorNode = null;
                _defaultCursorLoaded = false;
                DefaultCursorSrc = null;
#if !ENABLE_INPUT_SYSTEM
                _warnedNoInputSystem = false;
#endif
            }
        }
    }
}
