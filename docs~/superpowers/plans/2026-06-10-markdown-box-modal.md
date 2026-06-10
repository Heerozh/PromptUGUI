# MarkdownBox 内置模态 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增第 4 个内置模态 `MarkdownBox.Open(markdown, title, ...)` —— 无按钮的富文本只读显示框(公告/邮件),三通道关闭(右上角 ×、点 backdrop、ESC)。

**Architecture:** 完全复用 `ModalRequest<T>` / `UI.Modal.OpenAsync` 管线(零改动):新增 `MarkdownBoxRequest : ModalRequest<bool>` + 静态 `MarkdownBox` 门面 + 内置 XML `Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml`。内容区是现成的 `<Markdown>` 控件(自带 ScrollRect / `OnLinkClicked` / 图片 resolver 兜底)。

**Tech Stack:** Unity 6 uGUI、R3、Unity `Awaitable`(禁 .NET `Task`)、NUnit EditMode(UnityMCP 跑)、C# 9(`.lint` LangVersion 限制——**不可用 C#10 的 lambda 自然委托类型**)。

**Spec:** `docs~/superpowers/specs/2026-06-10-markdown-box-modal-design.md`

**文件总览:**

| 动作 | 路径 | 职责 |
|---|---|---|
| Create | `Tests/EditMode/Modals/MarkdownBoxTests.cs` | 全部 EditMode 测试 |
| Create | `Runtime/Application/Modals/MarkdownBoxRequest.cs` | Request + 静态门面 |
| Create | `Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml` | 内置布局 XML |
| Modify | `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 内置模态文档 + cheatsheet |

约定提醒(执行者零上下文必读):

- 每次改完 C# 源码:`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认零编译错误,再跑测试。
- `run_tests` 是异步的:拿到 `job_id` 后轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成。过滤单个测试类用 `group_names=["MarkdownBoxTests"]`。
- Unity 会为每个新文件生成 `.meta` sidecar —— **每次 commit 必须把 .meta 一起 add**(refresh 之后才会出现)。
- 仓库规则:禁止提交到 main;当前工作分支为 `feat/markdown-box-modal`。

---

### Task 1: 红测试 — MarkdownBoxTests.cs

**Files:**
- Create: `Tests/EditMode/Modals/MarkdownBoxTests.cs`

测试模式仿照 `Tests/EditMode/Modals/InputBoxTests.cs`:fake resolver 注册最小 XML、`XmlSrc` 指过去、`UI.ResetForTests()` 进出各一次。要点:

- backdrop 点击用 `ExecuteEvents.Execute(..., pointerDownHandler)` 派发 —— `Image.OnPointerDown` 的订阅会让控件挂上 `PointerEventRelay`(实现 `IPointerDownHandler`)。
- 自定义链接路由用 `Markdown.RaiseLinkClickedForTests`(internal,`InternalsVisibleTo` 已开)。
- `MarkdownBox.Open` 返回**非泛型** `Awaitable`,`GetAwaiter().GetResult()` 无返回值;直接走 `UI.Modal.OpenAsync(new MarkdownBoxRequest...)` 的测试拿 `Awaitable<bool>`。
- 关闭按钮 id 是 `close`,与 MessageBox 的 `close` 按钮同名但语义独立(本模态无 MsgBtn 概念)。

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine.EventSystems;
using PBtn = PromptUGUI.Controls.Btn;
using PImage = PromptUGUI.Controls.Image;
using PMarkdown = PromptUGUI.Controls.Markdown;
using PText = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.Modals
{
    public class MarkdownBoxTests
    {
        private const string MdBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/MdBox1'>
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
            _files = new Dictionary<string, string> { ["test/MdBox1"] = MdBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            MarkdownBox.XmlSrc = "test/MdBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static PMarkdown Md()
            => UI.Modal.TopScreen.Get<PMarkdown>("markdown");

        [Test]
        public void Open_sets_markdown_source_text()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "# Hello\nworld" });
            Assert.AreEqual("# Hello\nworld", Md().Text);
        }

        [Test]
        public void Null_title_hides_title_node()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
        }

        [Test]
        public void Title_present_shows_title_with_text()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body", Title = "公告" });
            var title = UI.Modal.TopScreen.Get<PText>("title");
            Assert.IsTrue(title.GameObject.activeSelf);
            Assert.AreEqual("公告", title.TmpComponent.text);
        }

        [Test]
        public void Click_close_btn_completes_and_closes()
        {
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Backdrop_pointer_down_closes()
        {
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            var backdrop = UI.Modal.TopScreen.Get<PImage>("backdrop");
            ExecuteEvents.Execute(backdrop.GameObject,
                new PointerEventData(EventSystem.current),
                ExecuteEvents.pointerDownHandler);
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void TryEscape_returns_true()
        {
            var req = new MarkdownBoxRequest { Text = "body" };
            Assert.IsTrue(req.TryEscape(out var r));
            Assert.IsTrue(r);
        }

        // ESC 走真实管线:pump → ModalEscapeListener → TryEscape → close。
        [Test]
        public void Escape_via_listener_closes()
        {
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            var listener = UI.Modal.TopScreen
                .RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener);
            listener.FireForTests();
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Custom_onLinkClicked_receives_url()
        {
            string captured = null;
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = "[a](https://example.com)",
                OnLinkClicked = url => captured = url,
            });
            Md().RaiseLinkClickedForTests("https://example.com");
            Assert.AreEqual("https://example.com", captured);
        }

        [Test]
        public void Static_Open_completes_on_close()
        {
            var task = MarkdownBox.Open("body", title: "T");
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            task.GetAwaiter().GetResult();   // 非泛型 Awaitable,不抛即通过
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Cancel_via_ct_throws_OperationCanceled()
        {
            var cts = new System.Threading.CancellationTokenSource();
            var task = MarkdownBox.Open("body", ct: cts.Token);
            cts.Cancel();
            Assert.Throws<System.OperationCanceledException>(
                () => task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }
    }
}
```

- [ ] **Step 2: refresh,确认红 = 编译错误**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: CS0246 `MarkdownBoxRequest` / `MarkdownBox` 不存在 —— 这就是本任务的红状态(编译失败即测试失败)。

- [ ] **Step 3: Commit(红测试单独入库)**

```bash
git add Tests/EditMode/Modals/MarkdownBoxTests.cs Tests/EditMode/Modals/MarkdownBoxTests.cs.meta
git commit -m "test: MarkdownBox 内置模态红测试"
```

(`.meta` 由 Step 2 的 refresh 生成;若没生成则再 refresh 一次。)

---

### Task 2: 实现 MarkdownBoxRequest + 静态门面

**Files:**
- Create: `Runtime/Application/Modals/MarkdownBoxRequest.cs`

- [ ] **Step 1: 写实现**

```csharp
using System;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class MarkdownBoxRequest : ModalRequest<bool>
    {
        public string Text;                       // markdown 源文
        public string Title;                      // null/空 → 隐藏标题行
        public Action<string> OnLinkClicked;      // null → 默认 Application.OpenURL

        public override string XmlSrc => MarkdownBox.XmlSrc;

        public override void Bind(IScreen screen, Action<bool> close)
        {
            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            var md = screen.Get<PromptUGUI.Controls.Markdown>("markdown");
            md.Text = Text ?? "";
            // C#9:lambda 无自然委托类型,不能写 `OnLinkClicked ?? (url => ...)`,
            // 在订阅内分支即可。传了 OnLinkClicked 则完全接管,不叠加默认 OpenURL。
            var onLink = OnLinkClicked;
            md.OnLinkClicked.Subscribe(url =>
            {
                if (onLink != null) onLink(url);
                else UnityEngine.Application.OpenURL(url);
            }).AddTo(screen);

            screen.Get<PromptUGUI.Controls.Btn>("close")
                .OnClick.Subscribe(_ => close(true)).AddTo(screen);

            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => close(true)).AddTo(screen);
        }

        public override bool TryEscape(out bool result)
        {
            result = true;   // 点背景都能关,ESC 行为一致
            return true;
        }
    }

    public static class MarkdownBox
    {
        // 必须带 .ui 后缀：Unity 只剥离 .ui.xml 文件名的最后 .xml。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MarkdownBox.ui";

        /// <summary>无按钮的富文本只读模态;关闭即完成(×/点背景/ESC 三通道)。</summary>
        public static async UnityEngine.Awaitable Open(
            string markdown,
            string title = null,
            Action<string> onLinkClicked = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default)
            => await UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = markdown,
                Title = title,
                OnLinkClicked = onLinkClicked,
                Configure = configure,
            }, mode, ct);
    }
}
```

- [ ] **Step 2: refresh + 零编译错误**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected: 无 error。

- [ ] **Step 3: 跑 MarkdownBoxTests,全绿**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownBoxTests"])
mcp__UnityMCP__get_test_job(job_id=...)   # 轮询到完成
```

Expected: 10/10 PASS。

- [ ] **Step 4: lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 退出码 0。若有 whitespace/style 报修,跑 `dotnet format whitespace PromptUGUI.Lint.slnx` 后复验。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Modals/MarkdownBoxRequest.cs Runtime/Application/Modals/MarkdownBoxRequest.cs.meta
git commit -m "feat: MarkdownBoxRequest + MarkdownBox.Open 静态门面"
```

---

### Task 3: 内置 XML + builtin 加载测试

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml`
- Modify: `Tests/EditMode/Modals/MarkdownBoxTests.cs`(追加 1 个测试)

- [ ] **Step 1: 追加 builtin 红测试**

加到 `MarkdownBoxTests` 类尾部(仿 `LoadingBuiltinTests`;`PromptUGUI/` 前缀走 `ModalSourceLoader` 的 Resources 同步分支,不经 fake resolver):

```csharp
        [Test]
        public void Default_xml_src_loads_builtin_template()
        {
            MarkdownBox.XmlSrc = "PromptUGUI/Modals/MarkdownBox.ui";
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "# T", Title = "公告" });
            Assert.AreEqual("# T", Md().Text);
            Assert.IsTrue(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            Assert.IsTrue(task.GetAwaiter().GetResult());
        }
```

- [ ] **Step 2: refresh + 跑该测试,确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownBoxTests"])
```

Expected: 该测试 FAIL,`InvalidOperationException: Builtin modal XML missing at Resources/PromptUGUI/Modals/MarkdownBox.ui.xml`。

- [ ] **Step 3: 写内置 XML**

`Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml`(margin 双分量 = `上下,左右`;× 按钮透明背景须 `sprite=""` **加** `color="#00000000"`,alpha 0 仍接收 raycast;字形用 Latin-1 `×` U+00D7,默认 TMP 字体不含 `✕` U+2715):

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/MarkdownBox.ui" reference="1920x1080" reference.portrait="1080x1920" >
    <Image id="backdrop" anchor="stretch" color="#000000FE"/>

    <Image id="dialog" sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"
           anchor="stretch" margin="160,480" margin.portrait="240,80">
      <VStack anchor="stretch" margin="24" spacing="12">
        <Text id="title" fontSize="24" height="40"/>
        <Markdown id="markdown" width="stretch" height="stretch"/>
      </VStack>
      <Btn id="close" anchor="top-right" size="36x36" margin="12"
           sprite="" color="#00000000" fontSize="28">×</Btn>
    </Image>
  </Screen>
</PromptUGUI>
```

- [ ] **Step 4: XML lint**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml
```

Expected: 退出码 0、无 error。(VStack 子节点只用了 `width`/`height`,没有非法 `anchor`/`margin`;× 按钮挂在非 layout-group 的 `dialog` 下,`anchor="top-right"` 合法。)

- [ ] **Step 5: refresh + 跑全类,绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["MarkdownBoxTests"])
```

Expected: 11/11 PASS。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml.meta Tests/EditMode/Modals/MarkdownBoxTests.cs
git commit -m "feat: MarkdownBox 内置布局 XML(stretch+margin 自适应,× 浮层)"
```

---

### Task 4: SKILL 文档更新

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

公开 C# API 新增 ⇒ 按 CLAUDE.md 必须同 PR 更新 C# skill。XML skill 不动(内置模态 XML 非作者可写面)。改四处(行号以当前 main 为准,执行时用内容定位):

- [ ] **Step 1: cheatsheet MODAL 块**(`var s = await InputBox.Open(...)` 之后)追加:

```
               await MarkdownBox.Open(markdown, title, onLinkClicked, mode, configure, ct)
                              // 无按钮富文本框(公告/邮件);×/点背景/ESC 三通道关闭,关闭即完成
                              // onLinkClicked null → 链接默认 Application.OpenURL
```

- [ ] **Step 2: `## Modal dialogs` 开头一段**,把内置清单从三个改四个(在提到 `MessageBox` / `InputBox` / `Loading` 的句子里补 `MarkdownBox`,一句话描述:no-button rich-text viewer for announcements/mail, built on the `<Markdown>` control)。

- [ ] **Step 3: `### Quick usage`** 追加示例:

```csharp
// MarkdownBox: read-only rich-text viewer (announcements / mail). No buttons;
// closes via the × button, clicking the backdrop, or ESC. Completes when closed.
await MarkdownBox.Open(noticeMarkdown, title: UI.Tr("Notice"));

// Custom link routing (replaces the default Application.OpenURL):
await MarkdownBox.Open(mailBody, title: mail.Subject,
    onLinkClicked: url => MyRouter.HandleDeepLink(url));
```

- [ ] **Step 4: `### API surface`** 在 `InputBox` 块后追加:

```csharp
public static class MarkdownBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MarkdownBox.ui";
    // Returns a non-generic Awaitable: completes when the box is closed
    // (× button / backdrop click / ESC). No buttons can be configured.
    public static Awaitable Open(
        string markdown, string title = null,
        Action<string> onLinkClicked = null,        // null → Application.OpenURL
        ModalMode mode = ModalMode.Popup,
        Action<IScreen> configure = null,
        CancellationToken ct = default);
}
```

并在 `### Behavior` 一节补一行:MarkdownBox has no result value — title null hides the title row (the `<Markdown>` area expands to fill), the × close button always floats top-right above the content. Resize via `configure` (e.g. `s.Get<Controls.Image>("dialog")`…)。

- [ ] **Step 5: `configure` 钩子那句** "Every builtin `Open` (`MessageBox` / `InputBox` / `Loading`)" 补上 `MarkdownBox`。

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): MarkdownBox 内置模态 API"
```

---

### Task 5: 全量回归 + 收尾验证

**Files:** 无新改动(验证任务)

- [ ] **Step 1: 全量测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: 全部 PASS(EditMode 基线 1496+11 新增;EditorOnly 183;PlayMode 132 —— 以实跑为准)。失败优先怀疑 Unity MCP flake(未保存场景/挂起),重跑或让用户重启 Unity 后再判定。

- [ ] **Step 2: lint 终验**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: 双零退出。

- [ ] **Step 3: 控制台无残留 error**

```
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 4: 收尾**

全绿后用 superpowers:finishing-a-development-branch 技能走分支收尾(推送 + 开 PR 到 main;本仓库禁止直接提交 main)。PR 描述引用 spec `docs~/superpowers/specs/2026-06-10-markdown-box-modal-design.md`,并提请用户做视觉 QA(横竖屏 margin、× 浮层与滚动区重叠、链接点击)。
