using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Router
        {
            private sealed class ActiveNode
            {
                public RouteNode Def;
                public string ScreenKey;                 // Page/Modal:_open 的 key(== screen 名)
                public System.Threading.CancellationTokenSource PromptCts;  // Prompt
                public bool Deactivated;                 // Prompt:reconcile 正在移除它,SelfPop 让位
            }

            private static readonly List<ActiveNode> _chain = new();
            private static readonly HashSet<string> _loadedSrcs = new();
            private static readonly Dictionary<string, string> _srcSingleScreen = new();

            public static event Action Changed;

            public static string Current =>
                _chain.Count > 0 ? _chain[_chain.Count - 1].Def.Name : null;

            public static IReadOnlyList<string> Chain
            {
                get
                {
                    var l = new List<string>(_chain.Count);
                    foreach (var a in _chain) l.Add(a.Def.Name);
                    return l;
                }
            }

            public static Awaitable Back()
            {
                if (_chain.Count == 0) return AwaitableHelpers.Completed();
                var parent = _chain[_chain.Count - 1].Def.Parent;
                if (parent == null) return AwaitableHelpers.Completed();   // 已在根
                return Open(parent);
            }

            public static Awaitable Reset()
            {
                AbandonPump(cancel: false);
                for (int i = _chain.Count - 1; i >= 0; i--) Deactivate(_chain[i]);
                _chain.Clear();
                Changed?.Invoke();
                return AwaitableHelpers.Completed();
            }

            private static (string name, RouteQuery query)? _pending;
            private static readonly List<AwaitableCompletionSource> _waiters = new();
            private static bool _reconciling;
            private static int _epoch;

            public static Awaitable Open(string name, RouteQuery query = null)
            {
                var tcs = new AwaitableCompletionSource();
                _waiters.Add(tcs);
                _pending = (name, query ?? RouteQuery.Empty);
                if (!_reconciling) _ = Pump();
                return tcs.Awaitable;
            }

            public static async Awaitable Navigate(string url)
            {
                var (name, query) = ParseUrl(url);
                await Open(name, query);
            }

            private static async Awaitable Pump()
            {
                _reconciling = true;
                int epoch = _epoch;
                Exception error = null;
                try
                {
                    while (_pending != null)
                    {
                        if (epoch != _epoch) return;   // 被 teardown 抛弃
                        var t = _pending.Value;
                        _pending = null;
                        error = null;
                        try { await Reconcile(t.name, t.query); }
                        catch (Exception ex) { error = ex; }
                    }
                }
                finally
                {
                    if (epoch == _epoch)
                    {
                        _reconciling = false;
                        var done = _waiters.ToArray();
                        _waiters.Clear();
                        foreach (var w in done)
                        {
                            if (error != null) w.TrySetException(error);
                            else w.TrySetResult();
                        }
                    }
                }
            }

            // 抛弃进行中的 pump:自增 epoch 让它恢复即退;完成所有等待者。
            // cancel=true(teardown)→ 抛 OCE;cancel=false(Reset)→ 正常完成。
            private static void AbandonPump(bool cancel)
            {
                _epoch++;
                _reconciling = false;
                _pending = null;
                var done = _waiters.ToArray();
                _waiters.Clear();
                foreach (var w in done)
                {
                    if (cancel) w.TrySetException(
                        new OperationCanceledException("Router torn down"));
                    else w.TrySetResult();
                }
            }

            private static async Awaitable Reconcile(string name, RouteQuery query)
            {
                var target = ResolveChain(name);   // 校验 + 根→叶

                // 完全相同的链路 = 无结构变化:跳过(后续 Task 6 的)临时模态清理,只刷新栈顶。
                // 这条也保证"重复导航到正在显示的 Prompt"是 no-op,不会把它误关再重开。
                if (SameChain(target))
                {
                    if (_chain.Count > 0) RefreshTarget(_chain[_chain.Count - 1], query);
                    Changed?.Invoke();
                    return;
                }

                int k = 0;
                while (k < _chain.Count && k < target.Count
                       && _chain[k].Def.Name == target[k].Name) k++;

                // 先把尾部从 _chain 快照 + 移除,再 deactivate。这样若 deactivate(或 Task 6 的
                // 临时模态清理)同步触发了某个 Prompt 续体的 SelfPop,它看到自己已不在 _chain → no-op,
                // 不会与这里的链路改动撞车。
                var removed = new List<ActiveNode>();
                for (int i = k; i < _chain.Count; i++) removed.Add(_chain[i]);
                if (removed.Count > 0) _chain.RemoveRange(k, removed.Count);

                // §3.3:顶上压着的 ad-hoc 临时模态(不属 router)先关掉 —— 等价"用户先关弹窗再导航"。
                // 尾部已先于此移除,故即便 CloseAll 同步唤醒某 Prompt 续体,其 SelfPop 也已 no-op。
                // SameChain 早退路径不经过这里:重复导航到正在显示的同一链路不会误关其对话框。
                if (UI.Modal.IsAnyOpen) UI.Modal.CloseAll();

                for (int i = removed.Count - 1; i >= 0; i--) Deactivate(removed[i]);

                for (int i = k; i < target.Count; i++)
                {
                    var active = await Activate(target[i], query);
                    _chain.Add(active);
                }

                // 目标落在公共前缀里(Open 某个祖先)→ 刷新目标 OnEnter
                if (k == target.Count && _chain.Count > 0)
                    RefreshTarget(_chain[_chain.Count - 1], query);

                Changed?.Invoke();
            }

            private static bool SameChain(List<RouteNode> target)
            {
                if (target.Count != _chain.Count) return false;
                for (int i = 0; i < target.Count; i++)
                    if (_chain[i].Def.Name != target[i].Name) return false;
                return true;
            }

            private static async Awaitable<ActiveNode> Activate(RouteNode def, RouteQuery query)
            {
                switch (def.Kind)
                {
                    case RouteKind.Page: return await ActivatePage(def, query);
                    default:
                        throw new RouteException($"route '{def.Name}': kind not yet supported");
                }
            }

            private static void Deactivate(ActiveNode a)
            {
                switch (a.Def.Kind)
                {
                    case RouteKind.Page:
                        UI.Close(a.ScreenKey);
                        break;
                }
            }

            private static void RefreshTarget(ActiveNode a, RouteQuery query)
            {
                if (a.Def.OnEnter == null) return;
                var screen = UI.Get(a.ScreenKey);
                if (screen != null) a.Def.OnEnter(screen, query);
            }

            // —— Page ——
            private static async Awaitable<ActiveNode> ActivatePage(RouteNode def, RouteQuery query)
            {
                var screenName = await EnsureLoaded(def);
                var screen = UI.Open(screenName);
                def.OnEnter?.Invoke(screen, query);
                return new ActiveNode { Def = def, ScreenKey = screenName };
            }

            // 加载 def.Src 一次,解析出要 Open 的 screen 名。
            private static async Awaitable<string> EnsureLoaded(RouteNode def)
            {
                if (!_loadedSrcs.Contains(def.Src))
                {
                    var names = await UI.LoadDocumentAsync(def.Src);
                    _loadedSrcs.Add(def.Src);
                    if (names.Count == 1) _srcSingleScreen[def.Src] = names[0];
                }
                if (!string.IsNullOrEmpty(def.Screen)) return def.Screen;
                if (_srcSingleScreen.TryGetValue(def.Src, out var single)) return single;
                throw new RouteException(
                    $"route '{def.Name}': src '{def.Src}' has multiple screens; specify screen=");
            }

            // UI.UnloadAll 调用:取消 prompt/清 router 链路;链路屏在 _open 里由 UnloadAll 的
            // _open 循环统一销毁。保留注册表(配置)。
            internal static void CancelAllForTeardown()
            {
                AbandonPump(cancel: true);
                foreach (var a in _chain)
                {
                    a.PromptCts?.Cancel();
                    a.PromptCts?.Dispose();
                }
                _chain.Clear();
                _loadedSrcs.Clear();
                _srcSingleScreen.Clear();
            }

            // UI.ResetForTests 调用:teardown + 清注册表 + 复位 Scheme/Changed。
            internal static void ResetForTestsInternal()
            {
                CancelAllForTeardown();
                _routes.Clear();
                Scheme = null;
                Changed = null;
            }
        }
    }
}
