# Addressables Resolver + 异步 Load API 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落实设计 spec `docs~/superpowers/specs/2026-05-11-addressable-resolver-async-load-design.md`：

1. `UI.SourceResolver` 改成 `Func<string, Awaitable<string>>`（异步签名）。
2. `LoadDocumentFromSrc` / `LoadCommonLibrary` / `Reload` / `ReloadCommonLibrary` → `*Async` 命名，返回 `Awaitable` / `Awaitable<...>`。
3. 新增 `UI.UseAddressableResolver()`，在 `Runtime/Application/AddressableResolverHelper.cs`；整个文件用 `#if PROMPTUGUI_HAS_ADDRESSABLES` 包起来，靠 `versionDefines` 在 `com.unity.addressables ≥ 1.0` 安装时启用。
4. Editor 下 Addressables hot reload：通过 `AddressableAssetSettings` 反查 `guid → key`，喂给 `HotReload.AssetPathToSrc`。
5. `HotReload.NotifyAssetChanged` 保持同步签名，内部 fire-and-forget async + log（AssetPostprocessor 无法 await）。
6. Samples / SKILL.md / CLAUDE.md 同步更新。

**Architecture:** `DocumentLoader.Load` 全链路异步化（`await resolver(src)` 串行递归 Imports，循环检测保留）。`UseResourcesResolver` 把同步 `Resources.Load<TextAsset>` 包成已完成 `Awaitable<string>`，Editor 反向映射不变。新增的 `AddressableResolverHelper.cs` 与 `ResourcesResolverHelper.cs` 同目录、同样是 `public static partial class UI` 的一部分（同一 asmdef，partial 合并）；文件整体 `#if PROMPTUGUI_HAS_ADDRESSABLES`。Runtime asmdef 加 `Unity.Addressables` + `Unity.Addressables.Editor` 两条 reference 与 `versionDefines`：装了 com.unity.addressables 时 define 触发、文件 enable；未装时 define 不触发、文件全段排除，asmdef 仍能编译（unresolved reference 在现代 Unity 中是 warning 级、不阻断编译）。`HotReload.NotifyAssetChanged` 内部包两个 `*AsyncLogged` 私有方法，`_ =` discard 触发，try/catch 日志。Addressables 测试单独走 `PromptUGUI.Tests.EditMode.Addressables` asmdef，用 `defineConstraints` 在没装 Addressables 的环境下整段跳过，主测试集合零污染。

**Tech Stack:** Unity 6 (6000.0+), Unity Awaitable, com.unity.addressables ≥ 1.x（可选），NUnit (Unity Test Framework), TextMeshPro, R3 (Cysharp)。承接 M1-M5 与 PR #1 / PR #3 已落地的 Application / DocumentLoader / DepGraph / HotReload。

---

## 假设与前置

工程师执行本计划前需要：

1. M1-M5 与 PR #1 / PR #3 已合并；`main` 与 `origin/main` 同步。
2. 宿主 Unity 项目位于 `C:\xsoft\PromptUGUIDev`；本仓库以 file:// UPM 引用。
3. UnityMCP 已连接；测试一律走 MCP，不要 spawn batch-mode Unity。
4. 宿主工程**预装 `com.unity.addressables`**（任意 ≥ 1.x 版本）才能跑 Section 2 的 Addressables 路径测试；如未装，Section 2 的 Addressables 测试 asmdef 将被 `defineConstraints` 自动跳过。

测试运行约定（每个 Task 完成后跑相应 assembly）：

- `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
- `mcp__UnityMCP__read_console(action="get", types=["error"])`
- `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`
- `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
- `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])`
- `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`

Section 1（Runtime / 测试机械迁移）是签名级 refactor，"红 → 绿 的形式 = 现有测试套件全绿"。TDD 应用到 Section 2 的真正新增能力（Addressables resolver + 反向映射）。

---

## 已知风险（spec §10 复述）

- **`async Awaitable<T>` 作为 lambda 返回类型**：spec §5.3 假设 `SourceResolver = async src => ...` 可写。Unity 6 实测 `async` 方法体内可 `await` 任意 awaiter，但 lambda 推断 `Awaitable<T>` 返回类型在某些版本可能失败。**本 plan 全程使用显式 `AwaitableHelpers.Completed/Faulted` + 命名 `async` 方法**，不依赖 async lambda 推断。
- **`Addressables.LoadAssetAsync` 桥接**：Addressables 1.21+ 的 `AsyncOperationHandle<T>` 直接可 `await`；老版本只暴露 `.Task`。本 plan Task 9 用 `await handle.Task`（向后兼容 1.x 全系列）。
- **asmdef reference 名实际值**：`Unity.Addressables` / `Unity.ResourceManager` / `Unity.Addressables.Editor` 是当前 Addressables 包 ≥ 1.x 的标准 asmdef 名。Task 8 与 Task 10 在 refresh 后检查 console；如有 unresolved reference 报错（不是 warning），改用真实名字（通过 Unity Inspector 选取 .asmdef 文件查看官方名）。

---

## 文件结构

```
PromptUGUI/                                                  # 仓库根
├── Runtime/
│   ├── PromptUGUI.Runtime.asmdef                            # Modify (T8): 加 Addressables refs + versionDefines
│   └── Application/
│       ├── AwaitableHelpers.cs                              # Create (T1)
│       ├── DocumentLoader.cs                                # Modify (T2)
│       ├── UI.cs                                            # Modify (T3,T5)
│       ├── ResourcesResolverHelper.cs                       # Modify (T4)
│       └── AddressableResolverHelper.cs                     # Create (T9)
├── Editor/
│   ├── UIAssetPostprocessor.cs                              # unchanged
│   └── PromptUGUI.Editor.asmdef                             # unchanged
├── Tests/EditMode/
│   ├── PromptUGUI.Tests.EditMode.asmdef                     # unchanged
│   ├── Application/
│   │   ├── CommonLibraryTests.cs                            # Modify (T7)
│   │   ├── HotReloadTests.cs                                # Modify (T7)
│   │   ├── ImportSemanticsTests.cs                          # Modify (T7)
│   │   ├── CanvasConfiguratorTests.cs                       # Modify (T7)
│   │   └── IconHotReloadTests.cs                            # unchanged
│   └── Addressables/                                        # NEW
│       ├── PromptUGUI.Tests.EditMode.Addressables.asmdef    # Create (T10)
│       ├── AddressableResolverTests.cs                      # Create (T11)
│       └── AddressableHotReloadTests.cs                     # Create (T12)
├── Samples~/
│   ├── MainMenu/MainMenuRunner.cs                           # Modify (T6)
│   └── CommonControls/CommonControlsRunner.cs               # Modify (T6)
├── .claude/skills/authoring-promptugui-xml/
│   └── SKILL.md                                             # Modify (T13)
├── CLAUDE.md                                                # Modify (T14)
└── package.json                                             # Modify (T10): testables 加 Addressables 测试 asmdef
```

注意：**Runtime 端不新建 `Runtime/Addressables/` 子目录**。`AddressableResolverHelper.cs` 直接放 `Runtime/Application/`，与 `ResourcesResolverHelper.cs` 平级——因为它也是 `public static partial class UI`（C# partial 不能跨 asmdef）。测试侧用单独的 `Tests/EditMode/Addressables/` 子目录隔离。

---

# Section 1 —— 异步基建（不改公开 API 名字，先把签名挪到位）

## Task 1：内部测试辅助 `AwaitableHelpers`

**Files:**
- Create: `Runtime/Application/AwaitableHelpers.cs`

`PromptUGUI.Tests.EditMode` 已通过 `Runtime/AssemblyInfo.cs` 的 `InternalsVisibleTo` 看到 internal。把同步值包成已完成的 `Awaitable<T>`、把异常包成 faulted `Awaitable<T>`——Resources helper、Addressables helper、测试 fake 都会用。

- [ ] **Step 1：创建 `AwaitableHelpers.cs`**

```csharp
// Runtime/Application/AwaitableHelpers.cs
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 把同步值包成已完成的 <see cref="Awaitable{T}"/>。
    /// Resources resolver 和 EditMode 测试 fake resolver 用。
    /// </summary>
    internal static class AwaitableHelpers
    {
        internal static Awaitable<T> Completed<T>(T value)
        {
            var src = new AwaitableCompletionSource<T>();
            src.SetResult(value);
            return src.Awaitable;
        }

        internal static Awaitable<T> Faulted<T>(System.Exception ex)
        {
            var src = new AwaitableCompletionSource<T>();
            src.SetException(ex);
            return src.Awaitable;
        }
    }
}
```

- [ ] **Step 2：refresh + 检查编译**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected：无 error。

- [ ] **Step 3：commit**

```bash
git add Runtime/Application/AwaitableHelpers.cs Runtime/Application/AwaitableHelpers.cs.meta
git commit -m "feat: AwaitableHelpers.Completed/Faulted 内部测试辅助"
```

注：`.cs.meta` 由 Unity 在 refresh 时自动生成，所以 refresh 必须先跑过。

---

## Task 2：`DocumentLoader` → 异步

**Files:**
- Modify: `Runtime/Application/DocumentLoader.cs`

把同步签名升到 `Awaitable<LoadedDoc>` + `await resolver(src)` 拉 xml。递归 Import 解析串行 `await`，循环检测 / `AllSrcs` 去重 / `allowScreens` 检查全部保留。

- [ ] **Step 1：替换 `DocumentLoader.cs` 全文**

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// 把一个 src 解析成"已合并 Templates 与 Screens 的 IR 文档"。
    /// 递归解析其 Import 链；同 src 在一次 Load 内只解析一次（cache）；A→B→A 循环报错。
    /// 不接触 commons pool；不入 depGraph。这两件事由 UI 上层负责。
    /// </summary>
    internal static class DocumentLoader
    {
        internal sealed class LoadedDoc
        {
            public string EntrySrc;
            public HashSet<string> AllSrcs = new();
            public List<ScreenDef> Screens = new();
            public Dictionary<TemplateKey, TemplateDef> Templates = new();
        }

        internal readonly struct TemplateKey : IEquatable<TemplateKey>
        {
            public readonly string Namespace;   // null = 裸名
            public readonly string Name;
            public TemplateKey(string ns, string name) { Namespace = ns; Name = name; }
            public bool Equals(TemplateKey o) => Namespace == o.Namespace && Name == o.Name;
            public override bool Equals(object o) => o is TemplateKey k && Equals(k);
            public override int GetHashCode() =>
                (Namespace?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0);
            public override string ToString() =>
                Namespace == null ? Name : $"{Namespace}.{Name}";
        }

        internal static async Awaitable<LoadedDoc> LoadAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            bool allowScreens)
        {
            if (resolver == null)
                throw new InvalidOperationException(
                    "UI.SourceResolver is not set; required for src-based loading");

            var loaded = new LoadedDoc { EntrySrc = src };
            var visiting = new Stack<string>();
            await LoadInternalAsync(src, resolver, allowScreens, loaded, visiting,
                                    applyNamespace: null);
            return loaded;
        }

        /// <summary>
        /// 加载 src 并把 commons 池合并进 LoadedDoc.Templates。供 LoadDocumentAsync 与 ReloadAsync 复用。
        /// commons 与 entry 同名 → 抛 TemplateException。
        /// </summary>
        internal static async Awaitable<LoadedDoc> LoadAndMergeAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            IReadOnlyDictionary<TemplateKey, TemplateDef> commonsPool)
        {
            var loaded = await LoadAsync(src, resolver, allowScreens: true);

            foreach (var kv in commonsPool)
            {
                if (loaded.Templates.ContainsKey(kv.Key))
                    throw new TemplateException(
                        $"template '{kv.Key}' conflicts with commons pool");
                loaded.Templates[kv.Key] = kv.Value;
            }
            return loaded;
        }

        private static async Awaitable LoadInternalAsync(
            string src,
            Func<string, Awaitable<string>> resolver,
            bool allowScreens,
            LoadedDoc agg,
            Stack<string> visiting,
            string applyNamespace)
        {
            if (visiting.Contains(src))
            {
                var chain = string.Join(" → ", visiting);
                throw new ParseException(
                    $"cyclic Import detected: {chain} → {src}");
            }
            if (!agg.AllSrcs.Add(src)) return;

            var xml = await resolver(src);
            if (string.IsNullOrEmpty(xml))
                throw new System.IO.IOException(
                    $"SourceResolver returned null/empty for src='{src}'");

            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xml); }
            catch (ParseException) { throw; }
            catch (Exception e)
            {
                throw new ParseException($"parsing src='{src}' failed: {e.Message}", e);
            }

            if (!allowScreens && doc.Screens.Count > 0)
                throw new ParseException(
                    $"src='{src}' is loaded as common library / nested import; <Screen> not allowed");

            if (allowScreens)
            {
                foreach (var s in doc.Screens) agg.Screens.Add(s);
            }

            foreach (var kv in doc.Templates)
            {
                var key = new TemplateKey(applyNamespace, kv.Key);
                if (agg.Templates.ContainsKey(key))
                    throw new TemplateException(
                        $"duplicate template '{key}' (loaded from src='{src}')");
                agg.Templates[key] = kv.Value;
            }

            visiting.Push(src);
            try
            {
                foreach (var imp in doc.Imports)
                {
                    var childNs = imp.Namespace ?? applyNamespace;
                    await LoadInternalAsync(imp.Src, resolver, allowScreens: false,
                                            agg, visiting, childNs);
                }
            }
            finally { visiting.Pop(); }
        }
    }
}
```

变更点：
- 类型 `Func<string, string>` → `Func<string, Awaitable<string>>`（3 处）
- `Load` / `LoadAndMerge` / `LoadInternal` 重命名为 `*Async` 并加 `async Awaitable<...>` / `async Awaitable`
- `xml = resolver(src)` → `xml = await resolver(src)`
- 递归调用 `LoadInternal(...)` → `await LoadInternalAsync(...)`
- 加 `using UnityEngine;`（为 `Awaitable` 类型）

- [ ] **Step 2：编译会失败（UI.cs 还在用旧名 / 旧签名）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected：UI.cs 报错 `DocumentLoader.Load`/`LoadAndMerge` 不存在 + signature 不匹配。**不要 commit**，立即进 Task 3。

---

## Task 3：`UI.SourceResolver` 类型 + 公开 API 重命名

**Files:**
- Modify: `Runtime/Application/UI.cs`

一次性把所有方法签名换掉。Task 4 才动 `UseResourcesResolver` 内部实现；Task 5 才动 `HotReload`。这一 Task 只关心 `UI` 静态类的公开方法签名 + DocumentLoader 调用点。

- [ ] **Step 1：替换 `SourceResolver` 字段类型（UI.cs 第 15 行附近）**

```csharp
// 原：
public static System.Func<string, string> SourceResolver { get; set; }
// 改为：
public static System.Func<string, UnityEngine.Awaitable<string>> SourceResolver { get; set; }
```

- [ ] **Step 2：重命名 + async 化 `LoadDocumentFromSrc` → `LoadDocumentAsync`**

替换 UI.cs 第 174-200 行整段：

```csharp
public static async UnityEngine.Awaitable<IReadOnlyList<string>> LoadDocumentAsync(string src)
{
    if (SourceResolver == null)
        throw new System.InvalidOperationException(
            "UI.SourceResolver must be set before LoadDocumentAsync");

    var loaded = await DocumentLoader.LoadAndMergeAsync(src, SourceResolver, _commonsPool);
    var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

    var added = new List<string>();
    foreach (var s in expanded.Screens)
    {
        if (_docs.ContainsKey(s.Name))
            throw new System.InvalidOperationException(
                $"Screen '{s.Name}' already loaded");
        _docs[s.Name] = s;
        added.Add(s.Name);
        _depGraph.ScreenDeps[s.Name] = new DepGraph.ScreenDep
        {
            EntrySrc = src,
            AllDeps = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs),
        };
    }
    _depGraph.SrcToDeps[src] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);
    return added;
}
```

- [ ] **Step 3：重命名 + async 化 `Reload` → `ReloadAsync`**

替换 UI.cs 第 202-242 行整段：

```csharp
public static async UnityEngine.Awaitable ReloadAsync(string screenName)
{
    if (!_depGraph.ScreenDeps.TryGetValue(screenName, out var dep))
        throw new System.InvalidOperationException(
            $"Screen '{screenName}' was not loaded by src; cannot reload " +
            $"(use LoadDocumentAsync instead of LoadDocument(label, xml))");

    if (SourceResolver == null)
        throw new System.InvalidOperationException(
            "UI.SourceResolver must be set before ReloadAsync");

    var loaded = await DocumentLoader.LoadAndMergeAsync(dep.EntrySrc, SourceResolver, _commonsPool);
    var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

    PromptUGUI.IR.ScreenDef newDef = null;
    foreach (var s in expanded.Screens)
    {
        if (s.Name == screenName) { newDef = s; break; }
    }

    var wasOpen = _open.ContainsKey(screenName);
    if (wasOpen) Close(screenName);

    _docs.Remove(screenName);
    _depGraph.ScreenDeps.Remove(screenName);

    _docs[screenName] = newDef ?? throw new System.InvalidOperationException(
            $"Screen '{screenName}' no longer present in src='{dep.EntrySrc}' after reload");
    _depGraph.ScreenDeps[screenName] = new DepGraph.ScreenDep
    {
        EntrySrc = dep.EntrySrc,
        AllDeps = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs),
    };
    _depGraph.SrcToDeps[dep.EntrySrc] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);

    if (wasOpen) Open(screenName);
}
```

- [ ] **Step 4：重命名 + async 化 `LoadCommonLibrary` → `LoadCommonLibraryAsync`**

替换 UI.cs 第 244-272 行整段：

```csharp
public static async UnityEngine.Awaitable LoadCommonLibraryAsync(string src, string @as = null)
{
    if (SourceResolver == null)
        throw new System.InvalidOperationException(
            "UI.SourceResolver must be set before LoadCommonLibraryAsync");

    var loaded = await DocumentLoader.LoadAsync(src, SourceResolver, allowScreens: false);

    var staged = new System.Collections.Generic.List<(DocumentLoader.TemplateKey Key, IR.TemplateDef Def)>();
    foreach (var kv in loaded.Templates)
    {
        var rebasedKey = @as == null
            ? kv.Key
            : new DocumentLoader.TemplateKey(@as, kv.Key.Name);
        if (_commonsPool.ContainsKey(rebasedKey))
            throw new PromptUGUI.Template.TemplateException(
                $"common library conflict: '{rebasedKey}' already in commons pool");
        staged.Add((rebasedKey, kv.Value));
    }

    foreach (var (key, def) in staged)
    {
        def.OriginSrc = src;
        _commonsPool[key] = def;
    }
    _depGraph.CommonsSources.Add(src);
    _depGraph.SrcToDeps[src] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);
}
```

- [ ] **Step 5：重命名 + async 化 `ReloadCommonLibrary` → `ReloadCommonLibraryAsync`**

替换 UI.cs 第 274-315 行整段：

```csharp
public static async UnityEngine.Awaitable ReloadCommonLibraryAsync(string src)
{
    if (!_depGraph.CommonsSources.Contains(src))
        throw new System.InvalidOperationException(
            $"src='{src}' is not a registered common library");

    if (SourceResolver == null)
        throw new System.InvalidOperationException(
            "UI.SourceResolver must be set before ReloadCommonLibraryAsync");

    // M4 v1 限制：commons 原始 as= namespace 不在 reload 时保留。
    var stashed = new System.Collections.Generic.List<
        System.Collections.Generic.KeyValuePair<DocumentLoader.TemplateKey, IR.TemplateDef>>();
    foreach (var kv in _commonsPool)
        if (kv.Value.OriginSrc == src) stashed.Add(kv);
    foreach (var kv in stashed) _commonsPool.Remove(kv.Key);

    var prevDeps = _depGraph.SrcToDeps.TryGetValue(src, out var d)
        ? new System.Collections.Generic.HashSet<string>(d) : null;
    _depGraph.CommonsSources.Remove(src);
    _depGraph.SrcToDeps.Remove(src);

    try
    {
        await LoadCommonLibraryAsync(src);
    }
    catch
    {
        foreach (var kv in stashed) _commonsPool[kv.Key] = kv.Value;
        _depGraph.CommonsSources.Add(src);
        if (prevDeps != null) _depGraph.SrcToDeps[src] = prevDeps;
        throw;
    }

    var names = new System.Collections.Generic.List<string>(_depGraph.ScreenDeps.Keys);
    foreach (var name in names) await ReloadAsync(name);
}
```

- [ ] **Step 6：内部测试 seam 同步**

UI.cs 第 393 行附近的 internal accessor 不需要改名（不是公共 API）。

- [ ] **Step 7：不 refresh / commit，直接进 Task 4**

`ResourcesResolverHelper` + `HotReload.NotifyAssetChanged` + 测试程序集仍坏。

---

## Task 4：`UseResourcesResolver` 包装 Awaitable

**Files:**
- Modify: `Runtime/Application/ResourcesResolverHelper.cs`

把同步 `Resources.Load<TextAsset>().text` 包成已完成 `Awaitable<string>`。

- [ ] **Step 1：替换文件全文**

```csharp
using System;
using System.IO;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// 内置 helper：把 SourceResolver 设为 Resources.Load(rootPath/{src}).text；
        /// 同时（仅 Editor）把 HotReload.AssetPathToSrc 设为反向映射，
        /// 让 AssetPostprocessor 能从 AssetDatabase 路径反推 src。
        /// </summary>
        public static void UseResourcesResolver(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("rootPath must be non-empty");
            var root = rootPath.TrimEnd('/');

            SourceResolver = src =>
            {
                if (string.IsNullOrEmpty(src))
                    return AwaitableHelpers.Faulted<string>(
                        new IOException("Resources lookup with empty src"));
                var ta = Resources.Load<TextAsset>($"{root}/{src}");
                if (ta == null)
                    return AwaitableHelpers.Faulted<string>(
                        new IOException($"Resources lookup failed: {root}/{src}"));
                return AwaitableHelpers.Completed(ta.text);
            };

#if UNITY_EDITOR
            HotReload.AssetPathToSrc = assetPath =>
            {
                if (string.IsNullOrEmpty(assetPath)) return null;
                var marker = $"/Resources/{root}/";
                var idx = assetPath.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) return null;
                var rel = assetPath.Substring(idx + marker.Length);
                return rel.EndsWith(".ui.xml")
                    ? rel.Substring(0, rel.Length - ".ui.xml".Length)
                    : null;
            };
#endif
        }
    }
}
```

变更：lambda body 不再 `throw`（throw 会让整个 resolver 调用立即抛而非走 awaiter）；改为返回 `AwaitableHelpers.Faulted<string>(...)`，由 `await` 端透传异常。这样和 Addressables helper 的失败语义对齐。

- [ ] **Step 2：不 refresh / commit，进 Task 5**

UI.cs `HotReload` 区块还在调旧名 `Reload` / `ReloadCommonLibrary`。

---

## Task 5：`HotReload.NotifyAssetChanged` 内部 fire-and-forget

**Files:**
- Modify: `Runtime/Application/UI.cs`（`HotReload` 嵌套类区块，`#if UNITY_EDITOR` 段）

把 `HotReload.NotifyAssetChanged` 的同步调用换成 fire-and-forget async + log。Public 签名不变（仍是 `void`）。

- [ ] **Step 1：替换 UI.cs `HotReload` 嵌套类**

定位 `#if UNITY_EDITOR` 到 `#endif` 之间整段（约 469-510 行），替换为：

```csharp
#if UNITY_EDITOR
        public static class HotReload
        {
            public static System.Func<string, string> AssetPathToSrc { get; set; }
            public static bool Enabled { get; set; } = true;

            public static void NotifyAssetChanged(string assetPath)
            {
                if (!Enabled || AssetPathToSrc == null) return;
                var src = AssetPathToSrc(assetPath);
                if (string.IsNullOrEmpty(src)) return;

                if (_depGraph.IsCommons(src))
                {
                    _ = ReloadCommonLibraryAsyncLogged(src);
                    return;
                }

                var affected = new System.Collections.Generic.List<string>();
                foreach (var name in _depGraph.ScreensDependingOn(src))
                    affected.Add(name);
                foreach (var name in affected) _ = ReloadAsyncLogged(name);
            }

            private static async UnityEngine.Awaitable ReloadAsyncLogged(string screenName)
            {
                try { await ReloadAsync(screenName); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] hot reload failed for screen '{screenName}': {e}");
                }
            }

            private static async UnityEngine.Awaitable ReloadCommonLibraryAsyncLogged(string src)
            {
                try { await ReloadCommonLibraryAsync(src); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] hot reload commons failed for src '{src}': {e}");
                }
            }

            public static System.Action IconResolverRebuilder { get; set; }

            public static void NotifyIconAssetsChanged()
            {
                if (!Enabled) return;
                IconResolverRebuilder?.Invoke();
                foreach (var s in _open.Values) s.ReSolve();
            }
        }
#endif
```

`NotifyAssetChanged` 是 fire-and-forget；EditMode 测试里 resolver 永远同步完成，整条 async 链没有真正的 yield 点，调用点同步跑到完。`HotReloadTests.NotifyAssetChanged_for_screen_src_reloads` 等测试依旧通过。

- [ ] **Step 2：refresh + 检查编译**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected：Runtime / Editor 错误清空；Sample + 测试程序集仍报错（Task 6 / Task 7 处理）。

---

## Task 6：Sample 迁移到 async

**Files:**
- Modify: `Samples~/MainMenu/MainMenuRunner.cs`
- Modify: `Samples~/CommonControls/CommonControlsRunner.cs`

Sample 不在 Tests asmdef 但跟 Runtime 一起编译。

- [ ] **Step 1：MainMenuRunner.cs —— `Start()` 改 `async void` 并 await**

替换 19-37 行 `Start` 方法：

```csharp
async void Start() {
    UI.UseResourcesResolver("UI");

    if (iconSets != null && iconSets.Length > 0)
        IconResolverHelpers.UseSpriteAtlasIconResolver(iconSets);

    await UI.LoadDocumentAsync("MainMenu.ui");
    var screen = UI.Open("MainMenu");

    screen.Get<Btn>("playBtn").OnClick
          .Subscribe(_ => Debug.Log("[Sample] play clicked")).AddTo(screen);
    screen.Get<Btn>("settingsBtn").OnClick
          .Subscribe(_ => Debug.Log("[Sample] settings clicked")).AddTo(screen);
    screen.Get<Btn>("quitBtn").OnClick
          .Subscribe(_ => Debug.Log("[Sample] quit clicked")).AddTo(screen);
}
```

- [ ] **Step 2：CommonControlsRunner.cs —— 同样 `async void Start()`**

替换前 5 行（17-21）：

```csharp
async void Start()
{
    UI.UseResourcesResolver("UI");
    await UI.LoadDocumentAsync("Settings.ui");
    var screen = UI.Open("Settings");
```

其余 25-50 不动。

- [ ] **Step 3：refresh + 看 console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected：Sample 编译通过；测试程序集仍有错（Task 7 处理）。

---

## Task 7：测试程序集机械迁移到新 API

**Files:**
- Modify: `Tests/EditMode/Application/CommonLibraryTests.cs`
- Modify: `Tests/EditMode/Application/HotReloadTests.cs`
- Modify: `Tests/EditMode/Application/ImportSemanticsTests.cs`
- Modify: `Tests/EditMode/Application/CanvasConfiguratorTests.cs`

机械替换规则（**对所有 4 个文件应用**）：

| 原 | 新 |
|---|---|
| `UI.SourceResolver = src => files.TryGetValue(src, out var v) ? v : null;` | `UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);` |
| `UI.SourceResolver = _ => xml;` | `UI.SourceResolver = _ => AwaitableHelpers.Completed(xml);` |
| `UI.SourceResolver = src => src == "main" ? @"..." : null;` | `UI.SourceResolver = src => AwaitableHelpers.Completed(src == "main" ? @"..." : null);` |
| `UI.LoadDocumentFromSrc("x")` | `UI.LoadDocumentAsync("x").GetAwaiter().GetResult()` |
| `UI.LoadCommonLibrary("x")` | `UI.LoadCommonLibraryAsync("x").GetAwaiter().GetResult()` |
| `UI.LoadCommonLibrary("x", @as: "ns")` | `UI.LoadCommonLibraryAsync("x", @as: "ns").GetAwaiter().GetResult()` |
| `UI.Reload("S")` | `UI.ReloadAsync("S").GetAwaiter().GetResult()` |
| `UI.ReloadCommonLibrary("x")` | `UI.ReloadCommonLibraryAsync("x").GetAwaiter().GetResult()` |
| `Assert.Throws<X>(() => UI.LoadDocumentFromSrc(...))` | `Assert.Throws<X>(() => UI.LoadDocumentAsync(...).GetAwaiter().GetResult())` |
| `Assert.Catch<Exception>(() => UI.Reload("S"))` | `Assert.Catch<Exception>(() => UI.ReloadAsync("S").GetAwaiter().GetResult())` |
| `Assert.DoesNotThrow(() => UI.LoadDocumentFromSrc(...))` | `Assert.DoesNotThrow(() => UI.LoadDocumentAsync(...).GetAwaiter().GetResult())` |

**Why `.GetAwaiter().GetResult()` 在 EditMode 测试里安全：** resolver 永远同步完成（`AwaitableHelpers.Completed` 立即设结果），整条 async 链上没有真正的 yield 点，全在调用线程同步跑完。`.GetResult()` 立即返回，不死锁。

- [ ] **Step 1：CommonLibraryTests.cs 全文按表替换**

`AwaitableHelpers` 是 internal，靠 `InternalsVisibleTo("PromptUGUI.Tests.EditMode")` 可见，不需要新 `using`。

21 处替换点（21 = LoadDocumentFromSrc 调用 7 + LoadCommonLibrary 调用 6 + SourceResolver 赋值 8）。

- [ ] **Step 2：HotReloadTests.cs 全文按表替换**

10 处 `UI.LoadDocumentFromSrc`、5 处 `UI.LoadCommonLibrary`、3 处 `UI.Reload`、3 处 `UI.ReloadCommonLibrary`、1 处 SourceResolver 赋值。

- [ ] **Step 3：ImportSemanticsTests.cs 替换**

2 处 SourceResolver、2 处 LoadDocumentFromSrc。

- [ ] **Step 4：CanvasConfiguratorTests.cs 替换**

1 处 SourceResolver、1 处 LoadDocumentFromSrc。

- [ ] **Step 5：refresh + 编译**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected：无 error。

- [ ] **Step 6：跑 EditMode 测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected：所有现有测试 PASS。如失败，最可能是某处 SourceResolver lambda 漏改或 `.GetAwaiter().GetResult()` 漏加。

- [ ] **Step 7：跑 EditorOnly + PlayMode 回归**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected：均 PASS。

- [ ] **Step 8：commit Section 1 全体（Task 2-7 合并提交）**

Task 1 (`AwaitableHelpers`) 在 Step 3 已独立提交。Task 2-7 是同一原子重命名：

```bash
git add Runtime/Application/UI.cs Runtime/Application/DocumentLoader.cs \
        Runtime/Application/ResourcesResolverHelper.cs \
        Samples~/MainMenu/MainMenuRunner.cs Samples~/CommonControls/CommonControlsRunner.cs \
        Tests/EditMode/Application/CommonLibraryTests.cs \
        Tests/EditMode/Application/HotReloadTests.cs \
        Tests/EditMode/Application/ImportSemanticsTests.cs \
        Tests/EditMode/Application/CanvasConfiguratorTests.cs
git commit -m "refactor: SourceResolver/Load*/Reload* 全链路异步化 + 重命名 *Async"
```

---

# Section 2 —— Addressables Resolver

## Task 8：Runtime asmdef 加 Addressables refs + versionDefines

**Files:**
- Modify: `Runtime/PromptUGUI.Runtime.asmdef`

加 `Unity.Addressables` + `Unity.Addressables.Editor` references，以及 `versionDefines` 把 `com.unity.addressables ≥ 1.0` 翻译成 `PROMPTUGUI_HAS_ADDRESSABLES` 编译符号。

- [ ] **Step 1：替换 asmdef 全文**

```json
{
  "name": "PromptUGUI.Runtime",
  "rootNamespace": "PromptUGUI",
  "references": [
    "Unity.TextMeshPro",
    "UnityEngine.UI",
    "Unity.Addressables",
    "Unity.Addressables.Editor"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [
    {
      "name": "com.unity.addressables",
      "expression": "1.0.0",
      "define": "PROMPTUGUI_HAS_ADDRESSABLES"
    }
  ],
  "noEngineReferences": false
}
```

注意 `Unity.Addressables.Editor` 仅在 Editor 编译时存在；Unity 在 Player Build 中会自动跳过 Editor-only 程序集引用。

- [ ] **Step 2：refresh + 检查 console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error", "warning"])
```

Expected（装了 Addressables）：无 error，可能有 zero warnings。
Expected（没装 Addressables）：可能有 "Could not find assembly with name 'Unity.Addressables'" 警告。如果是 **error** 阻断编译，那就改用方案 B：

```jsonc
// 方案 B fallback: 把 Addressables refs 移到独立 asmdef
// 仅在确认 Unity 在缺包时把 unresolved ref 当 error 时才走这条路
// 见 plan §"已知风险"
```

——但绝大多数 Unity 6 版本是 warning。如确认是 warning，继续 Step 3。

- [ ] **Step 3：commit**

```bash
git add Runtime/PromptUGUI.Runtime.asmdef
git commit -m "feat(asmdef): Unity.Addressables 可选依赖 + PROMPTUGUI_HAS_ADDRESSABLES versionDefine"
```

---

## Task 9：`AddressableResolverHelper.cs` —— runtime 加载路径 + Editor 反向映射

**Files:**
- Create: `Runtime/Application/AddressableResolverHelper.cs`

**TDD 注：** Section 2 是 implementation-first，不严格走红→绿。原因：Addressables 测试 fixture 涉及 `AddressableAssetSettings.CreateGroup` 等 Editor AssetDatabase 操作，必须在 Task 10 测试 asmdef 与 `PROMPTUGUI_HAS_ADDRESSABLES` 都就绪后才能 compile。先在本 Task 把实现写完，Task 11 / Task 12 写测试 → 预期一次通过；行为新增的保护网在 Task 11+12 的显式测试，Section 1 重构的保护网在现有测试套件。

整文件 `#if PROMPTUGUI_HAS_ADDRESSABLES` 包起来，加 `public static partial class UI` 扩展。

- [ ] **Step 1：建文件**

```csharp
// Runtime/Application/AddressableResolverHelper.cs
#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// 把 SourceResolver 设为 Addressables.LoadAssetAsync&lt;TextAsset&gt;(src)。
        /// src 直接当 Addressables key 用，不做 prefix 拼接。
        /// （未装 com.unity.addressables 时本方法不存在 —— PROMPTUGUI_HAS_ADDRESSABLES 未定义）
        ///
        /// Editor：同时设置 HotReload.AssetPathToSrc 走 guid → key 反查，让 AssetPostprocessor
        /// 在保存 .ui.xml 时能匹配到 Addressables 入口并触发热重载。
        /// </summary>
        public static void UseAddressableResolver()
        {
            SourceResolver = LoadFromAddressablesAsync;

#if UNITY_EDITOR
            BuildAddressablesReverseMapping();
            HotReload.AssetPathToSrc = AddressablesAssetPathToSrc;
#endif
        }

        private static Awaitable<string> LoadFromAddressablesAsync(string src)
        {
            if (string.IsNullOrEmpty(src))
                return AwaitableHelpers.Faulted<string>(
                    new IOException("Addressables lookup with empty src"));
            return LoadFromAddressablesInternalAsync(src);
        }

        private static async Awaitable<string> LoadFromAddressablesInternalAsync(string src)
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(src);
            try
            {
                // AsyncOperationHandle<T>.Task 在 Addressables 1.x 全系列稳定；
                // 1.21+ 也支持直接 `await handle`，但 .Task 兼容更广。
                var ta = await handle.Task;
                if (ta == null)
                    throw new IOException($"Addressables key not found or wrong type: {src}");
                return ta.text;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

#if UNITY_EDITOR
        private static System.Collections.Generic.Dictionary<string, string> _guidToKey;

        private static void BuildAddressablesReverseMapping()
        {
            _guidToKey = new System.Collections.Generic.Dictionary<string, string>();
            var settings = UnityEditor.AddressableAssets
                                       .AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.guid)) continue;
                    _guidToKey[entry.guid] = entry.address;
                }
            }
            settings.OnModification -= OnAddressableSettingsModified;
            settings.OnModification += OnAddressableSettingsModified;
        }

        private static void OnAddressableSettingsModified(
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent evt,
            object obj)
        {
            switch (evt)
            {
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryAdded:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryCreated:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryMoved:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryRemoved:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.EntryModified:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.GroupAdded:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.GroupRemoved:
                case UnityEditor.AddressableAssets.Settings.AddressableAssetSettings
                                .ModificationEvent.BatchModification:
                    BuildAddressablesReverseMapping();
                    break;
            }
        }

        private static string AddressablesAssetPathToSrc(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (!assetPath.EndsWith(".ui.xml")) return null;
            var guid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;
            return _guidToKey != null && _guidToKey.TryGetValue(guid, out var key)
                ? key
                : null;
        }
#endif
    }
}
#endif
```

技术要点：
- `LoadFromAddressablesAsync(src)` 是一个普通方法（非 async），用早期 `IsNullOrEmpty` 校验返回 faulted Awaitable，命中正常路径才走 `LoadFromAddressablesInternalAsync`。这样避免 `async` 方法签名带来的 Awaitable lambda 推断风险（spec §10 已知风险）。
- `await handle.Task`：把 `AsyncOperationHandle<T>` 桥接到 `Task<T>`，在 `async Awaitable<T>` 方法体里 await。C# async state machine 兼容混合 awaitable。

- [ ] **Step 2：refresh + 编译**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected（装了 Addressables）：无 error。
Expected（没装 Addressables）：`PROMPTUGUI_HAS_ADDRESSABLES` 未 set，整文件被 `#if` 包掉，无 error。

- [ ] **Step 3：commit**

```bash
git add Runtime/Application/AddressableResolverHelper.cs Runtime/Application/AddressableResolverHelper.cs.meta
git commit -m "feat: UI.UseAddressableResolver + Editor guid→key 反向映射"
```

---

## Task 10：建 Addressables 测试 asmdef + testable 注册

**Files:**
- Create: `Tests/EditMode/Addressables/PromptUGUI.Tests.EditMode.Addressables.asmdef`
- Modify: `package.json`

Addressables 测试单独成一个 asmdef，用 `defineConstraints` 在 Addressables 缺失时整段跳过，保证主测试集合不被警告污染。

- [ ] **Step 1：建目录**

```bash
mkdir -p Tests/EditMode/Addressables
```

- [ ] **Step 2：建 asmdef**

```json
{
    "name": "PromptUGUI.Tests.EditMode.Addressables",
    "rootNamespace": "PromptUGUI.Tests.Addressables",
    "references": [
        "PromptUGUI.Runtime",
        "PromptUGUI.Editor",
        "Unity.Addressables",
        "Unity.Addressables.Editor",
        "Unity.ResourceManager"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS",
        "PROMPTUGUI_HAS_ADDRESSABLES"
    ],
    "versionDefines": [
        {
            "name": "com.unity.addressables",
            "expression": "1.0.0",
            "define": "PROMPTUGUI_HAS_ADDRESSABLES"
        }
    ],
    "noEngineReferences": false
}
```

- [ ] **Step 3：更新 `package.json` testables**

```diff
     "testables": [
         "PromptUGUI.Tests.EditMode",
+        "PromptUGUI.Tests.EditMode.Addressables",
         "PromptUGUI.Tests.PlayMode"
     ],
```

- [ ] **Step 4：refresh，检查 console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Expected：无 error（asmdef 空 + testable 注册不报错）。

---

## Task 11：Addressables Resolver 加载测试

**Files:**
- Create: `Tests/EditMode/Addressables/AddressableResolverTests.cs`

**前置**：宿主 Unity 工程必须装 `com.unity.addressables`。未装则 asmdef 整段跳过，本测试不会被执行。

- [ ] **Step 1：写测试**

```csharp
// Tests/EditMode/Addressables/AddressableResolverTests.cs
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace PromptUGUI.Tests.Addressables
{
    public class AddressableResolverTests
    {
        private const string FixturesRoot = "Assets/PromptUGUI_TestFixtures";
        private const string FixtureKey   = "promptugui-test/main";
        private AddressableAssetGroup _testGroup;
        private string _xmlPath;

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();

            Directory.CreateDirectory(FixturesRoot);
            _xmlPath = $"{FixturesRoot}/main.ui.xml";
            File.WriteAllText(_xmlPath,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame id='a'/></Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(_xmlPath);

            var settings = AddressableAssetSettingsDefaultObject.Settings
                          ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            _testGroup = settings.CreateGroup(
                "PromptUGUI_Test", false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));

            var guid = AssetDatabase.AssetPathToGUID(_xmlPath);
            var entry = settings.CreateOrMoveEntry(guid, _testGroup);
            entry.address = FixtureKey;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && _testGroup != null)
                settings.RemoveGroup(_testGroup);

            if (File.Exists(_xmlPath)) AssetDatabase.DeleteAsset(_xmlPath);
            if (Directory.Exists(FixturesRoot)) AssetDatabase.DeleteAsset(FixturesRoot);
        }

        [Test]
        public void UseAddressableResolver_loads_by_key()
        {
            UI.UseAddressableResolver();
            var names = UI.LoadDocumentAsync(FixtureKey).GetAwaiter().GetResult();
            CollectionAssert.Contains(names, "S");
            var s = UI.Open("S");
            Assert.IsNotNull(s.Get<Frame>("a"));
        }

        [Test]
        public void UseAddressableResolver_unknown_key_throws()
        {
            UI.UseAddressableResolver();
            Assert.Catch<System.Exception>(() =>
                UI.LoadDocumentAsync("nonexistent-key").GetAwaiter().GetResult());
        }
    }
}
```

`Frame` 是 Runtime 类，asmdef 通过 `PromptUGUI.Runtime` ref 已经可见。

- [ ] **Step 2：refresh + 跑测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

Expected：两个测试都 PASS。

**故障排查**：
- `LoadAssetAsync<TextAsset>` 卡住或返回 null：Addressables EditMode 默认走 "Use Asset Database (faster)" play mode script，应该立即返回。若失败，检查 `settings.ActivePlayModeDataBuilderIndex` 是否指向 BuildScriptFastMode（Inspector 里看 AddressableAssetSettings asset）。
- `OperationCanceledException`：EditMode 没有 frame loop；`await handle.Task` 在 EditMode 是同步完成（Addressables 内部用 `Task.Run` / completed Task）。如果走到这里，确认宿主项目的 Addressables 版本 ≥ 1.18（更早版本 EditMode 行为不稳）。

---

## Task 12：Addressables HotReload `AssetPathToSrc` 测试

**Files:**
- Create: `Tests/EditMode/Addressables/AddressableHotReloadTests.cs`

验证 `HotReload.AssetPathToSrc(assetPath)` 能从 assetPath 反查到 Addressables key。

- [ ] **Step 1：写测试**

```csharp
// Tests/EditMode/Addressables/AddressableHotReloadTests.cs
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace PromptUGUI.Tests.Addressables
{
    public class AddressableHotReloadTests
    {
        private const string FixturesRoot = "Assets/PromptUGUI_TestFixtures";
        private const string FixtureKey   = "promptugui-test/hr";
        private AddressableAssetGroup _testGroup;
        private string _xmlPath;

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            Directory.CreateDirectory(FixturesRoot);
            _xmlPath = $"{FixturesRoot}/hr.ui.xml";
            File.WriteAllText(_xmlPath,
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame/></Screen>
                  </PromptUGUI>");
            AssetDatabase.ImportAsset(_xmlPath);

            var settings = AddressableAssetSettingsDefaultObject.Settings
                          ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            _testGroup = settings.CreateGroup(
                "PromptUGUI_HR_Test", false, false, false, null,
                typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));

            var guid = AssetDatabase.AssetPathToGUID(_xmlPath);
            var entry = settings.CreateOrMoveEntry(guid, _testGroup);
            entry.address = FixtureKey;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && _testGroup != null)
                settings.RemoveGroup(_testGroup);
            if (File.Exists(_xmlPath)) AssetDatabase.DeleteAsset(_xmlPath);
            if (Directory.Exists(FixturesRoot)) AssetDatabase.DeleteAsset(FixturesRoot);
        }

        [Test]
        public void AssetPathToSrc_resolves_addressables_key()
        {
            UI.UseAddressableResolver();
            Assert.AreEqual(FixtureKey, UI.HotReload.AssetPathToSrc(_xmlPath));
        }

        [Test]
        public void AssetPathToSrc_returns_null_for_unknown_path()
        {
            UI.UseAddressableResolver();
            Assert.IsNull(UI.HotReload.AssetPathToSrc("Assets/NotAnAddressable/x.ui.xml"));
        }

        [Test]
        public void AssetPathToSrc_returns_null_for_non_uixml_extension()
        {
            UI.UseAddressableResolver();
            Assert.IsNull(UI.HotReload.AssetPathToSrc(_xmlPath.Replace(".ui.xml", ".txt")));
        }
    }
}
```

- [ ] **Step 2：refresh + 跑测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

Expected：5 个测试（Task 11 的 2 + 本 Task 的 3）均 PASS。

- [ ] **Step 3：commit Section 2 全体（Task 10-12）**

```bash
git add Tests/EditMode/Addressables/ package.json
git commit -m "test: Addressables resolver + AssetPathToSrc 反查"
```

---

# Section 3 —— 文档同步

## Task 13：SKILL.md 更新

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

SKILL 是 LLM-facing 的 XML 作者指南。XML 语义这次完全没变；只更新 C# 加载入口 snippet + 新增 Addressables 段落。

- [ ] **Step 1：定位需要改的位置**

```bash
grep -n "LoadDocumentFromSrc\|LoadCommonLibrary\|UI.Reload\|UI.SourceResolver" \
  .claude/skills/authoring-promptugui-xml/SKILL.md
```

按 grep 结果对位替换：

| 原 | 新 |
|---|---|
| `UI.LoadDocumentFromSrc(...)` | `await UI.LoadDocumentAsync(...)` |
| `UI.LoadCommonLibrary(...)` | `await UI.LoadCommonLibraryAsync(...)` |
| `UI.Reload(...)` | `await UI.ReloadAsync(...)` |
| `UI.ReloadCommonLibrary(...)` | `await UI.ReloadCommonLibraryAsync(...)` |

- [ ] **Step 2：在 SKILL.md 加 Addressables 资源加载段落**

定位 "## Resources resolver" 或类似 boot snippet 章节后插入：

````markdown
### Addressables 资源加载（可选）

如果项目装了 `com.unity.addressables`，把 `.ui.xml` 放到 Addressables Group 里、按 key 加载：

```csharp
async void Start() {
    UI.UseAddressableResolver();
    await UI.LoadDocumentAsync("screens/MainMenu");   // src 即 Addressables key
    UI.Open("MainMenu");
}
```

Editor 下保存 `.ui.xml` 会自动 hot reload（同 Resources 路径）。Player Build 时通过 Addressables catalog 拉。
````

- [ ] **Step 3：commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "doc(SKILL): 更新 UI.*Async API 名字 + Addressables 资源加载示例"
```

---

## Task 14：CLAUDE.md 更新

**Files:**
- Modify: `CLAUDE.md`

`Pipeline (mental model)` 与 `Critical Conventions` 段需要小量更新。

- [ ] **Step 1：定位需要改的行**

```bash
grep -n "LoadDocumentFromSrc\|LoadDocument(label\|UseResourcesResolver\|SourceResolver\b\|Func<string, *string>" CLAUDE.md
```

更新点：
- "Two entry points to this pipeline" 段：`UI.LoadDocumentFromSrc(src)` → `UI.LoadDocumentAsync(src)`
- 描述里 "callers register a `Func<string,string> SourceResolver`" → "callers register a `Func<string, Awaitable<string>> SourceResolver`"

- [ ] **Step 2：Critical Conventions 段加一条**

紧贴现有规则之后新增：

```markdown
**Async-by-default load pipeline.** `SourceResolver` is `Func<string, Awaitable<string>>`. `LoadDocumentAsync` / `LoadCommonLibraryAsync` / `ReloadAsync` / `ReloadCommonLibraryAsync` are all `async Awaitable<...>`. EditMode tests synchronously unwrap with `.GetAwaiter().GetResult()` — `AwaitableHelpers.Completed(value)` produces a sync-completed `Awaitable<T>` so there's no real yield point and the call returns on the test thread. The sync `LoadDocument(label, xml)` overload remains for raw-XML callers (no resolver, no hot reload).
```

- [ ] **Step 3：commit**

```bash
git add CLAUDE.md
git commit -m "doc(CLAUDE): async resolver + Addressables note"
```

---

# Section 4 —— 收尾

## Task 15：全套测试回归 + lint

- [ ] **Step 1：全 refresh + 全测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected：全部 PASS。如果 EditMode.Addressables 在没装 Addressables 的环境下跑：runner 报"no test assembly found"或直接跳过——asmdef 被 defineConstraints 关掉，可接受。

- [ ] **Step 2：lint 检查**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style       PromptUGUI.Lint.slnx
dotnet format analyzers   PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected：`--verify-no-changes` 通过。如有 whitespace fixup，加进最后一次 commit。

- [ ] **Step 3：最终 commit（如有 dirty）**

```bash
git status
# 若 dotnet format 有改动：
git add -u
git commit -m "style: dotnet format whitespace"
```

---

## 验收清单

落地完成后应满足：

- [ ] `UI.SourceResolver` 类型是 `Func<string, Awaitable<string>>`
- [ ] `UI.LoadDocumentAsync` / `UI.LoadCommonLibraryAsync` / `UI.ReloadAsync` / `UI.ReloadCommonLibraryAsync` 都存在并返回 `Awaitable<...>`
- [ ] `UI.UseAddressableResolver()` 仅在装了 com.unity.addressables 时存在；调用一次后 `SourceResolver` + `HotReload.AssetPathToSrc` 都被设置
- [ ] Editor 改 `.ui.xml` 触发 AssetPostprocessor → Addressables 路径能 hot reload screen
- [ ] `LoadDocument(string label, string xml)` 仍是同步签名
- [ ] 全部测试 PASS：`PromptUGUI.Tests.EditMode` / `EditorOnly` / `PlayMode`，外加 `EditMode.Addressables`（若装了 Addressables）
- [ ] SKILL.md / CLAUDE.md 与代码同步
- [ ] `dotnet format --verify-no-changes` 通过
