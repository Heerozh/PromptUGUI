# Unity 2022 支持：Awaitable → UniTask 后端切换（polyfill 方案）设计

**日期**: 2026-07-06
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:

- 让 PromptUGUI 在 Unity 2022.3 LTS 上编译/运行（当前 `package.json` 要求 `6000.0`）。
- 唯一的 Unity-6-only 依赖是异步类型 `UnityEngine.Awaitable` / `AwaitableCompletionSource`（Unity 2023.1 / 6 才引入）。在 Unity <6 上用 **UniTask**（`com.cysharp.unitask`）作底座。
- **现有 71 个源文件 + 全部测试一行不改**——通过在 `UnityEngine` 命名空间下补一个仅 2022 编译的 polyfill 实现。

**依赖**: Unity <6 上新增可选依赖 **UniTask**（`com.cysharp.unitask` ≥ 2.0，OpenUPM/git 安装，无法做硬 UPM 依赖）。Unity 6+ 无新增依赖，走原生 `Awaitable`。复用：UniTask 自带的 `AsyncUniTaskMethodBuilder(<T>)`、`UniTaskCompletionSource(<T>)`。

---

## 1. 背景与动机

项目大量使用 Unity 6 的 `Awaitable`（`Awaitable<T>` 34 处、裸 `Awaitable` 126 处、`AwaitableCompletionSource` 24 处，散落 71 个文件）。`Awaitable` 在 Unity 2022 不存在，是当前无法降到 2022 的唯一硬阻塞。

目标是让 Unity 2022 用户装上 UniTask 后**无缝**使用本库，且**不牺牲** Unity 6 用户的任何东西（原生 Awaitable、零额外依赖、现有代码/测试完全不动）。

### 1.1 关键前置审计（决定了方案可行性，务必保留）

在选型前审计了整个异步面，三条结论是本方案成立的基础：

1. **完全不用 `Awaitable` 的帧/计时静态 API**——`Awaitable.NextFrameAsync` / `WaitForSecondsAsync` / `EndOfFrameAsync` / `FixedUpdateAsync` / `FromAsyncOperation` / `MainThreadAsync` / `BackgroundThreadAsync` 全 0 命中。异步面只是「返回 `Awaitable`/`Awaitable<T>` + `AwaitableCompletionSource` + `await` 内部 awaitable + resolver 委托」这一小子集。shim 只需覆盖这个子集。

2. **没有任何 `await` 依赖 Unity 内置 awaiter**（这条决定「零文件改动」成立，不是假设）：
   - UnityWebRequest 走**手动 completion-source 桥**：`op.completed += _ => acs.SetResult(true); if(!op.isDone) await acs.Awaitable;`（`UI.Markdown.cs:61`、`MarkdownBoxRequest.cs:166`）——由 shim 的 `AwaitableCompletionSource<T>` 覆盖。
   - Addressables 走 `await handle.Task`（`AddressableResolverHelper.cs:67` 等 3 处）——`.Task` 是 `System.Threading.Tasks.Task<T>`，全 Unity 版本通用的 .NET awaiter，与后端无关；源码注释亦注明「.Task 兼容更广」。
   - 其余 `await` 都是 await 我们自己返回 `Awaitable`/`AwaitableCompletionSource.Awaitable` 的方法——由 shim 覆盖。

   ⇒ **没有任何文件需要新增 `using Cysharp.Threading.Tasks;`**。

3. **不 await 任何后端相关 awaitable**（R3/LitMotion/AsyncOperation 直接 await 均 0），不会有「后端类型泄漏到公共面」的地方。

## 2. 目标 / 非目标

**目标**

- Unity 2022.3 上编译通过、可加载文档 / 开模态 / 跑 EditMode 同步解包。
- 公共 API 签名**逐字不变**（仍是 `UnityEngine.Awaitable<string>` 等），2022 下仅底座换成 UniTask。
- Unity 6 路径**零影响**：原生 Awaitable、无新增依赖、源文件与测试不动。
- 用户**零语义转换**：写 `async Awaitable<string>` resolver、`await UI.LoadDocumentAsync(...)`、把现成 `UniTask<T>` 塞进 `Func<…,Awaitable<T>>` 都直通。

**非目标（留口不做）**

- ~~不给 shim 加 Awaitable 的帧/计时静态 API（库里 0 使用）。~~ **【2026-07-06 修正】** 此判断过窄——库自己不用，但**消费者代码/samples 会用**（sample 的 `Awaitable.WaitForSecondsAsync`，PR #95 合并后暴露）。已补 PlayerLoop 系静态助手 `NextFrameAsync`/`EndOfFrameAsync`/`FixedUpdateAsync`/`WaitForSecondsAsync`（→ `UniTask.NextFrame`/`Yield(PlayerLoopTiming.*)`/`Delay`，WebGL 安全）。线程切换类（`MainThreadAsync`/`BackgroundThreadAsync`）仍不加（违背 WebGL 无线程原则）。
- 不做全库 2022 兼容大审计。基于 §1.1，Awaitable 是异步路径唯一的 6-only 依赖；其余包（LitMotion 最低 2021.3 / Addressables / Newtonsoft / uGUI 2.0）均支持 2022.3，在 §9 的 2022 验证步骤一并确认。
- UniTask 不做硬 UPM 依赖（不在 Unity registry）。
- 后端选择**版本驱动**，不提供「Unity 6 上也强制走 UniTask」的 define（已与用户确认）。

## 3. 程序集与门控

新增 `Runtime/Compat/PromptUGUI.Compat.UniTask.asmdef`（Unity 惯用的「可选依赖」模式）：

```jsonc
{
  "name": "PromptUGUI.Compat.UniTask",
  "rootNamespace": "",
  "references": ["UniTask"],
  "autoReferenced": true,
  "defineConstraints": ["!UNITY_6000_0_OR_NEWER", "PROMPTUGUI_HAS_UNITASK"],
  "versionDefines": [
    { "name": "com.cysharp.unitask", "expression": "2.0.0", "define": "PROMPTUGUI_HAS_UNITASK" }
  ]
}
```

- asmdef 层面：只在 **装了 UniTask** 时才产出程序集（`PROMPTUGUI_HAS_UNITASK` 约束）。那条 `UniTask` 引用因此**永远不会报 missing reference**——这正是**不把 `UniTask` 引用塞进 `PromptUGUI.Runtime`** 的原因（避免 Unity 6 上的 missing-reference 噪音，对比现有 Addressables 直引用的告警方式）。
- shim 类型放在 `namespace UnityEngine`，且 asmdef `autoReferenced: true`，故 `PromptUGUI.Runtime` / `PromptUGUI.Editor` **无需显式引用**即可看到 `UnityEngine.Awaitable`。

**版本门控的权威来源是 C# `#if`，不是 defineConstraints**（重要）：

- **`defineConstraints` 里的 `!UNITY_6000_0_OR_NEWER` 只是「尽力而为」的早退**。Unity 的 `defineConstraints` 对内置 `UNITY_*_OR_NEWER` 宏的支持历来不确定（它稳定支持的是 versionDefine 符号）。若它认这个宏→Unity 6 上 asmdef 直接不编译（最干净）；若不认→见下。
- **所有 shim 源文件整体包在 `#if !UNITY_6000_0_OR_NEWER … #endif`（§4）——这是权威门控**，C# 预处理器 100% 认得该宏。于是：
  - Unity 6 + 装了 UniTask（万一 asmdef 没被早退掉）：Compat 编译成**空程序集**（所有类型被 `#if` 剪掉）→ 不产出 `UnityEngine.Awaitable` → 与 Unity 原生零冲突。
  - Unity 6 + 没装 UniTask：asmdef 被 `PROMPTUGUI_HAS_UNITASK` 约束掉。
  - Unity <6 + 装了 UniTask：正常产出 shim。
  - Unity <6 + 没装 UniTask：asmdef 约束掉 → 命中 §7 护栏单条报错。
- 结论：设计对「defineConstraints 认不认版本宏」这一不确定性**免疫**——无论认不认，Unity 6 上都绝不会与原生 `Awaitable` 撞。

> versionDefine 与 defineConstraint 放在同一 asmdef 上是 Unity 标准惯用法（先算 versionDefines 再算 defineConstraints），与本仓 `PromptUGUI.Markdown` 的 `PROMPTUGUI_HAS_MARKDIG` 门控同构。

## 4. shim 类型面（`Runtime/Compat/`，`namespace UnityEngine`）

**所有 shim 源文件整体包在 `#if !UNITY_6000_0_OR_NEWER … #endif`**（§3 的权威门控）。按 §1.1 的实际用量精确匹配，均做成 **`sealed class`**（逐位匹配 Unity 语义：引用类型、可空、可 `= null`；代价是每次异步操作一次分配，UI 场景可接受，日后可池化）。

| 类型 | 内部字段 | 关键成员 |
|---|---|---|
| `Awaitable`（sealed class） | `UniTask _task` | `[AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder))]`、`GetAwaiter()`、隐式 `↔ UniTask` |
| `Awaitable<T>`（sealed class） | `UniTask<T> _task` | `[AsyncMethodBuilder(typeof(AwaitableAsyncMethodBuilder<>))]`、`GetAwaiter()`（`GetResult()` 返回 `T`）、隐式 `↔ UniTask<T>` |
| `AwaitableCompletionSource`（class） | `UniTaskCompletionSource _src` | `Awaitable Awaitable { get; }`、`SetResult()`、`SetException(Exception)`、`SetCanceled()` |
| `AwaitableCompletionSource<T>`（class） | `UniTaskCompletionSource<T> _src` | `Awaitable<T> Awaitable { get; }`、`SetResult(T)`、`SetException(Exception)`、`SetCanceled()` |

> `SetResult`（库内 12 处）/ `SetException`（2 处）是库实际用到的；`SetCanceled` 库内 0 使用，但它是 Unity `AwaitableCompletionSource` 的公共成员，保留以对齐 Unity API（用户代码可能调用），实现即 `_src.TrySetCanceled()`。

`GetAwaiter()` 转发到内部 `UniTask(<T>)` 的 awaiter，从而同时撑起：

- 全部 `await`（含 `await acs.Awaitable`）。
- 测试里 232 处 `.GetAwaiter().GetResult()` 同步解包——与现状同语义：仅对**已同步完成**的 awaitable 有效（fake resolver 走 `AwaitableHelpers.Completed`→已完成的 UniTask→`GetResult()` 同步返回；未完成不阻塞、直接抛，与 Unity 原生 Awaitable 行为一致）。

## 5. AsyncMethodBuilder + 隐式转换

**两个 builder**（`AwaitableAsyncMethodBuilder`、`AwaitableAsyncMethodBuilder<T>`）——**直接转发**给 UniTask 的 `Cysharp.Threading.Tasks.CompilerServices.AsyncUniTaskMethodBuilder(<T>)`，白嫖其池化：

```csharp
public struct AwaitableAsyncMethodBuilder<T>
{
    AsyncUniTaskMethodBuilder<T> _inner;
    public static AwaitableAsyncMethodBuilder<T> Create() => new() { _inner = AsyncUniTaskMethodBuilder<T>.Create() };
    public Awaitable<T> Task => _inner.Task;            // UniTask<T> --隐式--> Awaitable<T>
    public void SetResult(T value)            => _inner.SetResult(value);
    public void SetException(Exception e)     => _inner.SetException(e);
    public void SetStateMachine(IAsyncStateMachine sm) => _inner.SetStateMachine(sm);
    public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine => _inner.Start(ref sm);
    public void AwaitOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine
        => _inner.AwaitOnCompleted(ref a, ref sm);
    public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA a, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine
        => _inner.AwaitUnsafeOnCompleted(ref a, ref sm);
}
```

（非泛型 builder 同构，`Task` 返回 `Awaitable`。）

**双向 implicit 转换**（在 shim 类型上）——因为 2022 下 `Awaitable(<T>)` 内部就是一个 `UniTask(<T>)` 字段，拆包/重包平凡：

- `Awaitable<T> ⇐ UniTask<T>` / `UniTask<T> ⇒ Awaitable<T>`（及非泛型版）。

## 6. 用户零转换的三处保障（缺一不可）

| 用户写法 | 靠什么直通 |
|---|---|
| `async Awaitable<string> MyResolver(...) { ... return xml; }` | **builder**（§5） |
| `await UI.LoadDocumentAsync(...)`、`.GetAwaiter().GetResult()` | **`GetAwaiter()`**（§4） |
| `UI.SourceResolver = s => LoadXmlUniTask(s);`（lambda 体是 `UniTask<string>`，目标 `Awaitable<string>`） | **隐式转换**（§5） |
| `UniTask.WhenAll(UI.LoadDocumentAsync(a), UI.LoadDocumentAsync(b))` | **隐式转换**（`Awaitable ⇒ UniTask`） |

> 注：委托赋值不会对**返回类型**插入隐式转换，故 `UI.SourceResolver = LoadXmlUniTask;`（方法组直赋、返回类型不匹配）仍需包成 lambda `s => LoadXmlUniTask(s)`——这是 C# 语言限制，非本方案缺陷；常见的 `async` lambda 写法本就直通。

## 7. 友好护栏 + package.json + 文档

**编译护栏**——`Runtime/` 下加一个永远编译的小文件 `Runtime/Compat/UniTaskRequirement.cs`：

```csharp
#if !UNITY_6000_0_OR_NEWER && !PROMPTUGUI_HAS_UNITASK
#error PromptUGUI on Unity 2022 requires UniTask (com.cysharp.unitask). Install it via OpenUPM: https://openupm.com/packages/com.cysharp.unitask/
#endif
```

一条清晰报错替代几百个 CS0246。为让护栏在 Runtime 里看得见该 define，**`PromptUGUI.Runtime.asmdef` 的 `versionDefines` 也补上** `{ com.cysharp.unitask ≥ 2.0.0 → PROMPTUGUI_HAS_UNITASK }`。

**package.json**——`"unity": "6000.0"` → `"2022.3"`（本库唯一要改的一处）。UniTask 无法列为 `dependencies`（不在 Unity registry），改由文档说明。

依赖也须同时兼容两版本（2026-07-06 在真实 2022 工程实测确认）——这部分**由用户在 Windows 侧管理，本计划不改 `dependencies`**：

- `com.unity.ugui`：`"2.0.0"` → `"1.0.0"`。UPM 把 dependency 版本当「最低版本」：Unity 6 解析到内置 2.0.0、2022 解析到 1.0.0 → **一份 manifest 通吃**。
- `com.annulusgames.lit-motion`：git URL → `"2.0.2"`（用户确认两版本均可解析）。

> 教训：`ugui 2.0.0` 需 Unity 2023.2+，是 2022 加载本包的第一道坎；先把 deps 降到可解析，2022 才会「编到 Awaitable 才失败」（而非「包根本加载不了」）。

**文档**——README + `scripting-promptugui-csharp` skill 加一节「Unity 2022 支持」：说明装 UniTask（OpenUPM）即可，公共 API 无差异。公共 API 签名不变，其余 skill 无需改。

## 8. 边界与错误处理

- **Unity 6 上加了这套东西是否有副作用？** 无。Compat asmdef 要么被 `PROMPTUGUI_HAS_UNITASK` 约束掉（没装 UniTask），要么编成空程序集（装了 UniTask 但被 `#if` 剪空）；护栏 `#if` 短路；`package.json` 降 min-version 不影响 6。可在宿主（Unity 6）上验证：refresh 后 console 无新 error/warning。
- **2022 上没装 UniTask**：命中 §7 护栏，单条明确报错。
- **重复类型风险**：仅当 2022 工程里**另有**一个包也往 `UnityEngine` 塞 `Awaitable` 才冲突——概率极低；真遇到再文档指引。Unity 6 上因整个 asmdef 被约束掉，绝不可能与 Unity 原生 `Awaitable` 撞。
- **单次消费语义**：UniTask 与 Unity Awaitable 同为「只能 await 一次」，shim 包装不改变该语义，与现状一致。

## 9. 测试计划

> **环境更新（2026-07-06）**：MCP 现已切到一个真实 **Unity 2022.3 + UniTask** 工程，且该工程通过 bind-mount（`C:\xsoft\PromptUGUI` == 仓库 `/workspace-PromptUGUI`）直接编译本包源码。因此原「宿主是 Unity 6、shim 无法在宿主编译/测试」的硬限制**已解除**：可在 2022 上跑真·红→绿 TDD。核心机制已用隔离 spike（独立 asmdef 只引用 UniTask）实测通过——`[AsyncMethodBuilder]` 自定义类型 + 转发 `AsyncUniTaskMethodBuilder<T>` + 隐式转换 + `async`/`await` 全绿，且 `AsyncMethodBuilderAttribute` 跨程序集可访问（**故无需自补该 attribute**）。

**主验证：Unity 2022 + UniTask（MCP 当前指向，可自动化）**

- 加 shim 前：`read_console` 里全是 `Awaitable` / `Awaitable<>` / `AwaitableCompletionSource` 的 CS0246/CS0234（已实测，且**仅**这三种类型缺失 → 佐证 Awaitable 是唯一 6-only 依赖）。这是天然的整包级 RED。
- 逐步加 shim：每加一块 refresh，看对应错误消失 → 整包级 GREEN。
- shim 行为测试放 `Tests/EditMode/Compat/`，`#if !UNITY_6000_0_OR_NEWER` 门控，覆盖：`async Awaitable<T>` 经 builder 产出并 `await` / 同步 `GetResult()` / `AwaitableCompletionSource<T>` 桥 / 双向隐式转换。在 2022 上 `run_tests` 实跑绿。
- 全量既有 EditMode/PlayMode 套件在 2022 上跑（UniTask 后端），作回归。

**次验证：Unity 6（Awaitable 后端，需把 MCP 切回 6 或用户自测）**

- shim 源全在 `#if !UNITY_6000_0_OR_NEWER` 内 → 6 上不参与编译；现有 71 文件 + 测试一字未改 → Awaitable 后端**天然不受影响**。仍应在收尾时把 MCP 切回 Unity 6 跑一次全套件 + console 洁净，作最终双后端确认。

> **工作树卫生（bind-mount 副作用）**：2022 编辑器 refresh 会把包里几百个极简 `.meta` 展开成完整 `MonoImporter` 块（GUID 不变，无害 churn）；Unity 6 又会改回去。**提交时只显式 `git add` 目标源文件 + 新文件的 `.meta`，绝不 `git add -A`**；收尾用 `git checkout -- '**/*.meta'` 丢弃既有文件的 meta churn（新文件的 meta 已先行显式提交）。

## 10. 实现顺序（给 plan 的提示）

1. `PromptUGUI.Compat.UniTask.asmdef` + `.meta`（门控壳先立起来）。
2. shim 类型 `Awaitable` / `Awaitable<T>` / `AwaitableCompletionSource(<T>)`（§4）。
3. 两个 AsyncMethodBuilder（§5）+ 双向隐式转换。
4. `UniTaskRequirement.cs` 护栏 + `PromptUGUI.Runtime.asmdef` versionDefine（§7）。
5. `package.json` min-version + README/C# skill 文档节（§7）。
6. `#if !UNITY_6000_0_OR_NEWER` 门控的 shim 验证测试（§9）。
7. 2022 主验证：整包 console 洁净（Awaitable 错误清零）+ Compat 行为测试 + 全量既有套件在 2022 跑绿（§9）。
8. 次验证：把 MCP 切回 Unity 6，全套件 + console 洁净，确认 Awaitable 后端未受影响（§9）。

## 11. 实现修正与验证结果（2026-07-06 真机双后端）

实现时以下几处偏离了原设计（都已在真机验证）：

1. **asmdef 引用不传递**（原 §3 说 Runtime 靠 `autoReferenced` 自动看见 Compat——错）。`autoReferenced` 只让**预定义程序集**（Assembly-CSharp）自动引用；自定义 asmdef 之间必须显式引用。⇒ **每个在签名里用到 `Awaitable` 的程序集都显式引用 `PromptUGUI.Compat.UniTask`**：`Runtime` + `Tests.EditMode` + `Tests.PlayMode`。
2. **`await Awaitable` 泄漏 `UniTask.Awaiter`**（`GetAwaiter()` 返回 UniTask 的 awaiter，编译器需其可访问）⇒ 这些程序集**再加 `UniTask` 引用**。**实测 Unity 6（未装 UniTask）：0 错误、0 告警**——缺失的 `UniTask` 引用与被门控的 `Compat` 引用都被 Unity **静默丢弃**。故未采用 class-awaiter 隐藏方案（原 §5 note 的备选），保持简单。
3. **`AwaitableCompletionSource` 还需 `TrySetResult`/`TrySetException`/`TrySetCanceled`**（原 §4 审计只列了 `Set*`；库两者都用）——shim 两套都实现。
4. **TMP straggler（Awaitable 之外的唯一 6-only 依赖）**：`Text.cs`/`InputField.cs` 的 `textWrappingMode`/`TextWrappingModes` 是 TMP 3.2（Unity 2023.1+）API，2022 旧 TMP 用 `enableWordWrapping` ⇒ 按 `#if UNITY_2023_1_OR_NEWER` 条件化（改了这 2 个既有文件，是 §2 非目标里「全库审计」由 Task 4 整包编译自然暴露的）。
5. **测试基建（2022 侧，非包代码）**：新测试集要进 `package.json` `testables` **且重启编辑器**才被 Test Runner 发现；跑既有套件需 `com.unity.test-framework` ≥ 1.4（旧 1.1.33/ext.nunit 1.0.6 无 `Assert.ThrowsAsync`）；新工程须先 **Import TMP Essentials**（否则 Text/Btn 实例化 NRE，仅全量跑时暴露）。

**验证结果**：Unity 2022.3 + UniTask — 整包+全程序集编译，EditMode **2372/2372**，PlayMode **169/171**（2 个 InputFieldNav 失败=2022 InputSystem/TMP 输入时序，零 async，非 shim）。Unity 6 原生 Awaitable — 0 错误/告警，EditMode **2274/2274**，PlayMode **171/171**（含那 2 个 nav，坐实其为 2022 环境特有）。
