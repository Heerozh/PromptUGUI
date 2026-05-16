# Loading Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 加一个 `Loading.Open(text)` 内置模态：业务调用方拿到 `LoadingHandle`，跑后台任务，结束后 `handle.Close()`；视觉是三个 `<Animation fade loop>` 错相脉冲点；ESC 不可关闭；用户可通过 `Loading.XmlSrc` 整体替换默认 XML。

**Architecture:** 新增 `LoadingRequest : ModalRequest<R3.Unit>` + 静态 `Loading` façade + `LoadingHandle` 句柄。`IModalEntry` 加两个内部方法 `SetWaker(Action)` / `ResolveExternally()`——前者让 pump 注入唤醒回调，后者让外部句柄无视当前 modal 状态触发关闭。`UI.Modal.PumpAsync` 加两处 pre-show `if (entry.Resolved) continue` 检查 + 在 `RunBind` 前调 `SetWaker`。`Bind` 对 `<Text id="text">` 用 try/catch 容错，整 XML 都允许用户重写。

**Tech Stack:** Unity 6, R3 (`R3.Unit`), LitMotion (`<Animation fade loop>`), NUnit EditMode, UnityMCP for compile + test runs.

**Spec:** `docs~/superpowers/specs/2026-05-16-loading-modal-design.md`

---

## File Structure

**Create:**
- `Runtime/Application/Modals/LoadingRequest.cs` — 三个 public 类型一处：`LoadingRequest` / `Loading` / `LoadingHandle`（沿用 `MessageBoxRequest.cs` 一文件多类型的现有模式）
- `Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml` — 默认 XML 模板
- `Tests/EditMode/Modals/LoadingTests.cs` — EditMode 测试套

**Modify:**
- `Runtime/Application/Modals/ModalEntry.cs` — `IModalEntry` 接口加 `SetWaker` + `ResolveExternally` + `ModalEntry<TResult>` 实现
- `Runtime/Application/UI.Modal.cs` — `PumpAsync` 加两处 `Resolved` 检查 + `SetWaker` 注入；新增 internal `EnqueueRequest<T>` helper
- `.claude/skills/scripting-promptugui-csharp/SKILL.md` — 在 "Modal dialogs" 一节加 "Loading modal" 小节

**No changes to:**
- `Runtime/Application/Modals/ModalRequest.cs` — 基类签名不变
- `Runtime/Application/Modals/ModalEscapeListener.cs` — ESC listener 行为照旧（Loading 的 `TryEscape` 返回 false 让它静默 no-op）
- `Runtime/Application/Modals/ModalSourceLoader.cs` — Resources 加载流程不变

---

### Task 1: 加 `IModalEntry.SetWaker` + `ResolveExternally` plumbing + pump 改造

**Files:**
- Modify: `Runtime/Application/Modals/ModalEntry.cs:10-69`
- Modify: `Runtime/Application/UI.Modal.cs:32-86`

这一步**先不写 Loading 类型**，只动现有 modal 框架。后面 Task 2 的 Loading 才用到。但因为没法独立测试纯 plumbing（外部没人调），我们的"红"信号是 Task 3 的第一个 Loading 测试——本任务先让代码编译通过。

- [ ] **Step 1: 改 `Runtime/Application/Modals/ModalEntry.cs` — 加接口方法 + 实现**

完整替换文件内容（保留现有结构，加两个 method）：

```csharp
using System;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    internal interface IModalEntry
    {
        public string XmlSrc { get; }
        public void RunBind(IScreen screen, Action onClose);
        public bool TryEscape(Action wakePump);
        public void Cancel(Exception ex);
        public bool Resolved { get; }
        public void SetWaker(Action waker);
        public void ResolveExternally();
    }

    internal sealed class ModalEntry<TResult> : IModalEntry
    {
        private readonly ModalRequest<TResult> _request;
        private readonly AwaitableCompletionSource<TResult> _tcs = new();
        private Action _waker;

        public bool Resolved { get; private set; }
        public string XmlSrc => _request.XmlSrc;
        public Awaitable<TResult> Awaitable => _tcs.Awaitable;

        private ModalEntry(ModalRequest<TResult> request) { _request = request; }

        internal static (IModalEntry entry, Awaitable<TResult> awaitable) Create(
            ModalRequest<TResult> request)
        {
            var e = new ModalEntry<TResult>(request);
            return (e, e._tcs.Awaitable);
        }

        public void RunBind(IScreen screen, Action onClose)
        {
            _request.Bind(screen, result =>
            {
                if (Resolved) return;
                Resolved = true;
                _tcs.TrySetResult(result);
                onClose?.Invoke();
            });
        }

        public bool TryEscape(Action wakePump)
        {
            if (Resolved) return false;
            if (!_request.TryEscape(out var r)) return false;
            Resolved = true;
            _tcs.TrySetResult(r);
            wakePump?.Invoke();
            return true;
        }

        public void Cancel(Exception ex)
        {
            if (Resolved) return;
            Resolved = true;
            _tcs.TrySetException(ex);
        }

        public void SetWaker(Action waker) => _waker = waker;

        public void ResolveExternally()
        {
            if (Resolved) return;
            Resolved = true;
            _tcs.TrySetResult(default!);
            _waker?.Invoke();
        }
    }
}
```

- [ ] **Step 2: 改 `Runtime/Application/UI.Modal.cs` — pump 加两处 Resolved 检查 + SetWaker 注入 + EnqueueRequest helper**

定位 `PumpAsync` 方法（行 32-86 附近）。整个 `while` 循环替换为：

```csharp
while (_queue.Count > 0)
{
    var entry = _queue.Dequeue();
    if (entry.Resolved) continue;
    _current = entry;
    _currentScreenName = entry.XmlSrc;
    _currentWaiter = new AwaitableCompletionSource<bool>();
    try
    {
        if (!_loadedSrcs.Contains(entry.XmlSrc))
        {
            var xml = await ModalSourceLoader.LoadAsync(entry.XmlSrc);
            LoadDocument(entry.XmlSrc, xml);
            _loadedSrcs.Add(entry.XmlSrc);
        }
        if (entry.Resolved) continue;

        var screen = Open(entry.XmlSrc);
        var canvas = screen.RootGameObject.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrderBase;

        var waiter = _currentWaiter;
        var captured = entry;
        captured.SetWaker(() => waiter.TrySetResult(true));
        captured.RunBind(screen, () => waiter.TrySetResult(true));

        var listener = screen.RootGameObject
            .AddComponent<ModalEscapeListener>();
        listener.OnEscape = () =>
            captured.TryEscape(() => waiter.TrySetResult(true));

        await waiter.Awaitable;

        if (entry.Resolved && _open.ContainsKey(entry.XmlSrc))
            Close(entry.XmlSrc);
    }
    catch (Exception ex)
    {
        entry.Cancel(ex);
        if (_open.ContainsKey(entry.XmlSrc))
            Close(entry.XmlSrc);
    }
    finally
    {
        _current = null;
        _currentScreenName = null;
        _currentWaiter = null;
    }
}
```

注意三处新增：
1. `if (entry.Resolved) continue;` 在 Dequeue 之后立即
2. `if (entry.Resolved) continue;` 在 LoadAsync 之后立即
3. `captured.SetWaker(() => waiter.TrySetResult(true));` 在 `RunBind` 之前

在 `OpenAsync` 方法之后（行 30 附近），加一个 internal helper：

```csharp
internal static IModalEntry EnqueueRequest<TResult>(ModalRequest<TResult> request)
{
    if (request == null) throw new ArgumentNullException(nameof(request));
    var (entry, _) = ModalEntry<TResult>.Create(request);
    _queue.Enqueue(entry);
    if (!_pumping) _ = PumpAsync();
    return entry;
}
```

- [ ] **Step 3: 编译 — 通过 UnityMCP refresh**

调用：
```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

期望：无 error。如果有，定位 fix（常见：拼写 / using 漏 / 接口实现遗漏）。

- [ ] **Step 4: 跑现有 modal 测试，确认没破坏既有行为**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Modal")
```

期望：所有 `ModalQueueTests` / `MessageBoxRequestTests` / `ModalCancelTests` / `ModalReSolveTests` / `ModalSourceLoaderTests` / `MessageBoxStaticTests` / `MsgBtnFlagsTests` / `UIUnloadDocumentTests` 全绿（pump 改动是加法，不应破坏现有行为）。

- [ ] **Step 5: Lint + 提交**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

```bash
git add Runtime/Application/Modals/ModalEntry.cs Runtime/Application/UI.Modal.cs
git commit -m "feat(modal): IModalEntry.SetWaker + ResolveExternally + pump pre-show skip

Pump 加两处 Resolved 检查（dequeue 后 / LoadAsync 后）+ SetWaker 注入唤醒回调。
为 Loading 模态的外部主动关闭路径铺底；现有 MessageBox 流程不变。"
```

---

### Task 2: 创建 `LoadingRequest` + `Loading` + `LoadingHandle` 类型骨架

**Files:**
- Create: `Runtime/Application/Modals/LoadingRequest.cs`

- [ ] **Step 1: 创建 LoadingRequest.cs**

写 `Runtime/Application/Modals/LoadingRequest.cs`：

```csharp
using System;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class LoadingRequest : ModalRequest<Unit>
    {
        public string Text;

        public override string XmlSrc => Loading.XmlSrc;

        public override void Bind(IScreen screen, Action<Unit> close)
        {
            try
            {
                var textCtl = screen.Get<PromptUGUI.Controls.Text>("text");
                if (string.IsNullOrEmpty(Text)) textCtl.GameObject.SetActive(false);
                else textCtl.TextValue = Text;
            }
            catch (System.Collections.Generic.KeyNotFoundException) { /* text 元素可选 */ }
        }
    }

    public sealed class LoadingHandle
    {
        private readonly IModalEntry _entry;
        private bool _closed;

        public bool IsClosed => _closed;

        internal LoadingHandle(IModalEntry entry) => _entry = entry;

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _entry.ResolveExternally();
        }
    }

    public static class Loading
    {
        // .ui 后缀对齐 MessageBox：Unity 只剥离 .ui.xml 文件名的最后 .xml
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";

        public static LoadingHandle Open(string text = null)
        {
            var entry = UI.Modal.EnqueueRequest(new LoadingRequest { Text = text });
            return new LoadingHandle(entry);
        }
    }
}
```

- [ ] **Step 2: 编译通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

期望：无 error。`LoadingHandle` 接受 `internal` 类型 `IModalEntry` 作为 ctor 参数，对外公开但只能通过 `Loading.Open` 构造（ctor 是 internal）。

- [ ] **Step 3: 跑现有测试，再次确认没破坏**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Modal")
```

期望：全绿。

- [ ] **Step 4: Lint + 提交**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

```bash
git add Runtime/Application/Modals/LoadingRequest.cs
git commit -m "feat(modal): LoadingRequest + Loading façade + LoadingHandle

ModalRequest<R3.Unit> 子类 + 句柄 + 静态 Open(text) 入口。
Bind 用 try/catch 把 <Text id=text> 做成可选契约（让用户自定义 XML 更自由）。"
```

---

### Task 3: 写第一个 Loading 测试 — Open + Close 闭环

**Files:**
- Create: `Tests/EditMode/Modals/LoadingTests.cs`

- [ ] **Step 1: 写 LoadingTests 骨架 + 第一个测试（先红）**

写 `Tests/EditMode/Modals/LoadingTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class LoadingTests : ModalTestFixture
    {
        private const string LoadingTestXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading1'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame id='dialog' anchor='center' size='320x160'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='text' fontSize='16'/>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        public override void SetUp()
        {
            base.SetUp();
            Files["test/Loading1"] = LoadingTestXml;
            Loading.XmlSrc = "test/Loading1";
        }

        [Test]
        public void Open_returns_handle_and_modal_is_shown_with_text()
        {
            var handle = Loading.Open("加载中...");

            Assert.IsNotNull(handle);
            Assert.IsFalse(handle.IsClosed);
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            var screen = UI.Get("test/Loading1");
            Assert.IsNotNull(screen, "Loading screen 应该已经被 pump 实例化");

            var text = screen.Get<PromptUGUI.Controls.Text>("text");
            Assert.IsTrue(text.GameObject.activeSelf);
            Assert.AreEqual("加载中...", text.TmpComponent.text);
        }

        [Test]
        public void Close_dismisses_modal_and_marks_handle_closed()
        {
            var handle = Loading.Open("hi");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            handle.Close();

            Assert.IsTrue(handle.IsClosed);
            Assert.IsFalse(UI.Modal.IsAnyOpen);
            Assert.IsNull(UI.Get("test/Loading1"),
                "Loading screen 应该已经被 pump 关闭");
        }
    }
}
```

- [ ] **Step 2: 编译 + 跑测试 — 期望绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingTests")
```

期望：两个测试绿。如果红，问题最可能在 Task 1 / Task 2 的 plumbing —— 看具体 error message 定位。

- [ ] **Step 3: 提交**

```bash
git add Tests/EditMode/Modals/LoadingTests.cs
git commit -m "test(modal): LoadingTests open + close 闭环"
```

---

### Task 4: 加 pre-show close 测试 — Loading 被 MessageBox 挡住时关闭

**Files:**
- Modify: `Tests/EditMode/Modals/LoadingTests.cs`

这一步验证 Task 1 加的 pump pre-show skip 逻辑：MessageBox 阻塞 pump 时，Loading 排在队列里；调用 handle.Close() 标记 Resolved；MessageBox 关闭后 pump 转到 Loading entry，看到 Resolved=true 直接跳过，**根本不实例化 Loading screen**。

- [ ] **Step 1: 加测试 `Close_before_pump_skips_modal_instantiation`**

在 `LoadingTests.cs` 类末尾加：

```csharp
[Test]
public void Close_before_pump_skips_modal_instantiation()
{
    // MessageBox 先开 → pump 卡在它的 await waiter 上
    var mboxTask = UI.Modal.OpenAsync(new MessageBoxRequest
    {
        Text = "first",
        Buttons = MsgBtn.OK,
    });

    // Loading 入队，但 pump 没轮到它（仍在处理 MessageBox）
    var loading = Loading.Open("queued");
    Assert.AreEqual(2, UI.Modal.QueuedCount);
    Assert.IsNull(UI.Get("test/Loading1"),
        "Loading 还在队列里，screen 还没创建");

    // 关闭 Loading handle —— 走 ResolveExternally → entry.Resolved=true
    loading.Close();
    Assert.IsTrue(loading.IsClosed);
    Assert.IsNull(UI.Get("test/Loading1"),
        "Close() 在 pre-show 不应该实例化 screen");

    // 关掉 MessageBox → pump 转到 Loading entry，看到 Resolved=true，直接 continue
    UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
    Assert.AreEqual(MsgBtn.OK, mboxTask.GetAwaiter().GetResult());

    Assert.IsFalse(UI.Modal.IsAnyOpen);
    Assert.IsNull(UI.Get("test/Loading1"),
        "Loading screen 整个生命周期都不应该被创建过");
}
```

- [ ] **Step 2: 跑测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="Close_before_pump")
```

期望：绿。如果红：检查 Task 1 Step 2 的 pump `if (entry.Resolved) continue;` 是否真的加在了 Dequeue 后那个位置。

- [ ] **Step 3: 提交**

```bash
git add Tests/EditMode/Modals/LoadingTests.cs
git commit -m "test(modal): Loading pre-show close 跳过 screen 实例化"
```

---

### Task 5: 加幂等 / ESC no-op / 文案空串 / 自定义 XML 容错 这一组测试

**Files:**
- Modify: `Tests/EditMode/Modals/LoadingTests.cs`

- [ ] **Step 1: 加四个测试**

在 `LoadingTests.cs` 类末尾加：

```csharp
[Test]
public void Close_is_idempotent()
{
    var handle = Loading.Open("hi");
    handle.Close();
    Assert.IsTrue(handle.IsClosed);

    // 第二次 Close 不应该抛
    Assert.DoesNotThrow(() => handle.Close());
    Assert.IsTrue(handle.IsClosed);
    Assert.IsFalse(UI.Modal.IsAnyOpen);
}

[Test]
public void Text_null_hides_text_node()
{
    Loading.Open(null);
    var text = UI.Get("test/Loading1").Get<PromptUGUI.Controls.Text>("text");
    Assert.IsFalse(text.GameObject.activeSelf);
}

[Test]
public void Text_empty_hides_text_node()
{
    Loading.Open("");
    var text = UI.Get("test/Loading1").Get<PromptUGUI.Controls.Text>("text");
    Assert.IsFalse(text.GameObject.activeSelf);
}

[Test]
public void TryEscape_listener_does_not_close_loading()
{
    var handle = Loading.Open("press ESC and see nothing");
    var listener = UI.Get("test/Loading1")
        .RootGameObject.GetComponent<ModalEscapeListener>();
    Assert.IsNotNull(listener, "Pump 必须给 Loading screen 也挂 ModalEscapeListener");

    listener.FireForTests();

    Assert.IsTrue(UI.Modal.IsAnyOpen, "Loading 应该仍然显示");
    Assert.IsFalse(handle.IsClosed);
    UI.Modal.CloseAll();   // teardown 干净点
}

[Test]
public void Custom_xml_without_text_id_does_not_throw()
{
    // 模拟用户自定义 XML 时把 text 元素删了的场景
    const string customXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading2'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame anchor='center' size='200x100'>
      <Image anchor='stretch' color='white'/>
    </Frame>
  </Screen>
</PromptUGUI>";
    Files["test/Loading2"] = customXml;
    Loading.XmlSrc = "test/Loading2";

    var handle = Loading.Open("仍然传 text 但 XML 没 text 元素");
    Assert.IsNotNull(handle, "Bind 不应该抛 KeyNotFoundException");
    Assert.IsTrue(UI.Modal.IsAnyOpen);

    handle.Close();
    Assert.IsFalse(UI.Modal.IsAnyOpen);
}
```

- [ ] **Step 2: 跑测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingTests")
```

期望：所有 LoadingTests（含前面 task 加的）全绿。

- [ ] **Step 3: 提交**

```bash
git add Tests/EditMode/Modals/LoadingTests.cs
git commit -m "test(modal): Loading 幂等/ESC/空文案/自定义 XML id 容错"
```

---

### Task 6: 加 FIFO 混排 + UnloadAll teardown 测试

**Files:**
- Modify: `Tests/EditMode/Modals/LoadingTests.cs`

- [ ] **Step 1: 加两个测试**

在 `LoadingTests.cs` 类末尾加：

```csharp
[Test]
public void Mixed_with_MessageBox_respects_FIFO_queue()
{
    // 顺序：Loading 先 → MessageBox 后。Loading 显示，MessageBox 排队
    var loading = Loading.Open("step 1");
    var mboxTask = UI.Modal.OpenAsync(new MessageBoxRequest
    {
        Text = "step 2",
        Buttons = MsgBtn.OK,
    });

    Assert.AreEqual(2, UI.Modal.QueuedCount);
    Assert.IsNotNull(UI.Get("test/Loading1"), "Loading 应该先显示");
    Assert.IsNull(UI.Get("test/Box1"), "MessageBox 应该还在队列");

    // 关 Loading → pump 切到 MessageBox
    loading.Close();
    Assert.IsTrue(loading.IsClosed);
    Assert.IsNull(UI.Get("test/Loading1"), "Loading 应该已关闭");
    Assert.IsNotNull(UI.Get("test/Box1"), "MessageBox 应该接着显示");

    // 关 MessageBox
    UI.Get("test/Box1").Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
    Assert.AreEqual(MsgBtn.OK, mboxTask.GetAwaiter().GetResult());
    Assert.IsFalse(UI.Modal.IsAnyOpen);
}

[Test]
public void UnloadAll_cancels_loading_and_handle_close_after_is_noop()
{
    var handle = Loading.Open("about to be torn down");
    Assert.IsTrue(UI.Modal.IsAnyOpen);

    // 模拟 UI 全拆除路径（ResetForTests 内部会调 CancelAllForTeardown）
    UI.ResetForTests();

    // 之后 handle.Close 不应该抛 / 不应该副作用
    Assert.DoesNotThrow(() => handle.Close());
    Assert.IsTrue(handle.IsClosed);
}
```

- [ ] **Step 2: 跑测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingTests")
```

期望：所有 LoadingTests 全绿（应该 9 个测试现在）。

- [ ] **Step 3: 提交**

```bash
git add Tests/EditMode/Modals/LoadingTests.cs
git commit -m "test(modal): Loading 跟 MessageBox 混排 FIFO + teardown 路径"
```

---

### Task 7: 创建默认 `Loading.ui.xml` + UIXmlLint 校验

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml`

- [ ] **Step 1: 写默认 XML 模板**

创建 `Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/Loading.ui" reference="1920x1080" reference.portrait="1080x1920">
    <Image id="backdrop" anchor="stretch" color="#000000C0"/>

    <Frame id="dialog" anchor="center" size="320x160">
      <Image anchor="stretch" sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"/>
      <VStack anchor="stretch" margin="20" spacing="12">

        <!-- HStack 不加 anchor: VStack 是 layout group, child anchor 会被 PUI-LAYOUT-ANCHOR lint
             报错. VStack 默认 childAlignment=UpperCenter 已经水平居中 width=80 的 HStack. -->
        <HStack height="20" spacing="10" width="80">
          <Animation fade="0.25:1" duration="0.5s" delay="0s"    easing="in-out-quad" on="loop">
            <Image width="14" height="14" color="white"/>
          </Animation>
          <Animation fade="0.25:1" duration="0.5s" delay="0.15s" easing="in-out-quad" on="loop">
            <Image width="14" height="14" color="white"/>
          </Animation>
          <Animation fade="0.25:1" duration="0.5s" delay="0.3s"  easing="in-out-quad" on="loop">
            <Image width="14" height="14" color="white"/>
          </Animation>
        </HStack>

        <Text id="text" width="stretch" height="stretch" fontSize="16"/>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>
```

- [ ] **Step 2: UIXmlLint 校验**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml
```

期望：exit code 0，no errors。如果报 `PUI-LAYOUT-ANCHOR`：layout group（VStack/HStack/Grid）的子节点不能写 `anchor=`。本模板已避免此问题（HStack 在 VStack 里不写 anchor，靠 VStack 默认的 `childAlignment=UpperCenter` 水平居中）。出错时检查是否手动加了 anchor 属性。

- [ ] **Step 3: refresh Unity → 让 Resources 重新扫描新增的 TextAsset**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

期望：无 error。新 .ui.xml 被作为 TextAsset 导入。

- [ ] **Step 4: 加一个测试验证默认 XML 路径**

在 `LoadingTests.cs` 类末尾加：

```csharp
[Test]
public void Default_xml_src_loads_builtin_template()
{
    // 把 XmlSrc 切回默认（base.SetUp 把它改成了 test/Loading1）
    Loading.XmlSrc = "PromptUGUI/Modals/Loading.ui";

    var handle = Loading.Open("from real template");
    Assert.IsNotNull(handle);
    Assert.IsTrue(UI.Modal.IsAnyOpen);

    var screen = UI.Get("PromptUGUI/Modals/Loading.ui");
    Assert.IsNotNull(screen, "默认 Loading.ui.xml 应该能从 Resources 加载");

    var text = screen.Get<PromptUGUI.Controls.Text>("text");
    Assert.AreEqual("from real template", text.TmpComponent.text);

    handle.Close();
}
```

- [ ] **Step 5: 跑测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingTests")
```

期望：全部 LoadingTests（10 个）绿。如果 `Default_xml_src_loads_builtin_template` 红，最可能原因：
- Unity 还没 import 新 .ui.xml → 再跑一次 refresh
- 模板里某个属性导致 XML 解析失败 → `read_console` 看 ParseException

- [ ] **Step 6: 提交**

```bash
git add Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml Runtime/Resources/PromptUGUI/Modals/Loading.ui.xml.meta Tests/EditMode/Modals/LoadingTests.cs
git commit -m "feat(modal): 默认 Loading.ui.xml 模板 + 三点脉冲动画

三个 <Animation fade='0.25:1' on='loop'> 错相 0.15s yoyo。
9slice_round 边框沿用 MessageBox 视觉一致性。"
```

---

### Task 8: 更新 `scripting-promptugui-csharp/SKILL.md`

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md` —— 在 "Overriding the builtin MessageBox layout" 小节结束（当前 line 385 末尾）和下一个 `## <Trigger> and <Animation> from C#` 之间插入新小节

- [ ] **Step 1: 在 cheatsheet 表（line 312-315）追加 Loading 行**

定位 line 315 (`UI.Modal.SortingOrderBase = 1000             default`)，在它后面插入一行。**完全粘贴下面这行**（前导 15 个空格、双空格分隔左右列，沿用现有对齐）：

```
               var h = Loading.Open(text); h.Close()         loading spinner (no result)
```

最终四行 MODAL 区块应该是：

```
MODAL          var r = await MessageBox.Open(text, MsgBtn.OK|MsgBtn.Cancel, icon, title)
               UI.Modal.OpenAsync(new MyRequest())          custom ModalRequest<T>
               UI.Modal.CloseAll()                          cancel all pending
               UI.Modal.SortingOrderBase = 1000             default
               var h = Loading.Open(text); h.Close()         loading spinner (no result)
```

- [ ] **Step 2: 在 "Overriding the builtin MessageBox layout" 小节末尾（line 385 后）插入 Loading 小节**

具体定位是 line 385 的内容：

```
Your XML must declare a Screen with these ids: `text`, `title`, `ok`, `cancel`, `yes`, `no`, `close`. An optional `icon` id is supported but not required (Bind tolerates missing icon node). If you want icon support, your XML must include `<Icon id="icon" name="placeholder:something"/>` because PromptUGUI's parser requires the `name=` attribute on `<Icon>` elements.
```

之后空一行，插入：

````markdown
### Loading modal

A non-blocking "loading" modal: blocks UI while async work runs, then your code closes it. **Does not accept user input** (ESC cannot dismiss it).

```csharp
using PromptUGUI.Application.Modals;

var loading = Loading.Open(UI.Tr("Loading..."));
try
{
    await DoWorkAsync();
    var data = await FetchAsync();
}
finally
{
    loading.Close();   // idempotent — safe to call multiple times
}
```

Differences from `MessageBox.Open`:

- `Loading.Open(text)` returns a `LoadingHandle` **synchronously** — callers do not `await` the modal; they `await` their own background work and then call `handle.Close()`
- No `TResult` — the modal is not closed by user input
- `text` is optional; pass `null` or `""` to render the spinner alone
- Shares the FIFO queue with MessageBox: open Loading + MessageBox in either order, they take turns
- `handle.Close()` is safe after `UI.UnloadAll()` / `UI.ResetForTests()` — no-op once the entry is cancelled

#### Overriding the builtin Loading layout

```csharp
Loading.XmlSrc = "MyUI/Modals/PixelLoading.ui";   // resolved by UI.SourceResolver
```

Default key is `"PromptUGUI/Modals/Loading.ui"` (note the `.ui` suffix — same Unity multi-dot stripping caveat as MessageBox).

**id contract for custom XML**: only `<Text id="text">` is recognized by `Bind`, and even that is **optional** — Bind catches `KeyNotFoundException` so XML without a `text` element works (e.g. pure spinner with no caption). All other elements (backdrop, container Frame, spinner visuals) have no id contract — design them freely.
````

- [ ] **Step 3: 提交**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): Loading modal 用法 + Loading.XmlSrc 覆盖契约"
```

---

## 验收

跑完所有任务后，最后一次全套检查：

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

期望：
- `dotnet format` exit 0
- UIXmlLint 全部 .ui.xml 没有 error
- Unity console 无 error
- EditMode 测试全绿（含原有 + 10 个新 LoadingTests）
- spec §9 列出的 6 条验收标准全部满足
