using System;
using System.Collections.Generic;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Router
        {
            private static readonly Dictionary<string, RouteNode> _routes = new();

            private static readonly List<Func<string, bool>> _guards = new();
            private static bool _bypassGuardsOnce;

            /// <summary>Navigate(url) 校验的 scheme;null = 不校验(接受任意 scheme 或无 scheme)。</summary>
            public static string Scheme { get; set; }

            /// <summary>导航前置守卫:任一返回 false → Open/Navigate/Back 抛 NavigationRejectedException。</summary>
            public static void AddGuard(Func<string, bool> guard)
            {
                if (guard == null) throw new ArgumentNullException(nameof(guard));
                _guards.Add(guard);
            }

            public static void RemoveGuard(Func<string, bool> guard) => _guards.Remove(guard);

            /// <summary>下一次 Open 跳过整条 guard 链并复位(Tutorial 内部导航用)。</summary>
            internal static void BypassGuardsOnce() => _bypassGuardsOnce = true;

            private static void CheckGuards(string name)
            {
                if (_bypassGuardsOnce) { _bypassGuardsOnce = false; return; }
                foreach (var g in _guards)
                    if (!g(name)) throw new NavigationRejectedException(name);
            }

            public static void Map(string name, string src, string screen = null,
                RoutePresent present = RoutePresent.Page, string parent = null,
                Action<IScreen, RouteQuery> onEnter = null)
            {
                if (src == null) throw new RouteException($"route '{name}': src required");
                AddRoute(new RouteNode
                {
                    Name = Req(name),
                    Parent = parent,
                    Kind = present == RoutePresent.Modal ? RouteKind.Modal : RouteKind.Page,
                    Src = src,
                    Screen = screen,
                    OnEnter = onEnter,
                });
            }

            public static void MapTab(string name, string parent, string tabId,
                Action<IScreen, RouteQuery> onEnter = null)
            {
                if (parent == null) throw new RouteException($"tab route '{name}': parent required");
                if (tabId == null) throw new RouteException($"tab route '{name}': tabId required");
                AddRoute(new RouteNode
                {
                    Name = Req(name),
                    Parent = parent,
                    Kind = RouteKind.Tab,
                    TabId = tabId,
                    OnEnter = onEnter,
                });
            }

            public static void MapPrompt(string name, string parent, RoutePromptRun run)
            {
                if (run == null) throw new RouteException($"prompt route '{name}': run required");
                AddRoute(new RouteNode
                {
                    Name = Req(name),
                    Parent = parent,
                    Kind = RouteKind.Prompt,
                    Run = run,
                });
            }

            public static bool IsMapped(string name) => name != null && _routes.ContainsKey(name);

            /// <summary>清空注册表(不动活动链路;teardown 另走 ResetForTestsInternal)。</summary>
            public static void Clear() => _routes.Clear();

            private static string Req(string name) =>
                string.IsNullOrEmpty(name)
                    ? throw new RouteException("route name required")
                    : name;

            private static void AddRoute(RouteNode n)
            {
                if (_routes.ContainsKey(n.Name))
                    throw new RouteException($"route '{n.Name}' already mapped");
                _routes[n.Name] = n;
            }

            /// <summary>沿 Parent 走到根,返回 根→name 的链路。检测缺失/成环/prompt-as-parent。</summary>
            internal static List<RouteNode> ResolveChain(string name)
            {
                var chain = new List<RouteNode>();
                var visited = new HashSet<string>();
                var cur = name;
                while (cur != null)
                {
                    if (!visited.Add(cur))
                        throw new RouteException($"route '{name}': cyclic parent at '{cur}'");
                    if (!_routes.TryGetValue(cur, out var node))
                        throw new RouteException($"route '{cur}' not mapped");
                    chain.Add(node);
                    cur = node.Parent;
                }
                chain.Reverse();
                for (int i = 0; i < chain.Count - 1; i++)
                    if (chain[i].Kind == RouteKind.Prompt)
                        throw new RouteException(
                            $"route '{name}': prompt '{chain[i].Name}' cannot be a parent");
                return chain;
            }
            // <scheme>://<name>?<query>,或无 scheme 的 <name>?<query>。
            // ://(若有)与 ? 之间整段当 name(含斜杠不拆)。
            internal static (string name, RouteQuery query) ParseUrl(string url)
            {
                if (string.IsNullOrEmpty(url)) throw new RouteException("navigate: empty url");
                var rest = url;
                int s = url.IndexOf("://", StringComparison.Ordinal);
                if (s >= 0)
                {
                    var scheme = url.Substring(0, s);
                    if (Scheme != null && !string.Equals(scheme, Scheme, StringComparison.Ordinal))
                        throw new RouteException($"navigate: scheme '{scheme}' != Router.Scheme '{Scheme}'");
                    rest = url.Substring(s + 3);
                }
                else if (Scheme != null)
                    throw new RouteException($"navigate: url '{url}' missing scheme '{Scheme}'");

                int q = rest.IndexOf('?');
                var name = q >= 0 ? rest.Substring(0, q) : rest;
                var qs = q >= 0 ? rest.Substring(q + 1) : "";
                if (name.Length == 0) throw new RouteException($"navigate: url '{url}' has empty route name");
                return (name, RouteQuery.ParseQueryString(qs));
            }
        }
    }
}
