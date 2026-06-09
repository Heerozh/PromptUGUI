using System;
using System.Collections.Generic;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// Toast 子系统（spec §6/§8）。克隆 LoadingOverlay 的 materialize pump + epoch teardown，
    /// 再加分组堆叠布局与 Stacked/Sequential 准入。每条 toast 一份 Screen。
    /// </summary>
    internal static class ToastOverlay
    {
        internal sealed class ToastEntry
        {
            public string Text;
            public ToastPosition Position;
            public ToastStackMode Mode;
            public float Hold;
            public Action<IScreen> Configure;
        }

        private sealed class LiveToast
        {
            public object GroupKey;
            public ToastView View;
            public RectTransform Content;
            public Screen Screen;
            public string Key;
            public ToastPosition.Resolved Resolved;
        }

        private static readonly List<LiveToast> _live = new();        // 到达顺序
        private static readonly Queue<ToastEntry> _pending = new();    // 待 materialize
        private static readonly Queue<ToastEntry> _waiting = new();    // Sequential 等清空
        private static bool _materializing;
        private static int _materializeEpoch;

        internal static int ActiveCount => _live.Count;
        internal static int QueuedCount => _pending.Count + _waiting.Count;

        internal static IEnumerable<Screen> ActiveScreens
        {
            get { foreach (var t in _live) if (t.Screen != null) yield return t.Screen; }
        }

        internal static void Show(ToastEntry entry)
        {
            bool sequential = entry.Mode == ToastStackMode.Sequential;
            if (sequential && !IsIdle()) _waiting.Enqueue(entry);
            else QueueForMaterialize(entry);
        }

        private static bool IsIdle() =>
            _live.Count == 0 && _pending.Count == 0 && !_materializing;

        private static void QueueForMaterialize(ToastEntry e)
        {
            _pending.Enqueue(e);
            if (!_materializing) _ = MaterializePump();
        }

        private static async Awaitable MaterializePump()
        {
            if (_materializing) return;
            _materializing = true;
            int epoch = _materializeEpoch;
            try
            {
                while (_pending.Count > 0)
                {
                    if (epoch != _materializeEpoch) return;
                    var entry = _pending.Dequeue();
                    try { await Materialize(entry); }
                    catch (Exception ex) { Debug.LogError($"[PromptUGUI] Toast 显示失败: {ex}"); }
                }
            }
            finally
            {
                if (epoch == _materializeEpoch)
                {
                    _materializing = false;
                    PromoteWaiting();
                }
            }
        }

        private static async Awaitable Materialize(ToastEntry entry)
        {
            string src = UI.Toast.XmlSrc;
            await ModalDocCache.EnsureLoaded(src);   // 唯一 await，须在加入 _live 之前
            var (screen, key) = UI.OpenModalScreen(src);

            var root = screen.RootGameObject;
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = UI.Toast.SortingOrder;

            var cg = root.GetComponent<CanvasGroup>();
            if (!cg) cg = root.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 0f;

            // 文本节点（必需）+ 定位/测量节点（content 优先，回退 text）
            Text textCtl = TryGet<Text>(screen, "text");
            RectTransform content = TryGet<IControl>(screen, "content")?.RectTransform
                                    ?? textCtl?.RectTransform;
            if (textCtl == null || content == null)
            {
                Debug.LogError("[PromptUGUI] Toast 模板缺 id=\"text\" 节点，无法显示。");
                UI.CloseModalScreen(key);
                return;
            }

            textCtl.TextValue = entry.Text ?? "";
            entry.Configure?.Invoke(screen);

            // 尺寸：content = 文字 native + 2*Padding
            Vector2 native = textCtl.GetNativeSize() ?? Vector2.zero;
            content.sizeDelta = native + 2f * UI.Toast.Padding;

            // 定位：解析失败（控件路径/引用失效）→ 退回 DefaultPosition + warning
            var canvasRect = root.GetComponent<RectTransform>();
            var pos = entry.Position;
            if (!pos.TryResolve(canvasRect, UI.Toast.EdgeInset, out var resolved))
            {
                Debug.LogWarning("[PromptUGUI] Toast 控件定位解析失败，退回默认位置。");
                pos = FallbackPosition();
                pos.TryResolve(canvasRect, UI.Toast.EdgeInset, out resolved);
            }
            content.anchorMin = content.anchorMax = resolved.Anchor;
            content.pivot = resolved.Pivot;

            var live = new LiveToast
            {
                GroupKey = pos.GroupKey(),
                Content = content,
                Screen = screen,
                Key = key,
                Resolved = resolved,
            };

            EvictIfNeeded(live.GroupKey);
            _live.Add(live);

            var view = root.AddComponent<ToastView>();
            live.View = view;
            view.Init(cg, content,
                UI.Toast.FadeInSeconds, entry.Hold, UI.Toast.FadeOutSeconds, OnViewComplete);

            ReflowGroup(live.GroupKey, newest: live);
        }

        // DefaultPosition 若被设成 Control/ControlPath/Unspecified（不该），兜底 Bottom，避免递归失败。
        private static ToastPosition FallbackPosition()
        {
            var d = UI.Toast.DefaultPosition;
            return d.IsUnspecified ? ToastPosition.Bottom : d;
        }

        private static void ReflowGroup(object groupKey, LiveToast newest)
        {
            var members = _live.FindAll(t => Equals(t.GroupKey, groupKey));   // 到达顺序
            if (members.Count == 0) return;
            var basis = members[members.Count - 1].Resolved;   // 用最新一条的基准快照
            var heights = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
                heights[i] = members[i].Content != null ? members[i].Content.rect.height : 0f;
            var targets = ToastStack.ComputeTargets(heights, UI.Toast.Spacing, basis.Dir, basis.BasePos);
            for (int i = 0; i < members.Count; i++)
                members[i].View?.SetTarget(targets[i], snap: members[i] == newest);
        }

        private static void EvictIfNeeded(object groupKey)
        {
            int max = UI.Toast.MaxVisible;
            if (max <= 0) return;
            var members = _live.FindAll(t => Equals(t.GroupKey, groupKey));
            // 即将再加一条 → 已达上限就从最老起逐条挤走，直到留出一个名额
            for (int i = 0; members.Count - i >= max; i++)
                members[i].View?.Evict();
        }

        private static void OnViewComplete(ToastView view)
        {
            int idx = _live.FindIndex(t => t.View == view);
            if (idx < 0) return;
            var live = _live[idx];
            _live.RemoveAt(idx);
            UI.CloseModalScreen(live.Key);
            ReflowGroup(live.GroupKey, newest: null);   // 其余回收，无 snap
            PromoteWaiting();
        }

        private static void PromoteWaiting()
        {
            if (!IsIdle()) return;
            while (_waiting.Count > 0)
            {
                QueueForMaterialize(_waiting.Dequeue());
                return;
            }
        }

        internal static void CancelAllForTeardown()
        {
            _live.Clear();
            _pending.Clear();
            _waiting.Clear();
            _materializeEpoch++;     // 抛弃在途 pump
            _materializing = false;
            // toast Screen 在 UI._open 里，由 UnloadAll/ResetForTests 的 _open 循环统一关
        }

        // —— 测试钩子 —— //
        internal static bool CompleteOldestForTests()
        {
            if (_live.Count == 0) return false;
            OnViewComplete(_live[0].View);
            return true;
        }

        internal static bool OldestIsEvictingForTests()
            => _live.Count > 0 && _live[0].View != null && _live[0].View.IsEvicting;

        private static T TryGet<T>(Screen screen, string id) where T : class, IControl
        {
            try { return screen.Get<T>(id); }
            catch (KeyNotFoundException) { return null; }
            catch (InvalidCastException) { return null; }   // id 存在但类型不符（content 可为非 Text）
        }
    }
}
