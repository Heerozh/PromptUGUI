# Unity 2022 Awaitable→UniTask Shim 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 PromptUGUI 在 Unity 2022.3 上编译/运行——在 `UnityEngine` 命名空间下补一套仅 2022 编译、内部包 UniTask 的 `Awaitable` / `AwaitableCompletionSource` polyfill，现有 71 文件 + 测试一行不改。

**Architecture:** 新增受门控的 `PromptUGUI.Compat.UniTask` 程序集（`Runtime/Compat/`），内含 `namespace UnityEngine` 的 shim 类型；`Awaitable<T>` 带 `[AsyncMethodBuilder]`，builder 转发给 UniTask 的 `AsyncUniTaskMethodBuilder<T>`；shim 与 UniTask 之间双向隐式转换。Unity 6 上整套被 `#if !UNITY_6000_0_OR_NEWER` 剪掉，走 Unity 原生 Awaitable。

**Tech Stack:** Unity 2022.3 / C# 9 / UniTask (`com.cysharp.unitask` ≥ 2.0) / NUnit（EditMode 测试）/ UnityMCP（在真实 2022 工程跑 refresh + run_tests）。

**设计依据：** `docs~/superpowers/specs/2026-07-06-awaitable-unitask-2022-design.md`。核心机制已用隔离 spike 在真实 Unity 2022+UniTask 实测通过（见 spec §9）。

## Global Constraints

- **版本门控权威来源 = C# `#if !UNITY_6000_0_OR_NEWER`**，包住每个 shim/测试源文件全体。asmdef `defineConstraints` 里的 `!UNITY_6000_0_OR_NEWER` 只作 best-effort 早退（Unity 对内置版本宏的 defineConstraints 支持不确定），不可作为唯一门控。
- **后端版本驱动**：Unity 6+ 用原生 `UnityEngine.Awaitable`；Unity <6 用 UniTask。不提供「6 上强制 UniTask」的 define。
- shim 的 `Awaitable` / `Awaitable<T>` / `AwaitableCompletionSource(<T>)` 均为 **`sealed class`**（匹配 Unity 引用类型语义）。
- **确切 UniTask API**（spike + context7 已核实）：`Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskMethodBuilder` / `AsyncUniTaskMethodBuilder<T>`（public，`Create()`/`Task`/`SetResult`/`SetException`/`SetStateMachine`/`Start`/`AwaitOnCompleted`/`AwaitUnsafeOnCompleted`）；`Cysharp.Threading.Tasks.UniTaskCompletionSource` / `UniTaskCompletionSource<T>`（`.Task` / `TrySetResult` / `TrySetException` / `TrySetCanceled`）。**`System.Runtime.CompilerServices.AsyncMethodBuilderAttribute` 跨程序集可访问（无需自补）。**
- **公共 API 签名逐字不变**（仍 `UnityEngine.Awaitable<string>` 等）。
- **提交卫生**：只显式 `git add <目标源> <新文件.meta>`，**绝不 `git add -A`**。**不改 `package.json` 的 `dependencies`**（用户在 Windows 侧管理 ugui/lit-motion 降级）。**不提交既有文件的 `.meta` churn**（2022↔6 双编辑器互写，248 个）；新文件的 `.meta` 需显式提交。
- **不许提交到 main**（CLAUDE.md）。当前分支 `feat/awaitable-unitask-2022`。
- **SKILL 更新**：C# skill 加「Unity 2022 支持」节（安装 UniTask；公共 API 无差异）。
- **工作流**：文件用 bash 写进仓库（bind-mount 到 2022 工程 `C:\xsoft\PromptUGUI`）→ `refresh_unity(compile=request, mode=force)` → `read_console` / `run_tests`。已实测该回路可用。

---

### Task 1: 受门控的 Compat 程序集壳 + UniTask 缺失护栏

**Files:**
- Create: `Runtime/Compat/PromptUGUI.Compat.UniTask.asmdef`
- Create: `Runtime/UniTaskRequirement.cs`（注意：**在 `Runtime/` 根，不在 `Runtime/Compat/`**——`Runtime/Compat/` 会被 Compat asmdef 认领；护栏必须留在永远编译的 Runtime 程序集里）
- Modify: `Runtime/PromptUGUI.Runtime.asmdef`（加 `versionDefines` 一条）

**Interfaces:**
- Produces: 空的 `PromptUGUI.Compat.UniTask` 程序集（供后续 Task 放 shim 类型）；`PROMPTUGUI_HAS_UNITASK` 定义符（`com.cysharp.unitask` ≥ 2.0 时）。

- [ ] **Step 1: 建 Compat asmdef**

写 `Runtime/Compat/PromptUGUI.Compat.UniTask.asmdef`：

```json
{
    "name": "PromptUGUI.Compat.UniTask",
    "rootNamespace": "",
    "references": ["UniTask"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "autoReferenced": true,
    "defineConstraints": ["PROMPTUGUI_HAS_UNITASK", "!UNITY_6000_0_OR_NEWER"],
    "versionDefines": [
        { "name": "com.cysharp.unitask", "expression": "2.0.0", "define": "PROMPTUGUI_HAS_UNITASK" }
    ],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 建 UniTask 缺失护栏**

写 `Runtime/UniTaskRequirement.cs`：

```csharp
// PromptUGUI on Unity 2022 requires UniTask. This guard turns hundreds of
// "Awaitable not found" errors into one clear message when UniTask is missing.
#if !UNITY_6000_0_OR_NEWER && !PROMPTUGUI_HAS_UNITASK
#error PromptUGUI on Unity 2022 requires UniTask (com.cysharp.unitask). Install it via OpenUPM: https://openupm.com/packages/com.cysharp.unitask/
#endif
```

- [ ] **Step 3: 给 Runtime.asmdef 加 versionDefine**

在 `Runtime/PromptUGUI.Runtime.asmdef` 的 `versionDefines` 数组里**追加**（保留现有 addressables / aseprite 两条）：

```json
{ "name": "com.cysharp.unitask", "expression": "2.0.0", "define": "PROMPTUGUI_HAS_UNITASK" }
```

- [ ] **Step 4: 刷新并确认无新错误**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"], count="60")
```

Expected: 仍是 Task 前就有的一堆 `Awaitable` / `AwaitableCompletionSource` CS0246/CS0234（shim 还没写），**但不得出现任何提到 `PromptUGUI.Compat.UniTask`、`UniTaskRequirement.cs`、"reference not found"、`#error` 的新错误**。护栏静默（UniTask 已装 → `PROMPTUGUI_HAS_UNITASK` 已定义）。若出现 `#error` 那条 → UniTask 没装或 versionDefine 没生效，先修。

- [ ] **Step 5: 提交**

```bash
# 让 2022 编辑器生成新文件的 .meta 后再提交它们
git add Runtime/Compat/PromptUGUI.Compat.UniTask.asmdef Runtime/Compat/PromptUGUI.Compat.UniTask.asmdef.meta \
        Runtime/UniTaskRequirement.cs Runtime/UniTaskRequirement.cs.meta \
        Runtime/PromptUGUI.Runtime.asmdef
git commit -m "feat(compat): gated PromptUGUI.Compat.UniTask assembly + UniTask requirement guard"
```

---

### Task 2: `Awaitable` / `Awaitable<T>` 类型 + AsyncMethodBuilder + 双向隐式转换（TDD）

**Files:**
- Create: `Tests/EditMode/Compat/PromptUGUI.Tests.Compat.asmdef`
- Create: `Tests/EditMode/Compat/AwaitableShimTests.cs`
- Create: `Runtime/Compat/Awaitable.cs`
- Create: `Runtime/Compat/AwaitableAsyncMethodBuilder.cs`

**Interfaces:**
- Consumes: `PromptUGUI.Compat.UniTask` 程序集（Task 1）。
- Produces:
  - `UnityEngine.Awaitable`（sealed class，包 `UniTask`）+ `UnityEngine.Awaitable<T>`（sealed class，包 `UniTask<T>`）；各有 `GetAwaiter()`、隐式 `↔ UniTask(<T>)`。
  - `PromptUGUI.Compat.CompilerServices.AwaitableAsyncMethodBuilder` / `AwaitableAsyncMethodBuilder<T>`（public struct，转发 UniTask builder）。

- [ ] **Step 1: 建 Compat 测试 asmdef**

写 `Tests/EditMode/Compat/PromptUGUI.Tests.Compat.asmdef`：

```json
{
    "name": "PromptUGUI.Tests.Compat",
    "rootNamespace": "PromptUGUI.Tests.Compat",
    "references": ["PromptUGUI.Compat.UniTask", "UniTask", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
    "includePlatforms": ["Editor"],
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS", "PROMPTUGUI_HAS_UNITASK", "!UNITY_6000_0_OR_NEWER"],
    "versionDefines": [
        { "name": "com.cysharp.unitask", "expression": "2.0.0", "define": "PROMPTUGUI_HAS_UNITASK" }
    ],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 写失败测试（Awaitable 类型 + builder + 转换）**

写 `Tests/EditMode/Compat/AwaitableShimTests.cs`：

```csharp
#if !UNITY_6000_0_OR_NEWER
#pragma warning disable CS1998 // async without await: intentional for sync-completion tests
using NUnit.Framework;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace PromptUGUI.Tests.Compat
{
    public class AwaitableShimTests
    {
        private static async Awaitable<int> ProduceImmediate() => 7;

        private static async Awaitable<int> ProduceAfterYield()
        {
            await UniTask.Yield();
            return 9;
        }

        [Test]
        public void AsyncBuilder_SyncCompletion_ReturnsValue()
        {
            // async Awaitable<int> with no await completes synchronously;
            // exercises the builder's SetResult + Task property path.
            var v = ProduceImmediate().GetAwaiter().GetResult();
            Assert.AreEqual(7, v);
        }

        [Test]
        public void ImplicitConversion_UniTask_To_Awaitable_And_Back()
        {
            UniTask<int> u = UniTask.FromResult(5);
            Awaitable<int> a = u;      // implicit UniTask<int> -> Awaitable<int>
            UniTask<int> back = a;      // implicit Awaitable<int> -> UniTask<int>
            Assert.AreEqual(5, back.GetAwaiter().GetResult());
        }

        [Test]
        public void NonGeneric_Awaitable_Awaits()
        {
            UniTask u = UniTask.CompletedTask;
            Awaitable a = u;            // implicit UniTask -> Awaitable
            a.GetAwaiter().GetResult(); // completes without throwing
            Assert.Pass();
        }
    }
}
#endif
```

- [ ] **Step 3: 跑测试确认失败（RED）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"], count="40", filter_text="AwaitableShimTests")
```

Expected: `AwaitableShimTests.cs` 报 CS0246 —— `Awaitable` / `Awaitable<>` 找不到（shim 类型尚未实现）。这是 RED。

- [ ] **Step 4: 实现 Awaitable 类型**

写 `Runtime/Compat/Awaitable.cs`：

```csharp
#if !UNITY_6000_0_OR_NEWER
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using PromptUGUI.Compat.CompilerServices;

namespace UnityEngine
{
    // Polyfill for UnityEngine.Awaitable on Unity < 6 (where the type does not
    // exist). Backed by UniTask. sealed class to mirror Unity's reference-type
    // semantics. Compiled out on Unity 6+ where the native type is used.
    [AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder))]
    public sealed class Awaitable
    {
        private readonly UniTask _task;
        public Awaitable(UniTask task) { _task = task; }
        public UniTask.Awaiter GetAwaiter() => _task.GetAwaiter();
        public static implicit operator Awaitable(UniTask task) => new Awaitable(task);
        public static implicit operator UniTask(Awaitable awaitable) => awaitable._task;
    }

    [AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder<>))]
    public sealed class Awaitable<T>
    {
        private readonly UniTask<T> _task;
        public Awaitable(UniTask<T> task) { _task = task; }
        public UniTask<T>.Awaiter GetAwaiter() => _task.GetAwaiter();
        public static implicit operator Awaitable<T>(UniTask<T> task) => new Awaitable<T>(task);
        public static implicit operator UniTask<T>(Awaitable<T> awaitable) => awaitable._task;
    }
}
#endif
```

- [ ] **Step 5: 实现 AsyncMethodBuilder（转发 UniTask）**

写 `Runtime/Compat/AwaitableAsyncMethodBuilder.cs`（结构与 spike 实测通过的 `SpikeBuilder` 逐行一致）：

```csharp
#if !UNITY_6000_0_OR_NEWER
using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks.CompilerServices;
using UnityEngine;

namespace PromptUGUI.Compat.CompilerServices
{
    // Async method builders for UnityEngine.Awaitable(<T>) on Unity < 6.
    // Delegate to UniTask's builders (which correctly handle state-machine
    // boxing + pooling); Task property wraps the produced UniTask into our shim
    // via implicit conversion.
    public struct AwaitableAsyncMethodBuilder
    {
        private AsyncUniTaskMethodBuilder _inner;
        public static AwaitableAsyncMethodBuilder Create() =>
            new AwaitableAsyncMethodBuilder { _inner = AsyncUniTaskMethodBuilder.Create() };
        public Awaitable Task => _inner.Task; // UniTask -> Awaitable (implicit)
        public void SetResult() => _inner.SetResult();
        public void SetException(Exception e) => _inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _inner.SetStateMachine(stateMachine);
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            => _inner.Start(ref stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public struct AwaitableAsyncMethodBuilder<T>
    {
        private AsyncUniTaskMethodBuilder<T> _inner;
        public static AwaitableAsyncMethodBuilder<T> Create() =>
            new AwaitableAsyncMethodBuilder<T> { _inner = AsyncUniTaskMethodBuilder<T>.Create() };
        public Awaitable<T> Task => _inner.Task; // UniTask<T> -> Awaitable<T> (implicit)
        public void SetResult(T result) => _inner.SetResult(result);
        public void SetException(Exception e) => _inner.SetException(e);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _inner.SetStateMachine(stateMachine);
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            => _inner.Start(ref stateMachine);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}
#endif
```

- [ ] **Step 6: 跑测试确认通过（GREEN）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"], count="20", filter_text="AwaitableShimTests")   # 期望 0
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.Compat"])
# 轮询 get_test_job 直到完成
```

Expected: 3 个测试全绿。

- [ ] **Step 7: 提交**

```bash
git add Runtime/Compat/Awaitable.cs Runtime/Compat/Awaitable.cs.meta \
        Runtime/Compat/AwaitableAsyncMethodBuilder.cs Runtime/Compat/AwaitableAsyncMethodBuilder.cs.meta \
        Tests/EditMode/Compat/PromptUGUI.Tests.Compat.asmdef Tests/EditMode/Compat/PromptUGUI.Tests.Compat.asmdef.meta \
        Tests/EditMode/Compat/AwaitableShimTests.cs Tests/EditMode/Compat/AwaitableShimTests.cs.meta
git commit -m "feat(compat): UnityEngine.Awaitable(<T>) shim + async method builders (UniTask-backed)"
```

---

### Task 3: `AwaitableCompletionSource` / `AwaitableCompletionSource<T>`（TDD）

**Files:**
- Create: `Runtime/Compat/AwaitableCompletionSource.cs`
- Modify: `Tests/EditMode/Compat/AwaitableShimTests.cs`（加测试）

**Interfaces:**
- Consumes: `UnityEngine.Awaitable(<T>)`（Task 2）。
- Produces: `UnityEngine.AwaitableCompletionSource`（`.Awaitable` / `SetResult()` / `SetException` / `SetCanceled`）+ `UnityEngine.AwaitableCompletionSource<T>`（`.Awaitable` / `SetResult(T)` / `SetException` / `SetCanceled`）。

- [ ] **Step 1: 写失败测试**

在 `AwaitableShimTests.cs` 的 `AwaitableShimTests` 类里**追加**（`#if` 块内）：

```csharp
        [Test]
        public void CompletionSource_Generic_SetResult_SyncUnwrap()
        {
            var src = new AwaitableCompletionSource<int>();
            src.SetResult(11);
            var v = src.Awaitable.GetAwaiter().GetResult();
            Assert.AreEqual(11, v);
        }

        [Test]
        public void CompletionSource_NonGeneric_SetResult_Completes()
        {
            var src = new AwaitableCompletionSource();
            src.SetResult();
            src.Awaitable.GetAwaiter().GetResult(); // no throw
            Assert.Pass();
        }

        [Test]
        public void CompletionSource_Generic_SetException_Throws()
        {
            var src = new AwaitableCompletionSource<int>();
            src.SetException(new System.IO.IOException("boom"));
            Assert.Throws<System.IO.IOException>(() => src.Awaitable.GetAwaiter().GetResult());
        }
```

- [ ] **Step 2: 跑测试确认失败（RED）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"], count="20", filter_text="AwaitableShimTests")
```

Expected: CS0246 —— `AwaitableCompletionSource` / `AwaitableCompletionSource<>` 找不到。RED。

- [ ] **Step 3: 实现完成源**

写 `Runtime/Compat/AwaitableCompletionSource.cs`：

```csharp
#if !UNITY_6000_0_OR_NEWER
using System;
using Cysharp.Threading.Tasks;

namespace UnityEngine
{
    // Polyfill for UnityEngine.AwaitableCompletionSource on Unity < 6, backed by
    // UniTaskCompletionSource. Method names mirror Unity's (SetResult/SetException/
    // SetCanceled); implemented over UniTask's TrySet* (library sets each source once).
    public sealed class AwaitableCompletionSource
    {
        private readonly UniTaskCompletionSource _src = new UniTaskCompletionSource();
        public Awaitable Awaitable => _src.Task; // UniTask -> Awaitable (implicit)
        public void SetResult() => _src.TrySetResult();
        public void SetException(Exception exception) => _src.TrySetException(exception);
        public void SetCanceled() => _src.TrySetCanceled();
    }

    public sealed class AwaitableCompletionSource<T>
    {
        private readonly UniTaskCompletionSource<T> _src = new UniTaskCompletionSource<T>();
        public Awaitable<T> Awaitable => _src.Task; // UniTask<T> -> Awaitable<T> (implicit)
        public void SetResult(T value) => _src.TrySetResult(value);
        public void SetException(Exception exception) => _src.TrySetException(exception);
        public void SetCanceled() => _src.TrySetCanceled();
    }
}
#endif
```

- [ ] **Step 4: 跑测试确认通过（GREEN）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.Compat"])
# 轮询 get_test_job；期望 6 个测试全绿
```

- [ ] **Step 5: 提交**

```bash
git add Runtime/Compat/AwaitableCompletionSource.cs Runtime/Compat/AwaitableCompletionSource.cs.meta \
        Tests/EditMode/Compat/AwaitableShimTests.cs
git commit -m "feat(compat): UnityEngine.AwaitableCompletionSource(<T>) shim (UniTask-backed)"
```

---

### Task 4: 整包在 2022 编译通过 + 全量 EditMode 回归（集成检查点）

**Files:** 无新增；如出现遗漏用法/签名不匹配的 straggler，在对应文件就地修（应极少或没有——异步面已审计干净）。

**Interfaces:** 依赖 Task 1-3 的完整 shim。

- [ ] **Step 1: 整包编译，确认 Awaitable 错误清零**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"], count="100")
```

Expected: **零** `Awaitable` / `Awaitable<>` / `AwaitableCompletionSource` 相关 CS0246/CS0234（Task 前有几百条，现在全清）。若剩个别错误 → 是真 straggler（某处 Unity-6-only 用法 shim 没覆盖），逐条读、就地修，重跑至清零。

- [ ] **Step 2: 全量 EditMode 回归（UniTask 后端）**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
# 轮询 get_test_job
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.Compat"])
```

Expected: 既有 EditMode 全绿（这是 shim 端到端跑通库真实异步路径的证据）+ Compat 6 个绿。PlayMode 若该 2022 工程可跑也跑一遍（`PromptUGUI.Tests.PlayMode`）；若 runner 在该工程不稳，记录为待 Task 6 一并确认，不阻塞。

- [ ] **Step 3: 提交（若有 straggler 修复；否则跳过）**

```bash
# 仅当 Step 1 有就地修复时
git add <被修文件.cs>
git commit -m "fix(compat): cover straggler Awaitable usage on Unity 2022"
```

---

### Task 5: package.json `unity` 下限 + 文档

**Files:**
- Modify: `package.json`（**仅** `"unity"` 字段；**不碰 `dependencies`**——用户管理）
- Modify: `README.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

**Interfaces:** 无代码接口。

- [ ] **Step 1: 降 `unity` 字段**

`package.json`：`"unity": "6000.0"` → `"unity": "2022.3"`。**保留** `dependencies` 现状（`com.unity.ugui: 1.0.0` / `lit-motion: 2.0.2` 是用户为跨版本兼容改的，勿动）。

- [ ] **Step 2: README 加「Unity 2022 支持」节**

在 `README.md` 合适位置加一节（英文），说明：Unity 2022.3 需安装 UniTask（`com.cysharp.unitask`，OpenUPM）；Unity 6+ 无需任何额外依赖，走原生 Awaitable；公共 API 在两版本上完全一致。

- [ ] **Step 3: C# SKILL 加同款一节**

在 `.claude/skills/scripting-promptugui-csharp/SKILL.md` 加一节（英文）：Unity 2022 backend note —— 装 UniTask 即可，`UI.*` / `Awaitable` 返回类型的用法与 Unity 6 无差异；`SourceResolver` 等 `Func<…, Awaitable<…>>` 委托签名不变。

- [ ] **Step 4: 刷新 + 提交**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"], count="30")   # 期望仍清零
```

```bash
git add package.json README.md .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs: Unity 2022 support (unity floor 2022.3 + UniTask install note)"
```

---

### Task 6: 双后端最终确认（Unity 6）+ 收尾

**Files:** 无。

- [ ] **Step 1: 请用户把 MCP 切回 Unity 6 工程**

告诉用户切换（我无法自己切）。切回后 `refresh_unity(mode="force")`。

- [ ] **Step 2: Unity 6 上确认 Awaitable 后端未受影响**

```
mcp__UnityMCP__read_console(action="get", types=["error"], count="40")   # 期望 0（Compat 被门控掉，原生 Awaitable 生效）
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: 三套全绿、console 洁净（与 Task 前基线一致）。`PromptUGUI.Tests.Compat` 在 6 上被门控排除、不参与——正常。

- [ ] **Step 3: 提交卫生——丢弃既有文件 meta churn**

确认新文件的 `.meta` 已在各 Task 提交；然后丢弃 2022↔6 双编辑器造成的既有文件 meta churn：

```bash
git status --short | grep -v '\.meta$'                 # 应只剩预期内的已提交项（工作树应干净）
git checkout -- '**/*.meta' 2>/dev/null || git checkout -- '*.meta'
git status --short                                     # 期望干净
```

- [ ] **Step 4: 收尾**

用 superpowers:finishing-a-development-branch 决定合并/PR 方式（CLAUDE.md：不许直接提交 main；走 PR / merge-commit + `--delete-branch`）。

---

## 附：范围外（本计划不做）

- 不给 shim 加 Awaitable 帧/计时静态 API（库 0 使用）。
- 不改 `package.json` 的 `dependencies`（用户管理 ugui/lit-motion 跨版本降级）。
- 不做全库其它 2022-only API 审计——Task 4 的整包编译会暴露任何 straggler；异步面已确认 Awaitable 是唯一 6-only 依赖。
