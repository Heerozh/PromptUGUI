# Modal 分层重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 Loading 从 modal 队列抽成独立下层 overlay,并把 dialog 系统从单活跃 FIFO 队列改成"显示栈 + 等待队列",新增 `ModalMode` { Popup(默认)、Queued } —— 修掉 Loading↔MessageBox 死锁,支持 modal 叠加(嵌套对话框)。

**Architecture:** 两个独立子系统。`UI.Modal` = dialog 栈(`_stack` + `_waiting` 等待队列 + `_pending` 待实例化队列),`Popup` 压栈、`Queued` 等栈空。`LoadingOverlay` = 独立 overlay,坐在 dialog 之下的 sortingOrder 层带。两者共用 `ModalDocCache`(XML 加载缓存)与 `UI.OpenModalScreen`/`CloseModalScreen`(多实例 Screen 实例化)。modal/overlay Screen 注册进 `UI._open`,key 为 `{src}#m{n}` —— 同一份 XML 可叠多份实例,`OwnerScreenOf` / sprite-ReSolve 自动覆盖。

**Tech Stack:** C# 9 / Unity 6 `Awaitable`(不用 `Task`)、R3、NUnit、Unity MCP 跑测试。

**设计依据:** `docs~/superpowers/specs/2026-05-20-modal-layering-design.md`。

### 超出 spec 的实现级决策(spec 未覆盖,本 plan 定案)

1. **多实例同 src 叠加**:两个 modal 用同一 `XmlSrc`(如 MessageBox→MessageBox 二次确认)要能各自一份 Screen。`UI._open` 对 modal 用 key `{src}#m{n}`(`n` 单调递增)。后果:`UI.Get(xmlSrc)` 不再能拿到 modal Screen —— 测试改用新 internal seam `UI.Modal.TopScreen`。普通 Screen 的 `UI.Get` / `UI.Open` 不变。
2. **`ModalDocCache`**:新内部静态类,`UI.Modal` 与 `LoadingOverlay` 共用的 XML 加载缓存(取代旧 `UI.Modal._loadedSrcs`)。
3. **modal hot reload**:旧 `UI.Modal.InvalidateCacheForEditor` 实际从未被 `UIAssetPostprocessor` 调用(grep 确认),是半成品。本 plan 把它平移为 `ModalDocCache.Invalidate`(Editor-only),不新接 postprocessor —— 不扩大作用域。

---

## 文件结构

**新增:**
- `Runtime/Application/Modals/ModalMode.cs` — `enum ModalMode { Popup, Queued }`
- `Runtime/Application/Modals/ModalDocCache.cs` — 共用 XML 加载缓存
- `Runtime/Application/Modals/LoadingOverlay.cs` — Loading overlay 管理器
- `Runtime/Application/Modals/Loading.cs` — `Loading` 门面 + `LoadingHandle`(取代 `LoadingRequest.cs`)
- `Tests/EditMode/Modals/LoadingOverlayTests.cs`
- `Tests/EditMode/Modals/ModalStackTests.cs` — Popup 栈语义
- `Tests/EditMode/Modals/ModalQueuedModeTests.cs` — Queued 语义

**修改:**
- `Runtime/Application/UI.cs` — 加 `OpenModalScreen`/`CloseModalScreen`;teardown 接 `LoadingOverlay` + `ModalDocCache`
- `Runtime/Application/UI.Modal.cs` — 重写为栈
- `Runtime/Application/Modals/ModalEntry.cs` — 删 `ResolveExternally`/`SetWaker`
- `Runtime/Application/Modals/MessageBoxRequest.cs` — `MessageBox.Open` 加 `mode` 参数
- 多个测试文件(逐 Task 列出)

**删除:**
- `Runtime/Application/Modals/LoadingRequest.cs`(`LoadingRequest` 类删除;`Loading`/`LoadingHandle` 迁到 `Loading.cs`)

每个 Task 完成后:`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认无编译错误 → 跑测试 → `dotnet format --verify-no-changes --severity warn .lint/PromptUGUI.Lint.slnx`(从 `.lint/`)→ commit。

---

## Task 1: `ModalMode` 枚举

**Files:**
- Create: `Runtime/Application/Modals/ModalMode.cs`

- [ ] **Step 1: 创建枚举**

```csharp
namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// dialog 的显示行为,由每次 <see cref="UI.Modal.OpenAsync"/> 选择。
    /// </summary>
    public enum ModalMode
    {
        /// <summary>立刻压到显示栈顶。默认。</summary>
        Popup = 0,

        /// <summary>等显示栈清空后作为新栈底显示;多个 Queued 之间 FIFO。</summary>
        Queued = 1,
    }
}
```

- [ ] **Step 2: 编译验证**

`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `mcp__UnityMCP__read_console(action="get", types=["error"])`。预期:无错误。

- [ ] **Step 3: Commit**

```bash
git add Runtime/Application/Modals/ModalMode.cs Runtime/Application/Modals/ModalMode.cs.meta
git commit -m "feat: add ModalMode enum (Popup/Queued)"
```

> `.meta` 由 Unity 在 refresh 时生成;commit 时一并加入。后续 Task 同理,不再赘述。

---

## Task 2: `ModalDocCache` + `UI.OpenModalScreen` / `CloseModalScreen`

modal & overlay 共用的底座:XML 加载缓存 + 多实例 Screen 实例化。

**Files:**
- Create: `Runtime/Application/Modals/ModalDocCache.cs`
- Modify: `Runtime/Application/UI.cs`(在 `Close` 方法之后插入)
- Test: `Tests/EditMode/Modals/ModalDocCacheTests.cs`

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Modals/ModalDocCacheTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalDocCacheTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Inst'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(src == "test/Inst" ? Xml : null);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void EnsureLoaded_then_OpenModalScreen_twice_yields_two_distinct_screens()
        {
            ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult();

            var (s1, k1) = UI.OpenModalScreen("test/Inst");
            var (s2, k2) = UI.OpenModalScreen("test/Inst");

            Assert.AreNotSame(s1, s2, "同一份 XML 应能实例化出两份独立 Screen");
            Assert.AreNotEqual(k1, k2, "instance key 必须唯一");
            Assert.IsNotNull(s1.RootGameObject);
            Assert.IsNotNull(s2.RootGameObject);

            UI.CloseModalScreen(k1);
            UI.CloseModalScreen(k2);
            Assert.IsNull(UI.Get(k1));
        }

        [Test]
        public void EnsureLoaded_is_idempotent()
        {
            ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult();
            Assert.DoesNotThrow(() =>
                ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult(),
                "第二次 EnsureLoaded 不应重复 LoadDocument 而抛 already loaded");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalDocCacheTests")`。预期:编译失败(`ModalDocCache` / `UI.OpenModalScreen` 不存在)。

- [ ] **Step 3: 创建 `ModalDocCache.cs`**

```csharp
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// 内置 / 自定义 modal & overlay XML 的加载缓存。
    /// <see cref="UI.Modal"/> 与 <see cref="LoadingOverlay"/> 共用。
    /// </summary>
    internal static class ModalDocCache
    {
        private static readonly System.Collections.Generic.HashSet<string> _loaded = new();

        internal static async Awaitable EnsureLoaded(string src)
        {
            if (_loaded.Contains(src)) return;
            var xml = await ModalSourceLoader.LoadAsync(src);
            if (_loaded.Contains(src)) return;      // 并发双检:await 期间别人加载完了
            UI.LoadDocument(src, xml);
            _loaded.Add(src);
        }

        internal static void Clear() => _loaded.Clear();

#if UNITY_EDITOR
        internal static void Invalidate(string src)
        {
            if (string.IsNullOrEmpty(src)) return;
            if (_loaded.Remove(src)) UI.UnloadDocument(src);
        }
#endif
    }
}
```

- [ ] **Step 4: 在 `UI.cs` 加 `OpenModalScreen` / `CloseModalScreen`**

在 `UI.cs` 的 `Close` 方法(以 `_open.Remove(screenName);` `}` 结尾)之后、`Get` 方法之前插入:

```csharp
        private static int _modalInstanceSeq;

        /// <summary>
        /// modal / overlay 专用:从已加载的 _docs 实例化一份 Screen,登记进 _open
        /// 用唯一 key(`{docName}#m{n}`),使同一份 XML 可叠多份实例。
        /// 普通 Screen 仍走 Open(name)。
        /// </summary>
        internal static (Screen screen, string key) OpenModalScreen(string docName)
        {
            if (!_docs.TryGetValue(docName, out var def))
                throw new System.InvalidOperationException(
                    $"Modal screen '{docName}' not loaded; call LoadDocument first");
            var key = docName + "#m" + (++_modalInstanceSeq);
            var inst = new ScreenInstantiator(Registry, VariantStore);
            var screen = new Screen(def, inst, Registry, VariantStore);
            _open[key] = screen;                 // Open() 前登记,让 OwnerScreenOf 反查得到
            try { screen.Open(); }
            catch { _open.Remove(key); throw; }
            return (screen, key);
        }

        internal static void CloseModalScreen(string key)
        {
            if (_open.TryGetValue(key, out var s))
            {
                s.Close();
                _open.Remove(key);
            }
        }
```

并在 `UnloadAll` 与 `ResetForTests` 里接 `ModalDocCache` 的清理 —— 两者都 `_docs.Clear()`,XML 加载缓存必须同步清空,否则下次 `EnsureLoaded` 命中陈旧的 `_loaded` 会跳过 `LoadDocument`、与 `_docs` 不一致(也会导致 `ModalDocCacheTests` 两个用例互相污染)。在 `UI.cs` 的 `UnloadAll` 与 `ResetForTests` 里,各自在 `Modal.CancelAllForTeardown();` 之后插入一行 `Modals.ModalDocCache.Clear();`:

```csharp
        public static void UnloadAll()
        {
            Modal.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();          // ← 新增
            foreach (var s in _open.Values) s.Close();
            // ...其余不变
        }
```

```csharp
        internal static void ResetForTests()
        {
            // ...
            Modal.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();          // ← 新增
            foreach (var s in _open.Values) s.Close();
            // ...其余不变
        }
```

(Task 8 会在这行之前再插入 `LoadingOverlay.CancelAllForTeardown();`。)

- [ ] **Step 5: 跑测试确认通过**

`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console` 确认无错 → `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalDocCacheTests")`。预期:2 passed。

- [ ] **Step 6: lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx && cd ..
git add Runtime/Application/Modals/ModalDocCache.cs Runtime/Application/Modals/ModalDocCache.cs.meta Runtime/Application/UI.cs Tests/EditMode/Modals/ModalDocCacheTests.cs Tests/EditMode/Modals/ModalDocCacheTests.cs.meta
git commit -m "feat: ModalDocCache + UI.OpenModalScreen for multi-instance modal screens"
```

---

## Task 3: `LoadingOverlay`

Loading overlay 管理器 —— Loading 的真正实现,独立于 `UI.Modal`。

**Files:**
- Create: `Runtime/Application/Modals/LoadingOverlay.cs`
- Test: `Tests/EditMode/Modals/LoadingOverlayTests.cs`

> 本 Task 引入对 `Loading.XmlSrc` 的引用。`Loading` 类此刻还在 `LoadingRequest.cs` 里(Task 4 才迁移),`Loading.XmlSrc` 已存在,可直接引用,编译通过。

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Modals/LoadingOverlayTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class LoadingOverlayTests : ModalTestFixture
    {
        private const string LoadingXml = @"<?xml version='1.0' encoding='utf-8'?>
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
            Files["test/Loading1"] = LoadingXml;
            Loading.XmlSrc = "test/Loading1";
        }

        [Test]
        public void Open_shows_overlay_with_text()
        {
            var handle = Loading.Open("加载中...");

            Assert.IsNotNull(handle);
            Assert.IsFalse(handle.IsClosed);
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);

            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            var text = screen.Get<PromptUGUI.Controls.Text>("text");
            Assert.IsTrue(text.GameObject.activeSelf);
            Assert.AreEqual("加载中...", text.TmpComponent.text);
        }

        [Test]
        public void Close_destroys_overlay_and_marks_handle()
        {
            var handle = Loading.Open("hi");
            handle.Close();

            Assert.IsTrue(handle.IsClosed);
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }

        [Test]
        public void Close_is_idempotent()
        {
            var handle = Loading.Open("hi");
            handle.Close();
            Assert.DoesNotThrow(() => handle.Close());
            Assert.IsTrue(handle.IsClosed);
        }

        [Test]
        public void Concurrent_opens_each_get_their_own_overlay()
        {
            var h1 = Loading.Open("one");
            var h2 = Loading.Open("two");

            Assert.AreEqual(2, LoadingOverlay.ActiveCount);

            h1.Close();
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);
            Assert.IsFalse(h2.IsClosed);

            h2.Close();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }

        [Test]
        public void Text_null_hides_text_node()
        {
            Loading.Open(null);
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            Assert.IsFalse(screen.Get<PromptUGUI.Controls.Text>("text").GameObject.activeSelf);
        }

        [Test]
        public void Custom_xml_without_text_id_does_not_throw()
        {
            const string custom = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading2'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
    <Frame anchor='center' size='200x100'><Image anchor='stretch' color='white'/></Frame>
  </Screen>
</PromptUGUI>";
            Files["test/Loading2"] = custom;
            Loading.XmlSrc = "test/Loading2";

            var handle = Loading.Open("text 传了但 XML 没 text 元素");
            Assert.IsNotNull(handle);
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);
            handle.Close();
        }

        [Test]
        public void Overlay_has_no_escape_listener()
        {
            Loading.Open("press ESC, nothing");
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            Assert.IsNull(screen.RootGameObject.GetComponent<ModalEscapeListener>(),
                "Loading overlay 不响应 ESC,不应挂 ModalEscapeListener");
        }

        [Test]
        public void SortingOrder_below_modal_band()
        {
            Loading.Open("x");
            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.IsTrue(canvas.overrideSorting);
            Assert.AreEqual(LoadingOverlay.SortingOrder, canvas.sortingOrder);
            Assert.Less(canvas.sortingOrder, UI.Modal.SortingOrderBase,
                "Loading 必须在 dialog 之下");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingOverlayTests")`。预期:编译失败(`LoadingOverlay` 不存在)。

- [ ] **Step 3: 创建 `LoadingOverlay.cs`**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// Loading overlay 子系统。独立于 dialog 栈,坐在 dialog 之下的 sortingOrder
    /// 层带。每个 <see cref="Loading.Open"/> 各自一份 overlay Screen,由
    /// <see cref="LoadingHandle"/> 一一对应控制关闭。
    /// </summary>
    internal static class LoadingOverlay
    {
        internal sealed class LoadingEntry
        {
            public string Src;
            public string Text;
            public LoadingHandle Handle;
            public Screen Screen;     // 实例化前为 null
            public string Key;        // _open instance key,实例化前为 null
            public bool Closed;
        }

        private static readonly List<LoadingEntry> _entries = new();
        private static readonly Queue<LoadingEntry> _pending = new();
        private static bool _materializing;

        /// <summary>overlay 的 sortingOrder。须低于 <see cref="UI.Modal.SortingOrderBase"/>。</summary>
        public static int SortingOrder { get; set; } = 500;

        internal static int ActiveCount => _entries.Count;

        internal static IEnumerable<Screen> ActiveScreens
        {
            get
            {
                foreach (var e in _entries)
                    if (e.Screen != null) yield return e.Screen;
            }
        }

        internal static LoadingHandle Open(string text)
        {
            var entry = new LoadingEntry { Src = Loading.XmlSrc, Text = text };
            entry.Handle = new LoadingHandle(entry);
            _entries.Add(entry);
            _pending.Enqueue(entry);
            if (!_materializing) _ = MaterializePump();
            return entry.Handle;
        }

        internal static void CloseEntry(LoadingEntry entry)
        {
            if (entry.Closed) return;
            entry.Closed = true;
            if (entry.Screen != null)
            {
                UI.CloseModalScreen(entry.Key);
                entry.Screen = null;
            }
            _entries.Remove(entry);
        }

        internal static void CancelAllForTeardown()
        {
            foreach (var e in _entries) e.Closed = true;
            _entries.Clear();
            _pending.Clear();
            // overlay Screen 由 UnloadAll / ResetForTests 的 _open 循环统一关
        }

        private static async Awaitable MaterializePump()
        {
            if (_materializing) return;
            _materializing = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var entry = _pending.Dequeue();
                    if (entry.Closed) continue;          // 实例化前就 Close 了
                    try
                    {
                        await ModalDocCache.EnsureLoaded(entry.Src);
                        if (entry.Closed) continue;

                        var (screen, key) = UI.OpenModalScreen(entry.Src);
                        entry.Screen = screen;
                        entry.Key = key;

                        var canvas = screen.RootGameObject.GetComponent<Canvas>();
                        canvas.overrideSorting = true;
                        canvas.sortingOrder = SortingOrder;

                        BindText(screen, entry.Text);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PromptUGUI] Loading overlay 显示失败: {ex}");
                        entry.Closed = true;
                        _entries.Remove(entry);
                    }
                }
            }
            finally { _materializing = false; }
        }

        private static void BindText(Screen screen, string text)
        {
            try
            {
                var t = screen.Get<PromptUGUI.Controls.Text>("text");
                if (string.IsNullOrEmpty(text)) t.GameObject.SetActive(false);
                else t.TextValue = text;
            }
            catch (KeyNotFoundException) { /* text 元素可选 */ }
        }
    }
}
```

> `LoadingHandle` 此刻还在 `LoadingRequest.cs`,其 ctor 是 `internal LoadingHandle(IModalEntry entry)`。本 Task 暂不动它 —— `LoadingOverlay` 引用 `new LoadingHandle(entry)` 会编译失败。**因此 Task 3 与 Task 4 必须连续完成、合并为一次绿灯**:Step 4 紧接着改 `LoadingHandle`。

- [ ] **Step 4: 临时改 `LoadingHandle` ctor 以编译通过**

`LoadingRequest.cs` 里的 `LoadingHandle` —— Task 4 会整体迁移并重写。本步只把它改成接受 `LoadingOverlay.LoadingEntry`,使 Task 3 能编译:见 Task 4 Step 3 的 `Loading.cs` 最终形态。**执行时直接合并 Task 3 + Task 4**:先建 `LoadingOverlay.cs`(Step 3),再做 Task 4 的全部步骤,然后一起跑 Task 3 + Task 4 的测试、一起 commit。

> 即:Task 3 与 Task 4 是一个原子提交。下面 Task 4 给出最终代码。

---

## Task 4: 迁移 `Loading` / `LoadingHandle`,删除 `LoadingRequest`

**Files:**
- Create: `Runtime/Application/Modals/Loading.cs`
- Delete: `Runtime/Application/Modals/LoadingRequest.cs`
- Rewrite: `Tests/EditMode/Modals/LoadingTests.cs`(旧的 modal-based 断言删除)

- [ ] **Step 1: 创建 `Loading.cs`**

```csharp
namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// "加载中" overlay 的门面。挡屏、转圈、不接受用户输入,由代码主动关闭。
    /// 不是 modal —— 坐在 dialog 栈之下,与 dialog 共存。
    /// </summary>
    public static class Loading
    {
        // .ui 后缀:Unity 只剥离 .ui.xml 文件名的最后 .xml
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/Loading.ui";

        public static LoadingHandle Open(string text = null)
            => LoadingOverlay.Open(text);
    }

    /// <summary>
    /// <see cref="Loading.Open"/> 返回的句柄。<see cref="Close"/> 关闭对应 overlay,幂等。
    /// </summary>
    public sealed class LoadingHandle
    {
        private readonly LoadingOverlay.LoadingEntry _entry;

        internal LoadingHandle(LoadingOverlay.LoadingEntry entry) => _entry = entry;

        public bool IsClosed => _entry.Closed;

        public void Close() => LoadingOverlay.CloseEntry(_entry);
    }
}
```

- [ ] **Step 2: 删除 `LoadingRequest.cs`**

```bash
git rm Runtime/Application/Modals/LoadingRequest.cs Runtime/Application/Modals/LoadingRequest.cs.meta
```

`LoadingRequest`(`ModalRequest<Unit>` 子类)整体消失。其 `Bind` 逻辑已被 `LoadingOverlay.BindText` 取代。

- [ ] **Step 3: 重写 `LoadingTests.cs`**

Loading 不再是 modal,旧文件里依赖 `UI.Modal` 队列 / `UI.Get` / `ModalEscapeListener` 的断言全部失效。Loading 的功能性测试已由 Task 3 的 `LoadingOverlayTests` 覆盖。**删除 `Tests/EditMode/Modals/LoadingTests.cs`**(`LoadingOverlayTests` 是它的替代):

```bash
git rm Tests/EditMode/Modals/LoadingTests.cs Tests/EditMode/Modals/LoadingTests.cs.meta
```

新增一个用真实内置模板的 smoke 测试 `Tests/EditMode/Modals/LoadingBuiltinTests.cs`(对应旧 `Default_xml_src_loads_builtin_template`):

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class LoadingBuiltinTests : ModalTestFixture
    {
        [Test]
        public void Default_xml_src_loads_builtin_template()
        {
            Loading.XmlSrc = "PromptUGUI/Modals/Loading.ui";   // 内置,走 Resources
            var handle = Loading.Open("from real template");

            Assert.IsNotNull(handle);
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);

            var screen = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            var text = screen.Get<PromptUGUI.Controls.Text>("text");
            Assert.AreEqual("from real template", text.TmpComponent.text);

            handle.Close();
        }
    }
}
```

- [ ] **Step 4: 编译 + 跑 Task 3 & 4 全部测试**

`refresh_unity` → `read_console`(无错)→
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingOverlayTests")` →
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="LoadingBuiltinTests")`。
预期:全绿。

> 此刻 `UI.Modal` 仍是旧的队列实现,但已无任何 Loading 耦合的调用方(`EnqueueRequest` 仅剩定义、无调用)。`UI.Modal` 的旧测试此刻仍应通过。可顺带跑 `filter="ModalQueueTests"` 确认未误伤。

- [ ] **Step 5: lint + Commit(Task 3 + 4 合并提交)**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx && cd ..
git add Runtime/Application/Modals/LoadingOverlay.cs Runtime/Application/Modals/LoadingOverlay.cs.meta \
        Runtime/Application/Modals/Loading.cs Runtime/Application/Modals/Loading.cs.meta \
        Tests/EditMode/Modals/LoadingOverlayTests.cs Tests/EditMode/Modals/LoadingOverlayTests.cs.meta \
        Tests/EditMode/Modals/LoadingBuiltinTests.cs Tests/EditMode/Modals/LoadingBuiltinTests.cs.meta
git add -A Runtime/Application/Modals/LoadingRequest.cs Tests/EditMode/Modals/LoadingTests.cs
git commit -m "feat: extract Loading into standalone LoadingOverlay subsystem"
```

---

## Task 5: 重写 `UI.Modal` 为显示栈(Popup);精简 `ModalEntry`

本 Task 的核心。`UI.Modal` 从单活跃 FIFO 队列改成显示栈。本 Task 只实现 `Popup`(总是压栈),`Queued` 在 Task 6 加。`OpenAsync` 此刻已带 `mode` 参数但 `Queued` 暂时也走压栈路径(Task 6 补等待队列)。

**Files:**
- Rewrite: `Runtime/Application/UI.Modal.cs`
- Modify: `Runtime/Application/Modals/ModalEntry.cs`
- Create: `Tests/EditMode/Modals/ModalStackTests.cs`
- Rewrite: `Tests/EditMode/Modals/ModalQueueTests.cs`
- Modify: `Tests/EditMode/Modals/MessageBoxRequestTests.cs`、`MessageBoxStaticTests.cs`、`ModalReSolveTests.cs`(`UI.Get("test/Box1")` → `UI.Modal.TopScreen`)

- [ ] **Step 1: 精简 `ModalEntry.cs`**

`IModalEntry` 删 `SetWaker` / `ResolveExternally`;`ModalEntry<TResult>` 删 `_waker` 字段 + 这两个方法实现。最终:

```csharp
using System;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    // Non-generic queue entry interface — lets UI.Modal.cs work without referencing
    // the generic ModalRequest<TResult> type.
    internal interface IModalEntry
    {
        public string XmlSrc { get; }
        public void RunBind(IScreen screen, Action onClose);
        public bool TryEscape(Action wakePump);
        public void Cancel(Exception ex);
        public bool Resolved { get; }
    }

    internal sealed class ModalEntry<TResult> : IModalEntry
    {
        private readonly ModalRequest<TResult> _request;
        private readonly AwaitableCompletionSource<TResult> _tcs = new();

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
    }
}
```

- [ ] **Step 2: 写 `ModalStackTests.cs`(失败测试)**

```csharp
using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalStackTests : ModalTestFixture
    {
        private sealed class FakeRequest : ModalRequest<int>
        {
            public string Src = "test/Box1";
            public Action<IScreen, Action<int>> OnBind;
            public override string XmlSrc => Src;
            public override void Bind(IScreen screen, Action<int> close) => OnBind?.Invoke(screen, close);
        }

        [Test]
        public void Open_then_close_resolves_awaitable()
        {
            Action<int> close = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close = c });

            Assert.IsNotNull(close, "Bind 应同步跑(fake resolver 同步完成)");
            Assert.IsTrue(UI.Modal.IsAnyOpen);

            close(42);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Popup_default_stacks_both_modals_immediately()
        {
            Action<int> close1 = null, close2 = null;
            var t1 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close1 = c });
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.IsNotNull(close1, "第一个 Bind 跑");
            Assert.IsNotNull(close2, "Popup 默认 → 第二个 Bind 也立刻跑(叠在上面)");
            Assert.AreEqual(2, UI.Modal.QueuedCount);

            close2(2);                                  // 关栈顶
            Assert.AreEqual(2, t2.GetAwaiter().GetResult());
            Assert.IsTrue(UI.Modal.IsAnyOpen, "关掉栈顶,下面那个还在");

            close1(1);
            Assert.AreEqual(1, t1.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Stacked_modals_get_incrementing_sortingOrder()
        {
            UI.Modal.SortingOrderBase = 1000;
            Action<int> c1 = null, c2 = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c1 = c });
            var bottom = UI.Modal.TopScreen;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c2 = c });
            var top = UI.Modal.TopScreen;

            Assert.AreNotSame(bottom, top);
            Assert.AreEqual(1000, bottom.RootGameObject.GetComponent<UnityEngine.Canvas>().sortingOrder);
            Assert.AreEqual(1001, top.RootGameObject.GetComponent<UnityEngine.Canvas>().sortingOrder);

            c2(0); c1(0);
        }

        [Test]
        public void Same_src_can_stack_two_instances()
        {
            Action<int> c1 = null, c2 = null;
            UI.Modal.OpenAsync(new FakeRequest { Src = "test/Box1", OnBind = (_, c) => c1 = c });
            var s1 = UI.Modal.TopScreen;
            UI.Modal.OpenAsync(new FakeRequest { Src = "test/Box1", OnBind = (_, c) => c2 = c });
            var s2 = UI.Modal.TopScreen;

            Assert.AreNotSame(s1, s2, "同一 XmlSrc 的两个 modal 必须是两份独立 Screen");

            c2(0); c1(0);
        }

        [Test]
        public void Bind_exception_cancels_that_modal_and_pumps_next()
        {
            var t1 = UI.Modal.OpenAsync(new FakeRequest
            {
                OnBind = (_, __) => throw new InvalidOperationException("boom"),
            });
            Action<int> close2 = null;
            var t2 = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close2 = c });

            Assert.Throws<InvalidOperationException>(() => t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "后一个 modal 仍应被实例化");
            close2(7);
            Assert.AreEqual(7, t2.GetAwaiter().GetResult());
        }

        [Test]
        public void Close_double_call_is_idempotent()
        {
            Action<int> close = null;
            var task = UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close = c });
            close(7);
            close(99);                                  // 忽略
            Assert.AreEqual(7, task.GetAwaiter().GetResult());
        }

        [Test]
        public void Escape_listener_only_on_top_modal()
        {
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "bottom", Buttons = MsgBtn.OK | MsgBtn.Cancel });
            var bottom = UI.Modal.TopScreen;
            UI.Modal.OpenAsync(new MessageBoxRequest { Text = "top", Buttons = MsgBtn.OK | MsgBtn.Cancel });
            var top = UI.Modal.TopScreen;

            var bottomEsc = bottom.RootGameObject.GetComponent<ModalEscapeListener>();
            var topEsc = top.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsFalse(bottomEsc.enabled, "被压住的 modal 的 ESC listener 应禁用");
            Assert.IsTrue(topEsc.enabled, "栈顶 modal 的 ESC listener 应启用");

            UI.Modal.CloseAll();
        }
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalStackTests")`。预期:多条 FAIL/编译失败(`UI.Modal.TopScreen` 不存在等)。

- [ ] **Step 4: 重写 `UI.Modal.cs`**

整个文件替换为:

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static class Modal
        {
            private sealed class Slot
            {
                public readonly IModalEntry Entry;
                public readonly Screen Screen;
                public readonly string Key;
                public ModalEscapeListener Escape;
                public Slot(IModalEntry entry, Screen screen, string key)
                {
                    Entry = entry; Screen = screen; Key = key;
                }
            }

            private static readonly List<Slot> _stack = new();           // 自底向上
            private static readonly Queue<IModalEntry> _waiting = new();  // Queued,等栈空
            private static readonly Queue<IModalEntry> _pending = new();  // 待实例化
            private static bool _materializing;
            private static IModalEntry _inFlight;

            public static int SortingOrderBase { get; set; } = 1000;

            public static int QueuedCount =>
                _stack.Count + _waiting.Count + _pending.Count + (_inFlight != null ? 1 : 0);

            public static bool IsAnyOpen => _stack.Count > 0;

            /// <summary>测试 / 诊断用:当前栈顶 modal 的 Screen,无则 null。</summary>
            internal static Screen TopScreen =>
                _stack.Count > 0 ? _stack[_stack.Count - 1].Screen : null;

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

            // dialog 系统完全空闲:无在屏、无待实例化、无 in-flight、pump 未运行。
            private static bool IsIdle() =>
                _stack.Count == 0 && _pending.Count == 0 && _inFlight == null && !_materializing;

            private static void QueueForMaterialize(IModalEntry entry)
            {
                _pending.Enqueue(entry);
                if (!_materializing) _ = MaterializePump();
            }

            private static async Awaitable MaterializePump()
            {
                if (_materializing) return;
                _materializing = true;
                try
                {
                    while (_pending.Count > 0)
                    {
                        var entry = _pending.Dequeue();
                        if (entry.Resolved) continue;       // CloseAll 在实例化前取消
                        _inFlight = entry;
                        Slot slot = null;
                        try
                        {
                            await ModalDocCache.EnsureLoaded(entry.XmlSrc);
                            if (entry.Resolved) continue;   // CloseAll 在 await 期间取消

                            var (screen, key) = OpenModalScreen(entry.XmlSrc);
                            slot = new Slot(entry, screen, key);
                            _stack.Add(slot);

                            var canvas = screen.RootGameObject.GetComponent<Canvas>();
                            canvas.overrideSorting = true;
                            canvas.sortingOrder = SortingOrderBase + _stack.Count - 1;

                            var capturedSlot = slot;
                            var listener = screen.RootGameObject.AddComponent<ModalEscapeListener>();
                            listener.OnEscape = () => OnEscapePressed(capturedSlot);
                            slot.Escape = listener;

                            entry.RunBind(screen, () => OnEntryClosed(capturedSlot));
                            RefreshTopListener();
                        }
                        catch (Exception ex)
                        {
                            entry.Cancel(ex);
                            if (slot != null) RemoveSlot(slot);
                        }
                        finally
                        {
                            _inFlight = null;
                        }
                    }
                }
                finally
                {
                    _materializing = false;
                    PromoteWaiting();
                }
            }

            // modal 关闭(按钮 close 回调 / ESC):弹栈、销毁 Screen、提升等待队列。
            private static void OnEntryClosed(Slot slot)
            {
                if (!_stack.Contains(slot)) return;       // 已被移除(如 CloseAll)
                RemoveSlot(slot);
                RefreshTopListener();
                PromoteWaiting();
            }

            private static void OnEscapePressed(Slot slot)
            {
                if (_stack.Count == 0 || _stack[_stack.Count - 1] != slot) return;
                slot.Entry.TryEscape(() => OnEntryClosed(slot));
            }

            private static void RemoveSlot(Slot slot)
            {
                _stack.Remove(slot);
                CloseModalScreen(slot.Key);               // 销毁 Screen GameObject
            }

            private static void RefreshTopListener()
            {
                for (int i = 0; i < _stack.Count; i++)
                {
                    var esc = _stack[i].Escape;
                    if (esc != null) esc.enabled = (i == _stack.Count - 1);
                }
            }

            // 栈彻底空了 → 从等待队列拉下一个 Queued modal 作新栈底。
            private static void PromoteWaiting()
            {
                if (!IsIdle()) return;
                while (_waiting.Count > 0)
                {
                    var next = _waiting.Dequeue();
                    if (next.Resolved) continue;
                    QueueForMaterialize(next);
                    return;
                }
            }

            public static void CloseAll()
            {
                var oce = new OperationCanceledException("Modal cancelled (CloseAll)");
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    _stack[i].Entry.Cancel(oce);
                    CloseModalScreen(_stack[i].Key);
                }
                _stack.Clear();
                _inFlight?.Cancel(oce);
                while (_pending.Count > 0) _pending.Dequeue().Cancel(oce);
                while (_waiting.Count > 0) _waiting.Dequeue().Cancel(oce);
            }

            // UI.UnloadAll / UI.ResetForTests 调用:取消所有 await,但不关 Screen
            // —— modal Screen 在 UI._open 里,由 teardown 的 _open 循环统一关。
            internal static void CancelAllForTeardown()
            {
                var oce = new OperationCanceledException("Modal cancelled (UI teardown)");
                foreach (var slot in _stack) slot.Entry.Cancel(oce);
                _stack.Clear();
                _inFlight?.Cancel(oce);
                while (_pending.Count > 0) _pending.Dequeue().Cancel(oce);
                while (_waiting.Count > 0) _waiting.Dequeue().Cancel(oce);
            }

#if UNITY_EDITOR
            internal static void InvalidateCacheForEditor(string src) =>
                ModalDocCache.Invalidate(src);
#endif
        }
    }
}
```

> 旧的 `EnqueueRequest` / `IsModalScreen`(grep 确认无调用方)/ `_loadedSrcs` / pre-show `ResolveExternally` 逻辑全部消失。`InvalidateCacheForEditor` 保留为薄壳转发 `ModalDocCache.Invalidate`,使 `ModalHotReloadTests` 现有断言不变(Task 8 决定是否再清理)。

- [ ] **Step 5: 重写 `ModalQueueTests.cs` → 删除,被 `ModalStackTests` 取代**

`ModalQueueTests` 整个文件的断言都基于旧 FIFO 单活跃语义,已由 `ModalStackTests` 覆盖等价场景:

```bash
git rm Tests/EditMode/Modals/ModalQueueTests.cs Tests/EditMode/Modals/ModalQueueTests.cs.meta
```

- [ ] **Step 6: 改 `UI.Get("test/Box1")` → `UI.Modal.TopScreen`**

modal Screen 现在以 `test/Box1#m{n}` 注册进 `_open`,`UI.Get("test/Box1")` 返回 null。逐文件替换:

- `Tests/EditMode/Modals/MessageBoxRequestTests.cs` — 9 处 `UI.Get("test/Box1")` 全部改为 `UI.Modal.TopScreen`。语义不变(每个用例只开一个 modal,它就是栈顶)。
- `Tests/EditMode/Modals/MessageBoxStaticTests.cs` — 3 处 `UI.Get("test/Box1")` 改为 `UI.Modal.TopScreen`。
- `Tests/EditMode/Modals/ModalReSolveTests.cs` — 1 处 `UI.Get("test/Box1")` 改为 `UI.Modal.TopScreen`。

例(`MessageBoxRequestTests.Click_OK_returns_MsgBtn_OK`):

```csharp
        [Test]
        public void Click_OK_returns_MsgBtn_OK()
        {
            var task = UI.Modal.OpenAsync(new MessageBoxRequest { Text = "hi", Buttons = MsgBtn.OK });
            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
        }
```

`MessageBoxRequestTests.Escape_via_listener_*` 两条:`UI.Get("test/Box1").RootGameObject` → `UI.Modal.TopScreen.RootGameObject`,其余不变(单 modal 即栈顶,listener 已 enabled)。

- [ ] **Step 7: 编译 + 跑全部 EditMode modal 测试**

`refresh_unity` → `read_console`(无错)→
`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalStackTests")` →
`filter="MessageBoxRequestTests"` → `filter="MessageBoxStaticTests"` → `filter="ModalReSolveTests"` → `filter="ModalCancelTests"`。
预期:全绿。`ModalCancelTests` 未改文件 —— 两个 `StuckRequest` 现在叠在栈上,`CloseAll`/`UnloadAll`/`ResetForTests` 仍取消全部,断言依旧成立。

- [ ] **Step 8: lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx && cd ..
git add -A Runtime/Application/UI.Modal.cs Runtime/Application/Modals/ModalEntry.cs \
           Tests/EditMode/Modals/ModalStackTests.cs Tests/EditMode/Modals/ModalStackTests.cs.meta \
           Tests/EditMode/Modals/ModalQueueTests.cs \
           Tests/EditMode/Modals/MessageBoxRequestTests.cs Tests/EditMode/Modals/MessageBoxStaticTests.cs \
           Tests/EditMode/Modals/ModalReSolveTests.cs
git commit -m "feat: rewrite UI.Modal as a display stack (Popup mode)"
```

---

## Task 6: `Queued` 模式 + 等待队列

`UI.Modal` 的栈逻辑已就位(Task 5),`OpenAsync` 已含 `mode` 参数与 `_waiting` 路径。本 Task 只新增针对 `Queued` 的测试并验证 —— 若 Task 5 的代码已正确实现 `_waiting` / `PromoteWaiting`(上面给的完整代码已包含),本 Task 主要是测试补全。

**Files:**
- Create: `Tests/EditMode/Modals/ModalQueuedModeTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalQueuedModeTests : ModalTestFixture
    {
        private sealed class FakeRequest : ModalRequest<int>
        {
            public Action<IScreen, Action<int>> OnBind;
            public override string XmlSrc => "test/Box1";
            public override void Bind(IScreen screen, Action<int> close) => OnBind?.Invoke(screen, close);
        }

        [Test]
        public void Queued_waits_for_stack_to_empty()
        {
            Action<int> close1 = null, close2 = null;
            var t1 = UI.Modal.OpenAsync(
                new FakeRequest { OnBind = (_, c) => close1 = c }, ModalMode.Popup);
            var t2 = UI.Modal.OpenAsync(
                new FakeRequest { OnBind = (_, c) => close2 = c }, ModalMode.Queued);

            Assert.IsNotNull(close1, "第一个立即显示");
            Assert.IsNull(close2, "Queued 的应在等待队列里,Bind 还没跑");
            Assert.AreEqual(2, UI.Modal.QueuedCount);

            close1(1);
            Assert.AreEqual(1, t1.GetAwaiter().GetResult());
            Assert.IsNotNull(close2, "栈空 → Queued 的现在显示");

            close2(2);
            Assert.AreEqual(2, t2.GetAwaiter().GetResult());
            Assert.AreEqual(0, UI.Modal.QueuedCount);
        }

        [Test]
        public void Multiple_queued_show_in_FIFO_order()
        {
            Action<int> c1 = null, c2 = null, c3 = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c1 = c }, ModalMode.Popup);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c2 = c }, ModalMode.Queued);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c3 = c }, ModalMode.Queued);

            Assert.IsNull(c2); Assert.IsNull(c3);
            c1(0);
            Assert.IsNotNull(c2, "第一个 Queued 先显示"); Assert.IsNull(c3);
            c2(0);
            Assert.IsNotNull(c3, "第二个 Queued 接着显示");
            c3(0);
        }

        [Test]
        public void Queued_on_empty_stack_shows_immediately()
        {
            Action<int> close = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => close = c }, ModalMode.Queued);
            Assert.IsNotNull(close, "栈空时 Queued 等同 Popup,立即显示");
            close(0);
        }

        [Test]
        public void Popup_opened_during_queued_wait_stacks_on_current()
        {
            Action<int> c1 = null, cPopup = null, cQueued = null;
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => c1 = c }, ModalMode.Popup);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => cQueued = c }, ModalMode.Queued);
            UI.Modal.OpenAsync(new FakeRequest { OnBind = (_, c) => cPopup = c }, ModalMode.Popup);

            Assert.IsNotNull(cPopup, "Popup 立刻叠上去");
            Assert.IsNull(cQueued, "Queued 仍在等");

            cPopup(0); c1(0);
            Assert.IsNotNull(cQueued, "栈全空后 Queued 才出来");
            cQueued(0);
        }
    }
}
```

- [ ] **Step 2: 跑测试**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalQueuedModeTests")`。预期:全绿(Task 5 的 `UI.Modal` 代码已实现 `_waiting`)。若红:对照 Task 5 Step 4 的 `OpenAsync` / `PromoteWaiting` / `IsIdle` 修正。

- [ ] **Step 3: Commit**

```bash
git add Tests/EditMode/Modals/ModalQueuedModeTests.cs Tests/EditMode/Modals/ModalQueuedModeTests.cs.meta
git commit -m "test: ModalMode.Queued waiting-queue semantics"
```

---

## Task 7: `MessageBox.Open` 加 `mode` 参数

**Files:**
- Modify: `Runtime/Application/Modals/MessageBoxRequest.cs`
- Create: `Tests/EditMode/Modals/MessageBoxModeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MessageBoxModeTests : ModalTestFixture
    {
        [Test]
        public void Open_default_mode_is_popup_stacks()
        {
            var t1 = MessageBox.Open("a", MsgBtn.OK);
            var t2 = MessageBox.Open("b", MsgBtn.OK);          // 默认 Popup
            Assert.AreEqual(2, UI.Modal.QueuedCount, "默认 Popup 两个都在栈上");

            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t2.GetAwaiter().GetResult());
            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t1.GetAwaiter().GetResult());
        }

        [Test]
        public void Open_queued_mode_waits()
        {
            var t1 = MessageBox.Open("a", MsgBtn.OK);
            var t2 = MessageBox.Open("b", MsgBtn.OK, mode: ModalMode.Queued);
            Assert.IsNull(UI.Get("test/Box1"));               // sanity: 不再按名拿 modal

            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t1.GetAwaiter().GetResult());
            // 现在 t2 应已显示
            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, t2.GetAwaiter().GetResult());
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

`mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="MessageBoxModeTests")`。预期:编译失败(`mode` 参数不存在)。

- [ ] **Step 3: 改 `MessageBoxRequest.cs` 的 `MessageBox` 类**

`MessageBox` 类的两个 `Open` 重载末尾各加 `ModalMode mode = ModalMode.Popup`,并传给 `OpenAsync`:

```csharp
    public static class MessageBox
    {
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MessageBox.ui";

        public static UnityEngine.Awaitable<MsgBtn> Open(
            string text, MsgBtn buttons = MsgBtn.OK, string icon = null, string title = null,
            ModalMode mode = ModalMode.Popup)
            => UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = text,
                Buttons = buttons,
                Icon = icon,
                Title = title,
            }, mode);

        public static UnityEngine.Awaitable<MsgBtn> Open(
            string text,
            System.Collections.Generic.IEnumerable<(string label, MsgBtn key)> buttons,
            string icon = null, string title = null,
            ModalMode mode = ModalMode.Popup)
        {
            var list = new System.Collections.Generic.List<(string, MsgBtn)>(buttons);
            var mask = MsgBtn.None;
            foreach (var (_, k) in list) mask |= k;
            return UI.Modal.OpenAsync(new MessageBoxRequest
            {
                Text = text,
                CustomLabels = list,
                Buttons = mask,
                Icon = icon,
                Title = title,
            }, mode);
        }
    }
```

`MessageBoxRequest` 类本身不变。

- [ ] **Step 4: 跑测试确认通过**

`refresh_unity` → `read_console` → `run_tests(... filter="MessageBoxModeTests")`。预期:2 passed。

- [ ] **Step 5: lint + Commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx && cd ..
git add Runtime/Application/Modals/MessageBoxRequest.cs \
        Tests/EditMode/Modals/MessageBoxModeTests.cs Tests/EditMode/Modals/MessageBoxModeTests.cs.meta
git commit -m "feat: MessageBox.Open mode parameter (default Popup)"
```

---

## Task 8: teardown 接线 + Editor hot-reload

**Files:**
- Modify: `Runtime/Application/UI.cs`(`UnloadAll`、`ResetForTests`)
- Modify: `Tests/EditMode/Editor/ModalHotReloadTests.cs`

- [ ] **Step 1: 写失败测试**

新增 `Tests/EditMode/Modals/ModalTeardownTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalTeardownTests : ModalTestFixture
    {
        private const string LoadingXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Loading1'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
  </Screen>
</PromptUGUI>";

        public override void SetUp()
        {
            base.SetUp();
            Files["test/Loading1"] = LoadingXml;
            Loading.XmlSrc = "test/Loading1";
        }

        [Test]
        public void UnloadAll_tears_down_loading_overlay()
        {
            Loading.Open("x");
            Assert.AreEqual(1, LoadingOverlay.ActiveCount);

            UI.UnloadAll();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount,
                "UnloadAll 必须清掉 Loading overlay");
        }

        [Test]
        public void ResetForTests_tears_down_loading_overlay()
        {
            Loading.Open("x");
            UI.ResetForTests();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }

        [Test]
        public void LoadingHandle_Close_after_teardown_is_noop()
        {
            var handle = Loading.Open("about to be torn down");
            UI.ResetForTests();
            Assert.DoesNotThrow(() => handle.Close());
            Assert.IsTrue(handle.IsClosed);
        }

        [Test]
        public void Loading_and_MessageBox_coexist()
        {
            // spec §1.2 原死锁场景的回归:Loading 期间开 MessageBox,两者同时存在。
            var loading = Loading.Open("working...");
            var mbox = UI.Modal.OpenAsync(new MessageBoxRequest { Text = "q", Buttons = MsgBtn.OK });

            Assert.AreEqual(1, LoadingOverlay.ActiveCount, "Loading overlay 仍在");
            Assert.IsTrue(UI.Modal.IsAnyOpen, "MessageBox 同时显示,没被 Loading 挡在队列后");

            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, mbox.GetAwaiter().GetResult());
            Assert.AreEqual(1, LoadingOverlay.ActiveCount, "关掉 MessageBox 后 Loading 仍在");

            loading.Close();
            Assert.AreEqual(0, LoadingOverlay.ActiveCount);
        }
    }
}
```

> `Loading_and_MessageBox_coexist` 是 spec §1.2 死锁的核心回归 —— Loading 抽成独立子系统后,MessageBox 不再排在 Loading 之后。

- [ ] **Step 2: 跑测试确认失败**

`run_tests(... filter="ModalTeardownTests")`。预期:`UnloadAll`/`ResetForTests` 还没接 `LoadingOverlay` → ActiveCount 仍为 1,FAIL。

- [ ] **Step 3: 改 `UI.cs` 的 `UnloadAll`**

Task 2 已加入 `Modals.ModalDocCache.Clear();`。本步只在它之前插入 `Modals.LoadingOverlay.CancelAllForTeardown();`。最终形态:

```csharp
        public static void UnloadAll()
        {
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _commonsPool.Clear();
            _depGraph.Clear();
        }
```

- [ ] **Step 4: 改 `UI.cs` 的 `ResetForTests`**

Task 2 已加入 `Modals.ModalDocCache.Clear();`。本步只在它之前插入 `Modals.LoadingOverlay.CancelAllForTeardown();`。最终(`Modal.CancelAllForTeardown();` 起):

```csharp
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();
            foreach (var s in _open.Values) s.Close();
```

- [ ] **Step 5: 更新 `ModalHotReloadTests.cs`**

`UI.Modal.InvalidateCacheForEditor` 仍作为薄壳存在(Task 5 保留),现有用例不变 —— 跑一遍确认仍绿即可。可选清理:把测试改为直接调 `ModalDocCache.Invalidate`:

```csharp
        [Test]
        public void Invalidate_is_silent_for_unknown_src()
        {
            Assert.DoesNotThrow(() =>
                PromptUGUI.Application.Modals.ModalDocCache.Invalidate("not/cached"));
        }
```

若采用此清理,把 `UI.Modal.cs` 里 `#if UNITY_EDITOR InvalidateCacheForEditor` 薄壳一并删除。

- [ ] **Step 6: 跑测试 + lint + Commit**

`refresh_unity` → `read_console` →
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalTeardownTests")` →
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ModalCancelTests")` →
`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="ModalHotReloadTests")`。预期:全绿。

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx && cd ..
git add -A Runtime/Application/UI.cs Runtime/Application/UI.Modal.cs \
           Tests/EditMode/Modals/ModalTeardownTests.cs Tests/EditMode/Modals/ModalTeardownTests.cs.meta \
           Tests/EditMode/Editor/ModalHotReloadTests.cs
git commit -m "feat: wire LoadingOverlay + ModalDocCache into UI teardown"
```

---

## Task 9: PlayMode ESC 测试更新

**Files:**
- Modify: `Tests/PlayMode/Modals/ModalEscapePlayModeTests.cs`

- [ ] **Step 1: 改 `UI.Get(MessageBox.XmlSrc)` → `UI.Modal.TopScreen`**

3 处 `UI.Get(MessageBox.XmlSrc)` 全部改为 `UI.Modal.TopScreen`(单 modal 即栈顶)。例:

```csharp
        [UnityTest]
        public IEnumerator Escape_returns_Cancel_when_OK_Cancel_combo()
        {
            var task = MessageBox.Open("first", MsgBtn.OK | MsgBtn.Cancel);
            yield return null;
            yield return null;

            var screen = UI.Modal.TopScreen;
            Assert.IsNotNull(screen, "Screen should be loaded");
            var listener = screen.RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener, "ModalEscapeListener should be attached");

            listener.FireForTests();
            yield return null;

            Assert.AreEqual(MsgBtn.Cancel, task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }
```

`Escape_only_OK_does_not_close`、`SortingOrder_uses_SortingOrderBase` 同样把 `UI.Get(MessageBox.XmlSrc)` 换成 `UI.Modal.TopScreen`。`SortingOrder_uses_SortingOrderBase` 数值不变(单 modal → `SortingOrderBase + 0`)。

- [ ] **Step 2: 跑 PlayMode 测试**

`mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="ModalEscapePlayModeTests")`。预期:3 passed。

- [ ] **Step 3: Commit**

```bash
git add Tests/PlayMode/Modals/ModalEscapePlayModeTests.cs
git commit -m "test: update PlayMode ESC tests for modal stack"
```

---

## Task 10: SKILL.md 更新

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

CLAUDE.md trigger:公开 C# API 变更(`ModalMode`、`mode:` 参数)+ 行为变更 → C# skill 必须更新。XML skill / Addressables skill 不涉及。

- [ ] **Step 1: 更新 cheatsheet 的 MODAL 区**(SKILL.md 约 312–316 行)

```
MODAL          var r = await MessageBox.Open(text, MsgBtn.OK|MsgBtn.Cancel, icon, title)
               MessageBox.Open(text, ..., mode: ModalMode.Queued)  排队,不叠加
               UI.Modal.OpenAsync(new MyRequest(), ModalMode.Popup) custom ModalRequest<T>
               UI.Modal.CloseAll()                          cancel all
               UI.Modal.SortingOrderBase = 1000             default
               var h = Loading.Open(text); h.Close()        loading overlay(独立于 modal 栈)
               Loading.SortingOrder = 500                   overlay 层带,低于 dialog
```

- [ ] **Step 2: 重写 "### Behavior" 一节**

把 "Modal stacking: ... queue FIFO" 改为:

```markdown
### Behavior

- **Stacking (`ModalMode`)**: every `Open` takes a `mode`. **`ModalMode.Popup`** (default)
  shows the dialog immediately, stacked on top of any current dialog — use it for nested
  dialogs (e.g. a confirm dialog opened from inside another modal). **`ModalMode.Queued`**
  waits until the whole dialog stack is empty, then shows as the new base; multiple
  `Queued` dialogs show FIFO. Closing the top dialog reveals the one below.
- **ESC / Android Back**: only the top dialog responds; maps to `Cancel > No > Close`.
  ESC on an `OK`-only dialog does nothing.
- **Raycast block**: each dialog's Canvas overrides `sortingOrder` to
  `UI.Modal.SortingOrderBase + depth` (base default 1000), above every regular Screen.
- **Locale / Variant**: a dialog is a regular `Screen` — locale / Variants apply normally.
```

- [ ] **Step 3: 重写 "### Loading modal" 一节**

标题改为 "### Loading overlay"。把 "Shares the FIFO queue with MessageBox" 那条删掉,改为说明 Loading 是独立 overlay:

```markdown
### Loading overlay

A non-interactive overlay that blocks the screen while async work runs, then your code
closes it. It is **not a modal/dialog** — it is a separate subsystem that sits *below*
the dialog stack, so a MessageBox opened during a Loading appears on top of it.

```csharp
var loading = Loading.Open(UI.Tr("Loading..."));
try { await DoWorkAsync(); }
finally { loading.Close(); }   // idempotent
```

- `Loading.Open(text)` returns a `LoadingHandle` synchronously; close it from code.
- Does not accept input (ESC cannot dismiss it).
- Coexists with dialogs — a MessageBox opened while a Loading is showing stacks above it;
  opening one no longer deadlocks against the other.
- Concurrent `Loading.Open()` calls each get their own overlay.
- `Loading.SortingOrder` (default 500) is the overlay band; keep it below
  `UI.Modal.SortingOrderBase` (default 1000).
- `text` is optional (`null`/`""` → spinner only). Custom XML: only `<Text id="text">`
  is recognised by Bind, and it is optional.
```

> 删除旧 "Loading modal" 节里关于 "Differences from MessageBox.Open" 中 "Shares the FIFO queue" 一条;其余(同步返回 handle、无 TResult、可覆盖 XML)保留并入上面。

- [ ] **Step 4: lint + Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs: update C# skill for ModalMode + Loading overlay"
```

---

## 全量回归

所有 Task 完成后:

- [ ] `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console(types=["error"])` 无错。
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全绿。
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` 全绿。
- [ ] `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])` 全绿。
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 干净。
- [ ] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/` exit 0(modal XML 未改,确认未误伤)。
- [ ] 对照 spec §11 验收标准逐条核对。

---

## 验收标准(摘自 spec §11)

- §1.2 死锁代码:Loading 期间 `await MessageBox.Open` 正常弹出、返回(结构上由 Loading 独立子系统保证)。
- 嵌套确认框 `mode: ModalMode.Popup` 叠在父 modal 之上,答完露出父 modal。
- `mode: ModalMode.Queued` 的两个 modal 按 FIFO 依次显示。
- Loading overlay 始终在所有 dialog 之下(`SortingOrder 500 < SortingOrderBase 1000`)。
- ESC 只关栈顶 dialog;Loading 不响应 ESC。
- `UI.Modal.CloseAll()` 让显示栈 + 等待队列的全部 `await` 抛 `OperationCanceledException`。
- EditMode + PlayMode 测试全绿;`dotnet format --verify-no-changes` 干净。
