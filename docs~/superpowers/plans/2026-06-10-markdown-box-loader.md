# MarkdownBox 延迟内容加载 + HandleLink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MarkdownBox 支持"先开窗显示 Loading → 内容到达热替换 → 关窗自动取消加载"(`Open(loader)` / `OpenUrl` 糖),并新增 `UI.Markdown.HandleLink` 默认链接分发(Router scheme → `Router.Navigate`,否则 `Application.OpenURL`),MarkdownBox 默认链接行为切到它。

**Architecture:** 加载生命周期全部收进 `MarkdownBoxRequest`:`Loader` 字段非 null 时 Bind 先显示 `LoadingText`,建 CTS 并挂 Screen 销毁钩子(关窗任何通道→自动取消),fire-and-forget `FillAsync` 完成后热替换(`Markdown.Text` setter 自带重渲染,带取消守卫防触碰已销毁控件)。`OpenUrl` = `Open(loader)` + 内置 `UnityWebRequest` fetcher(镜像 `UI.Markdown.LoadWebTextureAsync` 的 ACS 模式 + `ct.Register(req.Abort)`)。`HandleLink` 放 `UI.Markdown`(与 `DefaultStyle`/`ImageResolver` 同层)。

**Tech Stack:** Unity 6 uGUI、R3、Unity `Awaitable`/`AwaitableCompletionSource`(禁 .NET `Task`)、`UnityWebRequest`(WebGL 安全)、NUnit EditMode(UnityMCP)、C# 9。

**Spec:** `docs~/superpowers/specs/2026-06-10-markdown-box-loader-design.md`
**分支:** 直接在 `feat/markdown-box-modal`(PR #64)上继续,不开新分支。

**文件总览:**

| 动作 | 路径 | 职责 |
|---|---|---|
| Create | `Tests/EditMode/Markdown/HandleLinkTests.cs` | HandleLink 分发测试(4) |
| Modify | `Runtime/Application/UI.Markdown.cs` | `HandleLink` + `OpenUrlHookForTests` + reset |
| Create | `Tests/EditMode/Modals/MarkdownBoxLoaderTests.cs` | Loader 生命周期 + 门面测试(7) |
| Modify | `Runtime/Application/Modals/MarkdownBoxRequest.cs` | `Loader`/`LoadingText`/`FillAsync` + `Open(loader)`/`OpenUrl`/`FetchAsync` + 默认链接改 `HandleLink` |
| Modify | `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 新 API 文档 |

约定提醒(执行者零上下文必读):

- 每次改完 C# 源码:`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])` 零错误后再跑测试。MCP 工具先 `ToolSearch(query="select:mcp__UnityMCP__refresh_unity,mcp__UnityMCP__read_console,mcp__UnityMCP__run_tests,mcp__UnityMCP__get_test_job", max_results=4)` 加载(select 须全名)。
- `run_tests` 异步:拿 `job_id` 轮询 `get_test_job`;按类过滤用 `group_names=[...]`。
- 新文件 commit 必须带 Unity 生成的 `.meta`(refresh 后出现)。
- C# 9:lambda 无自然委托类型;禁 .NET `Task`。
- 禁止提交 main;工作分支 `feat/markdown-box-modal`。
- commit message 末尾带 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。

---

### Task 1: `UI.Markdown.HandleLink`(红→绿)

**Files:**
- Create: `Tests/EditMode/Markdown/HandleLinkTests.cs`
- Modify: `Runtime/Application/UI.Markdown.cs`

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Markdown/HandleLinkTests.cs`,完整内容:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Markdown
{
    public class HandleLinkTests
    {
        private const string PageXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='p1'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
</Screen></PromptUGUI>";

        private List<string> _opened;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _opened = new List<string>();
            UI.Markdown.OpenUrlHookForTests = url => _opened.Add(url);
            var files = new Dictionary<string, string> { ["p1"] = PageXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Scheme_match_navigates_via_router()
        {
            UI.Router.Scheme = "app";
            UI.Router.Map("p1", "p1");
            UI.Markdown.HandleLink("app://p1");
            // EditMode 假 resolver 下 Navigate 同步完成,链路立即可断言
            CollectionAssert.AreEqual(new[] { "p1" }, UI.Router.Chain.ToList());
            Assert.IsEmpty(_opened);
        }

        [Test]
        public void Other_urls_fall_back_to_open_url()
        {
            UI.Router.Scheme = "app";
            UI.Markdown.HandleLink("https://example.com");
            CollectionAssert.AreEqual(new[] { "https://example.com" }, _opened);
        }

        [Test]
        public void No_scheme_configured_everything_falls_back()
        {
            UI.Router.Scheme = null;
            UI.Markdown.HandleLink("app://p1");
            CollectionAssert.AreEqual(new[] { "app://p1" }, _opened);
        }

        // scheme 命中但路由失败 → LogError,不回落系统浏览器(spec §4)
        [Test]
        public void Failed_navigation_logs_error_no_browser_fallback()
        {
            UI.Router.Scheme = "app";   // 不注册任何路由
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("HandleLink"));
            UI.Markdown.HandleLink("app://nope");
            Assert.IsEmpty(_opened);
        }
    }
}
```

- [ ] **Step 2: refresh,确认红 = 编译错误**

Expected: CS0117 `UI.Markdown` 不含 `OpenUrlHookForTests` / `HandleLink`。其他错误(usings/typo)修测试文件自身。

- [ ] **Step 3: 实现**

`Runtime/Application/UI.Markdown.cs`:在 `UseWebImageResolver()` 方法之前插入(类内任意稳定位置均可,建议紧跟 `ImageResolver` 属性之后):

```csharp
            /// <summary>Default link policy for markdown links: a url carrying the configured
            /// <see cref="UI.Router.Scheme"/> navigates via <see cref="UI.Router"/>; anything
            /// else opens in the system browser. <c>MarkdownBox</c> uses this when no
            /// <c>onLinkClicked</c> is given; standalone &lt;Markdown&gt; screens can call it
            /// from their own <c>OnLinkClicked</c> subscription.</summary>
            public static void HandleLink(string url)
            {
                if (string.IsNullOrEmpty(url)) return;
                var scheme = UI.Router.Scheme;
                if (!string.IsNullOrEmpty(scheme) &&
                    url.StartsWith(scheme + "://", StringComparison.Ordinal))
                {
                    // 失败只 LogError,不回落 OpenURL——深链交给系统浏览器只会更糟。
                    _ = NavigateLogged(url);
                    return;
                }
                if (OpenUrlHookForTests != null) OpenUrlHookForTests(url);
                else UnityEngine.Application.OpenURL(url);
            }

            internal static Action<string> OpenUrlHookForTests;

            private static async Awaitable NavigateLogged(string url)
            {
                try { await UI.Router.Navigate(url); }
                catch (Exception ex)
                {
                    Debug.LogError($"HandleLink: navigate '{url}' failed: {ex.Message}");
                }
            }
```

并在 `ResetForTestsInternal()` 里加一行(`Renderer = null;` 之后):

```csharp
                OpenUrlHookForTests = null;
```

- [ ] **Step 4: refresh + 跑 HandleLinkTests,绿**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["HandleLinkTests"])
```

Expected: 4/4 PASS。

- [ ] **Step 5: lint**

```bash
cd /workspace-PromptUGUI/.lint && dotnet restore PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 退出 0。

- [ ] **Step 6: Commit**

```bash
git add Tests/EditMode/Markdown/HandleLinkTests.cs Tests/EditMode/Markdown/HandleLinkTests.cs.meta Runtime/Application/UI.Markdown.cs
git commit -m "feat: UI.Markdown.HandleLink 默认链接分发(Router scheme 联动)"
```

---

### Task 2: MarkdownBox 默认链接行为切到 HandleLink

**Files:**
- Modify: `Tests/EditMode/Modals/MarkdownBoxTests.cs`(追加 1 测试)
- Modify: `Runtime/Application/Modals/MarkdownBoxRequest.cs`(Bind 内 1 行)

**TDD 例外说明**:本任务跳过"先看红"——失败路径是真调 `Application.OpenURL`,红跑一次会在开发机上弹真浏览器。规格由绿断言 + 一行 diff 评审兜底。

- [ ] **Step 1: 追加测试**

`Tests/EditMode/Modals/MarkdownBoxTests.cs` 类尾部追加:

```csharp
        // 默认链接行为 = UI.Markdown.HandleLink(spec 2026-06-10-markdown-box-loader §5)
        [Test]
        public void Default_link_handler_routes_through_HandleLink()
        {
            var opened = new List<string>();
            UI.Markdown.OpenUrlHookForTests = url => opened.Add(url);
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "x" });
            Md().RaiseLinkClickedForTests("https://x");
            CollectionAssert.AreEqual(new[] { "https://x" }, opened);
        }
```

(`List<string>` 的 using 已存在;hook 由 `UI.ResetForTests` 在 TearDown 清空。)

- [ ] **Step 2: 改默认行为**

`Runtime/Application/Modals/MarkdownBoxRequest.cs` 的 `Bind` 内,把:

```csharp
            // C#9:lambda 无自然委托类型,不能写 `OnLinkClicked ?? (url => ...)`,
            // 在订阅内分支即可。传了 OnLinkClicked 则完全接管,不叠加默认 OpenURL。
            var onLink = OnLinkClicked;
            md.OnLinkClicked.Subscribe(url =>
            {
                if (onLink != null) onLink(url);
                else UnityEngine.Application.OpenURL(url);
            }).AddTo(screen);
```

改为:

```csharp
            // C#9:lambda 无自然委托类型,不能写 `OnLinkClicked ?? (url => ...)`,
            // 在订阅内分支即可。传了 OnLinkClicked 则完全接管,不叠加默认分发。
            var onLink = OnLinkClicked;
            md.OnLinkClicked.Subscribe(url =>
            {
                if (onLink != null) onLink(url);
                else UI.Markdown.HandleLink(url);
            }).AddTo(screen);
```

同时把字段注释 `public Action<string> OnLinkClicked;      // null → 默认 Application.OpenURL` 改为 `// null → 默认 UI.Markdown.HandleLink`。

- [ ] **Step 3: refresh + 跑 MarkdownBoxTests,绿**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownBoxTests"])
```

Expected: 13/13 PASS(原 12 + 新 1)。

- [ ] **Step 4: lint 后 Commit**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
git add Tests/EditMode/Modals/MarkdownBoxTests.cs Runtime/Application/Modals/MarkdownBoxRequest.cs
git commit -m "feat: MarkdownBox 默认链接行为改走 UI.Markdown.HandleLink"
```

---

### Task 3: Loader 红测试

**Files:**
- Create: `Tests/EditMode/Modals/MarkdownBoxLoaderTests.cs`

- [ ] **Step 1: 写失败测试**(完整内容)

```csharp
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.TestTools;
using PBtn = PromptUGUI.Controls.Btn;
using PMarkdown = PromptUGUI.Controls.Markdown;

namespace PromptUGUI.Tests.Modals
{
    public class MarkdownBoxLoaderTests
    {
        private const string MdBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/MdBox2'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='600x400'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Markdown id='markdown' width='stretch' height='stretch'/>
      </VStack>
      <Btn id='close' anchor='top-right' size='36x36'>×</Btn>
    </Frame>
  </Screen>
</PromptUGUI>";

        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string> { ["test/MdBox2"] = MdBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            MarkdownBox.XmlSrc = "test/MdBox2";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static PMarkdown Md()
            => UI.Modal.TopScreen.Get<PMarkdown>("markdown");

        [Test]
        public void Completed_loader_replaces_content_immediately()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = _ => AwaitableHelpers.Completed("# done"),
            });
            Assert.AreEqual("# done", Md().Text);
        }

        [Test]
        public void Pending_loader_shows_loading_placeholder_then_result()
        {
            var acs = new AwaitableCompletionSource<string>();
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Loader = _ => acs.Awaitable });
            Assert.AreEqual("*Loading…*", Md().Text);
            acs.SetResult("# late");
            Assert.AreEqual("# late", Md().Text);
        }

        [Test]
        public void Custom_loading_text_used()
        {
            var acs = new AwaitableCompletionSource<string>();
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = _ => acs.Awaitable,
                LoadingText = "稍候…",
            });
            Assert.AreEqual("稍候…", Md().Text);
        }

        // 关窗 → loader 的 ct 取消;迟到的完成不得触碰已销毁控件(EditMode 是
        // DestroyImmediate,真触碰会抛)——本测试无异常即证明守卫生效。
        [Test]
        public void Close_cancels_loader_and_late_result_is_ignored()
        {
            CancellationToken seen = default;
            var acs = new AwaitableCompletionSource<string>();
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = ct => { seen = ct; return acs.Awaitable; },
            });
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            task.GetAwaiter().GetResult();
            Assert.IsTrue(seen.IsCancellationRequested);
            acs.SetResult("# late");   // 恢复 FillAsync;ct 守卫使其直接 return
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Loader_failure_shows_error_markdown()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("MarkdownBox loader failed"));
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = _ => throw new System.InvalidOperationException("boom"),
            });
            StringAssert.StartsWith("**Failed to load.**", Md().Text);
            StringAssert.Contains("boom", Md().Text);
        }

        [Test]
        public void Loader_wins_over_text()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = "ignored",
                Loader = _ => AwaitableHelpers.Completed("# loaded"),
            });
            Assert.AreEqual("# loaded", Md().Text);
        }

        [Test]
        public void Facade_open_loader_overload_works()
        {
            var task = MarkdownBox.Open(
                _ => AwaitableHelpers.Completed("# f"), title: "T");
            Assert.AreEqual("# f", Md().Text);
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            task.GetAwaiter().GetResult();
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }
    }
}
```

- [ ] **Step 2: refresh,确认红 = 编译错误**

Expected: CS0117/CS1061 —— `MarkdownBoxRequest` 不含 `Loader`/`LoadingText`,`MarkdownBox.Open` 无该重载。测试文件自身错误(usings/typo)就地修。

- [ ] **Step 3: Commit(红测试单独入库)**

```bash
git add Tests/EditMode/Modals/MarkdownBoxLoaderTests.cs Tests/EditMode/Modals/MarkdownBoxLoaderTests.cs.meta
git commit -m "test: MarkdownBox Loader 生命周期红测试"
```

---

### Task 4: 实现 Loader / FillAsync / Open(loader) / OpenUrl

**Files:**
- Modify: `Runtime/Application/Modals/MarkdownBoxRequest.cs`

- [ ] **Step 1: 文件头部 usings**

确保有(新增 `System.Threading` 与 `UnityEngine.Networking`):

```csharp
using System;
using System.Threading;
using R3;
using UnityEngine.Networking;
```

- [ ] **Step 2: Request 字段与 Bind**

`MarkdownBoxRequest` 字段区追加:

```csharp
        /// <summary>非 null 时忽略 <see cref="Text"/>:先显示 <see cref="LoadingText"/>,
        /// loader 完成后热替换;关窗(任何通道)自动取消传入的 ct。</summary>
        public Func<CancellationToken, UnityEngine.Awaitable<string>> Loader;
        public string LoadingText = "*Loading…*";
```

`Bind` 中把 `md.Text = Text ?? "";` 一行替换为:

```csharp
            if (Loader != null)
            {
                md.Text = LoadingText;
                var cts = new CancellationTokenSource();
                // 关窗(×/backdrop/ESC/外部 ct)→ Screen Dispose → 取消加载
                Disposable.Create(() => cts.Cancel()).AddTo(screen);
                _ = FillAsync(md, Loader, cts.Token);
            }
            else
            {
                md.Text = Text ?? "";
            }
```

(`Disposable.Create` 来自 R3。若所用 R3 版本缺失该工厂,用下面的等价私有类替代,行为相同:

```csharp
        private sealed class CancelOnDispose : IDisposable
        {
            private readonly CancellationTokenSource _cts;
            public CancelOnDispose(CancellationTokenSource cts) => _cts = cts;
            public void Dispose() => _cts.Cancel();
        }
        // 用法:new CancelOnDispose(cts).AddTo(screen);
```
)

- [ ] **Step 3: FillAsync**

`MarkdownBoxRequest` 类内追加:

```csharp
        private static async UnityEngine.Awaitable FillAsync(
            PromptUGUI.Controls.Markdown md,
            Func<CancellationToken, UnityEngine.Awaitable<string>> loader,
            CancellationToken ct)
        {
            string result;
            try
            {
                result = await loader(ct);
            }
            catch (OperationCanceledException)
            {
                return;                              // 关窗正常路径
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                UnityEngine.Debug.LogWarning($"MarkdownBox loader failed: {ex.Message}");
                md.Text = "**Failed to load.**\n\n" + ex.Message;
                return;
            }
            if (ct.IsCancellationRequested) return;  // 迟到的结果:控件已销毁,不得触碰
            md.Text = result;
        }
```

- [ ] **Step 4: 静态门面重载**

`MarkdownBox` 静态类内,在既有 `Open(string markdown, ...)` 之后追加:

```csharp
        /// <summary>延迟内容:先开窗显示占位 loading,loader 完成后热替换;
        /// 关窗自动取消 loader 的 ct。鉴权内容用此重载走游戏自己的网络栈。</summary>
        public static async UnityEngine.Awaitable Open(
            Func<CancellationToken, UnityEngine.Awaitable<string>> loader,
            string title = null,
            Action<string> onLinkClicked = null,
            string loadingText = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            CancellationToken ct = default)
        {
            var req = new MarkdownBoxRequest
            {
                Loader = loader,
                Title = title,
                OnLinkClicked = onLinkClicked,
                Configure = configure,
            };
            if (loadingText != null) req.LoadingText = loadingText;
            await UI.Modal.OpenAsync(req, mode, ct);
        }

        /// <summary>裸 GET 便捷重载(无鉴权;镜像 UseWebImageResolver 的取数模式)。</summary>
        public static UnityEngine.Awaitable OpenUrl(
            string url,
            string title = null,
            Action<string> onLinkClicked = null,
            string loadingText = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            CancellationToken ct = default)
            => Open(ct2 => FetchAsync(url, ct2),
                title, onLinkClicked, loadingText, mode, configure, ct);

        private static async UnityEngine.Awaitable<string> FetchAsync(
            string url, CancellationToken ct)
        {
            using var req = UnityWebRequest.Get(url);
            var op = req.SendWebRequest();
            var acs = new UnityEngine.AwaitableCompletionSource<bool>();
            op.completed += _ => acs.TrySetResult(true);
            using var reg = ct.Register(() => req.Abort());
            if (!op.isDone) await acs.Awaitable;
            ct.ThrowIfCancellationRequested();
            if (req.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException($"{url}: {req.error}");
            return req.downloadHandler.text;
        }
```

- [ ] **Step 5: refresh + 零编译错误 + 跑两个类,全绿**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownBoxLoaderTests"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownBoxTests"])
```

Expected: 7/7 + 13/13 PASS。

- [ ] **Step 6: lint**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 退出 0。

- [ ] **Step 7: Commit**

```bash
git add Runtime/Application/Modals/MarkdownBoxRequest.cs
git commit -m "feat: MarkdownBox Loader 延迟加载 + OpenUrl 裸 GET 重载"
```

---

### Task 5: C# SKILL 文档更新

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`(按内容定位,英文为主,cheatsheet 内中文注释沿用现状)

- [ ] **Step 1: cheatsheet MODAL 块**,在已有 `await MarkdownBox.Open(markdown, title, onLinkClicked, mode, configure, ct)` 三行之后追加:

```
               await MarkdownBox.Open(loader, title, ...)  // loader: Func<CT,Awaitable<string>>
                              // 先显示 loadingText 占位,完成后热替换;关窗自动取消 loader 的 ct
               await MarkdownBox.OpenUrl(url, title, ...)  // 裸 GET 糖;鉴权内容用 Open(loader)
```

- [ ] **Step 2: `### Quick usage`** 的 MarkdownBox 例子后追加:

```csharp
// Deferred content: opens immediately showing "*Loading…*", swaps in the markdown
// when the fetch completes, and cancels the fetch if the user closes the box first.
await MarkdownBox.OpenUrl("https://cdn.example.com/notice.md", title: UI.Tr("Notice"));

// Authenticated content: bring your own loader (the ct is cancelled on close).
await MarkdownBox.Open(ct => Api.FetchMailBodyAsync(mailId, ct), title: mail.Subject);
```

- [ ] **Step 3: `### API surface`** 的 `MarkdownBox` 块替换为(补两个重载 + LoadingText 语义):

```csharp
public static class MarkdownBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MarkdownBox.ui";
    // Returns a non-generic Awaitable: completes when the box is closed
    // (× button / backdrop click / ESC). No buttons can be configured.
    public static Awaitable Open(
        string markdown, string title = null,
        Action<string> onLinkClicked = null,        // null → UI.Markdown.HandleLink
        ModalMode mode = ModalMode.Popup,
        Action<IScreen> configure = null,
        CancellationToken ct = default);
    // Deferred content: shows loadingText (default "*Loading…*") until loader
    // completes, then hot-swaps. The loader's ct is cancelled when the box closes
    // (any channel). Loader errors render "**Failed to load.**" + message — catch
    // inside your loader to customize.
    public static Awaitable Open(
        Func<CancellationToken, Awaitable<string>> loader, string title = null,
        Action<string> onLinkClicked = null, string loadingText = null,
        ModalMode mode = ModalMode.Popup, Action<IScreen> configure = null,
        CancellationToken ct = default);
    // Plain unauthenticated GET sugar over Open(loader).
    public static Awaitable OpenUrl(string url, /* same trailing params */);
}
```

- [ ] **Step 4: `### Behavior`** 的 MarkdownBox 条目里,把链接默认行为句改为指向 `UI.Markdown.HandleLink`(原文是 "Links default to `Application.OpenURL`; pass a non-null `onLinkClicked` to fully replace that behaviour."),改为:

```
Links default to `UI.Markdown.HandleLink` (see below); pass a non-null `onLinkClicked` to fully replace that behaviour.
```

- [ ] **Step 5: `### Markdown`(C# bridge 一节)或 Behavior 附近**追加 HandleLink 条目(放在与 `UI.Markdown.DefaultStyle` / `UseWebImageResolver` 相邻的位置):

```markdown
**`UI.Markdown.HandleLink(string url)`** — the default link policy, also usable from
your own `OnLinkClicked` subscriptions on standalone `<Markdown>` screens. If
`UI.Router.Scheme` is set and the url starts with `<Scheme>://`, it navigates via
`UI.Router.Navigate` (failures are logged, NOT handed to the system browser);
everything else goes to `Application.OpenURL`. Note this changed MarkdownBox's default:
with a Router scheme configured, deep links inside markdown now navigate instead of
opening a browser. Want `.md` links to open nested MarkdownBoxes? That's one line:
`onLinkClicked: url => { if (url.EndsWith(".md")) _ = MarkdownBox.OpenUrl(url); else UI.Markdown.HandleLink(url); }`
```

- [ ] **Step 6: cheatsheet MARKDOWN 区**(若有 `UI.Markdown.*` 行)补一行:

```
               UI.Markdown.HandleLink(url)      // 默认链接分发:Router scheme → Navigate,否则 OpenURL
```

- [ ] **Step 7: 通读 6 处插入点上下文无破损后 Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): MarkdownBox Open(loader)/OpenUrl + UI.Markdown.HandleLink"
```

---

### Task 6: 全量回归 + 推送更新 PR #64

**Files:** 无新改动(验证任务)

- [ ] **Step 1: 全量测试**(三套顺序跑,逐个等 job 完成)

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: 全 PASS(EditMode 基线 1512 + 新 12 ≈ 1524;EditorOnly 183;PlayMode 132;以实跑为准)。

- [ ] **Step 2: lint 终验**

```bash
cd /workspace-PromptUGUI/.lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
cd /workspace-PromptUGUI && dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

- [ ] **Step 3: 推送 + 更新 PR 描述**

```bash
git push
gh pr view 64 --json body -q .body   # 取现有 body
# 在 Summary 末尾追加 loader/HandleLink 两条要点,并在 Test Plan 勾选项里更新测试计数,然后:
gh pr edit 64 --body "<更新后的完整 body>"
```

追加的 Summary 要点(措辞可微调):

```
- 延迟内容加载:`MarkdownBox.Open(loader)` 先显示占位 Loading、完成热替换、关窗自动取消;`OpenUrl(url)` 裸 GET 糖(镜像 UseWebImageResolver 模式 + ct.Register(Abort))
- `UI.Markdown.HandleLink` 默认链接分发:Router scheme 命中 → `Router.Navigate`(失败 LogError 不回落浏览器),否则 `Application.OpenURL`;MarkdownBox 默认链接行为切到它(设计文档 `2026-06-10-markdown-box-loader-design.md`)
```
