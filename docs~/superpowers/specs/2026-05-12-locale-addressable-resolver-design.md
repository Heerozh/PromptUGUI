# Locale (PO) Addressable Resolver 设计

**日期**：2026-05-12
**状态**：设计阶段（待 review，未进入实施）
**作用域**：在 `UI.Locale` 上新增 Addressables 路径的 .po 加载入口，让 user 把 .po 资产放进 Addressables 走按需下载 / 远端 CDN。把 `UI.PoResolver` 委托形状改成异步，给 `Locale.Set` / `Locale.ReloadCurrent` 增加 `*Async` 同名变体，sync 入口走 fire-and-forget。仅 Resources 默认路径行为完全不变。
**依赖**：[`2026-05-08-i18n-fonts-design.md`](2026-05-08-i18n-fonts-design.md)（PO / Locale / Tr 流水线）+ [`2026-05-11-addressable-resolver-async-load-design.md`](2026-05-11-addressable-resolver-async-load-design.md)（Addressables helper 风格与 `PROMPTUGUI_HAS_ADDRESSABLES` 条件编译）+ [`2026-05-12-addressable-icon-resolver-design.md`](2026-05-12-addressable-icon-resolver-design.md)（`UI.OnReset` event 钩子）。

---

## 1. 背景

`UI.UseAddressableResolver()` 接通了 `.ui.xml` → Addressables，`IconResolverHelpers.UseAddressableSpriteAtlasIconResolver()` 接通了 IconSet → Addressables。剩下的 i18n 通道目前只能从 `Resources/PromptUGUI/i18n/<locale>/`、`Resources/PromptUGUI/i18n-custom/<locale>/` 加载，不能走 Addressables。像素游戏出海后语言包按需下载是常见需求，本设计补齐这一通道。

跟图标 resolver 的关键差异：

1. **每个 locale 都要单独按需加载**。不像 IconSet 一次性预加载所有，PO 每切一种语言加载对应的子集 —— 所以 helper 不是"启动时预加载一次"模型，而是"`Set(locale)` 时按 label=`locale` 触发加载"模型。
2. **`Locale.Set` 调用点不能 `await`**。今天 `Set` 是 `void`，被业务代码、UI 按钮、System Language 检测、自动初始化等等多处同步调用。强行改成 `async Awaitable` 会牵连一大片调用方。所以 sync `Set` 必须保留，内部把异步加载 fire-and-forget；同时新增 `SetAsync` 满足"等加载完再继续"的场景。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| LAR-D1 | API 入口形状 | `UI.Locale.UseAddressableResolver()` —— 无 label 参数，硬规则 `label = locale` | 跟 user 期望一致；`Set("zh-Hans")` 就是去拉 label `"zh-Hans"` 的 TextAsset。把 label namespace（"i18n/zh-Hans"）这类灵活性推迟到将来真有需求时 |
| LAR-D2 | `PoResolver` 委托形状 | 改成 `Func<string, Awaitable<IEnumerable<PoEntry>>>` —— 异步签名 | 跟 `SourceResolver` 一样的"sync 路径返回已完成 Awaitable，async 路径返回真异步"统一模型；Resources 默认实现走 `AwaitableHelpers.Completed(...)`，对 caller 透明 |
| LAR-D3 | `Locale.Set` 语义 | 保持 `void`，内部 fire-and-forget；sync resolver 路径整条链同步走完（已完成 Awaitable 首个 await 不让步），异步路径返回后由 player loop 回调完成 | 不破坏现有调用模式；行为退化与 `UseAddressableResolver()`/`UseResourcesResolver()` 二者之间的差异自然形成 |
| LAR-D4 | 新增 `Locale.SetAsync` | `public static Awaitable SetAsync(string locale)` —— 真异步变体，加载完成后才 return | 给"切换后立刻 `UI.Tr` 取译文"的代码路径用；fire-and-forget 路径会在加载完成前看到 msgid 原文（短暂闪烁） |
| LAR-D5 | 新增 `Locale.ReloadCurrentAsync` | 同理 | `ReloadCurrent` 也是 fire-and-forget 包装，`ReloadCurrentAsync` 是真异步变体；Editor 端 `UIAssetPostprocessor` 继续调 sync 版（fire-and-forget），用户代码可以 await async 版 |
| LAR-D6 | `Set` 触发顺序 | 1) flip 旧 locale variant off + UnloadLocale → ReSolve（短暂闪烁回 msgid）；2) `Current = locale`；3) fire-and-forget 加载新 .po → `TranslationStore.Load` → flip 新 locale variant on → ReSolve（最终译文落屏）；4) `Changed?.Invoke()` 在加载完成后才触发 | 整个流程跟今天的 sync 行为对齐，只是步骤 3-4 可能在异步路径上后置；ReSolve 复用 VariantStore.Changed pipeline（`ControlAttributeApplier.cs:41,51` 在 ReSolve 里重新走 `TrResolver.Resolve(...)`），UI 文字自动更新 |
| LAR-D7 | 资产发现方式 | `Addressables.LoadAssetsAsync<TextAsset>(locale, callback: null)` —— 按 label 拉全部 .po TextAsset | 跟 icon resolver 对称；user 在 AA Group 里给每个 .po 资产打 label = locale 字符串 |
| LAR-D8 | 句柄生命周期 | 单次加载、加载完释放（不常驻） | 跟 doc resolver 一致（`AddressableResolverHelper.cs:54-57`）：parse 完拿到 `IEnumerable<PoEntry>` 后就 release，不需要保留 TextAsset 引用。区别于 icon resolver 的常驻句柄（icon 的 Sprite 还引用 atlas，PO 解析后是值类型不挂依赖） |
| LAR-D9 | 加载失败语义 | `Addressables.LoadAssetsAsync` 异常 → fire-and-forget 路径里 try/catch + `Debug.LogError`（参考 `HotReload.ReloadAsyncLogged` 模式）；`SetAsync` 路径让异常 throw 到 caller | sync caller 没机会接异常；async caller 用 try/catch 包 |
| LAR-D10 | 空结果语义 | label 命中 0 个 TextAsset → 不抛，TranslationStore 不写入该 locale，`UI.Tr` fallback 到 msgid | 跟 Resources 路径"目录下没有 .po"的行为对齐 |
| LAR-D11 | `Locale` 变 partial | `public static partial class Locale` —— 把新 helper 拆到独立文件 | 镜像 `UseAddressableResolver()`(doc) 的"partial UI 接到独立 helper 文件"模式 |
| LAR-D12 | 文件位置 | `Runtime/Application/LocaleAddressableResolverHelper.cs`，整文件 `#if PROMPTUGUI_HAS_ADDRESSABLES` 包住 | 镜像 `AddressableResolverHelper.cs` / `AddressableIconResolverHelper.cs` |
| LAR-D13 | Editor 热重载 | **本期不动**。Editor 端 `UIAssetPostprocessor` 现在只识别 `Resources/PromptUGUI/i18n[-custom]/<locale>/*.po`，Addressables 路径下的 .po 修改不会触发 `ReloadCurrent`。补 Addressables guid → labels 反查留给后续 PR | 范围控制；要做的话需要类似 doc resolver 的 `_guidToKey` 反查表加上 labels 维度 |
| LAR-D14 | SKILL.md 更新 | i18n 段落（line 287）追加 Addressables 路径示例 + 文件位置约定 | 参考 doc resolver 在 SKILL.md line 381 的写法 |

---

## 3. 完整使用示例

启动期（Addressables 路径）：

```csharp
// 前置：项目装了 com.unity.addressables；
//       Assets/Localization/zh-Hans/main.po 在 AA Group 里挂 label="zh-Hans"；
//       Assets/Localization/en/main.po 挂 label="en"。
//       label 命名跟 PromptUGUISettings.locales[].locale 字符串一一对应。

UI.UseAddressableResolver();
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver("IconSets");
UI.Locale.UseAddressableResolver();

UI.Locale.Set("zh-Hans");     // fire-and-forget；界面短暂回落 msgid，下载完后自动切到译文
// 或：await UI.Locale.SetAsync("zh-Hans");  // 等加载完再继续，无闪烁

await UI.LoadDocumentAsync("screens/MainMenu");
UI.Open("MainMenu");
```

切语言：

```csharp
UI.Locale.Set("en");  // 旧 zh-Hans translation unload → 异步拉 en label → 加载完 ReSolve
```

---

## 4. 公开 API 表

| 状态 | 签名 | 说明 |
|---|---|---|
| **改签名** | `public static Func<string, Awaitable<IEnumerable<PoEntry>>> UI.PoResolver` | 旧 `Func<string, IEnumerable<PoEntry>>`。破坏性，详见 §7 |
| 新增 | `public static void UI.Locale.UseAddressableResolver()` | 仅在 `PROMPTUGUI_HAS_ADDRESSABLES` 下可见；label = locale |
| 不变 | `public static void UI.Locale.Set(string locale)` | 行为 sync 入口；Resources 默认路径下与今天完全等价；Addressables 路径下加载部分变 fire-and-forget |
| 新增 | `public static Awaitable UI.Locale.SetAsync(string locale)` | 等到 .po 加载 + Translation 入库 + Variant 翻转 + Changed 触发完后才 return |
| 不变 | `public static void UI.Locale.ReloadCurrent()` | 同 Set 的退化模型 |
| 新增 | `public static Awaitable UI.Locale.ReloadCurrentAsync()` | 同 SetAsync |
| 不变 | `public static event Action UI.Locale.Changed` | 加载完成后才触发（Addressables 路径） |

---

## 5. 落地细节

### 5.1 `UI.cs` 修改要点

```csharp
// 改签名：
public static Func<string, Awaitable<IEnumerable<I18n.PoEntry>>> PoResolver { get; set; }

public static partial class Locale   // 加 partial
{
    public static void Set(string locale)
    {
        if (Current == locale) return;
        if (Current != null)
        {
            VariantStore.Set(Current, false);              // 先广播一次：界面回 msgid
            TranslationStore.Instance.UnloadLocale(Current);
        }
        Current = locale;
        if (locale != null)
        {
            _ = LoadPoFilesAndApplyAsyncLogged(locale);   // fire-and-forget
        }
        else
        {
            VariantStore.NotifyChangedInternal();
            Changed?.Invoke();
        }
    }

    public static async Awaitable SetAsync(string locale)
    {
        if (Current == locale) return;
        if (Current != null)
        {
            VariantStore.Set(Current, false);
            TranslationStore.Instance.UnloadLocale(Current);
        }
        Current = locale;
        if (locale != null)
        {
            await LoadPoFilesAndApplyAsync(locale);
        }
        else
        {
            VariantStore.NotifyChangedInternal();
            Changed?.Invoke();
        }
    }

    public static void ReloadCurrent()
    {
        if (Current == null) return;
        _ = ReloadCurrentAsyncLogged();
    }

    public static async Awaitable ReloadCurrentAsync()
    {
        if (Current == null) return;
        TranslationStore.Instance.UnloadLocale(Current);
        await LoadPoFilesAndApplyAsync(Current);
    }

    // 两个 async 实现：
    private static async Awaitable LoadPoFilesAndApplyAsync(string locale)
    {
        await LoadPoFilesAsync(locale);
        if (Current != locale) return;    // raced by另一次 Set；丢弃本次结果
        VariantStore.Set(locale, true);   // 触发 ReSolve；UI 文字最终落屏
        Changed?.Invoke();
    }

    private static async Awaitable LoadPoFilesAndApplyAsyncLogged(string locale)
    {
        try { await LoadPoFilesAndApplyAsync(locale); }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[PromptUGUI] locale load failed for '{locale}': {e}");
        }
    }

    private static async Awaitable ReloadCurrentAsyncLogged()
    {
        try { await ReloadCurrentAsync(); }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(
                $"[PromptUGUI] locale reload failed for '{Current}': {e}");
        }
    }
}

private static async Awaitable LoadPoFilesAsync(string locale)
{
    if (PoResolver != null)
    {
        var entries = await PoResolver(locale);
        if (Current != locale) return;    // raced；不污染 TranslationStore
        if (entries != null)
            TranslationStore.Instance.Load(locale, entries);
        return;
    }
    LoadPoFromResourcesPath($"PromptUGUI/i18n/{locale}", locale);
    LoadPoFromResourcesPath($"PromptUGUI/i18n-custom/{locale}", locale);
}
```

`LoadPoFromResourcesPath` 是原 `LoadPoFromPath` 改名（语义没变，只是同步路径下的 .po 解析）。

### 5.2 `LocaleAddressableResolverHelper.cs`（新文件）

```csharp
#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using PromptUGUI.I18n;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Locale
        {
            /// <summary>
            /// 把 PoResolver 设为按 label=locale 加载所有 .po TextAsset 的 Addressables 实现。
            /// Set("zh-Hans") → Addressables.LoadAssetsAsync&lt;TextAsset&gt;("zh-Hans", null)。
            /// 仅在装了 com.unity.addressables 时存在（PROMPTUGUI_HAS_ADDRESSABLES 编译定义）。
            ///
            /// 注意 fire-and-forget 模型下 Set 返回后 UI 还看到 msgid，要等下载完才切译文；
            /// 想避免闪烁用 await Locale.SetAsync(...)。
            /// </summary>
            public static void UseAddressableResolver()
            {
                PoResolver = LoadPoFromAddressablesAsync;
            }

            private static async Awaitable<IEnumerable<PoEntry>> LoadPoFromAddressablesAsync(
                string locale)
            {
                var handle = Addressables.LoadAssetsAsync<TextAsset>(locale, null);
                try
                {
                    var assets = await handle.Task;
                    var entries = new List<PoEntry>();
                    foreach (var ta in assets ?? Array.Empty<TextAsset>())
                    {
                        if (ta == null) continue;
                        try
                        {
                            foreach (var e in PoParser.Parse(ta.text)) entries.Add(e);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"[PromptUGUI] failed to parse .po asset '{ta.name}': {ex.Message}");
                        }
                    }
                    return entries;
                }
                finally
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                }
            }
        }
    }
}
#endif
```

### 5.3 SKILL.md 更新

在 i18n 段落（line 287 起）`UI.Locale.Set("en")` 示例之后追加：

````markdown
**.po file location**

By default `.po` files live in `Assets/Resources/PromptUGUI/i18n/<locale>/` or
`/PromptUGUI/i18n-custom/<locale>/`. Files anywhere under those paths are picked
up; subfolder names are ignored.

When the project ships .po via Addressables, call `UI.Locale.UseAddressableResolver()`
at boot. The resolver loads all TextAssets whose Addressables label matches the
locale string (so `Locale.Set("zh-Hans")` loads everything labelled `zh-Hans`).
Files can live anywhere. Only available when `com.unity.addressables` ≥ 1.0 is
installed (gated by `PROMPTUGUI_HAS_ADDRESSABLES`).

```csharp
UI.Locale.UseAddressableResolver();
UI.Locale.Set("zh-Hans");                  // sync; UI flashes msgid briefly during download
// or:
await UI.Locale.SetAsync("zh-Hans");       // awaits download + parse + ReSolve
```
````

---

## 6. 测试策略

### 6.1 新文件 `Tests/EditMode/Addressables/LocaleAddressableResolverTests.cs`

沿用 `AddressableResolverTests.cs` 的 EditMode 异步限制说明（class-level comment）。

| 测试 | 断言 |
|---|---|
| `UseAddressableResolver_sets_po_resolver` | 调用前 `UI.PoResolver` 为 null（`ResetForTests` 已置 null）；调用后非 null |
| `UseAddressableResolver_then_PoResolver_invocation_returns_an_awaitable` | 调用 `UI.PoResolver("zh-Hans")` 返回非 null `Awaitable<IEnumerable<PoEntry>>`；不 await（EditMode 限制） |

### 6.2 新文件 `Tests/EditMode/Application/LocaleSetAsyncTests.cs`

不依赖 Addressables；用 fake 异步 resolver 验证 `Set` / `SetAsync` 时序契约。

| 测试 | 断言 |
|---|---|
| `Set_with_sync_completed_PoResolver_loads_synchronously` | fake resolver 返回 `AwaitableHelpers.Completed(entries)` → `Set("en")` 返回后 `UI.Tr` 立刻拿到译文（Resources 退化路径回归测试） |
| `Set_with_deferred_PoResolver_does_not_load_before_Set_returns` | fake resolver 返回挂起的 `AwaitableCompletionSource` → `Set("en")` 返回后 `UI.Tr` 还看不到译文；手动 `SetResult` → 等一帧（或同步推进 player loop） → `UI.Tr` 看到译文 |
| `SetAsync_with_deferred_PoResolver_completes_only_after_load` | `await SetAsync("en")` 在 `SetResult` 之前不返回；返回后 `UI.Tr` 立刻拿到译文 |
| `Set_async_path_logs_error_on_resolver_throw` | fake resolver 在 await 后抛 → fire-and-forget 路径捕获并 `Debug.LogError`（用 `LogAssert.Expect`） |
| `SetAsync_path_throws_on_resolver_throw` | `await SetAsync(...)` 抛出 resolver 的异常 |
| `Set_triggers_two_variant_changed_events_on_locale_switch` | 第一次广播是 flip 旧 off（旧 locale 退场），第二次是 flip 新 on（加载完）；订阅 `VariantStore.Changed` 计数 |
| `Set_rapid_consecutive_with_async_resolver_discards_stale_load` | `Set("zh")` 挂起；`Set("en")`；`SetResult` 让 zh 加载完成 → zh 译文不入 TranslationStore，zh variant 不被翻回 on；只有 en 完成时 UI 才切到 en 译文 |

### 6.3 现有测试迁移

| 文件 | 改动 |
|---|---|
| `Tests/PlayMode/E2E/I18nHotReloadTests.cs:22` | `UI.PoResolver = _ => Enumerable.Empty<PoEntry>()` → `UI.PoResolver = _ => AwaitableHelpers.Completed<IEnumerable<PoEntry>>(Array.Empty<PoEntry>())` |
| `Tests/PlayMode/E2E/I18nFontSwapTests.cs:31` | 同上 |

`AwaitableHelpers` 是 internal，PlayMode 测试通过 `InternalsVisibleTo` 已经能访问（`Runtime/AssemblyInfo.cs` 已暴露给 `PromptUGUI.Tests.PlayMode`）。

### 6.4 Locale 测试 setup/teardown

`LocaleAddressableResolverTests` 跟 `AddressableResolverTests` 一样在 `[SetUp]` / `[TearDown]` 调 `UI.ResetForTests()`；`ResetForTests` 已经把 `PoResolver` 置 null（`UI.cs:429`），不需要新加 cleanup 逻辑。

---

## 7. 迁移与破坏性影响

### 7.1 `UI.PoResolver` 签名变化

旧：`Func<string, IEnumerable<PoEntry>>`
新：`Func<string, Awaitable<IEnumerable<PoEntry>>>`

外部 caller 迁移：

```csharp
// 旧
UI.PoResolver = locale => MyEntries(locale);

// 新（用 Unity 6 公开的 AwaitableCompletionSource）
UI.PoResolver = locale => {
    var src = new AwaitableCompletionSource<IEnumerable<PoEntry>>();
    src.SetResult(MyEntries(locale));
    return src.Awaitable;
};
```

仓库内部受影响：

- `Tests/PlayMode/E2E/I18nHotReloadTests.cs:22`
- `Tests/PlayMode/E2E/I18nFontSwapTests.cs:31`

两处都改成用 `AwaitableHelpers.Completed<IEnumerable<PoEntry>>(Array.Empty<PoEntry>())`（详见 §6.3）。

### 7.2 `Locale.Set` 行为变化

Resources 默认路径：完全无感知。`AwaitableHelpers.Completed` 让首个 `await` 不让步，整条 async 链同步走完，外部观察跟今天的 sync `Set` 完全一致。

Addressables 路径：`Set` 返回后 UI 短暂回落 msgid，下载完后自动 ReSolve 到译文。想避免闪烁用 `await SetAsync`。

### 7.3 SKILL.md

详见 §5.3，仅在 i18n 段落追加一小节，不动现有内容。

### 7.4 Samples~

Samples~/MainMenu 不动。本次新示例不放进 sample，避免给 sample 加 Addressables 依赖。

---

## 8. 非目标 / 推迟

- **Editor 热重载**（LAR-D13）：Addressables 路径下保存 .po 不触发 `ReloadCurrent`。补这个要在 `UIAssetPostprocessor` 加 `guid → AA labels` 反查，类似 `AddressableResolverHelper.BuildAddressablesReverseMapping` 但多一个 labels 维度。留给后续 PR。
- **多 label 合并加载**：`UseAddressableResolver(string[] labels)` 之类的扩展（按多 label `Or` / `And` 加载）—— 单 label 已覆盖 90% 场景，按需再扩。
- **Locale fallback chain**：`Set("zh-Hans-CN")` 自动回落 `zh-Hans` 等 BCP-47 父语言 —— 跟 Addressables 无关，是 `LocaleHelpers` 层面的事，独立设计。
- **Fonts 通道走 Addressables**：当前字体走 `PromptUGUISettings.fonts[].Locales[].font` 直接引用，依赖 Unity 序列化打包；如果将来要走 Addressables，新开设计。

---

## 9. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| Set fire-and-forget 路径短暂闪烁 msgid | 视觉体验：第一帧或多帧（取决于下载速度）显示英文 / 原文 | 文档明示；提供 SetAsync 给 caller 选择；推荐配合 loading 遮罩 |
| `Addressables.LoadAssetsAsync<TextAsset>` 在 label 命中 0 个时的返回值不稳定 | 空 list 路径分支挂掉 | `assets ?? Array.Empty<TextAsset>()` 兜底（参考 icon resolver 同样的护栏） |
| fire-and-forget 路径的异常进 `Debug.LogError`，sync caller 不知道加载失败 | 静默失败：UI 一直显示 msgid | caller 可监听 `Locale.Changed` 检测加载完成；或者用 `SetAsync` 在 try/catch 里接异常 |
| 用户在 Addressables 里给 .po 打了拼错的 label（比如 `zh_Hans` vs `zh-Hans`） | 加载结果为空，UI 看不到译文 | Addressables 标签拼错没办法运行时检测；推荐配套一个 Editor 校验工具（非本 PR） |
| `Locale.Set` 同步快连发（`Set("zh"); Set("en");`） | 异步加载乱序 → 后到的可能覆盖先到的，但 `Current` 已经是 `"en"`，覆盖到错 locale | `LoadPoFilesAndApplyAsync` 内部对比 `Current`，如果不一致就丢弃结果。**实施时加这个守卫** |
| Domain reload / Play stop 时未完成的 Addressables handle 没显式 release | Addressables 内部 ref count 不清；进程退出 OS 回收 | 不缓解；与现有 doc resolver 一致 |

---

## 10. 实施粒度（提示）

writing-plans 阶段细化，大致 5 块：

1. **改 `UI.PoResolver` 签名 + 迁移 2 处 test 用例**（先红：测试编译失败 → 改实现 → 绿）
2. **`UI.Locale.Set` / `SetAsync` / `ReloadCurrent` / `ReloadCurrentAsync` 实现**（含 fire-and-forget 守卫 §9 的 `Current` 不一致丢弃；先写 `LocaleSetAsyncTests` 红 → 实现绿）
3. **`LocaleAddressableResolverHelper.cs` 新文件 + `LocaleAddressableResolverTests` smoke**（红 → 绿）
4. **SKILL.md 更新**（i18n 段落加 Addressables 子段；line 585 reference 表加 `UseAddressableResolver` 一行）
5. **lint pass + Unity MCP `refresh_unity` + `run_tests` 三个 assembly 全跑过**
