# Addressables Resolver + 异步 Load API 设计

**日期**：2026-05-11
**状态**：设计阶段（待 review，未进入实施）
**作用域**：把 `SourceResolver` 改成异步签名；为 Addressables 提供官方 helper（含 Editor hot reload）；重命名相关 Load API。不含运行时层之外的修改。
**依赖**：M4 设计 [`2026-05-08-m4-import-autoimport-hotreload-xsd-design.md`](2026-05-08-m4-import-autoimport-hotreload-xsd-design.md) §D1 / §D6 / §D8 / §D12

---

## 1. 背景

M4 时 PromptUGUI 通过 `UI.SourceResolver: Func<string, string>` 把"`src` → XML 字符串"完全交给调用方。`UseResourcesResolver(rootPath)` 是首个内置 helper：同步读 `Resources.Load<TextAsset>` 并附带 Editor 反向映射用于热重载。

像素游戏的实际工程里，`Resources/` 之外的常见选择是 **Addressables**——`.ui.xml` 在 Addressables Group 里挂 key，运行时按 key 异步加载，Editor 模式下走本地资源、Build 后从 catalog/AssetBundle 拉。Resources 接口同步、Addressables 接口异步，二者不能共用同一个 `Func<string,string>` 抽象。

同时 Unity 6 提供了原生 `Awaitable<T>` 作为协程/Task 之外的轻量异步原语，比 `Task` 更贴合引擎主线程模型。本设计借这次扩展把"加载入口"统一收编到 `Awaitable` 上，并直接把 Addressables 当一等公民支持。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| AR-D1 | `SourceResolver` 签名 | 改为 `Func<string, Awaitable<string>>`（单一异步路径） | 简单；Resources 包成已完成 Awaitable 几乎零开销；避免维护两条 resolver 通路 |
| AR-D2 | Resources helper 是否保留 | `UseResourcesResolver(rootPath)` 保留，行为不变，内部用 `AwaitableCompletionSource<string>` 立即完成 | 现有用户零迁移成本 |
| AR-D3 | Addressables helper 形态 | `UI.UseAddressableResolver()` 无参；`src` 直接当 Addressables key | Addressables Group 已是用户自管的命名空间，再叠一层 prefix 冗余 |
| AR-D4 | Addressables 依赖方式 | asmdef `versionDefines` 条件编译；`PROMPTUGUI_HAS_ADDRESSABLES` 在 `com.unity.addressables ≥ 1.x` 安装时生效 | 保持"运行时内容无关"承诺；不强制包消费者装 Addressables |
| AR-D5 | 公开 Load 入口命名 | `LoadDocumentFromSrc` → `LoadDocumentAsync`；`LoadCommonLibrary` → `LoadCommonLibraryAsync`；`Reload` → `ReloadAsync`；`ReloadCommonLibrary` → `ReloadCommonLibraryAsync`；返回 `Awaitable` / `Awaitable<IReadOnlyList<string>>` | 新名字反映"异步 + 走 resolver"的双重语义，与同步的 `LoadDocument(label, xml)` 形成明确对照 |
| AR-D6 | `LoadDocument(label, xml)` 是否异步化 | 保持同步 | 没有 I/O，加 `async` 是仪式感；该入口本就在 M4-D12 被排除在依赖图外 |
| AR-D7 | HotReload 与异步的对接 | `HotReload.NotifyAssetChanged` 保持同步签名，内部 fire-and-forget 调用 `ReloadAsync` / `ReloadCommonLibraryAsync` 并 try/catch | AssetPostprocessor 是同步上下文；await 不可行；保持 M4-D8 的"解析失败保留旧状态 + log"语义 |
| AR-D8 | 旧 API 是否兼容 | 不保留 sync wrapper；旧名直接删除 | 包还在 1.0.0、未公开发布；不引入永久死代码 |
| AR-D9 | Addressables 资源类型 | `TextAsset` | 与 Resources path 一致；用户在 Addressables Group 里挂 `.ui.xml`（Unity 默认认成 TextAsset） |
| AR-D10 | Addressables 句柄释放 | 加载完取 `.text` 后立即 `Addressables.Release(handle)` | XML 已读进字符串，asset 本身无须常驻 |
| AR-D11 | Editor 反向映射数据源 | 从 `AddressableAssetSettingsDefaultObject.Settings` 枚举 `AssetEntry`，按 GUID 命中 | 不依赖 catalog；Editor 下 settings 是 ground truth |
| AR-D12 | 反向映射缓存策略 | `UseAddressableResolver` 调用时构建 `guid → key` 字典；订阅 `AddressableAssetSettings.OnModification` 在 group 变更时重建 | 避免每次 AssetPostprocessor 调用都遍历所有 entry |

---

## 3. 完整使用示例

启动期（Resources 路径，行为完全不变）：

```csharp
UI.UseResourcesResolver("UI");
await UI.LoadCommonLibraryAsync("common/Buttons");
await UI.LoadCommonLibraryAsync("third-party/MyLib", @as: "ml");
var screenNames = await UI.LoadDocumentAsync("screens/MainMenu");
```

启动期（Addressables 路径，新增）：

```csharp
// 前置：项目装了 com.unity.addressables；
//       MainMenu.ui.xml 在 AA Group 里挂 key="screens/MainMenu"。

UI.UseAddressableResolver();
await UI.LoadCommonLibraryAsync("common/Buttons");          // 走 Addressables.LoadAssetAsync<TextAsset>("common/Buttons")
var screenNames = await UI.LoadDocumentAsync("screens/MainMenu");
UI.Open("MainMenu");
```

测试期（in-memory fake resolver，churn 示例）：

```csharp
// 旧：
UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;

// 新：
UI.SourceResolver = src => UITestHelpers.AsAwaitable(files.TryGetValue(src, out var v) ? v : null);
//  AsAwaitable 是 internal 测试辅助：
//    new AwaitableCompletionSource<string>() { result = s }.Awaitable
```

---

## 4. 公开 API 表

| 状态 | 签名 | 说明 |
|---|---|---|
| 改 | `public static Func<string, Awaitable<string>> SourceResolver { get; set; }` | 类型变更；语义不变（src→XML） |
| 改 | `public static Awaitable<IReadOnlyList<string>> LoadDocumentAsync(string src)` | 原 `LoadDocumentFromSrc` |
| 改 | `public static Awaitable LoadCommonLibraryAsync(string src, string @as = null)` | 原 `LoadCommonLibrary` |
| 改 | `public static Awaitable ReloadAsync(string screenName)` | 原 `Reload` |
| 改 | `public static Awaitable ReloadCommonLibraryAsync(string src)` | 原 `ReloadCommonLibrary` |
| 不变 | `public static void LoadDocument(string label, string xml)` | 同步；不参与 DepGraph |
| 不变 | `public static void UseResourcesResolver(string rootPath)` | 内部包成 Awaitable |
| 新增 | `public static void UseAddressableResolver()` | 仅在 `PROMPTUGUI_HAS_ADDRESSABLES` 下可见 |
| 不变 | `UI.HotReload.AssetPathToSrc`, `NotifyAssetChanged`, `Enabled`, `IconResolverRebuilder`, `NotifyIconAssetsChanged` | 签名维持同步 |

---

## 5. 异步管线落地细节

### 5.1 `DocumentLoader.Load`

`DocumentLoader.LoadInternal` 当前同步递归解析 Imports。改造方式：

- `Load` / `LoadAndMerge` 改返回 `Awaitable<LoadedDoc>`，内部 `await resolver(src)` 拿到 xml 再 parse。
- 递归仍然是按 Import 顺序串行 `await`（不并行）—— Import 之间可能存在依赖断言 / 错误传播，串行更可预测；后续若有需要再优化成 `Awaitable.WhenAll`。
- 循环检测、`AllSrcs` 去重、`allowScreens` 检查全部保留。

### 5.2 `UseResourcesResolver`

包装方式：

```csharp
SourceResolver = src => {
    if (string.IsNullOrEmpty(src))
        throw new IOException("Resources lookup with empty src");
    var ta = Resources.Load<TextAsset>($"{root}/{src}")
             ?? throw new IOException($"Resources lookup failed: {root}/{src}");
    var src_ = new AwaitableCompletionSource<string>();
    src_.SetResult(ta.text);
    return src_.Awaitable;
};
```

行为同 M4：同步 I/O，仅签名升级。Editor 反向映射 (`HotReload.AssetPathToSrc`) 不变。

### 5.3 `UseAddressableResolver`

文件 `Runtime/Application/AddressableResolverHelper.cs`，整个用 `#if PROMPTUGUI_HAS_ADDRESSABLES` 包起来：

```csharp
public static void UseAddressableResolver() {
    SourceResolver = async src => {
        if (string.IsNullOrEmpty(src))
            throw new IOException("Addressables lookup with empty src");
        var handle = Addressables.LoadAssetAsync<TextAsset>(src);
        try {
            var ta = await handle;
            if (ta == null) throw new IOException($"Addressables key not found: {src}");
            return ta.text;
        } finally {
            Addressables.Release(handle);
        }
    };

#if UNITY_EDITOR
    BuildAddressablesReverseMapping();
    HotReload.AssetPathToSrc = AddressablesAssetPathToSrc;
#endif
}
```

Editor 反向映射（5.4 详述）。

注：`async src => ...` 是 C# 异步 lambda；返回类型从 `Awaitable<string>` 推导（Unity 的 `Awaitable<T>` 是 `AsyncMethodBuilder` 兼容类型，可作为 `async` 方法返回类型）。这里需要在实施前实测，因为部分早期 Unity 6 版本对自定义 awaitable 作为返回类型支持不全；若不支持，则改写成显式 `AwaitableCompletionSource<T>` 形式。

### 5.4 Addressables → AssetPath 反向映射（Editor）

在 `#if UNITY_EDITOR` 段：

```csharp
private static Dictionary<string, string> _guidToKey;  // GUID → Addressables key

private static void BuildAddressablesReverseMapping() {
    _guidToKey = new();
    var settings = AddressableAssetSettingsDefaultObject.Settings;
    if (settings == null) return;
    foreach (var group in settings.groups) {
        if (group == null) continue;
        foreach (var entry in group.entries) {
            if (entry == null || string.IsNullOrEmpty(entry.guid)) continue;
            _guidToKey[entry.guid] = entry.address;  // primary key = address
        }
    }
    // Editor-only：订阅 Group 变更，重建表
    settings.OnModification -= OnSettingsModified;
    settings.OnModification += OnSettingsModified;
}

private static string AddressablesAssetPathToSrc(string assetPath) {
    if (string.IsNullOrEmpty(assetPath)) return null;
    if (!assetPath.EndsWith(".ui.xml")) return null;
    var guid = AssetDatabase.AssetPathToGUID(assetPath);
    if (string.IsNullOrEmpty(guid)) return null;
    return _guidToKey != null && _guidToKey.TryGetValue(guid, out var key) ? key : null;
}
```

`OnSettingsModified` 收到 `EntryCreated` / `EntryMoved` / `EntryRemoved` / `EntryModified` / `GroupAdded` / `GroupRemoved` 时调 `BuildAddressablesReverseMapping`。

### 5.5 HotReload + 异步

`HotReload.NotifyAssetChanged` 现在同步调用 `ReloadCommonLibrary` / `Reload`。改造：

```csharp
public static void NotifyAssetChanged(string assetPath) {
    if (!Enabled || AssetPathToSrc == null) return;
    var src = AssetPathToSrc(assetPath);
    if (string.IsNullOrEmpty(src)) return;

    if (_depGraph.IsCommons(src)) {
        _ = ReloadCommonLibraryAsyncLogged(src);
        return;
    }
    var affected = new List<string>();
    foreach (var name in _depGraph.ScreensDependingOn(src))
        affected.Add(name);
    foreach (var name in affected)
        _ = ReloadAsyncLogged(name);
}

private static async Awaitable ReloadAsyncLogged(string screenName) {
    try { await ReloadAsync(screenName); }
    catch (Exception e) {
        Debug.LogError($"[PromptUGUI] hot reload failed for screen '{screenName}': {e}");
    }
}
// 同形 ReloadCommonLibraryAsyncLogged
```

`_ = ` discard + Awaitable 是 fire-and-forget；Unity Awaitable 的异常会被 logger 吃；显式 try/catch 保守一点。

`UIAssetPostprocessor.OnPostprocessAllAssets` 不变——它已经在 try/catch 里调 `NotifyAssetChanged`。

### 5.6 测试辅助

`Runtime/AssemblyInfo.cs` 已经 `InternalsVisibleTo("PromptUGUI.Tests.EditMode")`。在 Runtime 加一个 internal helper：

```csharp
// Runtime/Application/UITestHelpers.cs
internal static class UITestHelpers {
    internal static Awaitable<string> AsAwaitable(string s) {
        var src = new AwaitableCompletionSource<string>();
        src.SetResult(s);
        return src.Awaitable;
    }
}
```

EditMode 测试用 `UITestHelpers.AsAwaitable(...)` 包字符串。PlayMode / Editor tests 同样可用。

测试侧 `await` 处理：EditMode 测试用 `[UnityTest]` + `IEnumerator` 是 NUnit 的协程，不能直接 `await Awaitable`。Unity 6 的 NUnit 集成允许 `async Task` test methods，但 `Awaitable` 需要桥接：

```csharp
[Test]
public void LoadDocumentAsync_returns_screen_names() {
    UI.SourceResolver = ...;
    var task = UI.LoadDocumentAsync("main");
    var names = task.GetAwaiter().GetResult();   // EditMode 同步 await 安全：resolver 已是已完成 Awaitable
    Assert.That(names, ...);
}
```

EditMode 测试里 resolver 总是已完成的 Awaitable（in-memory dict），`GetAwaiter().GetResult()` 不会真正阻塞——await 等同于 fast-path。PlayMode 测试如果将来引入真异步 Addressables，可用 `[UnityTest]` + `yield return task` 配 Awaitable→IEnumerator 桥（Unity 6 提供 `Awaitable.GetAwaiter()` 但不直接产 IEnumerator；必要时自己写小辅助）。

---

## 6. asmdef / 可选依赖

`Runtime/PromptUGUI.Runtime.asmdef` 增量：

```jsonc
{
  "name": "PromptUGUI.Runtime",
  "rootNamespace": "PromptUGUI",
  "references": [
    "Unity.TextMeshPro",
    "UnityEngine.UI",
    "Unity.Addressables"        // ← 新增（仅当装了 com.unity.addressables 时实际生效）
  ],
  ...
  "versionDefines": [
    {
      "name": "com.unity.addressables",
      "expression": "1.0.0",
      "define": "PROMPTUGUI_HAS_ADDRESSABLES"
    }
  ]
}
```

asmdef `references` 引用一个未安装的包不会阻止编译（Unity 会静默丢弃 unresolved reference），但 `using UnityEngine.AddressableAssets;` 在未装时会编译失败——所以 `AddressableResolverHelper.cs` 整体必须用 `#if PROMPTUGUI_HAS_ADDRESSABLES` 包起来。

注：`Unity.Addressables` 这个 reference 名需要在实施时按真实 asmdef 名核对（可能是 `Unity.Addressables` 或 `UnityEngine.AddressableAssets`，取决于 com.unity.addressables 版本）。

---

## 7. 迁移与破坏性影响

包尚未公开发布（`package.json` v1.0.0、PR #1/#3 在 main），无外部消费者。内部迁移面：

- `Samples~/MainMenu`, `Samples~/CommonControls` 里的 boot script——把 `LoadDocumentFromSrc` 改成 `await LoadDocumentAsync`，boot 入口需要 `async`。Samples 是文档+示例，churn 可接受。
- `Tests/EditMode/Application/*.cs` 大约 30 处 resolver 赋值、~20 处 `LoadDocumentFromSrc` 调用。机械替换。
- Editor 工具（`IconAtlasAutoSync` 等）只用了 `HotReload.NotifyIconAssetsChanged`，签名未变，零迁移。
- 主 spec / SKILL.md / CLAUDE.md：需要更新 API 名字与签名（SKILL.md 在同一 PR 内更新；主 spec 在 PR 描述里指出节号需后续修订）。

---

## 8. 测试策略

EditMode 新增 / 改造：

1. `UI.SourceResolver` 异步签名兼容性——已完成 Awaitable 立即可读，所有现有 `LoadDocumentFromSrc` 测试转 `LoadDocumentAsync` 后通过。
2. resolver 抛异常（`return Task.FromException` 等价 Awaitable）→ `LoadDocumentAsync` 把异常透传。
3. `UseResourcesResolver` 不变——`Resources.Load` 仍是同步路径；现有测试改名后通过。
4. `UseAddressableResolver`（在 PROMPTUGUI_HAS_ADDRESSABLES 下）：
   - host 工程装 com.unity.addressables，做一个最小 fixture：临时 group + 一个 .ui.xml 的 AssetEntry
   - 验证 `LoadDocumentAsync("test/key")` 能拉到内容
   - 验证 `HotReload.AssetPathToSrc(assetPath)` 反查能命中

PlayMode：
- 现有 `HotReloadTests` 的"AssetPostprocessor → Reload"路径转异步后仍通过（fire-and-forget + 同帧完成）。

Addressables 是可选依赖。如果 host 工程未装，对应测试通过 `#if PROMPTUGUI_HAS_ADDRESSABLES` 跳过。

---

## 9. 非目标 / 推迟

- **并行 Import 解析**：当前串行 `await`；Import 数通常 < 10 且本地资源，性能不是瓶颈。
- **Addressables 句柄缓存复用**：每次加载即 `Release`；不维护"key → 长生命周期 handle"映射。XML 只在 Load 时读一次，缓存意义不大。
- **运行时 Addressables 反向映射**：仅 Editor 提供 path↔key 反查；Player Build 没有 AssetDatabase 也没有 AddressableAssetSettings。运行时 `HotReload.AssetPathToSrc` 始终 null 即可。
- **现有 Resources helper 的弃用**：保留作为零依赖默认选项；Resources 仍是 Unity 最简单的内置 asset 通道，对小项目 / 内置 sample 友好。

---

## 10. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| `Awaitable<T>` 作为 async lambda 返回类型在某些 Unity 6 版本不被 builder 识别 | `UseAddressableResolver` 写不出 `async src => ...` | 实施时先验证；不行就改 `AwaitableCompletionSource<T>` 显式形式 |
| `AddressableAssetSettings.OnModification` 在编辑器某些路径不发事件（如脚本修改 group） | 反向映射陈旧、热重载找不到 src | 给 `BuildAddressablesReverseMapping` 加一个公开重建入口（`UI.HotReload.RebuildAddressablesMapping()`），Editor menu / 用户脚本可手动触发 |
| EditMode test 没有事件循环，`GetAwaiter().GetResult()` 在真正阻塞的 Awaitable 上死锁 | 测试 hang | 限制：resolver 在测试里必须返回**已完成** Awaitable；helper 加注释；测试用 fake 不能 `await Task.Delay` |
| asmdef reference 名字写错（`Unity.Addressables` vs 其他） | 装了 Addressables 也编不过 | plan 阶段先在 host 工程实测 asmdef 解析 |

---

## 11. 实施粒度（提示）

实施 plan（在 writing-plans 阶段细化）大致分块：

1. `DocumentLoader.Load*` 异步化 + EditMode 测试机械迁移
2. `UI.SourceResolver` 签名 + `LoadDocument*` / `Reload*` 改名 + `UseResourcesResolver` 包装
3. `HotReload.NotifyAssetChanged` 异步桥
4. `AddressableResolverHelper` + asmdef versionDefines + Editor 反向映射
5. Samples / SKILL.md / CLAUDE.md 更新

每块 EditMode 测试先红再绿；4 块需要 host 工程装 Addressables 才能真测。
