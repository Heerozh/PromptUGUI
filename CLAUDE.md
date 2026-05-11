# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

PromptUGUI is a Unity 6+ UPM package that translates compact `.ui.xml` files into runtime uGUI hierarchies. Target use case: pixel-art game that ships PC widescreen and mobile portrait from one description.

The library is **content-agnostic at runtime**: it never reads the filesystem itself. Callers register a `Func<string, Awaitable<string>> SourceResolver` that maps an opaque `src` key to XML content; how the user obtains that content (Resources, Addressables, custom paths) is their concern. Built-in helpers: `UI.UseResourcesResolver(rootPath)` and (when `com.unity.addressables` ≥ 1.0 is installed) `UI.UseAddressableResolver()`.

## Canonical Design Sources

`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` is the master spec for the description language and C# API. Per-milestone specs and plans live alongside it. Always read the master spec before changing public API or XML semantics — section numbers (e.g. "spec §7.6") are referenced throughout the codebase and PR descriptions.

`.claude/skills/authoring-promptugui-xml/SKILL.md` is the LLM-facing authoring guide. **Any functional change or addition must be reflected here in the same PR.** Triggers requiring a SKILL update:

- New / removed / renamed XML elements (e.g. adding a `<Toggle>` builtin, retiring `<Btn>`)
- New / removed / renamed attributes on any built-in tag, including type changes
- Changes to anchor / size / margin / Variant / Template / Import / `if=` semantics
- Public C# API surface changes (anything callers touch: `UI.*`, `IScreen`, `IControl`, `ControlRegistry`, `Variants`, `[UIAttr]` / `[Bind]`)
- Changes to the `id` path / scoping rules
- New / changed parser-time errors that authors will hit

Internal refactors, test-only changes, performance work, and Editor tooling that doesn't affect XML or the public API do **not** require a SKILL update.

## Project Layout

| Asmdef | Where | Compiled into Player? |
|---|---|---|
| `PromptUGUI.Runtime` | `Runtime/` | yes |
| `PromptUGUI.Editor` | `Editor/` | no (Editor-only) |
| `PromptUGUI.Tests.EditMode` | `Tests/EditMode/` | no |
| `PromptUGUI.Tests.EditorOnly` | `Tests/EditMode/Editor/` | no (tests for `PromptUGUI.Editor`) |
| `PromptUGUI.Tests.PlayMode` | `Tests/PlayMode/` | no |
| `PromptUGUI.Tests.EditMode.Addressables` | `Tests/EditMode/Addressables/` | no (gated by `PROMPTUGUI_HAS_ADDRESSABLES`) |

`Runtime/AssemblyInfo.cs` exposes internals to `PromptUGUI.Tests.EditMode`, `PromptUGUI.Tests.PlayMode`, `PromptUGUI.Editor`, and `PromptUGUI.Tests.EditMode.Addressables` via `InternalsVisibleTo`.

`Runtime/` is split into:
- `Core/IR/` — pure POCOs (`UIDocument`, `ScreenDef`, `TemplateDef`, `ElementNode`, `ImportRef`, `VariantBlock`, `AddDirective`)
- `Core/Parser/` — `UIDocumentParser` (XML → IR) + `ParseException`
- `Core/Template/` — `TemplateExpander` (inlines Template invocations) + `Substitution` / `Truthy`
- `Core/Variants/` — `VariantResolver` (last-active-wins for `attr.var` overrides)
- `Core/Layout/` — `AnchorResolver` / `MarginResolver` / `SizeSpec`
- `Controls/` — built-in primitives (`Frame`, `Image`, `Text`, `VStack`, `HStack`, `Grid`, `Btn`) + the `Control` base class
- `Registry/` — `ControlRegistry` + `ControlMeta` (reflects `[UIAttr]` / `[Bind]`)
- `Application/` — `UI` static facade (loading/lifecycle), `Screen`, `ScreenInstantiator`, `DocumentLoader`, `DepGraph`, `VariantStore`, `BuiltinPrimitives`

Write Red test first, and then write implementation. Always use Unity MCP to run tests in the host Unity project.
If MCP is unavailable, try reconnect or tell user to restart MCP.

Always check lint after write code. `.lint/` 放了 stub csproj + `Directory.Build.props`，让 `dotnet format` 能在 Unity 外面跑（Roslyn 工作区独立于 Unity 的编译流程）。从仓库根：

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx                  # 安全
dotnet format style       PromptUGUI.Lint.slnx                 # 默认 warn 级，安全
dotnet format analyzers   PromptUGUI.Lint.slnx                 # 默认 warn 级，安全
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

**不要用 `dotnet format analyzers --severity info`**——Roslyn 的 info 级 fixer 会做下面这些"自动修复"，每一条在这个 Unity 项目里都会炸编译或破坏 Unity 反射契约：

| 规则 | 自动改成 | 为何在 Unity 里炸 |
|---|---|---|
| CA1822 | 方法标 `static` | 误判：方法调用了同类的实例方法 → CS0120；或方法是接口实现 → CS0736 |
| CA1846 | `value.Substring(...)` → `value.AsSpan(...)` | Unity Mono 没有 `float.Parse(ReadOnlySpan<char>, IFormatProvider)` 重载 → CS1503 |
| CA2016 | 给 `Async` 调用补 `CancellationToken` | Unity 的 `HttpContent.ReadAsStringAsync()` 没有 CT 重载 → CS1501 |
| IDE0032 | `[SerializeField] T _x;` + `T X => _x;` 折叠成 `[field: SerializeField] T X { get; }` | `SerializedObject.FindProperty("oldFieldName")` 找不到字段 → 运行时 NRE |
| IDE0044 | 给私有字段加 `readonly` | 构造函数外还有赋值时 → CS0191 |

仓库配置里已固化的护栏：

- `.lint/Directory.Build.props`: `<LangVersion>9.0</LangVersion>`——跟 Unity 6 一致，挡掉 primary constructor、collection expression `[]`、`[field: SerializeField]` 等 C# 10+ 特性建议
- `.editorconfig`: `dotnet_diagnostic.CA1846.severity = none`

`Local.props`（gitignored）放每个开发者本机的 Unity 安装路径 + host 工程 `Library/ScriptAssemblies`。没填会出 CS0246 噪音，但 style/IDE/CA 分析器照常工作。

剩下的 info 级诊断（命名 `s_`/`_` 前缀、`var` 偏好等）`NamingStyleCodeFixProvider` 不支持 FixAll，需要时在 IDE 里手动 Quick-Fix。

## Pipeline (mental model)

```
src key ──[SourceResolver]──> xml string
                                  │
                              UIDocumentParser.Parse
                                  │
                                  ▼
                         UIDocument (raw IR)
                                  │
       (commons pool + recursive Imports merge here)
                                  │
                       DocumentLoader.LoadAndMerge
                                  │
                                  ▼
                            LoadedDoc
                                  │
                       TemplateExpander.Expand
                                  │  (Template invocations inlined; (ns,name) lookup)
                                  ▼
                       UIDocument (expanded)
                                  │
                          ScreenInstantiator
                                  │
                                  ▼
                         live Screen (GameObjects, _byId, _nodeMap)
```

Two entry points to this pipeline:
- `await UI.LoadDocumentAsync(src)` — full pipeline; populates DepGraph for hot reload; returns `Awaitable<IReadOnlyList<string>>`
- `UI.LoadDocument(label, xmlString)` — sync; bypasses resolver/DepGraph; raw XML; **cannot be hot-reloaded**

## Critical Conventions

**Templates are inlined at expansion time.** After `TemplateExpander.Expand`, no `<TitledPanel>` invocations remain — they've been replaced with their bodies. Don't try to look up Templates at runtime; the only post-expansion artifact is the `IsTemplateInstanceRoot` flag + `ScopedIds` for id-path resolution.

**Common (auto-imported) Templates live in `_commonsPool` keyed by `(ns, name)`.** `LoadCommonLibraryAsync(src, [as])` populates it once at boot. Subsequent `LoadDocumentAsync` calls merge commons → entry templates with hard conflict errors.

**Async-by-default load pipeline.** `SourceResolver` is `Func<string, Awaitable<string>>`. `LoadDocumentAsync` / `LoadCommonLibraryAsync` / `ReloadAsync` / `ReloadCommonLibraryAsync` are all `async Awaitable<...>`. EditMode tests synchronously unwrap with `.GetAwaiter().GetResult()` — `AwaitableHelpers.Completed(value)` (internal) produces a sync-completed `Awaitable<T>` so there's no real yield point and the call returns on the test thread. The sync `LoadDocument(label, xml)` overload remains for raw-XML callers. `HotReload.NotifyAssetChanged` stays `void`; internally it fires `_ = ReloadAsyncLogged(...)` / `_ = ReloadCommonLibraryAsyncLogged(...)` with try/catch + `Debug.LogError` because AssetPostprocessor is a sync context.

**Variants don't rebuild GameObjects.** `VariantStore.Changed` triggers `Screen.ReSolve` which re-applies attribute values via `ControlAttributeApplier`. Add blocks use Strategy C: instantiate once on first activation and only toggle `SetActive`. Never `Destroy` an Add block while the Screen is open — references and R3 subscriptions must survive variant toggles.

**Editor-only code goes through `PromptUGUI.Editor` asmdef OR `#if UNITY_EDITOR`.** The `UI.HotReload` nested class is wrapped in `#if UNITY_EDITOR` so Player builds don't see it. `Editor/UIAssetPostprocessor.cs` is the AssetPostprocessor that calls `UI.HotReload.NotifyAssetChanged`.

**`Screen.Close()` branches on `Application.isPlaying`** to use `DestroyImmediate` in EditMode (so EditMode tests don't log "Destroy may not be called from edit mode"). Don't revert this back to a single `Object.Destroy` call.

**`anchor` has hard structural rules.** `anchor="stretch"` (or `stretch-X` / `X-stretch`) means the corresponding axis is pulled by margin, not size. Setting `size`/`width`/`height` on a stretched axis is a parse error, not a layout suggestion. See spec §6.2.

## Build & Test

The host Unity project is at `C:\xsoft\PromptUGUIDev`; this repo is referenced as a UPM package via `file://`. R3 (Cysharp) is provided by NuGetForUnity in the host project.

**Always test via UnityMCP, not batch-mode Unity.** Tools (deferred — load with `ToolSearch(query="select:run_tests,refresh_unity,read_console", max_results=3)`):

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Filter to a single test class with `filter="ClassName"` (matches by name fragment).

After any source edit, refresh first, then check console for compile errors before running tests.

**Forbidden MCP calls** (do not invoke unless the user explicitly allows it during an alignment step):

- `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")` — pops a modal confirmation dialog in Unity ("Are you sure you want to reimport all assets..."). The MCP call itself returns immediately, but **every subsequent MCP call will be blocked by the unclosed modal** until someone manually dismisses it in the Unity window. Recovering from an accidental trigger requires user intervention. For routine full refreshes, use `refresh_unity(mode="force", scope="all")` — it goes through `AssetDatabase.Refresh(ForceUpdate)`, no dialog, no editor restart.

## Test Conventions

EditMode test classes that touch `UI` must call `UI.ResetForTests()` in `[SetUp]` and `[TearDown]`. `ResetForTests` rebuilds the registry with built-ins pre-registered — tests don't need to register them manually. The fake-files pattern for resolver-driven tests is established in `DocumentLoaderTests.cs` and `HotReloadTests.cs`.

XSD generator tests use substring assertions (`StringAssert.Contains`) rather than byte-exact snapshots — small XSD changes won't trigger fixture churn.

## Workflow

`docs~/superpowers/specs/<date>-<topic>-design.md` is the spec format; `docs~/superpowers/plans/<date>-<topic>.md` is the implementation plan format. New milestones go through brainstorming → spec → plan → feature branch → PR → merge to main. Recent merges (PR #1 M3, PR #3 M4) used merge commits with `--delete-branch`.
