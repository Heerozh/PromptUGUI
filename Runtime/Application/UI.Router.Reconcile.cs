using System;
using System.Collections.Generic;
using PromptUGUI.Application.Modals;
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
                CheckGuards(name);
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
                        try { await Reconcile(t.name, t.query, epoch); }
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

            // reconnect 自检:运行时若有 routed Page/Modal 的 root 被外部销毁(场景重载,未走 Router)而
            // Router 未被告知,_chain 与 _open 脱节 —— reconcile 会静默拿到 null(RefreshTarget 跳过)或
            // 走错早退,表现为界面空白却无报错。这里 fail-fast 带指引,把沉默失效变成可见的 RouteException。
            private static void AssertChainNotStale()
            {
                foreach (var a in _chain)
                {
                    if (a.Def.Kind != RouteKind.Page && a.Def.Kind != RouteKind.Modal) continue;
                    var s = UI.Get(a.ScreenKey);
                    if (s == null || s.RootGameObject == null)
                        throw new RouteException(
                            $"Routed screen '{a.ScreenKey}' was destroyed outside the Router " +
                            "(typically a scene reload / reconnect). The Router's active chain is now " +
                            "stale, so navigation can't reconcile. Call UI.UnloadAll() at your reconnect " +
                            "boundary before re-registering routes and navigating again.");
                }
            }

            private static async Awaitable Reconcile(string name, RouteQuery query, int epoch)
            {
                AssertChainNotStale();
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
                    if (epoch != _epoch)
                    {
                        // 异步加载期间发生了 teardown/Reset:_chain 已被清空,别再塞回陈旧节点。
                        // 关掉这个孤儿(Page/Modal 已 UI.Open),Prompt 释放其 CTS,避免泄漏。
                        if (active.Def.Kind == RouteKind.Page || active.Def.Kind == RouteKind.Modal)
                            UI.Close(active.ScreenKey);
                        else
                            active.PromptCts?.Dispose();
                        return;
                    }
                    _chain.Add(active);
                    if (active.Def.Kind == RouteKind.Prompt) StartPrompt(active, query);
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
                    case RouteKind.Page: return await ActivatePage(def, query, modal: false);
                    case RouteKind.Modal: return await ActivatePage(def, query, modal: true);
                    case RouteKind.Tab: return ActivateTab(def, query);
                    case RouteKind.Prompt: return ActivatePrompt(def);
                    default:
                        throw new RouteException($"route '{def.Name}': kind not yet supported");
                }
            }

            private static void Deactivate(ActiveNode a)
            {
                switch (a.Def.Kind)
                {
                    case RouteKind.Page:
                    case RouteKind.Modal:
                        UI.Close(a.ScreenKey);
                        break;
                    case RouteKind.Prompt:
                        a.Deactivated = true;          // 告诉 SelfPop 让位:reconcile 负责移除
                        a.PromptCts?.Cancel();
                        a.PromptCts?.Dispose();
                        break;
                }
            }

            private static void RefreshTarget(ActiveNode a, RouteQuery query)
            {
                if (a.Def.OnEnter == null) return;
                var screen = UI.Get(a.ScreenKey);
                if (screen != null) a.Def.OnEnter(screen, query);
            }

            // —— Page / Modal ——
            private static async Awaitable<ActiveNode> ActivatePage(
                RouteNode def, RouteQuery query, bool modal)
            {
                var screenName = await EnsureLoaded(def);
                var screen = UI.Open(screenName);
                if (modal)
                {
                    var canvas = screen.RootGameObject.GetComponent<Canvas>();
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = UI.Modal.SortingOrderBase + CountModalsInChain();
                    var esc = screen.RootGameObject.AddComponent<ModalEscapeListener>();
                    var captured = def.Name;
                    esc.OnEscape = () =>
                    {
                        if (UI.Tutorial.IsBlockingInput) return;
                        // 只栈顶 routed modal 响应;有 ad-hoc 模态在上时让位给它
                        if (IsTop(captured) && !UI.Modal.IsAnyOpen) _ = Back();
                    };
                }
                def.OnEnter?.Invoke(screen, query);
                return new ActiveNode { Def = def, ScreenKey = screenName };
            }

            // —— Tab —— 宿主 = 链路里最近的已激活 Page/Modal(此刻已在 _chain 中,因 bottom-up 激活)
            private static ActiveNode ActivateTab(RouteNode def, RouteQuery query)
            {
                var hostKey = ResolveHostScreenKey(def);
                var host = UI.Get(hostKey)
                    ?? throw new RouteException(
                        $"tab route '{def.Name}': host screen '{hostKey}' not open");
                PromptUGUI.Controls.Tab tab;
                try { tab = host.Get<PromptUGUI.Controls.Tab>(def.TabId); }
                catch (Exception ex)
                {
                    throw new RouteException(
                        $"tab route '{def.Name}': tab '{def.TabId}' not found in host '{hostKey}'", ex);
                }
                tab.IsOn = true;
                def.OnEnter?.Invoke(host, query);
                return new ActiveNode { Def = def, ScreenKey = hostKey };
            }

            private static string ResolveHostScreenKey(RouteNode tabDef)
            {
                for (int i = _chain.Count - 1; i >= 0; i--)
                    if (_chain[i].Def.Kind == RouteKind.Page || _chain[i].Def.Kind == RouteKind.Modal)
                        return _chain[i].ScreenKey;
                throw new RouteException(
                    $"tab route '{tabDef.Name}': no Page/Modal host ancestor active");
            }

            // —— Prompt —— 无 src,由 run handler 支撑;入链后由 StartPrompt 起跑。
            private static ActiveNode ActivatePrompt(RouteNode def)
            {
                return new ActiveNode
                {
                    Def = def,
                    PromptCts = new System.Threading.CancellationTokenSource(),
                };
            }

            private static void StartPrompt(ActiveNode node, RouteQuery query)
            {
                _ = RunPrompt(node, query);
            }

            private static async Awaitable RunPrompt(ActiveNode node, RouteQuery query)
            {
                try
                {
                    await node.Def.Run(query, node.PromptCts.Token);
                }
                catch (OperationCanceledException) { /* 被导航走 */ }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"prompt route '{node.Def.Name}' run failed: {ex}");
                }
                finally
                {
                    SelfPop(node);
                }
            }

            // run 正常结束 → 自动出栈(仅当它仍是栈顶且未被 reconcile 接管移除)。
            private static void SelfPop(ActiveNode node)
            {
                if (node.Deactivated) return;   // reconcile 正在移除它
                if (_chain.Count > 0 && _chain[_chain.Count - 1] == node)
                {
                    _chain.RemoveAt(_chain.Count - 1);
                    node.PromptCts?.Dispose();
                    Changed?.Invoke();
                }
            }

            // 当前链路里已有的 Modal 节点数(此节点尚未入链)→ modal 带内的层序偏移。
            private static int CountModalsInChain()
            {
                int n = 0;
                foreach (var a in _chain) if (a.Def.Kind == RouteKind.Modal) n++;
                return n;
            }

            private static bool IsTop(string name) =>
                _chain.Count > 0 && _chain[_chain.Count - 1].Def.Name == name;

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
                    a.Deactivated = true;          // 防止 SelfPop 续体在 Cancel 同步回调里修改 _chain
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
                _guards.Clear();
                _bypassGuardsOnce = false;
                Scheme = null;
                Changed = null;
            }
        }
    }
}
