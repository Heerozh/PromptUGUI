# Addressable IconSet Resolver 设计

**日期**：2026-05-12
**状态**：设计阶段（待 review，未进入实施）
**作用域**：在 `IconResolverHelpers` 上新增 Addressables 路径的 IconSet 加载入口，让 user 把 IconSet（含其依赖的 SpriteAtlas）放进 Addressables 自动下载/加载。仅增量新代码 + `UI.ResetForTests()` 加一个内部 reset 事件钩子。不动现有 `UseSpriteAtlasIconResolver` 两个重载的行为。
**依赖**：[`2026-05-08-icon-assets-design.md`](2026-05-08-icon-assets-design.md)（IconSet / IconResolver 形状）+ [`2026-05-11-addressable-resolver-async-load-design.md`](2026-05-11-addressable-resolver-async-load-design.md)（Addressables helper 风格与 `PROMPTUGUI_HAS_ADDRESSABLES` 条件编译）。

---

## 1. 背景

当前 `IconResolverHelpers` 有两个同步入口：

- `UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets")` —— `Resources.LoadAll<IconSet>` 枚举资源目录
- `UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets)` —— 调用方直接喂 IconSet 引用

像素游戏的实际工程里，IconSet 加上它指向的 SpriteAtlas 体积可观（几 MB 起步），常被放进 Addressables Group 走按需下载 / 远端 CDN。`UI.UseAddressableResolver()` 已经把 `.ui.xml` 通道接入 Addressables；本设计补齐 IconSet 通道。

设计上的两点差异决定了 API 形状不能直接照搬 `UseAddressableResolver()`：

1. **IconResolver 是同步的**（`Func<string, Sprite>`），它在 `<Icon name>` setter 里被调用，调用点不能 `await`。所以 IconSet 必须在 Screen 打开之前**预加载**，方法签名要返回 `Awaitable` 让 user `await` 一次完成预加载。
2. **句柄必须常驻**。`IconSet.Entries` 里的 `Sprite` 是 SpriteAtlas 的子对象引用，Addressables 一旦 release 句柄，atlas 被卸载，sprite 失效。所以本 helper 要把句柄存为 static 字段，跟 IconSet 生命周期一致。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| AI-D1 | API 形状 | `IconResolverHelpers` 上新增 `public static async Awaitable UseAddressableSpriteAtlasIconResolver(string label = "IconSets")` | 同名族延续 `UseSpriteAtlasIconResolver`，参数语义跟 Resources 版本的 `resourcesSubpath="IconSets"` 对齐 |
| AI-D2 | 资产发现方式 | `Addressables.LoadAssetsAsync<IconSet>(label, callback: null)` —— 按 label 拉全部 | IconSet 数量通常 < 10，列表 API 比单 key 多次调用简单；user 在 Addressables 里给 IconSet 资产打 label 即可，与 Resources 版本"丢进文件夹"对称 |
| AI-D3 | SpriteAtlas 是否需要额外标记 | 不需要 | Addressables 跟踪 asset 依赖：加载 IconSet 会自动把 `entries[i].sprite` 引用的 SpriteAtlas 作为依赖一起加载 |
| AI-D4 | 异步签名 | 返回 `Awaitable`（非泛型，无 result） | IconResolver 是 sync 的；本方法只是预加载副作用，没有要 yield 的 result |
| AI-D5 | 句柄生命周期 | static 字段保留 `AsyncOperationHandle<IList<IconSet>>`；二次调用本方法先 release 旧句柄；`UI.ResetForTests()` 触发新增的内部 `OnReset` 事件，helper 在事件里 release | sprite 引用依赖 atlas 常驻；ResetForTests 是测试间隔离的契约位置，把句柄清理塞在那里跟现有 `IconResolverRebuilder = null` 同位 |
| AI-D6 | `UI.OnReset` 事件可见性 | `internal static event Action OnReset` | 测试和 helper 都在 Runtime asmdef，不需要 public；与 `AwaitableHelpers` 一样用 `InternalsVisibleTo` 走通 |
| AI-D7 | HotReload 协议 | 设置 `UI.HotReload.IconResolverRebuilder = Rebuild`；Rebuild 不重新走 Addressables，只从已缓存的 IconSet 列表重新 `BuildLookup` | 本地 Editor `IconAtlasSyncer` 改 `IconSet.entries` 后 ScriptableObject 已更新，引用不变；不需要重新发起 Addressables 请求 |
| AI-D8 | 加载失败语义 | `Addressables.LoadAssetsAsync` 失败（label 不存在 / 资源类型错）→ `await` 抛异常透传给调用方 | 跟 `UseAddressableResolver()` 一致，让调用方决定要不要重试 / fallback |
| AI-D9 | 空结果语义 | label 命中 0 个 IconSet → 不抛异常，`UI.IconResolver` 被设为返回 null 的查表函数 | 跟 `UseSpriteAtlasIconResolver(Array.Empty<IconSet>())` 现有行为对齐（参见 `IconResolverTests.UseSpriteAtlasIconResolver_with_empty_list_builds_resolver`） |
| AI-D10 | Editor reverse mapping | 不做 | XML 走的 `AssetPathToSrc` 是为 `.ui.xml` 热重载；IconSet 的热重载入口是 `IconAtlasSyncer` 直接调 `UI.HotReload.NotifyIconAssetsChanged()`，跟资产路径无关 |
| AI-D11 | 文件位置 | `Runtime/Application/AddressableIconResolverHelper.cs`，整文件 `#if PROMPTUGUI_HAS_ADDRESSABLES` 包住；类用 `public static partial class IconResolverHelpers`，与 `IconResolverHelpers.cs` 同名合并 | 镜像 `AddressableResolverHelper.cs` 的模式（也是 partial class 形式接到 `UI`） |
| AI-D12 | SKILL.md 更新 | 在 "Icon system setup" 段落追加 Addressables 用法示例 | CLAUDE.md 要求 public C# API 变更同步 SKILL；新 API 不属于"transparent default"豁免 |

---

## 3. 完整使用示例

启动期（Addressables 路径）：

```csharp
// 前置：项目装了 com.unity.addressables；
//       SolarIconSet.asset / GameIconSet.asset 在 AA Group 里挂 label="IconSets"。
//       注意 Addressables 会自动把它们引用的 SpriteAtlas 作为依赖一起打包/下载。

UI.UseAddressableResolver();
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver();   // 默认 label="IconSets"

await UI.LoadDocumentAsync("screens/MainMenu");
UI.Open("MainMenu");   // <Icon name="ui:heart"/> 此时已可解析
```

自定义 label：

```csharp
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver("MyIcons");
```

二次调用（切语言包 / 切主题）：

```csharp
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver("IconSets_zh");
// 旧句柄已被 release，旧 IconSet / atlas 引用计数归零会被 Addressables 回收
// （前提：场景里没有其他 user 代码持有那些 Sprite 引用）
```

---

## 4. 公开 API 表

| 状态 | 签名 | 说明 |
|---|---|---|
| 新增 | `public static async Awaitable IconResolverHelpers.UseAddressableSpriteAtlasIconResolver(string label = "IconSets")` | 仅在 `PROMPTUGUI_HAS_ADDRESSABLES` 下可见 |
| 不变 | `IconResolverHelpers.UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets")` | Resources 路径 |
| 不变 | `IconResolverHelpers.UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets)` | 调用方直接喂 |
| 新增（internal） | `internal static event Action UI.OnReset` | `ResetForTests` 触发；helper 用来 release 句柄 |

---

## 5. 落地细节

### 5.1 `UI.OnReset` 钩子

`Runtime/Application/UI.cs` 在现有 `ResetForTests()` 末尾加：

```csharp
internal static event Action OnReset;

public static void ResetForTests() {
    // ...现有逻辑：unload 所有 Screen、清 SourceResolver、IconResolver、HotReload.* ...

    OnReset?.Invoke();
}
```

事件在所有现有 reset 逻辑跑完之后触发——保证订阅者看到的是"刚 reset 完的状态"。

### 5.2 `AddressableIconResolverHelper.cs`

```csharp
#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PromptUGUI.Application
{
    public static partial class IconResolverHelpers
    {
        private static AsyncOperationHandle<IList<IconSet>>? _addressableIconHandle;
        private static bool _addressableResetHooked;
        internal static int _testReleaseCount;   // tests-only observation point

        public static async Awaitable UseAddressableSpriteAtlasIconResolver(
            string label = "IconSets")
        {
            ReleaseAddressableIconHandle();   // 二次调用先放旧句柄
            HookResetOnce();

            var handle = Addressables.LoadAssetsAsync<IconSet>(label, null);
            _addressableIconHandle = handle;
            var sets = await handle.Task;   // 失败抛异常透传
            var snapshot = new List<IconSet>(sets ?? Array.Empty<IconSet>());

            void Rebuild() {
                var map = BuildLookup(snapshot);
                UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
        }

        private static void HookResetOnce() {
            if (_addressableResetHooked) return;
            UI.OnReset += ReleaseAddressableIconHandle;
            _addressableResetHooked = true;
        }

        private static void ReleaseAddressableIconHandle() {
            if (_addressableIconHandle.HasValue && _addressableIconHandle.Value.IsValid()) {
                Addressables.Release(_addressableIconHandle.Value);
                _testReleaseCount++;
            }
            _addressableIconHandle = null;
        }
    }
}
#endif
```

`BuildLookup` 在 `IconResolverHelpers.cs` 已经是 `private static`，partial class 合并后两边可见。

### 5.3 SKILL.md 更新

在 "Icon system setup" 段落（约 line 408 起）现有两个示例之后追加：

````markdown
Addressables 路径（项目装了 `com.unity.addressables` 时）：

```csharp
// 把 IconSet 资产在 Addressables 里打 label="IconSets"，
// Addressables 会自动把它们引用的 SpriteAtlas 作为依赖一起打包。
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver();
// 或自定义 label：
await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver("MyIcons");
```

调用 await 后才能打开含 `<Icon>` 的 Screen。仅在 `PROMPTUGUI_HAS_ADDRESSABLES` 编译定义下可见。
````

---

## 6. 测试策略

新文件 `Tests/EditMode/Addressables/AddressableIconResolverTests.cs`（沿用 `AddressableResolverTests.cs` 的 EditMode 异步限制说明）：

| 测试 | 断言 |
|---|---|
| `UseAddressableSpriteAtlasIconResolver_invocation_returns_an_awaitable` | 调用返回非 null `Awaitable`；不 await（EditMode 没有 player loop，Addressables 异步 continuation 不 resume） |
| `UseAddressableSpriteAtlasIconResolver_with_unknown_label_does_not_set_resolver_synchronously` | 调用立刻返回 Awaitable 时 `UI.IconResolver` 还没被设（异步路径），不抛 |
| `UseAddressableSpriteAtlasIconResolver_releases_previous_handle_on_second_call` | 连续调两次不抛；通过 helper 暴露的 `internal static int _testReleaseCount` 计数器验证 +1 |
| `ResetForTests_releases_addressable_icon_handle` | 订阅 `UI.OnReset` 的 release 路径生效：调用方法一次 → `ResetForTests()` → `_testReleaseCount` +1 |
| `OnReset_event_fires_after_reset` | EditMode 独立测试：订阅 `UI.OnReset`、调 `ResetForTests`、验证回调被调一次（覆盖 §5.1 钩子本身，不依赖 Addressables） |

端到端"label → IconSet → IconResolver 返回正确 sprite"的验证留给 PlayMode（如果将来加 PlayMode 测试），EditMode 跑不出真正的 Addressables continuation —— 同 `AddressableResolverTests` 的注释。

`Tests/EditMode/Application/IconResolverTests.cs` 现有行为完全不动。

---

## 7. 迁移与破坏性影响

- 既有 `UseSpriteAtlasIconResolver` 两个重载行为不变，调用方零迁移。
- 新增 `UI.OnReset` 是 internal event，对外部 user 不可见，不算 API 表面变化。
- Samples 不动。Samples~/MainMenu 仍然走 Resources 路径，新示例不放进 sample，避免给 sample 加 Addressables 依赖。

---

## 8. 非目标 / 推迟

- **AssetReference / AssetReferenceT<IconSet> 入口**：单独按 reference 加载更精确，但日常用法是"一次性预加载所有 IconSet"，label 入口已经覆盖。如果将来需要按需加载单个 IconSet，再扩。
- **运行时切包热更**：本设计支持二次调用切换 label，但不处理"切换中已打开 Screen 的 Sprite 仍指向旧 atlas"的过渡帧——调用方负责在切换前关闭含 `<Icon>` 的 Screen，或接受切换瞬间的旧 sprite 显示。
- **Addressables 反向映射**：IconSet 的热重载入口是 `IconAtlasSyncer` 直接广播 `NotifyIconAssetsChanged`，不像 `.ui.xml` 需要 path→key 反查。

---

## 9. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| `Addressables.LoadAssetsAsync<IconSet>` 在 label 命中 0 个时的返回值（null vs 空 list）随版本不同 | empty list 路径分支挂掉 | 实施时 `sets ?? Array.Empty<IconSet>()` 兜底 |
| `UI.OnReset` event 多次订阅 | 同一 helper 加载多次后，reset 时 release 多次 | `_addressableResetHooked` 标志位防重复订阅 |
| user 在场景里把 sprite 引用拷出去（缓存到自家 MonoBehaviour）后调用切换，旧 atlas 被卸载 | sprite 失效、显示白图 | 文档里写明：sprite 引用只在当前 IconResolver 生效期内有效；不持有缓存 |
| Player 退出 / 域 reload 时 `_addressableIconHandle` 不被显式 release | Addressables 内部 ref count 没清；进程退出时 OS 回收，无实际泄漏 | 不缓解；与 Unity 引擎进程生命周期一致 |

---

## 10. 实施粒度（提示）

writing-plans 阶段细化，大致 3 块：

1. `UI.OnReset` event + `ResetForTests` 触发 + EditMode 测试（红→绿）
2. `AddressableIconResolverHelper.cs` 新文件 + smoke 测试（红→绿）
3. SKILL.md 更新 + lint pass + Unity MCP `run_tests` 全跑过
