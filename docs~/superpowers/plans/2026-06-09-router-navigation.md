# Router 导航系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 `UI.Router` 导航子系统——游戏内所有界面打开的统一入口:`Open(name)` 把当前层栈 reconcile 到 name 的标准链路;深链 `Navigate(url)` 与手点同入口、同结果。支持 Page / Modal / Tab / Prompt 四种呈现。

**Architecture:** 稳定不透明名字 + 注册时声明的标准 parent → 一棵导航节点树。`Open` 求目标的根→叶链路,与当前活动链路求最长公共前缀,关掉非共享尾部(top-down)、开缺失尾部(bottom-up)。Page/Modal 复用 `UI.Open`/`UI.Close`(Modal 另把 Canvas 提到 modal sorting 带 + route-aware ESC);Tab 复用现有 `<Tab>.IsOn`;Prompt 由一段 async handler 支撑、自动出栈,靠给 `InputBox`/`MessageBox.Open` 新增的 `CancellationToken` 重载实现"被导航走即撤销"。reconcile 异步串行(epoch + latest-wins),临时 ad-hoc 模态在 reconcile 前清掉。

**Tech Stack:** C# (Unity 6, LangVersion 9)、Unity `Awaitable` / `AwaitableCompletionSource`(禁用 .NET `Task`/`Thread`)、`System.Threading.CancellationToken`、uGUI、R3、NUnit EditMode + PlayMode、UnityMCP 跑测试、`dotnet format` 做 lint。

参考 spec:`docs~/superpowers/specs/2026-06-09-router-navigation-design.md`

---

## File Structure

| 文件 | 动作 | 责任 |
|---|---|---|
| `Runtime/Application/Router/RouteException.cs` | 新建 | `public sealed class RouteException : Exception` |
| `Runtime/Application/Router/RouteQuery.cs` | 新建 | `public sealed class RouteQuery`:`Has`/`Get`/`GetInt`/索引器/`Raw` + 内部 `ParseQueryString` |
| `Runtime/Application/Router/RouteNode.cs` | 新建 | `public enum RoutePresent { Page, Modal }`、`public delegate Awaitable RoutePromptRun(...)`、`internal enum RouteKind`、`internal sealed class RouteNode` POCO |
| `Runtime/Application/UI.Router.cs` | 新建 | `partial class UI { static partial class Router }`:facade(Scheme / Map / MapTab / MapPrompt / IsMapped / Clear)+ 注册表 `_routes` + `ResolveChain` + URL 解析 |
| `Runtime/Application/UI.Router.Reconcile.cs` | 新建 | 同 `partial class Router`:活动链路 `_chain` + `Open`/`Navigate`/`Back`/`Reset` + reconcile + 四种 activate/deactivate + 串行化 + teardown 内部方法 + `Current`/`Chain`/`Changed` |
| `Runtime/Application/AwaitableHelpers.cs` | 修改 | 补非泛型 `Completed()` |
| `Runtime/Application/UI.cs` | 修改 | `UnloadAll`(845)、`ResetForTests`(1006)各加一行 router teardown 钩 |
| `Runtime/Application/UI.Modal.cs` | 修改 | `OpenAsync` 加 `CancellationToken ct = default` + 新 `internal CancelEntry` |
| `Runtime/Application/Modals/InputBoxRequest.cs` | 修改 | `InputBox.Open` 加 `CancellationToken ct = default` 透传 |
| `Runtime/Application/Modals/MessageBoxRequest.cs` | 修改 | `MessageBox.Open` 两重载各加 `CancellationToken ct = default` 透传 |
| `Tests/EditMode/Router/RouteQueryTests.cs` | 新建 | RouteQuery 解析 / getter |
| `Tests/EditMode/Router/RouteRegistryTests.cs` | 新建 | 注册 / 重复 / 缺失 parent / 环 / prompt-as-parent / ResolveChain |
| `Tests/EditMode/Router/RouterReconcileTests.cs` | 新建 | Page 推/弹/跨分支/兄弟、Current/Chain/Changed、query、串行化 |
| `Tests/EditMode/Router/RouterNavigateTests.cs` | 新建 | URL 解析 / Scheme / 临时模态清理 |
| `Tests/EditMode/Router/RouterModalTabTests.cs` | 新建 | Modal 带 + ESC=Back;Tab 选中 / 兄弟切换 / drill-in |
| `Tests/EditMode/Router/RouterPromptTests.cs` | 新建 | Prompt 自动出栈 / 被导航走撤销 / 改名范式 |
| `Tests/EditMode/Modals/ModalCancelTests.cs` | 修改 | 追加 `ct` 取消 OpenAsync 的用例 |
| `Tests/PlayMode/RouterPlayTests.cs` | 新建 | Modal sorting 分带、ESC 实按、Prompt 实弹 |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 修改 | 新增 Router 节 + InputBox/MessageBox 的 `ct` 重载说明 + cheatsheet |

**约定**:每改源码先 `refresh_unity` 再 `read_console` 看编译错误,再 `run_tests`。EditMode 里不存在的类型/成员表现为**编译错误**(本计划的"红"),非 NUnit 失败。新 `.cs` 在 `refresh_unity` 后会生成 `.cs.meta`,commit 时一并 `git add`。Router 不新增 XML 标签 → **不动** `Runtime/Core/Lint/BuiltinTags.cs` / XSD。

---

## Task 1: 公共类型 — RouteException / RouteQuery / RouteNode

**Files:**
- Create: `Runtime/Application/Router/RouteException.cs`
- Create: `Runtime/Application/Router/RouteQuery.cs`
- Create: `Runtime/Application/Router/RouteNode.cs`
- Test: `Tests/EditMode/Router/RouteQueryTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Router/RouteQueryTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouteQueryTests
    {
        [Test]
        public void Empty_HasNothing()
        {
            Assert.IsFalse(RouteQuery.Empty.Has("x"));
            Assert.IsNull(RouteQuery.Empty["x"]);
            Assert.AreEqual("fb", RouteQuery.Empty.Get("x", "fb"));
            Assert.AreEqual(7, RouteQuery.Empty.GetInt("x", 7));
        }

        [Test]
        public void Parse_SplitsPairs_AndUrlDecodes()
        {
            var q = RouteQuery.ParseQueryString("uid=123&name=a%20b&flag=");
            Assert.AreEqual("123", q["uid"]);
            Assert.AreEqual(123, q.GetInt("uid"));
            Assert.AreEqual("a b", q["name"]);
            Assert.IsTrue(q.Has("flag"));
            Assert.AreEqual("", q["flag"]);
        }

        [Test]
        public void Parse_Empty_ReturnsEmptyQuery()
        {
            var q = RouteQuery.ParseQueryString("");
            Assert.IsFalse(q.Has("x"));
        }

        [Test]
        public void GetInt_NonNumeric_ReturnsFallback()
        {
            var q = RouteQuery.ParseQueryString("n=abc");
            Assert.AreEqual(-1, q.GetInt("n", -1));
        }
    }
}
```

- [ ] **Step 2: 刷新并确认红(编译错误)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: `CS0103`/`CS0246` —— `RouteQuery` 不存在。

- [ ] **Step 3: 实现三个类型文件**

新建 `Runtime/Application/Router/RouteException.cs`:

```csharp
using System;

namespace PromptUGUI.Application
{
    /// <summary>Router 注册 / 导航期的错误(未注册、缺失/成环 parent、tab 解析失败等)。</summary>
    public sealed class RouteException : Exception
    {
        public RouteException(string message) : base(message) { }
        public RouteException(string message, Exception inner) : base(message, inner) { }
    }
}
```

新建 `Runtime/Application/Router/RouteQuery.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PromptUGUI.Application
{
    /// <summary>路由查询参数的只读包装。从 URL 的 ?k=v&... 或 Open 传入的字典构造。</summary>
    public sealed class RouteQuery
    {
        public static readonly RouteQuery Empty = new(new Dictionary<string, string>(0));

        private readonly IReadOnlyDictionary<string, string> _q;

        public RouteQuery(IReadOnlyDictionary<string, string> query)
            => _q = query ?? new Dictionary<string, string>(0);

        public bool Has(string key) => _q.ContainsKey(key);

        public string Get(string key, string fallback = null)
            => _q.TryGetValue(key, out var v) ? v : fallback;

        public int GetInt(string key, int fallback = 0)
            => _q.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

        public string this[string key] => Get(key);

        public IReadOnlyDictionary<string, string> Raw => _q;

        /// <summary>解析 "k=v&k2=v2"(无前导 '?')。空串 → Empty。k/v 各做 URL decode。</summary>
        internal static RouteQuery ParseQueryString(string qs)
        {
            if (string.IsNullOrEmpty(qs)) return Empty;
            var d = new Dictionary<string, string>();
            foreach (var pair in qs.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                var k = eq >= 0 ? pair.Substring(0, eq) : pair;
                var v = eq >= 0 ? pair.Substring(eq + 1) : "";
                if (k.Length == 0) continue;
                d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }
            return new RouteQuery(d);
        }
    }
}
```

新建 `Runtime/Application/Router/RouteNode.cs`:

```csharp
using System;

namespace PromptUGUI.Application
{
    /// <summary>screen-backed 节点的呈现方式。Tab/Prompt 由 MapTab/MapPrompt 表达,不在此枚举。</summary>
    public enum RoutePresent { Page, Modal }

    /// <summary>Prompt 节点的支撑 handler:跑内置对话框 + 处理结果。ct 取消 = 被导航走。</summary>
    public delegate UnityEngine.Awaitable RoutePromptRun(
        RouteQuery query, System.Threading.CancellationToken ct);

    internal enum RouteKind { Page, Modal, Tab, Prompt }

    /// <summary>一个路由节点的注册记录(运行时,非 XML IR)。</summary>
    internal sealed class RouteNode
    {
        public string Name;                      // 稳定不透明 ID,全局唯一
        public string Parent;                    // null = 根
        public RouteKind Kind;
        public string Src;                       // Page/Modal:.ui.xml 的 src key
        public string Screen;                    // Page/Modal:screen 名;null → 激活时按"单 Screen"解析
        public string TabId;                     // Tab:宿主 screen 内 <Tab> 控件的 id 路径
        public RoutePromptRun Run;               // Prompt
        public Action<IScreen, RouteQuery> OnEnter;  // Page/Modal/Tab
    }
}
```

- [ ] **Step 4: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouteQueryTests")
```
Expected: 无编译错误;`RouteQueryTests` 4 条全绿。

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 退出码 0。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Router/RouteException.cs Runtime/Application/Router/RouteException.cs.meta \
        Runtime/Application/Router/RouteQuery.cs Runtime/Application/Router/RouteQuery.cs.meta \
        Runtime/Application/Router/RouteNode.cs Runtime/Application/Router/RouteNode.cs.meta \
        Tests/EditMode/Router/RouteQueryTests.cs Tests/EditMode/Router/RouteQueryTests.cs.meta
git commit -m "feat(router): RouteException / RouteQuery / RouteNode types"
```

---

## Task 2: 注册表 + ResolveChain + 校验

**Files:**
- Create: `Runtime/Application/UI.Router.cs`
- Test: `Tests/EditMode/Router/RouteRegistryTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Router/RouteRegistryTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouteRegistryTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Map_ThenIsMapped()
        {
            UI.Router.Map("home", "Screens/Home");
            Assert.IsTrue(UI.Router.IsMapped("home"));
            Assert.IsFalse(UI.Router.IsMapped("nope"));
        }

        [Test]
        public void Map_Duplicate_Throws()
        {
            UI.Router.Map("home", "Screens/Home");
            Assert.Throws<RouteException>(() => UI.Router.Map("home", "Screens/Other"));
        }

        [Test]
        public void Map_NullSrc_Throws()
            => Assert.Throws<RouteException>(() => UI.Router.Map("home", null));

        [Test]
        public void ResolveChain_RootToLeaf_InOrder()
        {
            UI.Router.Map("home", "S/Home");
            UI.Router.Map("shop", "S/Shop", parent: "home");
            UI.Router.MapTab("shop/deals", parent: "shop", tabId: "bar/deals");
            var names = UI.Router.ResolveChain("shop/deals").Select(n => n.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "home", "shop", "shop/deals" }, names);
        }

        [Test]
        public void ResolveChain_UnmappedParent_Throws()
        {
            UI.Router.Map("shop", "S/Shop", parent: "ghost");
            Assert.Throws<RouteException>(() => UI.Router.ResolveChain("shop"));
        }

        [Test]
        public void ResolveChain_Cycle_Throws()
        {
            UI.Router.Map("a", "S/A", parent: "b");
            UI.Router.Map("b", "S/B", parent: "a");
            Assert.Throws<RouteException>(() => UI.Router.ResolveChain("a"));
        }

        [Test]
        public void ResolveChain_PromptAsParent_Throws()
        {
            UI.Router.Map("home", "S/Home");
            UI.Router.MapPrompt("ask", parent: "home", run: (q, ct) => null);
            UI.Router.Map("deep", "S/Deep", parent: "ask");
            Assert.Throws<RouteException>(() => UI.Router.ResolveChain("deep"));
        }

        [Test]
        public void Open_Unmapped_Throws()
            => Assert.ThrowsAsync<RouteException>(async () => await UI.Router.Open("ghost"));
    }
}
```

> 注:`Open_Unmapped_Throws` 用 `ThrowsAsync` —— `Open` 在 Task 3 实现;本步它编译不过(红的一部分),Task 3 才转绿。先写在这里以免遗漏。

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: `UI.Router` 不存在(`CS0117`/`CS0246`)。

- [ ] **Step 3: 实现注册 facade + ResolveChain**

新建 `Runtime/Application/UI.Router.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Router
        {
            private static readonly Dictionary<string, RouteNode> _routes = new();

            /// <summary>Navigate(url) 校验的 scheme;null = 不校验(接受任意 scheme 或无 scheme)。</summary>
            public static string Scheme { get; set; }

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
                    Name = Req(name), Parent = parent, Kind = RouteKind.Tab,
                    TabId = tabId, OnEnter = onEnter,
                });
            }

            public static void MapPrompt(string name, string parent, RoutePromptRun run)
            {
                if (run == null) throw new RouteException($"prompt route '{name}': run required");
                AddRoute(new RouteNode
                {
                    Name = Req(name), Parent = parent, Kind = RouteKind.Prompt, Run = run,
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
        }
    }
}
```

- [ ] **Step 4: 刷新 + 确认 registry 测试绿(Open_Unmapped 仍红)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 仍有 1 个编译错误 —— `UI.Router.Open` 不存在(来自 `Open_Unmapped_Throws`)。这是预期的;Task 3 实现 `Open` 后整文件转绿。**本步不跑 run_tests**(编译未过)。

- [ ] **Step 5: Commit(WIP,留到 Task 3 一起绿)**

```bash
git add Runtime/Application/UI.Router.cs Runtime/Application/UI.Router.cs.meta \
        Tests/EditMode/Router/RouteRegistryTests.cs Tests/EditMode/Router/RouteRegistryTests.cs.meta
git commit -m "feat(router): registration table + ResolveChain validation"
```

---

## Task 3: reconcile 核心 + Page 呈现 + 链路状态

**Files:**
- Create: `Runtime/Application/UI.Router.Reconcile.cs`
- Test: `Tests/EditMode/Router/RouterReconcileTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Router/RouterReconcileTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouterReconcileTests
    {
        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='bg' anchor='stretch'/>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = Xml("home"),
                ["shop"] = Xml("shop"),
                ["battle"] = Xml("battle"),
                ["item"] = Xml("item"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            // src == screen name 简化:每个 src 内 <Screen name> 同名
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.Map("battle", "battle", parent: "home");
            UI.Router.Map("item", "item", parent: "shop");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static List<string> Chain() => UI.Router.Chain.ToList();

        [Test]
        public void Open_BuildsCanonicalChainFromRoot()
        {
            UI.Router.Open("shop").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop" }, Chain());
            Assert.AreEqual("shop", UI.Router.Current);
            Assert.IsNotNull(UI.Get("home"));
            Assert.IsNotNull(UI.Get("shop"));
        }

        [Test]
        public void Open_Child_PushesOnly()
        {
            UI.Router.Open("shop").GetAwaiter().GetResult();
            UI.Router.Open("item").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop", "item" }, Chain());
        }

        [Test]
        public void Open_Ancestor_PopsToIt()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();
            UI.Router.Open("shop").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop" }, Chain());
            Assert.IsNull(UI.Get("item"));   // item 屏被销毁
        }

        [Test]
        public void Open_SiblingBranch_ClosesToCommonAncestor()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();   // home/shop/item
            UI.Router.Open("battle").GetAwaiter().GetResult(); // home/battle
            CollectionAssert.AreEqual(new[] { "home", "battle" }, Chain());
            Assert.IsNull(UI.Get("shop"));
            Assert.IsNull(UI.Get("item"));
            Assert.IsNotNull(UI.Get("home"));  // 公共前缀 home 不重建
        }

        [Test]
        public void OnEnter_ReceivesQuery_OnTargetAndNewIntermediates()
        {
            var seen = new Dictionary<string, string>();
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = Xml("home"), ["shop"] = Xml("shop"), ["item"] = Xml("item"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home",
                onEnter: (s, q) => seen["shop"] = q["k"]);
            UI.Router.Map("item", "item", parent: "shop",
                onEnter: (s, q) => seen["item"] = q["k"]);

            var q = new RouteQuery(new Dictionary<string, string> { ["k"] = "v" });
            UI.Router.Open("item", q).GetAwaiter().GetResult();

            Assert.AreEqual("v", seen["shop"]);   // 新激活中间节点也收到
            Assert.AreEqual("v", seen["item"]);   // 目标收到
        }

        [Test]
        public void ReNavigate_SameTarget_RefreshesOnEnter_NoRebuild()
        {
            int count = 0;
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["home"] = Xml("home") };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home", onEnter: (s, q) => count++);

            UI.Router.Open("home").GetAwaiter().GetResult();
            var first = UI.Get("home");
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.AreEqual(2, count);                 // OnEnter 再触发
            Assert.AreSame(first, UI.Get("home"));     // 同一 Screen,未重建
        }

        [Test]
        public void Changed_FiresOnNavigation()
        {
            int fired = 0;
            UI.Router.Changed += () => fired++;
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.GreaterOrEqual(fired, 1);
        }
    }
}
```

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: `UI.Router.Open` / `Chain` / `Current` / `Changed` 不存在。

- [ ] **Step 3: 实现 reconcile + Page(单飞版,串行化留 Task 4)**

新建 `Runtime/Application/UI.Router.Reconcile.cs`:

```csharp
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

            public static async Awaitable Open(string name, RouteQuery query = null)
            {
                await Reconcile(name, query ?? RouteQuery.Empty);
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
        }
    }
}
```

- [ ] **Step 4: 刷新并确认绿(含 Task 2 的 Open_Unmapped)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterReconcileTests")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouteRegistryTests")
```
Expected: 两个测试类全绿(`RouteRegistryTests` 含 `Open_Unmapped_Throws` 现已可编译并通过)。

- [ ] **Step 5: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Router.Reconcile.cs Runtime/Application/UI.Router.Reconcile.cs.meta \
        Tests/EditMode/Router/RouterReconcileTests.cs Tests/EditMode/Router/RouterReconcileTests.cs.meta
git commit -m "feat(router): reconcile core + Page presentation + Current/Chain/Changed"
```

---

## Task 4: 串行化(epoch + latest-wins) + teardown 接线

**Files:**
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(替换 `Open` + 加 pump/teardown)
- Modify: `Runtime/Application/UI.cs:845`(`UnloadAll`)、`:1006`(`ResetForTests`)
- Test: `Tests/EditMode/Router/RouterReconcileTests.cs`(追加)

- [ ] **Step 1: 写失败测试**

在 `RouterReconcileTests` 类末尾(最后一个 `}` 前)追加:

```csharp
        [Test]
        public void ConcurrentOpen_LatestWins_BothAwaitablesComplete()
        {
            // 不 await 第一个,立刻发第二个 —— 最终落到 battle。
            var a = UI.Router.Open("item");
            var b = UI.Router.Open("battle");
            b.GetAwaiter().GetResult();
            a.GetAwaiter().GetResult();   // 被取代的也应完成,不挂死
            CollectionAssert.AreEqual(new[] { "home", "battle" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void ResetForTests_ClearsChain()
        {
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.IsNotEmpty(UI.Router.Chain);
            UI.ResetForTests();
            Assert.IsEmpty(UI.Router.Chain);
            Assert.IsNull(UI.Router.Current);
        }
```

> EditMode 下 fake resolver 经 `AwaitableHelpers.Completed` 同步完成,`Open` 内无真实 yield,因此 `a`/`b` 实际同步推进;此测试主要锁"被取代请求的 Awaitable 不挂死"与 latest-wins 的最终态。

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterReconcileTests")
```
Expected:`ResetForTests_ClearsChain` 红(`ResetForTests` 尚未清链路,链路仍有残留 → 断言失败或后续测试串扰)。

- [ ] **Step 3: 用串行化版替换 `Open`,并加 pump + teardown 方法**

把 `UI.Router.Reconcile.cs` 里的:

```csharp
            public static async Awaitable Open(string name, RouteQuery query = null)
            {
                await Reconcile(name, query ?? RouteQuery.Empty);
            }
```

替换为:

```csharp
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

            // UI.UnloadAll 调用:取消 pump + prompt,清链路;链路屏在 _open 里由 UnloadAll 的
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
```

- [ ] **Step 4: 在 `UI.cs` 接线 teardown**

在 `Runtime/Application/UI.cs` 的 `UnloadAll()`(约 845 行)里,把:

```csharp
        public static void UnloadAll()
        {
            Modal.CancelAllForTeardown();
```

改为:

```csharp
        public static void UnloadAll()
        {
            Router.CancelAllForTeardown();
            Modal.CancelAllForTeardown();
```

在 `ResetForTests()`(约 1006 行)里,把:

```csharp
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Toasts.ToastOverlay.CancelAllForTeardown();
```

改为:

```csharp
            Router.ResetForTestsInternal();
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Toasts.ToastOverlay.CancelAllForTeardown();
```

> `Router.ResetForTestsInternal` 放在 `_open` 销毁循环之前:它只取消 prompt/清 router 链路,不关 screen;screen 由其后的 `foreach (_open) s.Close()` 统一关。

- [ ] **Step 5: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterReconcileTests")
```
Expected: `RouterReconcileTests` 全绿(含新 2 条)。

- [ ] **Step 6: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Router.Reconcile.cs Runtime/Application/UI.cs \
        Tests/EditMode/Router/RouterReconcileTests.cs
git commit -m "feat(router): serialize reconcile (epoch + latest-wins) + teardown wiring"
```

---

## Task 5: Back + Reset(+ 非泛型 AwaitableHelpers.Completed)

**Files:**
- Modify: `Runtime/Application/AwaitableHelpers.cs`(加非泛型 `Completed()`)
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(加 `Back`/`Reset`)
- Test: `Tests/EditMode/Router/RouterReconcileTests.cs`(追加)

- [ ] **Step 1: 写失败测试**

在 `RouterReconcileTests` 末尾追加:

```csharp
        [Test]
        public void Back_GoesToParent()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();   // home/shop/item
            UI.Router.Back().GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Back_AtRoot_IsNoop()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            UI.Router.Back().GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Back_EmptyChain_IsNoop()
            => Assert.DoesNotThrow(() => UI.Router.Back().GetAwaiter().GetResult());

        [Test]
        public void Reset_ClosesWholeChain()
        {
            UI.Router.Open("item").GetAwaiter().GetResult();
            UI.Router.Reset().GetAwaiter().GetResult();
            Assert.IsEmpty(UI.Router.Chain.ToList());
            Assert.IsNull(UI.Get("home"));
            Assert.IsNull(UI.Get("shop"));
            Assert.IsNull(UI.Get("item"));
        }
```

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: `UI.Router.Back` / `Reset` 不存在。

- [ ] **Step 3: 加非泛型 `Completed()`**

在 `Runtime/Application/AwaitableHelpers.cs` 的 `Completed<T>` 之上插入:

```csharp
        internal static Awaitable Completed()
        {
            var src = new AwaitableCompletionSource();
            src.SetResult();
            return src.Awaitable;
        }

```

- [ ] **Step 4: 实现 `Back` / `Reset`**

在 `UI.Router.Reconcile.cs` 的 `Current` 属性下方插入:

```csharp
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
```

- [ ] **Step 5: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterReconcileTests")
```
Expected: 全绿(含新 4 条)。

- [ ] **Step 6: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/AwaitableHelpers.cs Runtime/Application/UI.Router.Reconcile.cs \
        Tests/EditMode/Router/RouterReconcileTests.cs
git commit -m "feat(router): Back() + Reset() + non-generic AwaitableHelpers.Completed"
```

---

## Task 6: Navigate(URL 解析 + Scheme) + 临时模态清理(§3.3)

**Files:**
- Modify: `Runtime/Application/UI.Router.cs`(加 `ParseUrl`)
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(加 `Navigate`;reconcile 头部加 §3.3)
- Test: `Tests/EditMode/Router/RouterNavigateTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Router/RouterNavigateTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Router
{
    public class RouterNavigateTests
    {
        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Btn id='ok'>OK</Btn>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = Xml("home"), ["profile"] = Xml("profile"), ["mbox"] = Xml("mbox"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("profile", "profile", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Navigate_ParsesNameAndQuery()
        {
            string seen = null;
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["home"] = Xml("home"), ["profile"] = Xml("profile") };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("profile", "profile", parent: "home", onEnter: (s, q) => seen = q["uid"]);

            UI.Router.Navigate("appid://profile?uid=123").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "profile" }, UI.Router.Chain.ToList());
            Assert.AreEqual("123", seen);
        }

        [Test]
        public void Navigate_SlashInName_NotSplitAsHierarchy()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["home"] = Xml("home"), ["f"] = Xml("f") };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("home/friend", "f", parent: "home");   // 名字含斜杠,但只是名字

            UI.Router.Navigate("appid://home/friend").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "home/friend" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Navigate_SchemeMismatch_Throws()
        {
            UI.Router.Scheme = "appid";
            Assert.ThrowsAsync<RouteException>(
                async () => await UI.Router.Navigate("other://profile"));
        }

        [Test]
        public void Navigate_NoSchemeConfigured_AcceptsAny()
        {
            UI.Router.Navigate("whatever://home").GetAwaiter().GetResult();
            Assert.AreEqual("home", UI.Router.Current);
        }

        [Test]
        public void Reconcile_ClosesAdHocModalFirst()
        {
            MessageBox.XmlSrc = "mbox";
            UI.Router.Open("home").GetAwaiter().GetResult();
            // 起一个 ad-hoc 模态(不进 router)
            _ = MessageBox.Open("hi");
            Assert.IsTrue(UI.Modal.IsAnyOpen);
            // 导航 → 先关掉它
            UI.Router.Open("profile").GetAwaiter().GetResult();
            Assert.IsFalse(UI.Modal.IsAnyOpen);
            CollectionAssert.AreEqual(new[] { "home", "profile" }, UI.Router.Chain.ToList());
        }
    }
}
```

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: `UI.Router.Navigate` 不存在。

- [ ] **Step 3: 加 `ParseUrl`(UI.Router.cs)**

在 `UI.Router.cs` 的 `ResolveChain` 方法下方(类内最后一个 `}` 前)插入:

```csharp
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
```

- [ ] **Step 4: 加 `Navigate` + reconcile 头部清临时模态**

在 `UI.Router.Reconcile.cs` 的 `Open` 方法下方插入:

```csharp
            public static Awaitable Navigate(string url)
            {
                var (name, query) = ParseUrl(url);   // 同步抛 RouteException(scheme/格式)
                return Open(name, query);
            }
```

> 注:`Navigate` 让 `ParseUrl` 的同步异常通过返回的 `Awaitable` 抛给 `await`(`async Awaitable` 方法体里调用即可)。把上面改成:
> ```csharp
>             public static async Awaitable Navigate(string url)
>             {
>                 var (name, query) = ParseUrl(url);
>                 await Open(name, query);
>             }
> ```
> 这样 `ThrowsAsync<RouteException>` 能捕获 scheme 不符。

在 `Reconcile` 方法体里(Task 3 的健壮形态),把"移除尾部 → deactivate"之间插入 §3.3。即把:

```csharp
                if (removed.Count > 0) _chain.RemoveRange(k, removed.Count);

                for (int i = removed.Count - 1; i >= 0; i--) Deactivate(removed[i]);
```

改为:

```csharp
                if (removed.Count > 0) _chain.RemoveRange(k, removed.Count);

                // §3.3:顶上压着的 ad-hoc 临时模态(不属 router)先关掉 —— 等价"用户先关弹窗再导航"。
                // 尾部已先于此移除,故即便 CloseAll 同步唤醒某 Prompt 续体,其 SelfPop 也已 no-op。
                // SameChain 早退路径不经过这里:重复导航到正在显示的同一链路不会误关其对话框。
                if (UI.Modal.IsAnyOpen) UI.Modal.CloseAll();

                for (int i = removed.Count - 1; i >= 0; i--) Deactivate(removed[i]);
```

- [ ] **Step 5: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterNavigateTests")
```
Expected: `RouterNavigateTests` 5 条全绿。

- [ ] **Step 6: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Router.cs Runtime/Application/UI.Router.Reconcile.cs \
        Tests/EditMode/Router/RouterNavigateTests.cs Tests/EditMode/Router/RouterNavigateTests.cs.meta
git commit -m "feat(router): Navigate(url) + scheme + close ad-hoc modals on reconcile"
```

---

## Task 7: Modal 呈现(modal sorting 带 + route-aware ESC=Back)

**Files:**
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(`Activate`/`Deactivate` 支持 Modal)
- Test: `Tests/EditMode/Router/RouterModalTabTests.cs`(新建,本任务先写 Modal 部分)

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Router/RouterModalTabTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    public class RouterModalTabTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        private static string ModalXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='panel' anchor='center' size='300x200'/>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["settings"] = ModalXml("settings"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Modal_Activates_OnModalSortingBand()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "settings" }, UI.Router.Chain.ToList());
            var canvas = UI.Get("settings").RootGameObject.GetComponent<Canvas>();
            Assert.IsTrue(canvas.overrideSorting);
            Assert.GreaterOrEqual(canvas.sortingOrder, UI.Modal.SortingOrderBase);
        }

        [Test]
        public void Modal_Esc_GoesBackToParent()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            var esc = UI.Get("settings").RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(esc);
            esc.FireForTests();
            // ESC = Back → 回到 home
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
            Assert.IsNull(UI.Get("settings"));
        }

        [Test]
        public void Modal_Deactivate_ClosesScreen()
        {
            UI.Router.Open("settings").GetAwaiter().GetResult();
            UI.Router.Open("home").GetAwaiter().GetResult();
            Assert.IsNull(UI.Get("settings"));
        }
    }
}
```

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterModalTabTests")
```
Expected: `Activate` 抛 "kind not yet supported"(Modal 分支未实现)→ `Modal_*` 三条红。

- [ ] **Step 3: 实现 Modal 激活**

在 `UI.Router.Reconcile.cs` 顶部 `using` 区确保有 `using PromptUGUI.Application.Modals;`(没有则加)。

把 `Activate` 的 switch 改为:

```csharp
            private static async Awaitable<ActiveNode> Activate(RouteNode def, RouteQuery query)
            {
                switch (def.Kind)
                {
                    case RouteKind.Page: return await ActivatePage(def, query, modal: false);
                    case RouteKind.Modal: return await ActivatePage(def, query, modal: true);
                    default:
                        throw new RouteException($"route '{def.Name}': kind not yet supported");
                }
            }
```

把 `ActivatePage` 改为带 `modal` 参数:

```csharp
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
                        // 只栈顶 routed modal 响应;有 ad-hoc 模态在上时让位给它(§12)
                        if (IsTop(captured) && !UI.Modal.IsAnyOpen) _ = Back();
                    };
                }
                def.OnEnter?.Invoke(screen, query);
                return new ActiveNode { Def = def, ScreenKey = screenName };
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
```

把 `Deactivate` 的 Page 分支扩成 Page+Modal:

```csharp
            private static void Deactivate(ActiveNode a)
            {
                switch (a.Def.Kind)
                {
                    case RouteKind.Page:
                    case RouteKind.Modal:
                        UI.Close(a.ScreenKey);
                        break;
                }
            }
```

- [ ] **Step 4: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterModalTabTests")
```
Expected: `Modal_*` 三条绿。

- [ ] **Step 5: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Router.Reconcile.cs \
        Tests/EditMode/Router/RouterModalTabTests.cs Tests/EditMode/Router/RouterModalTabTests.cs.meta
git commit -m "feat(router): Modal presentation (sorting band + route-aware ESC=Back)"
```

---

## Task 8: Tab 呈现(宿主解析 + IsOn 选中 + 兄弟切换不重建)

**Files:**
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(`Activate` 支持 Tab + 宿主解析)
- Test: `Tests/EditMode/Router/RouterModalTabTests.cs`(追加 Tab 部分)

- [ ] **Step 1: 写失败测试**

在 `RouterModalTabTests` 类末尾追加(并在文件顶部补 `using PromptUGUI.Controls;` 若无):

```csharp
        private const string ShopWithTabsXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='shop'>
  <TabBar id='bar' anchor='stretch'>
    <Tab id='deals'>Deals</Tab>
    <Tab id='cart'>Cart</Tab>
  </TabBar>
</Screen></PromptUGUI>";

        private void SetUpShopTabs()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["shop"] = ShopWithTabsXml,
                ["item"] = PageXml("item"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
            UI.Router.MapTab("shop/deals", parent: "shop", tabId: "bar/deals");
            UI.Router.MapTab("shop/cart", parent: "shop", tabId: "bar/cart");
            UI.Router.Map("item", "item", parent: "shop/deals");   // drill-in 自某个 tab
        }

        [Test]
        public void Tab_Activate_SelectsTab_HostOpened()
        {
            SetUpShopTabs();
            UI.Router.Open("shop/deals").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "shop", "shop/deals" }, UI.Router.Chain.ToList());
            var deals = UI.Get("shop").Get<PromptUGUI.Controls.Tab>("bar/deals");
            Assert.IsTrue(deals.IsOn);
        }

        [Test]
        public void Tab_SiblingSwitch_DoesNotRebuildHost()
        {
            SetUpShopTabs();
            UI.Router.Open("shop/deals").GetAwaiter().GetResult();
            var hostBefore = UI.Get("shop");
            UI.Router.Open("shop/cart").GetAwaiter().GetResult();
            Assert.AreSame(hostBefore, UI.Get("shop"));    // 宿主未重建
            Assert.IsTrue(UI.Get("shop").Get<PromptUGUI.Controls.Tab>("bar/cart").IsOn);
            CollectionAssert.AreEqual(new[] { "home", "shop", "shop/cart" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Tab_DrillIn_FullChainReconciles()
        {
            SetUpShopTabs();
            UI.Router.Open("item").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(
                new[] { "home", "shop", "shop/deals", "item" }, UI.Router.Chain.ToList());
            Assert.IsTrue(UI.Get("shop").Get<PromptUGUI.Controls.Tab>("bar/deals").IsOn);
            Assert.IsNotNull(UI.Get("item"));
        }
```

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterModalTabTests")
```
Expected: `Tab_*` 三条红("kind not yet supported")。

- [ ] **Step 3: 实现 Tab 激活 + 宿主解析**

把 `Activate` 的 switch 加上 Tab 分支:

```csharp
            private static async Awaitable<ActiveNode> Activate(RouteNode def, RouteQuery query)
            {
                switch (def.Kind)
                {
                    case RouteKind.Page: return await ActivatePage(def, query, modal: false);
                    case RouteKind.Modal: return await ActivatePage(def, query, modal: true);
                    case RouteKind.Tab: return ActivateTab(def, query);
                    default:
                        throw new RouteException($"route '{def.Name}': kind not yet supported");
                }
            }
```

在 `ActivatePage` 下方加:

```csharp
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
```

> Tab 的 `Deactivate` 无需新分支:宿主仍在屏时 TabBar 永远有选中态(`SyncInitialSelection`),Tab 节点出栈不强改控件;宿主整屏关闭则 tab 随之消失(spec §5.3)。

- [ ] **Step 4: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterModalTabTests")
```
Expected: `RouterModalTabTests` 全绿(Modal 3 + Tab 3)。

- [ ] **Step 5: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Router.Reconcile.cs Tests/EditMode/Router/RouterModalTabTests.cs
git commit -m "feat(router): Tab presentation (host resolve + IsOn select + sibling/drill-in)"
```

---

## Task 9: InputBox/MessageBox 加 CancellationToken + UI.Modal.CancelEntry

**Files:**
- Modify: `Runtime/Application/UI.Modal.cs`(`OpenAsync` 加 `ct` + 新 `CancelEntry`)
- Modify: `Runtime/Application/Modals/InputBoxRequest.cs`(`InputBox.Open` 加 `ct`)
- Modify: `Runtime/Application/Modals/MessageBoxRequest.cs`(两 `Open` 加 `ct`)
- Test: `Tests/EditMode/Modals/ModalCancelTests.cs`(追加)

- [ ] **Step 1: 写失败测试**

在 `Tests/EditMode/Modals/ModalCancelTests.cs` 的类末尾追加(若类名/命名空间不同,按文件实际为准):

```csharp
        [Test]
        public void OpenAsync_CancellationToken_CancelsAndClosesModal()
        {
            var cts = new System.Threading.CancellationTokenSource();
            var box = MessageBox.Open("hi", MsgBtn.OK, ct: cts.Token);
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            cts.Cancel();

            Assert.IsFalse(UI.Modal.IsAnyOpen);   // modal 被关
            Assert.ThrowsAsync<System.OperationCanceledException>(async () => await box);
            cts.Dispose();
        }
```

> 该文件已有 `using PromptUGUI.Application;` / `using PromptUGUI.Application.Modals;` 与 `MessageBox.XmlSrc` 的 fake-files 设置(沿用其既有 `[SetUp]`)。若 `ModalCancelTests` 未设 `MessageBox.XmlSrc`,在该测试开头加 `MessageBox.XmlSrc = "test/Box1";` 并确保 `UI.SourceResolver` 提供该 src(参照 `ModalTestFixture`)。

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: `MessageBox.Open` 无 `ct` 命名参数(`CS1739`/`CS1503`)。

- [ ] **Step 3: `UI.Modal.OpenAsync` 加 `ct` + `CancelEntry`**

在 `Runtime/Application/UI.Modal.cs`,把:

```csharp
            public static Awaitable<TResult> OpenAsync<TResult>(
                ModalRequest<TResult> request, ModalMode mode = ModalMode.Popup)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                var (entry, awaitable) = ModalEntry<TResult>.Create(request);
                if (mode == ModalMode.Queued && !IsIdle())
                    _waiting.Enqueue(entry);
                else
                    QueueForMaterialize(entry);
                return awaitable;
            }
```

替换为:

```csharp
            public static Awaitable<TResult> OpenAsync<TResult>(
                ModalRequest<TResult> request, ModalMode mode = ModalMode.Popup,
                System.Threading.CancellationToken ct = default)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                var (entry, awaitable) = ModalEntry<TResult>.Create(request);
                if (ct.CanBeCanceled)
                    ct.Register(() => CancelEntry(entry, new OperationCanceledException(ct)));
                if (mode == ModalMode.Queued && !IsIdle())
                    _waiting.Enqueue(entry);
                else
                    QueueForMaterialize(entry);
                return awaitable;
            }

            // 取消某个 entry,不论它在 stack / pending / inflight / waiting 哪一态。
            // 在 stack 上 → 同 close:resolve + 销毁 Screen + 弹栈 + 提升等待队列。
            // 其余态 → 仅 Cancel(标记 Resolved);pump 与 PromoteWaiting 出队时跳过 Resolved。
            internal static void CancelEntry(IModalEntry entry, Exception ex)
            {
                for (int i = 0; i < _stack.Count; i++)
                {
                    if (_stack[i].Entry == entry)
                    {
                        var slot = _stack[i];
                        entry.Cancel(ex);
                        RemoveSlot(slot);
                        RefreshTopListener();
                        PromoteWaiting();
                        return;
                    }
                }
                entry.Cancel(ex);
            }
```

- [ ] **Step 4: `InputBox.Open` / `MessageBox.Open` 透传 `ct`**

`InputBoxRequest.cs`:把 `InputBox.Open` 的签名与调用改为带 `ct`:

```csharp
        public static UnityEngine.Awaitable<string> Open(
            string title,
            string message = null,
            string initial = null,
            string placeholder = null,
            string contentType = null,
            string okLabel = null,
            string cancelLabel = null,
            ModalMode mode = ModalMode.Popup,
            System.Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default)
            => UI.Modal.OpenAsync(new InputBoxRequest
            {
                Title = title,
                Message = message,
                Initial = initial,
                Placeholder = placeholder,
                ContentType = contentType,
                OkLabel = okLabel,
                CancelLabel = cancelLabel,
                Configure = configure,
            }, mode, ct);
```

`MessageBoxRequest.cs`:两个 `Open` 各加 `ct` 并传给 `OpenAsync`:

第一个重载——签名末尾加 `System.Threading.CancellationToken ct = default`,调用末尾 `}, mode)` 改 `}, mode, ct)`。
第二个重载——同样在签名末尾加 `System.Threading.CancellationToken ct = default`,`return UI.Modal.OpenAsync(... }, mode);` 改为 `}, mode, ct);`。

- [ ] **Step 5: 刷新并确认绿(含既有 Modal 测试不回归)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalCancelTests")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Modal")
```
Expected: 新用例绿;`Modal*` 全部不回归。

- [ ] **Step 6: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Modal.cs Runtime/Application/Modals/InputBoxRequest.cs \
        Runtime/Application/Modals/MessageBoxRequest.cs Tests/EditMode/Modals/ModalCancelTests.cs
git commit -m "feat(modal): CancellationToken overload on Open + UI.Modal.CancelEntry"
```

---

## Task 10: Prompt 呈现(MapPrompt + 自动出栈 + 被导航走撤销)

**Files:**
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(`Activate`/`Deactivate` 支持 Prompt + RunPrompt/SelfPop)
- Test: `Tests/EditMode/Router/RouterPromptTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Router/RouterPromptTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Router
{
    public class RouterPromptTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"), ["shop"] = PageXml("shop"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Prompt_RunCompletes_AutoPops()
        {
            bool ran = false;
            UI.Router.MapPrompt("ask", parent: "home", run: (q, ct) =>
            {
                ran = true;
                return AwaitableHelpers.Completed();   // 立即完成
            });

            UI.Router.Open("ask").GetAwaiter().GetResult();

            Assert.IsTrue(ran);
            // run 同步完成 → 自动出栈,回到 home
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Prompt_ReceivesQuery()
        {
            string seen = null;
            UI.Router.MapPrompt("ask", parent: "home", run: (q, ct) =>
            {
                seen = q["reason"];
                return AwaitableHelpers.Completed();
            });
            UI.Router.Navigate("appid://ask?reason=illegal").GetAwaiter().GetResult();
            Assert.AreEqual("illegal", seen);
        }

        [Test]
        public void Prompt_NavigatedAway_Cancels()
        {
            // run 永不自完成 → 靠 ct 取消。canceled 在 ct.Register 回调里置位:
            // cts.Cancel() 同步触发该回调,不依赖 await 续体的恢复时机(EditMode 无 PlayerLoop)。
            bool canceled = false;
            UI.Router.MapPrompt("ask", parent: "home", run: async (q, ct) =>
            {
                var box = new AwaitableCompletionSource();
                ct.Register(() => { canceled = true; box.TrySetResult(); });
                await box.Awaitable;
            });

            UI.Router.Open("ask").GetAwaiter().GetResult();   // 起 prompt(挂起)
            CollectionAssert.AreEqual(new[] { "home", "ask" }, UI.Router.Chain.ToList());

            UI.Router.Open("shop").GetAwaiter().GetResult();  // 导航走 → 取消 prompt
            Assert.IsTrue(canceled);                          // 取消同步发生
            CollectionAssert.AreEqual(new[] { "home", "shop" }, UI.Router.Chain.ToList());
        }
    }
}
```

- [ ] **Step 2: 刷新并确认红**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterPromptTests")
```
Expected: Prompt 激活抛 "kind not yet supported" → 三条红。

- [ ] **Step 3: 实现 Prompt 激活 + 生命周期**

把 `Activate` 的 switch 加 Prompt 分支(其余分支保持):

```csharp
                    case RouteKind.Prompt: return ActivatePrompt(def);
```

在 `Reconcile` 的激活循环里,加入"prompt 入链后再起 run"(防止 run 同步完成时节点尚未入链)。把:

```csharp
                for (int i = k; i < target.Count; i++)
                {
                    var active = await Activate(target[i], query);
                    _chain.Add(active);
                }
```

改为:

```csharp
                for (int i = k; i < target.Count; i++)
                {
                    var active = await Activate(target[i], query);
                    _chain.Add(active);
                    if (active.Def.Kind == RouteKind.Prompt) StartPrompt(active, query);
                }
```

把 `Deactivate` 加 Prompt 分支:

```csharp
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
```

在 `ActivateTab` 下方加:

```csharp
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
```

- [ ] **Step 4: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterPromptTests")
```
Expected: `RouterPromptTests` 三条绿。

- [ ] **Step 5: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Runtime/Application/UI.Router.Reconcile.cs \
        Tests/EditMode/Router/RouterPromptTests.cs Tests/EditMode/Router/RouterPromptTests.cs.meta
git commit -m "feat(router): Prompt presentation (MapPrompt + auto-pop + cancel-on-navigate)"
```

---

## Task 11: 改名范式(InputBox-backed Prompt)EditMode 可达性 + PlayMode 完整流程

> **拆分理由**:`run` 里 `await InputBox.Open(...)` 之后的"应用改名 + 自动出栈"挂在一个**续体**上;EditMode 无 PlayerLoop,点 OK(`SimulateClick`)解析 InputBox 的 awaitable 后,该续体不保证同步运行 → EditMode 只验证**同步可达**(导航到 rename → 链路 `[home,rename]` + InputBox 已实例化);**点 OK→改名→出栈**的完整流程放 PlayMode 用逐帧轮询。

**Files:**
- Test: `Tests/EditMode/Router/RouterPromptTests.cs`(追加可达性)
- Test: `Tests/PlayMode/RouterPlayTests.cs`(新建)

- [ ] **Step 1: 写 EditMode 可达性测试**

在 `RouterPromptTests` 末尾追加。InputBox 的 fake XML 含 `InputBoxRequest.Bind` 需要的 id(`title`/`field`/`ok`/`cancel`):

```csharp
        private const string IBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='ibox'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='dialog' anchor='center' size='400x200'>
    <VStack anchor='stretch' margin='16' spacing='8'>
      <Text id='title'/>
      <InputField id='field'/>
      <Btn id='ok'>OK</Btn>
      <Btn id='cancel'>Cancel</Btn>
    </VStack>
  </Frame>
</Screen></PromptUGUI>";

        [Test]
        public void Prompt_RenameViaInputBox_ReachableShowsDialog()
        {
            InputBox.XmlSrc = "ibox";   // 非 builtin 前缀 → 走 SourceResolver(fake)
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["ibox"] = IBoxXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Clear();
            UI.Router.Map("home", "home");
            UI.Router.MapPrompt("rename", parent: "home", run: async (q, ct) =>
            {
                var name = await InputBox.Open("改名", initial: "old", ct: ct);
                // 确认后的结果处理在 PlayMode 验;EditMode 只验证可达 + 弹窗已起
            });

            UI.Router.Navigate("appid://rename").GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "home", "rename" }, UI.Router.Chain.ToList());
            // InputBox 已在 modal 栈实例化(materialize pump 在 EditMode 同步完成)
            var ibox = UI.Modal.TopScreen;   // internal,Tests.EditMode 可见
            Assert.IsNotNull(ibox, "InputBox 应已实例化在 modal 栈顶");
            Assert.DoesNotThrow(() => ibox.Get<PromptUGUI.Controls.InputField>("field"));
        }
```

- [ ] **Step 2: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="RouterPromptTests")
```
Expected: 可达性测试绿。

- [ ] **Step 3: 写 PlayMode 测试(sorting 带 + 改名完整流程)**

新建 `Tests/PlayMode/RouterPlayTests.cs`。改名用例**逐帧轮询**(`SimulateClick` 是 `Btn` 的既有测试钩,见 `InputBoxTests.cs`):

```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.PlayMode
{
    public class RouterPlayTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";
        private static string ModalXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/></Screen></PromptUGUI>";
        private const string IBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='ibox'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Frame id='dialog' anchor='center' size='400x200'>
    <VStack anchor='stretch' margin='16' spacing='8'>
      <Text id='title'/><InputField id='field'/>
      <Btn id='ok'>OK</Btn><Btn id='cancel'>Cancel</Btn>
    </VStack>
  </Frame>
</Screen></PromptUGUI>";

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Setup(Dictionary<string, string> files)
        {
            UI.ResetForTests();
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
        }

        [UnityTest]
        public IEnumerator Modal_SortsAbovePage()
        {
            Setup(new Dictionary<string, string>
            {
                ["home"] = PageXml("home"), ["settings"] = ModalXml("settings"),
            });
            UI.Router.Map("home", "home");
            UI.Router.Map("settings", "settings", present: RoutePresent.Modal, parent: "home");

            _ = UI.Router.Open("settings");
            for (int i = 0; i < 10 && UI.Get("settings") == null; i++) yield return null;

            var home = UI.Get("home").RootGameObject.GetComponent<Canvas>();
            var settings = UI.Get("settings").RootGameObject.GetComponent<Canvas>();
            Assert.Greater(settings.sortingOrder, home.sortingOrder);
            Assert.GreaterOrEqual(settings.sortingOrder, UI.Modal.SortingOrderBase);
        }

        [UnityTest]
        public IEnumerator Prompt_RenameConfirm_AppliesAndAutoPops()
        {
            Setup(new Dictionary<string, string> { ["home"] = PageXml("home"), ["ibox"] = IBoxXml });
            InputBox.XmlSrc = "ibox";
            UI.Router.Map("home", "home");
            string applied = null;
            UI.Router.MapPrompt("rename", parent: "home", run: async (q, ct) =>
            {
                var name = await InputBox.Open("改名", initial: "old", ct: ct);
                if (name != null) applied = name;
            });

            _ = UI.Router.Navigate("appid://rename");
            for (int i = 0; i < 10 && UI.Modal.TopScreen == null; i++) yield return null;
            var ibox = UI.Modal.TopScreen;
            Assert.IsNotNull(ibox);

            ibox.Get<PromptUGUI.Controls.InputField>("field").TextValue = "newName";
            ibox.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();   // = close(field.TextValue)

            for (int i = 0; i < 10 && applied == null; i++) yield return null;

            Assert.AreEqual("newName", applied);
            CollectionAssert.AreEqual(new[] { "home" }, new List<string>(UI.Router.Chain));  // 自动出栈
        }
    }
}
```

> 核对点:`Btn.SimulateClick()` 已确认存在(`InputBoxTests.cs` 用 `Get<PBtn>("ok").SimulateClick()`)。`UI.Modal.TopScreen` 为 `internal`,`PromptUGUI.Tests.PlayMode` 在 `InternalsVisibleTo` 列表内 → 可用。

- [ ] **Step 4: 刷新并确认绿(EditMode + PlayMode)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Router")
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="RouterPlayTests")
```
Expected: 所有 `Router*` EditMode 绿;`RouterPlayTests` 2 条绿。

- [ ] **Step 5: Lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd .. && git add Tests/EditMode/Router/RouterPromptTests.cs \
        Tests/PlayMode/RouterPlayTests.cs Tests/PlayMode/RouterPlayTests.cs.meta
git commit -m "test(router): rename-via-InputBox prompt (EditMode reach + PlayMode confirm)"
```

---

## Task 12: SKILL.md 更新 + 全量回归 + 收尾

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: 读现状 + 写 Router 节**

先读 `.claude/skills/scripting-promptugui-csharp/SKILL.md` 找到合适插入点(modal 节附近 + cheatsheet)。新增一节,覆盖:

- 心智模型:稳定不透明名字(URL 只认名字)+ 注册时声明标准 parent → 树;`Open(name)` reconcile 当前层栈到标准链路;深链 `Navigate(url)` = 解析后 `Open`,与手点同结果。
- API:`UI.Router.Map(name, src, screen?, present: Page|Modal, parent?, onEnter?)`、`MapTab(name, parent, tabId, onEnter?)`、`MapPrompt(name, parent, run)`、`Open(name, query?)` / `Navigate(url)` / `Back()` / `Reset()`、`Current` / `Chain` / `Changed`、`Scheme`、`IsMapped` / `Clear`。
- 四种呈现表(Page / Modal / Tab / Prompt)+ 各自 activate 语义。
- 改名 Prompt 范式代码(button 与深链同走 `Open("rename")`,逻辑一份)。
- 共存须知:routed screen 只经 router 开关;Modal 节点的关闭按钮接 `Router.Back()`;ad-hoc `MessageBox`/`InputBox` 不进 router、导航时被清掉。
- modal 节补一句:`InputBox.Open` / `MessageBox.Open` 新增 `CancellationToken ct` 重载,routed Prompt 用它实现"被导航走即撤销"。
- cheatsheet 增 ROUTER 区。

具体行文按该 SKILL 既有风格写(英文)。

- [ ] **Step 2: 全量回归(EditMode + EditorOnly + PlayMode)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```
Expected: 全绿,无回归。

- [ ] **Step 3: 全量 lint**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 退出码 0(忽略 `Local.props` 缺失的 CS0246 噪音)。

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "doc(skill): document UI.Router navigation in scripting-promptugui-csharp"
```

---

## 自检(写完计划后回看 spec)

**Spec 覆盖**(逐节对照):
- §2 节点模型(Name/Parent/Kind/present/OnEnter)→ Task 1(RouteNode)+ Task 2(注册)。
- §3.1-3.2 reconcile(公共前缀 / query 投递 / 目标刷新)→ Task 3。§3.3 临时模态 → Task 6。§3.5 串行化 → Task 4。
- §4 URL / RouteQuery → Task 1(RouteQuery)+ Task 6(ParseUrl/Navigate)。
- §5.1 Page → Task 3;§5.2 Modal → Task 7;§5.3 Tab → Task 8;§5.4 Prompt(含 InputBox `ct`)→ Task 9 + 10 + 11。
- §6 临时模态边界 → Task 6。§7 TabBar 集成 → Task 8。§8 Back/Current/Chain/Changed → Task 3 + 5。
- §9 API 全表 → Task 2/3/5/6 合计。§10 错误处理 → Task 2(注册/链路)+ Task 8(tab 解析)。
- §11 共存 / teardown → Task 4(UnloadAll/ResetForTests 钩)。§14 SKILL → Task 12。§16 验收 → Task 11 + 12 回归。

**占位符扫描**:无 TBD/TODO;每个改代码的 Step 均含完整代码。Task 11 用已确认存在的 `Btn.SimulateClick()` + `UI.Modal.TopScreen`(`internal`,两个测试 asmdef 均在 `InternalsVisibleTo` 内),非待定写法。

**异步语义健壮性**(EditMode 无 PlayerLoop):凡断言"`await` 未完成 awaitable 之后的续体副作用"的点,要么改为在 `ct.Register` 同步回调里置位(Task 10 取消用例),要么移到 PlayMode 逐帧轮询(Task 11 改名确认)。`Reconcile` 采用"先移除尾部再 deactivate" + `SameChain` 早退,使 §3.3 `CloseAll` 唤醒 Prompt 续体导致的 `SelfPop` 重入安全(节点已不在链中 → no-op),并保证重复导航到正在显示的 Prompt 不被误关。

**类型一致性**:`RouteNode`/`RouteKind`/`RoutePresent`/`RoutePromptRun`/`RouteQuery`/`RouteException`、`ActiveNode`(`Def`/`ScreenKey`/`PromptCts`/`Deactivated`)、`_chain`/`_routes`/`_pending`/`_waiters`/`_epoch`、`ResolveChain`/`Reconcile`/`Activate`/`ActivatePage(modal)`/`ActivateTab`/`ActivatePrompt`/`StartPrompt`/`RunPrompt`/`SelfPop`/`Deactivate`/`EnsureLoaded`/`CountModalsInChain`/`IsTop`/`ResolveHostScreenKey`/`AbandonPump`/`CancelAllForTeardown`/`ResetForTestsInternal`、`UI.Modal.CancelEntry`——跨 Task 命名一致。

**已知 v1 边界**(spec §12,计划内已落地为具体取舍,非遗漏):用户手点 tab 不反向同步 router 链路;routed Modal 的 ESC 在 `UI.Modal.IsAnyOpen` 时让位给 ad-hoc 栈顶;§3.3 只清 ad-hoc dialog 栈(`UI.Modal.CloseAll`),不动 Loading/Toast;routed screen 的 src 仅经 router 加载(外部已 `LoadDocumentAsync` 过会撞"already loaded")。
