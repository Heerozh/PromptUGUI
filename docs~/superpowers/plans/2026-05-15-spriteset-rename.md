# SpriteSet Rename + sprite= Dual-syntax Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `IconSet`/`IconResolver`/`IconResolverHelpers` 家族重命名为 `SpriteSet`/`SpriteResolver`/`SpriteResolverHelpers`,新增 `UI.ResolveSprite` 助手,让 7 个内置控件 `sprite=` 支持 `"ns:name"` 走 SpriteResolver 的双语法。`<Icon>` XML tag 保持不变。

**Architecture:** 三层:(1)Runtime API rename + 新增 `UI.ResolveSprite` 双语法分流入口;(2)Controls 收敛——内置 7 控件 `sprite=` setter 统一调 `UI.ResolveSprite`,`<Icon>` 直连 `UI.SpriteResolver`;(3)Editor `SpriteAtlasSyncer` 的 XML scan 扩展到 `<* sprite="ns:name">`,维持 package-time pruning。库未上线,无向后兼容包袱(不留 `[Obsolete]` / `[MovedFrom]`)。

**Tech Stack:** Unity 6 + C# 9 + R3 (Cysharp) + LitMotion;Unity Addressables(可选,gated by `PROMPTUGUI_HAS_ADDRESSABLES`);测试通过 Unity MCP 跑(`mcp__UnityMCP__run_tests`),lint 通过 `.lint/` 内 `dotnet format` 跑。

**Spec:** [`docs~/superpowers/specs/2026-05-15-spriteset-rename-design.md`](../specs/2026-05-15-spriteset-rename-design.md)

---

## File Structure (overview)

**Renamed (git mv + class/identifier rename):**

| 旧路径 | 新路径 |
|---|---|
| `Runtime/Application/IconSet.cs` | `Runtime/Application/SpriteSet.cs` |
| `Runtime/Application/IconResolverHelpers.cs` | `Runtime/Application/SpriteResolverHelpers.cs` |
| `Runtime/Application/AddressableIconResolverHelper.cs` | `Runtime/Application/AddressableSpriteResolverHelper.cs` |
| `Editor/IconSetEditor.cs` | `Editor/SpriteSetEditor.cs` |
| `Editor/IconAtlasSyncer.cs` | `Editor/SpriteAtlasSyncer.cs` |
| `Editor/IconAtlasMenu.cs` | `Editor/SpriteAtlasMenu.cs` |
| `Editor/IconAtlasAutoSync.cs` | `Editor/SpriteAtlasAutoSync.cs` |
| `Editor/IconAtlasBuildHook.cs` | `Editor/SpriteAtlasBuildHook.cs` |
| `Tests/EditMode/Application/IconResolverTests.cs` | `Tests/EditMode/Application/SpriteResolverTests.cs` |
| `Tests/EditMode/Application/IconHotReloadTests.cs` | `Tests/EditMode/Application/SpriteHotReloadTests.cs` |
| `Tests/EditMode/Editor/IconAtlasSyncerTests.cs` | `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs` |
| `Tests/EditMode/Addressables/AddressableIconResolverTests.cs` | `Tests/EditMode/Addressables/AddressableSpriteResolverTests.cs` |
| `Samples~/MainMenu/SolarIconSet.asset` | `Samples~/MainMenu/SolarSpriteSet.asset` |

**Modified (in place, identifier/text replacements):**

- `Runtime/Application/UI.cs` — `IconResolver` field rename + `HotReload.IconResolverRebuilder`/`NotifyIconAssetsChanged` rename + 新增 `ResolveSprite` 方法
- `Runtime/Controls/Icon.cs` — `UI.IconResolver` → `UI.SpriteResolver`;错误消息更新
- `Runtime/Controls/{Image,Btn,Toggle,Slider,Dropdown,ScrollList,InputField}.cs` — `Sprite` setter 改用 `UI.ResolveSprite`
- `Samples~/MainMenu/MainMenuRunner.cs` — `IconSet` → `SpriteSet`,`IconResolverHelpers.UseSpriteAtlasIconResolver` → `SpriteResolverHelpers.UseSpriteSetResolver`
- `README.md` — `IconSet` / `IconResolverHelpers.UseAddressableSpriteAtlasIconResolver` 等引用更新
- `.claude/skills/authoring-promptugui-xml/SKILL.md` — `IconSet` → `SpriteSet`,菜单路径 `Icon → Sync` → `Sprite → Sync`,`<Image sprite=>` 双语法说明
- `.claude/skills/scripting-promptugui-csharp/SKILL.md` — 全部 `IconSet`/`IconResolver`/`UseSpriteAtlasIconResolver` 字面替换,新增 "sprite= 双语法" 段落
- `.claude/skills/using-promptugui-addressables/SKILL.md` — `UseAddressableSpriteAtlasIconResolver` → `UseAddressableSpriteSetResolver`,默认 label `"IconSets"` → `"SpriteSets"`,`IconSet` 全部替换

**New (TDD):**

- `Runtime/Application/UI.cs` 内新增 `public static Sprite ResolveSprite(string value)` 方法(见 Task 2)
- `Tests/EditMode/Application/ResolveSpriteTests.cs` — 新测试文件(见 Task 2)
- `Editor/SpriteAtlasSyncer.cs` 内新增对 `<* sprite=>` 的扫描分支(见 Task 9)
- `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs` 内新增 sprite= 扫描测试用例(见 Task 9)

---

## Implementer notes

**Unity 文件移动**:Unity 用 `.meta` 文件里的 GUID 跟踪 asset。`.cs` 文件移动时必须把对应 `.cs.meta` 一起 `git mv`,否则 Unity 会重新生成 GUID,导致所有引用断链。

**编译 / 测试 feedback**:每次编辑 `.cs` 后跑

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

通过 MCP 加载工具:

```
ToolSearch(query="select:refresh_unity,read_console,run_tests", max_results=3)
```

每个 phase 结束跑相关 assembly 的 `run_tests`,见各 task 末尾。

**禁用 MCP 调用**:不要调 `mcp__UnityMCP__execute_menu_item(menu_path="Assets/Reimport All")`(会弹模态,阻塞所有后续 MCP 调用)。用 `refresh_unity(mode="force", scope="all")` 代替。

**MCP 不可用**:检查用户 MCP 配置,若没装,警告并停;若装了但没连,要求用户开 Unity Editor。不要继续硬跑测试。

---

## Tasks

### Task 1: 重命名 IconSet → SpriteSet(ScriptableObject)

**Files:**
- Rename: `Runtime/Application/IconSet.cs` → `Runtime/Application/SpriteSet.cs`(+ `.meta`)
- Modify (类型引用): `Runtime/Application/IconResolverHelpers.cs`, `Runtime/Application/AddressableIconResolverHelper.cs`, `Runtime/Application/UI.cs`(`Func<string, Sprite>` 字段类型保持 `Sprite`,但 XML doc 注释里的 `IconSet` 要改)
- Modify (类型引用): `Editor/IconSetEditor.cs`, `Editor/IconAtlasSyncer.cs`, `Editor/IconAtlasMenu.cs`, `Editor/IconAtlasAutoSync.cs`, `Editor/IconAtlasBuildHook.cs`
- Modify (类型引用): 4 个测试文件,`Samples~/MainMenu/MainMenuRunner.cs`
- Modify (YAML): `Samples~/MainMenu/SolarIconSet.asset` 的 `m_EditorClassIdentifier`

**注意:**本 task 只动 `IconSet` 这个类型(类名 + 文件名 + 所有引用 `IconSet`)。其他 rename(`IconResolver`, `IconResolverHelpers` 等)在后续 task。`IconAtlasSyncer.FindAllIconSets()` 方法的返回类型变 `SpriteSet[]`,但方法名 `FindAllIconSets` 留到 Task 8 一起改。

- [ ] **Step 1: 移动文件 + .meta**

```bash
git mv Runtime/Application/IconSet.cs Runtime/Application/SpriteSet.cs
git mv Runtime/Application/IconSet.cs.meta Runtime/Application/SpriteSet.cs.meta
```

- [ ] **Step 2: 类名 / namespace 注释 rename**

Edit `Runtime/Application/SpriteSet.cs`:把 `public sealed class IconSet` → `public sealed class SpriteSet`(类名);XML doc comment / inline comment 里出现的 `IconSet` 也改为 `SpriteSet`。

- [ ] **Step 3: 全局 grep 找所有 `IconSet` 类型引用**

```bash
grep -rn "\bIconSet\b" Runtime/ Editor/ Tests/ Samples~/ 2>/dev/null
```

按位置批量改:文件路径 + 行号都从 grep 输出来,逐个 Edit 替换。`\bIconSet\b` 正则确保只匹配整个单词(不动 `IconSets` 复数、不动 `IconResolver` 这类无关 token)。

`IconSets`(复数,如 `IList<IconSet>` 不会出现但 `IconSet[] iconSets` 这种字段名要看实际语义)——本 task 只改类型名 `IconSet` → `SpriteSet`,字段名 `iconSets`(变量名)留到 Task 3 跟 helper rename 一起改更自然(因为变量含义是"传给 helper 的集合")。这里保留 `iconSets`、`Icon Sets` 字符串、`IconSet` 注释等暂不动。

具体改的 token 限制为:**只有 `IconSet` 作为类型名 / `typeof(IconSet)` / 泛型实参 / cast 时的引用**。例如:

| 是 | 改为 |
|---|---|
| `private static AsyncOperationHandle<IList<IconSet>>?` | `private static AsyncOperationHandle<IList<SpriteSet>>?` |
| `IconSet[] sets` | `SpriteSet[] sets` |
| `[SerializeField] IconSet[] iconSets` | `[SerializeField] SpriteSet[] iconSets`(字段名 `iconSets` 暂留) |
| `IEnumerable<IconSet> sets` | `IEnumerable<SpriteSet> sets` |
| `if (o is IconSet s)` | `if (o is SpriteSet s)` |
| `Resources.LoadAll<IconSet>(...)` | `Resources.LoadAll<SpriteSet>(...)` |
| `CustomEditor(typeof(IconSet))` | `CustomEditor(typeof(SpriteSet))` |

- [ ] **Step 4: 更新 SolarIconSet.asset 的 class identifier**

Edit `Samples~/MainMenu/SolarIconSet.asset`(只改一行):

```yaml
m_EditorClassIdentifier: PromptUGUI.Runtime::PromptUGUI.Application.IconSet
```
改为
```yaml
m_EditorClassIdentifier: PromptUGUI.Runtime::PromptUGUI.Application.SpriteSet
```

(asset 文件名 `SolarIconSet.asset` 本身留到 Task 11 一起改,这里只动文件内的 class id。)

- [ ] **Step 5: refresh + 检查编译错误**

```
ToolSearch(query="select:refresh_unity,read_console", max_results=2)
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

预期:无 error。如有 error,通常是漏改的引用——grep 没覆盖到的位置(如字符串 nameof、字典 key)。修复后重跑。

- [ ] **Step 6: 跑相关测试 sanity check**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

预期:全 pass(类型 rename 不影响测试逻辑)。

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: rename IconSet ScriptableObject → SpriteSet

Pure type-level rename; class name, file name, .meta GUID-preserved
via git mv. All references (Runtime / Editor / Tests / Samples / .asset
class identifier) updated to SpriteSet. No behavior change."
```

---

### Task 2: UI 重命名 IconResolver → SpriteResolver + HotReload 内嵌成员 + 新增 ResolveSprite(TDD)

**Files:**
- Modify: `Runtime/Application/UI.cs`(rename 字段 + HotReload 内嵌类成员 + 新方法)
- Modify: `Runtime/Controls/Icon.cs`(从 `UI.IconResolver` 改为 `UI.SpriteResolver`,错误消息 Task 5 再细化,本 task 只改 API 引用)
- Modify: `Runtime/Application/IconResolverHelpers.cs`(setter 调用点)
- Modify: `Runtime/Application/AddressableIconResolverHelper.cs`(setter + Rebuilder 调用点)
- Create: `Tests/EditMode/Application/ResolveSpriteTests.cs`
- Create: `Tests/EditMode/Application/ResolveSpriteTests.cs.meta`(Unity 会自动生成,但 Test 框架 fixture 需要包含 InternalsVisibleTo)

**注意:** `Tests/EditMode/Application/IconResolverTests.cs` 现有测试断言 `UI.IconResolver` 行为(被设置 / 被调用),Task 2 同步把这些测试改为读 `UI.SpriteResolver`,测试文件本身**不重命名**(Task 10 才改文件名)。

- [ ] **Step 1: 写 UI.ResolveSprite 的红测试**

Create `Tests/EditMode/Application/ResolveSpriteTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.EditMode.Application
{
    [TestFixture]
    public class ResolveSpriteTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();
        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void ResolveSprite_with_null_returns_null()
        {
            Assert.IsNull(UI.ResolveSprite(null));
        }

        [Test]
        public void ResolveSprite_with_empty_string_returns_null()
        {
            Assert.IsNull(UI.ResolveSprite(""));
        }

        [Test]
        public void ResolveSprite_with_colon_routes_to_SpriteResolver()
        {
            var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            string capturedKey = null;
            UI.SpriteResolver = key => { capturedKey = key; return stub; };

            var actual = UI.ResolveSprite("ui:bell");

            Assert.AreSame(stub, actual);
            Assert.AreEqual("ui:bell", capturedKey);
        }

        [Test]
        public void ResolveSprite_without_colon_does_not_call_SpriteResolver()
        {
            var resolverCalled = false;
            UI.SpriteResolver = _ => { resolverCalled = true; return null; };

            UI.ResolveSprite("path/to/sprite");

            Assert.IsFalse(resolverCalled, "Bare path should fall back to Resources.Load, not call SpriteResolver");
        }

        [Test]
        public void ResolveSprite_without_colon_missing_resource_returns_null_silently()
        {
            // No LogAssert.Expect — bare path returning null should NOT log.
            var actual = UI.ResolveSprite("does/not/exist/sprite");
            Assert.IsNull(actual);
            // LogAssert.NoUnexpectedReceived runs implicitly at TearDown via UnityTest
            // framework; if Resources.Load null path logs, this test fails.
        }

        [Test]
        public void ResolveSprite_with_colon_and_null_resolver_logs_error_and_returns_null()
        {
            UI.SpriteResolver = null;
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));

            var actual = UI.ResolveSprite("ui:bell");

            Assert.IsNull(actual);
        }

        [Test]
        public void ResolveSprite_with_colon_and_resolver_returns_null_logs_error()
        {
            UI.SpriteResolver = _ => null;
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("resolver returned null"));

            var actual = UI.ResolveSprite("ui:missing");

            Assert.IsNull(actual);
        }
    }
}
```

- [ ] **Step 2: 跑测试,验证 fail(红)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ResolveSpriteTests")
```

预期:**编译失败**,因为 `UI.ResolveSprite` 和 `UI.SpriteResolver` 都还不存在。这就是 TDD 的 "red" —— 编译失败也是测试失败的一种。

- [ ] **Step 3: rename `UI.IconResolver` → `UI.SpriteResolver` + 新增 `UI.ResolveSprite`**

Edit `Runtime/Application/UI.cs`(找 `public static Func<string, Sprite> IconResolver` 那一行,大约 line 17):

```csharp
public static System.Func<string, UnityEngine.Sprite> SpriteResolver { get; set; }

public static UnityEngine.Sprite ResolveSprite(string value)
{
    if (string.IsNullOrEmpty(value)) return null;

    if (value.IndexOf(':') >= 0)
    {
        if (SpriteResolver == null)
        {
            UnityEngine.Debug.LogError(
                $"sprite '{value}': UI.SpriteResolver is not registered. " +
                $"Call SpriteResolverHelpers.UseSpriteSetResolver(spriteSets) " +
                $"before opening Screens that reference sprite='ns:name'.");
            return null;
        }
        var sprite = SpriteResolver(value);
        if (sprite == null)
            UnityEngine.Debug.LogError(
                $"sprite '{value}': resolver returned null. " +
                $"Check the sprite name spelling, or run " +
                $"Tools → PromptUGUI → Sprite → Sync Atlases (All Sets) " +
                $"to include it in the SpriteSet's atlas.");
        return sprite;
    }

    return UnityEngine.Resources.Load<UnityEngine.Sprite>(value);
}
```

同文件内,把所有其他出现的 `IconResolver` 改为 `SpriteResolver`,包括:
- `ResetForTests()` 里 `IconResolver = null;` → `SpriteResolver = null;`
- `HotReload` 内嵌类:`public static Action IconResolverRebuilder` → `public static Action SpriteResolverRebuilder`
- `HotReload` 内嵌类:`public static void NotifyIconAssetsChanged()` → `public static void NotifySpriteAssetsChanged()`(方法体里调用 `IconResolverRebuilder?.Invoke()` → `SpriteResolverRebuilder?.Invoke()`)
- 任何 XML doc / 注释 mention 改为新名

- [ ] **Step 4: 更新 Icon.cs / Helpers / AddressableHelper 内部对 `UI.IconResolver` 的引用**

```bash
grep -rn "UI\.IconResolver\|HotReload\.IconResolverRebuilder\|HotReload\.NotifyIconAssetsChanged" Runtime/ Editor/ Tests/ Samples~/ 2>/dev/null
```

逐处 Edit 改名(本 task 内只动 `UI.IconResolver` / `HotReload.IconResolverRebuilder` / `HotReload.NotifyIconAssetsChanged` 这三个引用点;helper 类名 / 方法名留到 Task 3-4)。

- `Runtime/Controls/Icon.cs:27, 36, 39`(`UI.IconResolver == null`、`UI.IconResolver(value)`)
- `Runtime/Application/IconResolverHelpers.cs:15, 19, 29, 33`(`UI.IconResolver = ...` + `UI.HotReload.IconResolverRebuilder = Rebuild;`)
- `Runtime/Application/AddressableIconResolverHelper.cs`(同上)
- `Editor/IconAtlasMenu.cs:20, 36`(`UI.HotReload.NotifyIconAssetsChanged()`)
- `Editor/IconAtlasAutoSync.cs:46`(`UI.HotReload.NotifyIconAssetsChanged()`)
- 测试文件(暂保留 Icon* 文件名,只改内部引用)

- [ ] **Step 5: 更新现有 `IconResolverTests.cs` 测试断言**

Edit `Tests/EditMode/Application/IconResolverTests.cs`:把所有 `UI.IconResolver` 引用改为 `UI.SpriteResolver`(文件本身留到 Task 10 才 rename)。同样 `IconHotReloadTests.cs` 改 `IconResolverRebuilder` → `SpriteResolverRebuilder`、`NotifyIconAssetsChanged` → `NotifySpriteAssetsChanged`。

- [ ] **Step 6: refresh + 跑 ResolveSpriteTests(应该 pass)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ResolveSpriteTests")
```

预期:无编译 error,`ResolveSpriteTests` 全 pass(7 个测试)。

- [ ] **Step 7: 跑全套 EditMode 测试 verify nothing broken**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

预期:全 pass。如果 `IconResolverTests` 里有引用 `UI.IconResolver` 没改干净,这里会报错。

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: rename UI.IconResolver → UI.SpriteResolver + add UI.ResolveSprite

Rename the Func<string, Sprite> resolver field and HotReload nested
members (IconResolverRebuilder → SpriteResolverRebuilder,
NotifyIconAssetsChanged → NotifySpriteAssetsChanged). New UI.ResolveSprite
helper implements dual-syntax: 'ns:name' → SpriteResolver, bare path →
Resources.Load. Helper classes (IconResolverHelpers, AddressableIconResolverHelper)
still have old names; renamed in subsequent commits."
```

---

### Task 3: 重命名 IconResolverHelpers → SpriteResolverHelpers + 方法 rename

**Files:**
- Rename: `Runtime/Application/IconResolverHelpers.cs` → `Runtime/Application/SpriteResolverHelpers.cs`(+ `.meta`)
- Modify (调用点): `Samples~/MainMenu/MainMenuRunner.cs`, 各 SKILL.md(暂留,Task 13 统一改), `README.md`(暂留,Task 12)
- Modify (类型内部 + 方法名):`Runtime/Application/AddressableIconResolverHelper.cs`(它声明的是 `public static partial class IconResolverHelpers`,需要同步改 partial 类名)
- Modify (测试调用点):`Tests/EditMode/Application/IconResolverTests.cs`, `Tests/EditMode/Application/IconHotReloadTests.cs`

**重命名映射(本 task 范围内):**
- 类:`IconResolverHelpers` → `SpriteResolverHelpers`
- 方法:`UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets")` → `UseSpriteSetResolver(string resourcesSubpath = "SpriteSets")`
- 方法:`UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets)` → `UseSpriteSetResolver(IEnumerable<SpriteSet> sets)`(IconSet 类型在 Task 1 已改成 SpriteSet)
- 默认参数值:`"IconSets"` → `"SpriteSets"`

- [ ] **Step 1: 移动文件 + .meta**

```bash
git mv Runtime/Application/IconResolverHelpers.cs Runtime/Application/SpriteResolverHelpers.cs
git mv Runtime/Application/IconResolverHelpers.cs.meta Runtime/Application/SpriteResolverHelpers.cs.meta
```

- [ ] **Step 2: 改类名 + 方法名 + 默认值**

Edit `Runtime/Application/SpriteResolverHelpers.cs`:
- `public static partial class IconResolverHelpers` → `public static partial class SpriteResolverHelpers`
- 两个 `UseSpriteAtlasIconResolver` → `UseSpriteSetResolver`
- 默认参数 `string resourcesSubpath = "IconSets"` → `string resourcesSubpath = "SpriteSets"`
- 注释、XML doc、warning 消息里的 `IconSet` 字面(`"Duplicate IconSet name"` 等)→ `SpriteSet`

- [ ] **Step 3: 改 AddressableIconResolverHelper partial 类名**

Edit `Runtime/Application/AddressableIconResolverHelper.cs`(文件名 Task 4 才改):
- `public static partial class IconResolverHelpers` → `public static partial class SpriteResolverHelpers`(同 partial 类名)

否则两个 partial 不再聚合,编译报错。

- [ ] **Step 4: 改所有调用点**

```bash
grep -rn "IconResolverHelpers\|UseSpriteAtlasIconResolver" Runtime/ Editor/ Tests/ Samples~/ 2>/dev/null
```

逐处 Edit:
- `Samples~/MainMenu/MainMenuRunner.cs:25`:`IconResolverHelpers.UseSpriteAtlasIconResolver(iconSets)` → `SpriteResolverHelpers.UseSpriteSetResolver(iconSets)`(变量名 `iconSets` 可一并改 `spriteSets`,看个人偏好)
- `Tests/EditMode/Application/IconResolverTests.cs`:所有调用点
- `Tests/EditMode/Application/IconHotReloadTests.cs`:所有调用点
- 测试里 `"IconSets"` 字符串常量(若有,作为 Resources 子路径传入 helper)改 `"SpriteSets"`,但如果测试只是用任意 path,保留原值也行——具体看测试逻辑

`README.md` 和 SKILL.md 留到 Task 12-13。

- [ ] **Step 5: refresh + read_console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

预期:无 error。

- [ ] **Step 6: 跑测试 verify**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

预期:全 pass。注意 `IconResolverTests` / `IconHotReloadTests` 文件还没改名(留到 Task 10),但内部断言已经针对新 API,应该 pass。

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: rename IconResolverHelpers → SpriteResolverHelpers

UseSpriteAtlasIconResolver → UseSpriteSetResolver, default
'IconSets' subpath → 'SpriteSets'. AddressableIconResolverHelper.cs
(file rename in next commit) updated to share the new partial class
name. All call sites in Samples / Tests updated."
```

---

### Task 4: 重命名 AddressableIconResolverHelper → AddressableSpriteResolverHelper

**Files:**
- Rename: `Runtime/Application/AddressableIconResolverHelper.cs` → `Runtime/Application/AddressableSpriteResolverHelper.cs`(+ `.meta`)
- Modify (方法名):同上文件,`UseAddressableSpriteAtlasIconResolver` → `UseAddressableSpriteSetResolver`,默认 `label = "IconSets"` → `"SpriteSets"`,内部 `_addressableIconHandle` 字段名 → `_addressableSpriteSetHandle`(可选,内部细节)
- Modify (调用点):`Tests/EditMode/Addressables/AddressableIconResolverTests.cs`

- [ ] **Step 1: 移动文件 + .meta**

```bash
git mv Runtime/Application/AddressableIconResolverHelper.cs Runtime/Application/AddressableSpriteResolverHelper.cs
git mv Runtime/Application/AddressableIconResolverHelper.cs.meta Runtime/Application/AddressableSpriteResolverHelper.cs.meta
```

- [ ] **Step 2: 改方法名 + 默认值 + 内部字段名**

Edit `Runtime/Application/AddressableSpriteResolverHelper.cs`:
- `UseAddressableSpriteAtlasIconResolver` → `UseAddressableSpriteSetResolver`(两个重载)
- 默认 `string label = "IconSets"` → `string label = "SpriteSets"`
- 内部字段 `_addressableIconHandle` → `_addressableSpriteSetHandle`(可选,但建议改以保持代码内部一致)
- `_testReleaseCount` 保留(测试观察点,跟 IconSet 无语义关联)
- 文件级 `#if PROMPTUGUI_HAS_ADDRESSABLES` 保留

- [ ] **Step 3: 改测试调用点**

Edit `Tests/EditMode/Addressables/AddressableIconResolverTests.cs`(文件本身 Task 10 才改名):
- `IconResolverHelpers.UseAddressableSpriteAtlasIconResolver` → `SpriteResolverHelpers.UseAddressableSpriteSetResolver`
- 任何 `"IconSets"` 默认 label 测试用值 → `"SpriteSets"`

- [ ] **Step 4: refresh + read_console**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

预期:无 error。

- [ ] **Step 5: 跑 Addressables 测试 assembly verify**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

预期:全 pass。注意 `PROMPTUGUI_HAS_ADDRESSABLES` 编译符号必须开,否则该 assembly 内容空跑无错过测。

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: rename AddressableIconResolverHelper → AddressableSpriteResolverHelper

UseAddressableSpriteAtlasIconResolver → UseAddressableSpriteSetResolver,
default 'IconSets' label → 'SpriteSets'. Internal field renames
mirror the new naming. Gated by PROMPTUGUI_HAS_ADDRESSABLES."
```

---

### Task 5: 更新 Icon.cs 内部 — 错误消息指向新菜单 + 新 helper 名

**Files:**
- Modify: `Runtime/Controls/Icon.cs:27-43`(错误消息)

**注意:**Task 2 已经把 `UI.IconResolver` 引用改成 `UI.SpriteResolver`。本 task 只更新两条 `Debug.LogError` 消息的字面,让 user 看到的指引指向新 helper 名 + 新菜单路径。

- [ ] **Step 1: 更新错误消息**

Edit `Runtime/Controls/Icon.cs:29-32`(第一条 Debug.LogError):

```csharp
// 旧
$"Icon '{value}': UI.IconResolver is not registered. " +
$"Call IconResolverHelpers.UseSpriteAtlasIconResolver(iconSets) " +
$"before opening Screens that contain <Icon>.");

// 新
$"Icon '{value}': UI.SpriteResolver is not registered. " +
$"Call SpriteResolverHelpers.UseSpriteSetResolver(spriteSets) " +
$"before opening Screens that contain <Icon>.");
```

Edit `Runtime/Controls/Icon.cs:38-42`(第二条):

```csharp
// 旧
$"Icon '{value}': resolver returned null. " +
$"Check the icon name spelling, or run " +
$"Tools → PromptUGUI → Sync Icon Atlases (All Sets) to " +
$"include it in the IconSet's atlas.");

// 新
$"Icon '{value}': resolver returned null. " +
$"Check the icon name spelling, or run " +
$"Tools → PromptUGUI → Sprite → Sync Atlases (All Sets) to " +
$"include it in the SpriteSet's atlas.");
```

(菜单路径 `Sprite/Sync Atlases (All Sets)` 在 Task 8 才正式生效;本 task 提前更新文案为最终值,Task 8 不再回头改 Icon.cs。)

- [ ] **Step 2: refresh + 检查**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 3: 跑 Icon 测试 sanity check**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconResolverTests")
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="IconRuntimeTests")
```

预期:全 pass。如果有测试 `LogAssert.Expect` 匹配旧错误消息(用 substring 或 regex),会失败 —— 需要更新断言里的正则。常见 substring 例:`"UseSpriteAtlasIconResolver"`、`"IconSet's atlas"` 等。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: update Icon.cs error messages to new API + menu names

Point users at SpriteResolverHelpers.UseSpriteSetResolver and the
Sprite menu group. Menu path Tools/PromptUGUI/Sprite/... becomes
effective in a later commit; messages preempt the final names."
```

---

### Task 6: 7 个内置控件 Sprite setter 改用 UI.ResolveSprite

**Files:**
- Modify: `Runtime/Controls/Image.cs:28-35`(Sprite setter)
- Modify: `Runtime/Controls/Btn.cs:96-104`
- Modify: `Runtime/Controls/Toggle.cs:108-115`(注意 Toggle 的 sprite= 写到 `_checkmark`,不是背景)
- Modify: `Runtime/Controls/Slider.cs:108-115`
- Modify: `Runtime/Controls/Dropdown.cs:195-203`
- Modify: `Runtime/Controls/ScrollList.cs:177-185`
- Modify: `Runtime/Controls/InputField.cs:171-179`

**目标 setter 形状(每个控件一致):**

```csharp
[UIAttr]
public string Sprite
{
    set
    {
        _bg.sprite = UI.ResolveSprite(value);
    }
}
```

(Image 是 `_img.sprite`,Toggle 是 `_checkmark.sprite`,其他都是 `_bg.sprite`。)

**注意:**当前每个 setter 都有 `if (string.IsNullOrEmpty(value)) { ...sprite = null; return; }` 的早 return。`UI.ResolveSprite` 内部已经处理了这种情况(null/empty 返回 null),所以 setter 可以简化为单行。但保留 `using` 引用确认 `PromptUGUI.Application.UI`(7 个控件都在 `PromptUGUI.Controls` namespace,可能需要加 `using PromptUGUI.Application;`)。

- [ ] **Step 1: 改 Image.cs**

Edit `Runtime/Controls/Image.cs:27-35`,替换整个 Sprite setter:

```csharp
[UIAttr]
public string Sprite
{
    set
    {
        _img.sprite = UI.ResolveSprite(value);
    }
}
```

如果文件顶部没 `using PromptUGUI.Application;`,加上。

- [ ] **Step 2: 改 Btn.cs**

Edit `Runtime/Controls/Btn.cs:96-104`,同上 pattern,`_img.sprite` 改为 `_bg.sprite`(Btn 字段名)。

- [ ] **Step 3: 改 Toggle.cs**

Edit `Runtime/Controls/Toggle.cs:108-115`,`_checkmark.sprite = UI.ResolveSprite(value);`。

- [ ] **Step 4: 改 Slider.cs**

Edit `Runtime/Controls/Slider.cs:107-115`,`_bg.sprite = UI.ResolveSprite(value);`。

- [ ] **Step 5: 改 Dropdown.cs**

Edit `Runtime/Controls/Dropdown.cs:195-203`,`_bg.sprite = UI.ResolveSprite(value);`。

- [ ] **Step 6: 改 ScrollList.cs**

Edit `Runtime/Controls/ScrollList.cs:177-185`,`_bg.sprite = UI.ResolveSprite(value);`。

- [ ] **Step 7: 改 InputField.cs**

Edit `Runtime/Controls/InputField.cs:171-179`,`_bg.sprite = UI.ResolveSprite(value);`。

- [ ] **Step 8: refresh + 检查**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 9: 跑 controls 测试 + PlayMode smoke**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

预期:全 pass。如果 PlayMode 测试里有用 `sprite="path/to/sprite"` 形式且依赖 `Resources.Load`,该路径仍 work(无 `:`,走 fallback);如果用 `sprite="ns:name"` 形式,需要先在 SetUp 里设 `UI.SpriteResolver = stub`,否则会报 LogError + sprite null。看具体测试做 fixture 调整。

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: route 7 built-in controls' sprite= through UI.ResolveSprite

Image, Btn, Toggle, Slider, Dropdown, ScrollList, InputField:
sprite= setter now calls UI.ResolveSprite(value), which dispatches
'ns:name' to UI.SpriteResolver and bare paths to Resources.Load.
Behavior for existing Resources-path sprite= attributes is unchanged."
```

---

### Task 7: 重命名 IconSetEditor → SpriteSetEditor

**Files:**
- Rename: `Editor/IconSetEditor.cs` → `Editor/SpriteSetEditor.cs`(+ `.meta`)
- Modify: 同文件内 `CustomEditor(typeof(IconSet))` 在 Task 1 已经 → `typeof(SpriteSet)`;本 task 只改类名 `IconSetEditor` → `SpriteSetEditor`

- [ ] **Step 1: 移动文件 + .meta**

```bash
git mv Editor/IconSetEditor.cs Editor/SpriteSetEditor.cs
git mv Editor/IconSetEditor.cs.meta Editor/SpriteSetEditor.cs.meta
```

- [ ] **Step 2: 改类名**

Edit `Editor/SpriteSetEditor.cs`:`public class IconSetEditor` → `public class SpriteSetEditor`;XML doc / 注释里 `IconSet` 字面 → `SpriteSet`。

- [ ] **Step 3: refresh + 检查**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

预期:无 error。EditorOnly assembly 应正常编译。

- [ ] **Step 4: 跑 EditorOnly 测试(若有)**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

预期:全 pass(原 inventory 显示 EditorOnly 没有 IconSetEditor 单测,所以这里主要是 sanity check)。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename IconSetEditor → SpriteSetEditor (Editor)"
```

---

### Task 8: 重命名 Editor 的 IconAtlas* → SpriteAtlas* + 菜单路径更新

**Files:**
- Rename: `Editor/IconAtlasSyncer.cs` → `Editor/SpriteAtlasSyncer.cs`(+ `.meta`)
- Rename: `Editor/IconAtlasMenu.cs` → `Editor/SpriteAtlasMenu.cs`(+ `.meta`)
- Rename: `Editor/IconAtlasAutoSync.cs` → `Editor/SpriteAtlasAutoSync.cs`(+ `.meta`)
- Rename: `Editor/IconAtlasBuildHook.cs` → `Editor/SpriteAtlasBuildHook.cs`(+ `.meta`)
- Modify: 四个文件的类名 / `[MenuItem]` 路径 / 日志字面 / EditorPrefs key
- Modify: 引用 `IconAtlasSyncer`/`IconAtlasMenu` 等类名的所有调用点
- Modify: 重命名方法 `IconAtlasSyncer.FindAllIconSets` → `SpriteAtlasSyncer.FindAllSpriteSets`

**菜单路径映射:**

| 旧 | 新 |
|---|---|
| `Tools/PromptUGUI/Icon/Sync Atlases (All Sets)` | `Tools/PromptUGUI/Sprite/Sync Atlases (All Sets)` |
| `Tools/PromptUGUI/Icon/Sync Atlases (Selected Set)` | `Tools/PromptUGUI/Sprite/Sync Atlases (Selected Set)` |
| `Tools/PromptUGUI/Icon/Auto-sync Atlases on Save` | `Tools/PromptUGUI/Sprite/Auto-sync Atlases on Save` |

**EditorPrefs key:**
- 旧:`"PromptUGUI.IconAtlas.AutoSyncOnSave"` → 新:`"PromptUGUI.SpriteAtlas.AutoSyncOnSave"`(用户的本地开关会丢失;库未上线,可接受)

**日志前缀:**
- 旧:`"[IconSync]"`、`"[PromptUGUI Icon Sync]"`、`"[PromptUGUI] No IconSet"` 等 → 新:`"[SpriteSync]"`、`"[PromptUGUI Sprite Sync]"`、`"[PromptUGUI] No SpriteSet"`

- [ ] **Step 1: 移动四个文件 + .meta**

```bash
git mv Editor/IconAtlasSyncer.cs Editor/SpriteAtlasSyncer.cs
git mv Editor/IconAtlasSyncer.cs.meta Editor/SpriteAtlasSyncer.cs.meta
git mv Editor/IconAtlasMenu.cs Editor/SpriteAtlasMenu.cs
git mv Editor/IconAtlasMenu.cs.meta Editor/SpriteAtlasMenu.cs.meta
git mv Editor/IconAtlasAutoSync.cs Editor/SpriteAtlasAutoSync.cs
git mv Editor/IconAtlasAutoSync.cs.meta Editor/SpriteAtlasAutoSync.cs.meta
git mv Editor/IconAtlasBuildHook.cs Editor/SpriteAtlasBuildHook.cs
git mv Editor/IconAtlasBuildHook.cs.meta Editor/SpriteAtlasBuildHook.cs.meta
```

- [ ] **Step 2: 改 SpriteAtlasSyncer.cs**

Edit `Editor/SpriteAtlasSyncer.cs`:
- `public static class IconAtlasSyncer` → `public static class SpriteAtlasSyncer`
- `public static IList<IconSet> FindAllIconSets()` → `public static IList<SpriteSet> FindAllSpriteSets()`(原方法名 `FindAllIconSets` 已经在 Task 1 隐式失效——IconSet 类型不存在了,但 Task 1 只改了类型,方法名 `FindAllIconSets` 还在;这里同步改方法名)
- `ProgressTitle = "PromptUGUI Icon Sync"` → `"PromptUGUI Sprite Sync"`
- 所有 `[IconSync]` 日志前缀 → `[SpriteSync]`
- 注释、XML doc 里 `IconSet` / `Icon` 字面 → `SpriteSet` / `Sprite`(注意:`<Icon>` XML tag 引用必须**保留**,因为 `<Icon>` 控件不改名)
- 警告消息里 `IconSet.alwaysInclude` → `SpriteSet.alwaysInclude`

- [ ] **Step 3: 改 SpriteAtlasMenu.cs**

Edit `Editor/SpriteAtlasMenu.cs`:
- `public static class IconAtlasMenu` → `public static class SpriteAtlasMenu`
- `[MenuItem("Tools/PromptUGUI/Icon/Sync Atlases (All Sets)")]` → `[MenuItem("Tools/PromptUGUI/Sprite/Sync Atlases (All Sets)")]`
- `[MenuItem("Tools/PromptUGUI/Icon/Sync Atlases (Selected Set)")]` → 同上 `Sprite` 替换
- `[MenuItem("Tools/PromptUGUI/Icon/Sync Atlases (Selected Set)", true)]` → 同上
- `IconAtlasSyncer.FindAllIconSets()` → `SpriteAtlasSyncer.FindAllSpriteSets()`
- `IconAtlasSyncer.SyncAll(...)` → `SpriteAtlasSyncer.SyncAll(...)`
- `Debug.Log("[PromptUGUI] No IconSet assets found")` → `"No SpriteSet assets found"`
- `Debug.Log($"[PromptUGUI] Synced {sets.Count} IconSet(s)")` → `"Synced {sets.Count} SpriteSet(s)"`
- `Debug.LogWarning("[PromptUGUI] No IconSet selected")` → `"No SpriteSet selected"`

- [ ] **Step 4: 改 SpriteAtlasAutoSync.cs**

Edit `Editor/SpriteAtlasAutoSync.cs`:
- `public sealed class IconAtlasAutoSync` → `public sealed class SpriteAtlasAutoSync`
- `const string PrefKey = "PromptUGUI.IconAtlas.AutoSyncOnSave"` → `"PromptUGUI.SpriteAtlas.AutoSyncOnSave"`
- `[MenuItem("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save")]`(两处)→ `[MenuItem("Tools/PromptUGUI/Sprite/Auto-sync Atlases on Save")]`
- `Menu.SetChecked("Tools/PromptUGUI/Icon/Auto-sync Atlases on Save", ...)` → 同上 `Sprite` 替换
- `IconAtlasSyncer.FindAllIconSets()` → `SpriteAtlasSyncer.FindAllSpriteSets()`
- `IconAtlasSyncer.SyncAll(sets)` → `SpriteAtlasSyncer.SyncAll(sets)`
- `IconSet` 局部变量类型 → 已在 Task 1 改成 `SpriteSet`

- [ ] **Step 5: 改 SpriteAtlasBuildHook.cs**

Edit `Editor/SpriteAtlasBuildHook.cs`(完整读一次确认结构,然后类名 rename + 日志字面更新):
- 类名 `IconAtlasBuildHook` → `SpriteAtlasBuildHook`
- 所有 `IconAtlasSyncer.*` 调用点 → `SpriteAtlasSyncer.*`
- 日志 `IconSet` / `Icon` 字面 → 对应 `SpriteSet` / `Sprite`

- [ ] **Step 6: 改 IconResolverTests 内对 FindAllIconSets / IconAtlasSyncer 的引用**

```bash
grep -rn "IconAtlasSyncer\|FindAllIconSets" Tests/ 2>/dev/null
```

逐处 Edit。`IconAtlasSyncerTests.cs` 文件本身留到 Task 10 才改名。

- [ ] **Step 7: refresh + 检查**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

- [ ] **Step 8: 跑测试**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

预期:全 pass。

- [ ] **Step 9: 手工 verify 菜单出现在新路径**

打开 Unity Editor 菜单 → 检查 `Tools → PromptUGUI → Sprite` 下应有三项:
- Sync Atlases (All Sets)
- Sync Atlases (Selected Set)
- Auto-sync Atlases on Save (toggle)

旧路径 `Tools → PromptUGUI → Icon` 应不存在。

(此 step 是人眼 verify,无自动化检查。如果用户跑 plan 不在 Editor 旁边,跳过。)

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: rename Editor IconAtlas* → SpriteAtlas* + menu paths

IconAtlasSyncer/Menu/AutoSync/BuildHook → SpriteAtlas* (4 files).
Menu paths Tools/PromptUGUI/Icon/* → Tools/PromptUGUI/Sprite/*.
Log prefixes [IconSync] → [SpriteSync]. EditorPrefs key for
auto-sync toggle migrated (local pref reset for users on update;
acceptable since the library has not shipped)."
```

---

### Task 9: 扩展 SpriteAtlasSyncer XML scan 到 `<* sprite="ns:name">`(TDD)

**Files:**
- Modify: `Tests/EditMode/Editor/IconAtlasSyncerTests.cs`(文件本身 Task 10 才 rename,内容此 task 加新 cases)
- Modify: `Editor/SpriteAtlasSyncer.cs:135-255`(`AnalyzeIconNode` / `CollectFromNode` / `CollectFromAttr` 的泛化重构)

**新 scan 规则(对齐 spec section 5.3):**

| 形态 | 处理 |
|---|---|
| `sprite="ns:name"` 字面 | 作为 SpriteSet ref 收集 |
| `sprite="ns:{{x}}"` 半占位 | 取 Template Param 各 arg 值,与 `ns` 拼出 ref |
| `sprite="{{x}}"` 全占位 | 取 Template Param 各 arg 值,有 `:` 的视作完整 ref |
| 多占位 / 不可解析 | logs warning,不收 |
| `sprite="ui/dialog"`(无 `:` 字面) | **不收**(Resources 路径) |

`<Icon name=>` 现有扫描逻辑保持不变。

**重构方式:**抽出一个共享 `AnalyzeAttrFlow(string value, ...)` 和 `CollectAttrRef(string value, string elementTag, string attrName, ...)`,既被 Icon 路径调用,也被 sprite 路径调用。日志消息加入 `elementTag` / `attrName` 参数以正确显示 `<Image sprite='...'>` 而不是硬编码 `<Icon name='...'>`。

- [ ] **Step 1: 写新红测试 case(append 到 IconAtlasSyncerTests)**

Edit `Tests/EditMode/Editor/IconAtlasSyncerTests.cs`(append 到现有 fixture):

```csharp
[Test]
public void ScanXmlReferences_picks_up_Image_sprite_colon_form()
{
    var xml = @"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image sprite='ui:dialog'/>
  </Screen>
</PromptUGUI>";
    var refs = ScanInlineXml(xml);
    Assert.IsTrue(refs.Contains(("ui", "dialog")));
}

[Test]
public void ScanXmlReferences_picks_up_Btn_Toggle_Slider_Dropdown_ScrollList_InputField_sprite()
{
    var xml = @"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Btn sprite='ui:btn-bg'/>
    <Toggle sprite='ui:check'/>
    <Slider sprite='ui:slider-bg'/>
    <Dropdown sprite='ui:dropdown-bg'/>
    <ScrollList sprite='ui:scroll-bg' itemTemplate='Frame'/>
    <InputField sprite='ui:input-bg'/>
  </Screen>
</PromptUGUI>";
    var refs = ScanInlineXml(xml);
    Assert.IsTrue(refs.Contains(("ui", "btn-bg")));
    Assert.IsTrue(refs.Contains(("ui", "check")));
    Assert.IsTrue(refs.Contains(("ui", "slider-bg")));
    Assert.IsTrue(refs.Contains(("ui", "dropdown-bg")));
    Assert.IsTrue(refs.Contains(("ui", "scroll-bg")));
    Assert.IsTrue(refs.Contains(("ui", "input-bg")));
}

[Test]
public void ScanXmlReferences_ignores_Image_sprite_without_colon()
{
    var xml = @"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image sprite='ui/dialog-frame'/>
    <Image sprite='ui:atlas-form'/>
  </Screen>
</PromptUGUI>";
    var refs = ScanInlineXml(xml);
    Assert.IsFalse(refs.Contains(("ui", "dialog-frame")),
        "Bare path (no colon) is a Resources.Load ref, must not be collected");
    Assert.IsTrue(refs.Contains(("ui", "atlas-form")),
        "Colon form must still be collected alongside Resources-form siblings");
}

[Test]
public void ScanXmlReferences_template_param_driven_sprite_full_placeholder()
{
    var xml = @"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Template name='Themed'>
    <Param name='bg'/>
    <Image sprite='{{bg}}'/>
  </Template>
  <Screen name='S'>
    <Themed bg='ui:dialog'/>
  </Screen>
</PromptUGUI>";
    var refs = ScanInlineXml(xml);
    Assert.IsTrue(refs.Contains(("ui", "dialog")));
}

[Test]
public void ScanXmlReferences_template_param_driven_sprite_partial_placeholder()
{
    var xml = @"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Template name='Themed'>
    <Param name='kind'/>
    <Image sprite='ui:{{kind}}'/>
  </Template>
  <Screen name='S'>
    <Themed kind='panel-bg'/>
  </Screen>
</PromptUGUI>";
    var refs = ScanInlineXml(xml);
    Assert.IsTrue(refs.Contains(("ui", "panel-bg")));
}

[Test]
public void ScanXmlReferences_unanalyzable_sprite_logs_warning()
{
    var xml = @"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Template name='X'>
    <Param name='a'/>
    <Param name='b'/>
    <Image sprite='{{a}}:{{b}}'/>
  </Template>
  <Screen name='S'>
    <X a='ui' b='bell'/>
  </Screen>
</PromptUGUI>";
    UnityEngine.TestTools.LogAssert.Expect(
        LogType.Warning, new System.Text.RegularExpressions.Regex("non-trivial substitution|cannot analyze"));
    ScanInlineXml(xml);   // we just want the warning, not the refs
}
```

`ScanInlineXml(xml)` 是测试 helper —— 把 XML 字符串落盘到临时 `.ui.xml` 文件 + 调 `SpriteAtlasSyncer.ScanXmlReferences(showProgress: false)` + 清理。原 `IconAtlasSyncerTests` 应该已有类似 helper,复用。如果没有,加一个 SetUp/TearDown + Temp file 模式(查 fixture 现有 pattern)。

- [ ] **Step 2: 跑测试,验证 fail(红)**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconAtlasSyncerTests")
```

预期:6 个新 case fail(因为 scan 还不识别 sprite=)。原有 Icon-related cases 全 pass。

- [ ] **Step 3: 重构 SpriteAtlasSyncer scan 为共享 + sprite 分支**

Edit `Editor/SpriteAtlasSyncer.cs:135-255`:

把 `AnalyzeIconNode` 改成通用的 `AnalyzeNode`:

```csharp
private static void AnalyzeNode(ElementNode node,
                            Dictionary<string, IconParamFlow> flows,
                            string path, string tplName)
{
    if (node == null) return;

    // <Icon name=...> 老路径
    if (node.Tag == "Icon" && node.Namespace == null)
    {
        if (node.Attributes.TryGetValue("name", out var v))
            TryAddFlow(v, flows, path, tplName, "Icon", "name");
        if (node.VariantOverrides.TryGetValue("name", out var list))
            foreach (var (_, vv) in list)
                TryAddFlow(vv, flows, path, tplName, "Icon", "name");
    }

    // 新:任何元素的 sprite= 属性(含 variant override)
    if (node.Attributes.TryGetValue("sprite", out var sv))
        TryAddFlow(sv, flows, path, tplName, node.Tag, "sprite");
    if (node.VariantOverrides.TryGetValue("sprite", out var spList))
        foreach (var (_, vv) in spList)
            TryAddFlow(vv, flows, path, tplName, node.Tag, "sprite");

    foreach (var c in node.Children) AnalyzeNode(c, flows, path, tplName);
}
```

`TryAddFlow` 签名加 `string elementTag`, `string attrName` 用来生成警告:

```csharp
private static void TryAddFlow(string value,
                        Dictionary<string, IconParamFlow> flows,
                        string path, string tplName,
                        string elementTag, string attrName)
{
    if (string.IsNullOrEmpty(value)) return;
    if (!value.Contains(DynamicMarker)) return;

    var m = FullPlaceholder.Match(value);
    if (m.Success)
    {
        flows[m.Groups[1].Value] = new IconParamFlow(null);
        return;
    }
    m = PartialPlaceholder.Match(value);
    if (m.Success)
    {
        flows[m.Groups[2].Value] = new IconParamFlow(m.Groups[1].Value);
        return;
    }
    Debug.LogWarning(
        $"[SpriteSync] {path}: <Template name='{tplName}'>: <{elementTag} {attrName}='{value}'> " +
        $"uses a non-trivial substitution; only `{{x}}` and `set:{{x}}` are " +
        $"statically analyzable. List candidates in SpriteSet.alwaysInclude.");
}
```

把 `CollectFromNode` 改成通用的 `CollectFromNodeAll`:

```csharp
private static void CollectFromNodeAll(ElementNode node,
                            HashSet<(string, string)> refs,
                            IReadOnlyDictionary<string, TemplateFlow> templateFlows,
                            string path)
{
    if (node == null) return;

    // <Icon name=> 老路径
    if (node.Tag == "Icon" && node.Namespace == null)
    {
        if (node.Attributes.TryGetValue("name", out var n))
            CollectFromAttr(n, refs, path, "Icon", "name");
        if (node.VariantOverrides.TryGetValue("name", out var list))
            foreach (var (_, v) in list) CollectFromAttr(v, refs, path, "Icon", "name");
    }
    else if (templateFlows.TryGetValue(node.Tag, out var tf))
    {
        // Template 调用:把 invocation arg 转成 refs
        foreach (var (paramName, flow) in tf.Flows)
        {
            if (!node.Attributes.TryGetValue(paramName, out var arg) ||
                string.IsNullOrEmpty(arg))
                continue;
            CollectFromTemplateArg(arg, flow, refs, path, node.Tag, paramName);
        }
    }

    // 新:任何元素的 sprite= 属性(literal 路径,不走 Template flow)
    if (node.Attributes.TryGetValue("sprite", out var sv))
        CollectFromAttr(sv, refs, path, node.Tag, "sprite");
    if (node.VariantOverrides.TryGetValue("sprite", out var spList))
        foreach (var (_, v) in spList)
            CollectFromAttr(v, refs, path, node.Tag, "sprite");

    foreach (var c in node.Children) CollectFromNodeAll(c, refs, templateFlows, path);
}
```

`CollectFromAttr` 签名加 `elementTag` / `attrName`:

```csharp
private static void CollectFromAttr(string value,
                            HashSet<(string, string)> refs, string path,
                            string elementTag, string attrName)
{
    if (string.IsNullOrEmpty(value)) return;
    var colon = value.IndexOf(':');
    if (colon <= 0 || colon == value.Length - 1) return;   // Resources 路径或畸形
    var ns = value.Substring(0, colon);
    var name = value.Substring(colon + 1);
    if (ns.Contains(DynamicMarker))
    {
        Debug.LogWarning(
            $"[SpriteSync] {path}: <{elementTag} {attrName}='{value}'>: " +
            $"dynamic namespace ({DynamicMarker}...) is not analyzable; skipping");
        return;
    }
    if (name.Contains(DynamicMarker))
    {
        Debug.LogWarning(
            $"[SpriteSync] {path}: <{elementTag} {attrName}='{value}'>: " +
            $"dynamic name ({DynamicMarker}...); list candidates in SpriteSet.alwaysInclude");
        return;
    }
    refs.Add((ns, name));
}
```

`CollectFromTemplateArg` 同样接收 `elementTag` / `paramName`(原来已经有了),warning 字面里 `IconSet.alwaysInclude` → `SpriteSet.alwaysInclude`,前缀 `[IconSync]` → `[SpriteSync]`(Task 8 已改部分,此 task 检查残留)。

主调用点 `ScanXmlReferences` 把 `AnalyzeIconNode` / `CollectFromNode` 调用换成 `AnalyzeNode` / `CollectFromNodeAll`。

- [ ] **Step 4: refresh + 跑测试 verify green**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="IconAtlasSyncerTests")
```

预期:6 个新 case + 全部老 Icon case 全 pass。如有 fail:
- 重读 spec section 5.3 优先级表;
- 检查 `node.Attributes.TryGetValue("sprite", ...)` 路径是否覆盖了所有元素 tag(对 `<Icon>` 也会触发 sprite=,但 `<Icon>` 没 sprite= 属性,Try 返回 false,跳过)。

- [ ] **Step 5: 跑全套 EditMode 测试 verify nothing broken**

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: extend SpriteAtlasSyncer XML scan to <* sprite='ns:name'>

The XML scan now collects sprite atlas refs from sprite= attributes
on any element (Image, Btn, Toggle, etc.), in addition to <Icon name=>.
Bare-path sprite= (Resources.Load) is ignored. Template placeholder
rules ({{x}} full, ns:{{x}} half) apply uniformly. Refactor extracts
a shared AnalyzeNode/CollectFromNodeAll path; warning messages now
include the source element tag and attribute name."
```

---

### Task 10: 重命名 4 个测试文件 Icon* → Sprite*

**Files:**
- Rename: `Tests/EditMode/Application/IconResolverTests.cs` → `Tests/EditMode/Application/SpriteResolverTests.cs`(+ `.meta`)
- Rename: `Tests/EditMode/Application/IconHotReloadTests.cs` → `Tests/EditMode/Application/SpriteHotReloadTests.cs`(+ `.meta`)
- Rename: `Tests/EditMode/Editor/IconAtlasSyncerTests.cs` → `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`(+ `.meta`)
- Rename: `Tests/EditMode/Addressables/AddressableIconResolverTests.cs` → `Tests/EditMode/Addressables/AddressableSpriteResolverTests.cs`(+ `.meta`)

**保留不改名(测试对象是 `<Icon>` XML tag / 控件,不是 resolver):**
- `Tests/PlayMode/Controls/IconRuntimeTests.cs`
- `Tests/EditMode/Parser/IconParserTests.cs`

- [ ] **Step 1: 移动四个文件 + .meta**

```bash
git mv Tests/EditMode/Application/IconResolverTests.cs Tests/EditMode/Application/SpriteResolverTests.cs
git mv Tests/EditMode/Application/IconResolverTests.cs.meta Tests/EditMode/Application/SpriteResolverTests.cs.meta
git mv Tests/EditMode/Application/IconHotReloadTests.cs Tests/EditMode/Application/SpriteHotReloadTests.cs
git mv Tests/EditMode/Application/IconHotReloadTests.cs.meta Tests/EditMode/Application/SpriteHotReloadTests.cs.meta
git mv Tests/EditMode/Editor/IconAtlasSyncerTests.cs Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs
git mv Tests/EditMode/Editor/IconAtlasSyncerTests.cs.meta Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs.meta
git mv Tests/EditMode/Addressables/AddressableIconResolverTests.cs Tests/EditMode/Addressables/AddressableSpriteResolverTests.cs
git mv Tests/EditMode/Addressables/AddressableIconResolverTests.cs.meta Tests/EditMode/Addressables/AddressableSpriteResolverTests.cs.meta
```

- [ ] **Step 2: 更新四个文件内的类名**

每个文件内 fixture 类名跟文件名一致(NUnit 不强制,但项目惯例):
- `IconResolverTests` → `SpriteResolverTests`
- `IconHotReloadTests` → `SpriteHotReloadTests`
- `IconAtlasSyncerTests` → `SpriteAtlasSyncerTests`
- `AddressableIconResolverTests` → `AddressableSpriteResolverTests`

测试方法名内含 `Icon` 子串的(如 `IconResolver_returns_sprite_for_known_key`)同步改 `SpriteResolver_...` —— 如果该方法**特指 `<Icon>` 控件**(eg. 测试 `<Icon>` tag 的解析),保留原名;如果指 resolver 本身,改名。判定标准:看测试调用的 API。

- [ ] **Step 3: refresh + 跑测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

预期:全 pass。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: rename Icon* test files → Sprite*

Renames the 4 fixtures that test the resolver mechanism (not the
<Icon> control itself). IconRuntimeTests.cs and IconParserTests.cs
keep their names — they test the <Icon> tag, which is unchanged."
```

---

### Task 11: 更新 Samples — MainMenu

**Files:**
- Modify: `Samples~/MainMenu/MainMenuRunner.cs:13-25`(注释 + 字段名 + helper 调用)
- Rename: `Samples~/MainMenu/SolarIconSet.asset` → `Samples~/MainMenu/SolarSpriteSet.asset`(+ `.meta`)
- Modify (asset 内 m_Name): `Samples~/MainMenu/SolarSpriteSet.asset` 内 `m_Name: SolarIconSet` → `m_Name: SolarSpriteSet`

**注意:**`m_EditorClassIdentifier` 字段在 Task 1 已经改成 `SpriteSet`。此 task 只处理文件名 / 资产名层级。

- [ ] **Step 1: 移动 .asset + .meta**

```bash
git mv Samples~/MainMenu/SolarIconSet.asset Samples~/MainMenu/SolarSpriteSet.asset
git mv Samples~/MainMenu/SolarIconSet.asset.meta Samples~/MainMenu/SolarSpriteSet.asset.meta
```

- [ ] **Step 2: 改 .asset 内的 m_Name**

Edit `Samples~/MainMenu/SolarSpriteSet.asset`:

```yaml
m_Name: SolarIconSet
```
改为
```yaml
m_Name: SolarSpriteSet
```

(同文件内 `setName: solar` 不动 —— 这是 SpriteSet 的 `SetName` 字段值,作为 `solar:icon-name` 的 namespace 前缀,与 asset 名独立。)

- [ ] **Step 3: 改 MainMenuRunner.cs 注释 + 字段名 + helper 调用**

Edit `Samples~/MainMenu/MainMenuRunner.cs:13-25`:
- 注释里 `SolarIconSet.asset` → `SolarSpriteSet.asset`
- 注释里 `Icon Sets 字段` → `Sprite Sets 字段`
- 注释里 `SolarIconSet 的 SpriteAtlas` → `SolarSpriteSet 的 SpriteAtlas`
- 字段名 `[SerializeField] SpriteSet[] iconSets;` → `[SerializeField] SpriteSet[] spriteSets;`(注:`SpriteSet` 类型在 Task 1 已改)
- 调用 `SpriteResolverHelpers.UseSpriteSetResolver(iconSets);` → `SpriteResolverHelpers.UseSpriteSetResolver(spriteSets);`(Task 3 已经把 helper 名改了,此处只改变量名)

**Unity Inspector 上的字段引用**:如果 MainMenuRunner 的 `iconSets` 字段已经在场景里被赋值,改字段名会丢失序列化(Unity 用字段名作 key)。Samples 是分发资源,场景里可能预设了引用 —— 如果丢失,user 重新打开 sample 时要手动重新拖 SpriteSet 资产。可接受(库未上线,sample 文档里写明)。

如果担心 sample 用户体验,可加 `[FormerlySerializedAs("iconSets")]` 字段属性保留引用 —— 但 spec SR-D12 明确不做向后兼容,**不加**。

- [ ] **Step 4: refresh + 检查**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```

Samples~ 文件夹的 `~` 后缀让 Unity 不导入它,所以 refresh 不会重新编译 sample 代码;但 sample 中如果有 .asset.meta,Unity 重新读取时 m_Name / m_EditorClassIdentifier 字段不一致也只是 warning,不阻塞。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "samples: rename SolarIconSet.asset → SolarSpriteSet.asset

Sample asset filename + m_Name updated to match the new SpriteSet
class. MainMenuRunner.cs field renamed iconSets → spriteSets;
existing scene serialization referencing the old name will need
re-assignment in the Inspector (acceptable; sample, not shipped)."
```

---

### Task 12: 更新 README.md

**Files:**
- Modify: `README.md`

- [ ] **Step 1: grep 找 README 内 Icon-related 引用**

```bash
grep -n "IconSet\|IconResolver\|UseSpriteAtlasIconResolver\|UseAddressableSpriteAtlasIconResolver\|IconResolverHelpers" README.md
```

- [ ] **Step 2: 逐处 Edit 改字面**

主要替换:
- `IconSet` → `SpriteSet`
- `IconResolverHelpers` → `SpriteResolverHelpers`
- `UseSpriteAtlasIconResolver` → `UseSpriteSetResolver`
- `UseAddressableSpriteAtlasIconResolver` → `UseAddressableSpriteSetResolver`
- `"IconSets"` 默认 label → `"SpriteSets"`
- `Icon Sets` UI 字面 → `Sprite Sets`

`<Icon>` XML tag 文字(eg. "use `<Icon>` for icons")**保留**。

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: update README for SpriteSet rename"
```

---

### Task 13: 更新 SKILL.md 三份

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `.claude/skills/using-promptugui-addressables/SKILL.md`

- [ ] **Step 1: 改 authoring-promptugui-xml/SKILL.md**

`grep -n "IconSet\|IconResolver" .claude/skills/authoring-promptugui-xml/SKILL.md` 找到 ~8 处。

逐处替换:
- "IconSet" → "SpriteSet"
- 菜单路径 `Tools → PromptUGUI → Icon → Sync Atlases` → `Tools → PromptUGUI → Sprite → Sync Atlases`(全文搜 "Sync Icon" / "Icon → Sync" / "Icon/Sync" 类似变体)
- 在 `<Image sprite=>` / `<Btn sprite=>` 等内置 primitives 表格下的描述 **新增双语法说明**(spec section 5.6 列出的改动):

  在 "Built-in primitives" 段落 line ~206(`内置 <Image> / <Btn> / <Toggle> 的 sprite= 走 Resources.Load<Sprite>(value)...` 那一行)替换为:

  ```markdown
  - 内置 `<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>` 的 `sprite=` 走 `UI.ResolveSprite(value)`:
    - 含 `:` 的值(`sprite="ui:dialog"`)走 `UI.SpriteResolver` → SpriteSet/atlas 通道
    - 无 `:` 的值(`sprite="ui/dialog"`)走 `Resources.Load<Sprite>(value)`
  - `<Icon>` 仍强制 `ns:name` 形式,只走 SpriteResolver。
  ```

- Common mistakes 表(全文末)新增一行:
  ```markdown
  | `<Image sprite='ns:name'>` 显示白图,控制台报 "SpriteResolver is not registered" | 启动期未调 `SpriteResolverHelpers.UseSpriteSetResolver(spriteSets)` | 在 `UI.LoadDocumentAsync` 之前调一次 |
  ```
- "Discovering available icons" 段落里 `PromptUGUI.Application.IconSet` → `PromptUGUI.Application.SpriteSet`,`grep -m1 "^  setName:"` 等命令保留(setName 字段未改)。
- 该 SKILL 末尾的 "Icon" 段落保留全部(`<Icon>` tag 文档不变)。

- [ ] **Step 2: 改 scripting-promptugui-csharp/SKILL.md**

`grep -n "IconResolver\|IconSet\|UseSpriteAtlasIconResolver" .claude/skills/scripting-promptugui-csharp/SKILL.md` 找到 ~9 处。

逐处替换 helper / API 名(参考 Task 12 替换表)。

**新增 "sprite= 双语法" 小节**(放在现有 "Icon system setup" 段落附近,作为新独立段落):

```markdown
### `sprite=` 双语法分流

内置控件(`<Image>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>`)的 `sprite=` 属性统一走 `UI.ResolveSprite(string)`:

- 含 `:` 的值 → `UI.SpriteResolver` → SpriteSet/atlas 通道(`<Image sprite="ui:dialog">`)
- 无 `:` 的值 → `Resources.Load<Sprite>(value)`(`<Image sprite="ui/dialog">`)

自定义控件 subclass 想保持一致行为,直接调 `UI.ResolveSprite`:

```csharp
public sealed class MyImage : PromptUGUI.Controls.Control
{
    private UnityEngine.UI.Image _img;
    public override void OnAttached()
        => _img = GameObject.GetComponent<UnityEngine.UI.Image>()
                  ?? GameObject.AddComponent<UnityEngine.UI.Image>();

    [UIAttr]
    public string Sprite
    {
        set => _img.sprite = UI.ResolveSprite(value);
    }
}
\```

错误处理:`:` 形式且 SpriteResolver 未注册 → `Debug.LogError`,sprite 设 null;Resources 形式找不到 → null,**不报错**(沿用现 `Resources.Load` 静默行为)。
```

(注意上面代码 fence 用三反引号;在 SKILL.md 内嵌套时用占位 \``` 然后实际写时用三反引号。)

- [ ] **Step 3: 改 using-promptugui-addressables/SKILL.md**

`grep -n "IconResolver\|IconSet\|UseAddressableSpriteAtlasIconResolver" .claude/skills/using-promptugui-addressables/SKILL.md` 找到 ~17 处。

逐处替换:
- `IconResolverHelpers.UseAddressableSpriteAtlasIconResolver` → `SpriteResolverHelpers.UseAddressableSpriteSetResolver`(全部 5 处类似调用)
- `IconSet` → `SpriteSet`(包括 description 字段内的引用)
- 默认 label `"IconSets"` → `"SpriteSets"`
- 注释里 `<Icon>` 引用 **保留**(`<Icon>` tag 不变)
- "Sprite was captured in a user field across a `UseAddressable...Resolver` call" 这类描述里 `UseAddressable...Resolver` → 调整成新 helper 名
- frontmatter `description:` 字段更新:`UseAddressableSpriteAtlasIconResolver` → `UseAddressableSpriteSetResolver`

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/
git commit -m "docs: update SKILL.md (xml / csharp / addressables) for SpriteSet rename

- authoring-promptugui-xml: built-in primitives sprite= dual-syntax,
  Common mistakes row for SpriteResolver-not-registered, menu path
  rename, IconSet→SpriteSet
- scripting-promptugui-csharp: new 'sprite= dual-syntax' section
  showing UI.ResolveSprite + subclass authoring pattern; helper API
  renames
- using-promptugui-addressables: UseAddressableSpriteSetResolver
  + SpriteSets label default; <Icon> mentions kept intact
"
```

---

### Task 14: Final verification — lint + 全套测试

- [ ] **Step 1: Lint pass**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style PromptUGUI.Lint.slnx
dotnet format analyzers PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

预期:`--verify-no-changes` 返回 0(无 diff)。如有 diff,说明前面的 task commit 漏了 whitespace / style 修正;查看 diff,补 commit。

**禁忌:不要用 `dotnet format analyzers --severity info`**(CLAUDE.md 明确)。`info` 级 fixer 会改 Substring → AsSpan 等等会爆编译的修复。

- [ ] **Step 2: 全套 Unity MCP 测试**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
mcp__UnityMCP__read_console(action="get", types=["error"])
```

预期:四个 assembly 全 pass,console 无 error。Addressables assembly 仅在 `PROMPTUGUI_HAS_ADDRESSABLES` 编译符号开时有测;若未开,该 run 显示 0 tests 是正常的。

- [ ] **Step 3: grep 残留检查**

```bash
grep -rn "IconSet\|IconResolver\|IconAtlasSyncer\|IconAtlasMenu\|IconAtlasAutoSync\|IconAtlasBuildHook\|UseSpriteAtlasIconResolver\|UseAddressableSpriteAtlasIconResolver" Runtime/ Editor/ Tests/ Samples~/ .claude/skills/ README.md 2>/dev/null
```

预期输出**应只包含**:
- `Tests/PlayMode/Controls/IconRuntimeTests.cs`(测试 `<Icon>` 控件)
- `Tests/EditMode/Parser/IconParserTests.cs`(测试 `<Icon>` 解析)
- 任何 `<Icon>` XML tag 字面 + 相关注释/说明

不应包含任何 `IconSet` / `IconResolver` / `IconAtlas*` / `UseSpriteAtlasIconResolver` 字面。如有,定位并清理。

- [ ] **Step 4: Final commit(若 Step 3 有清理)+ Push**

```bash
# 若 step 3 有清理:
git add -A
git commit -m "chore: clean up residual Icon* references after rename"

# 否则直接 push:
git push -u origin feat/spriteset-rename
```

(Push 是可选的——用户可能想先在本地 review 整个分支再 push。问用户。)

---

## Self-Review Checklist

实施完成前对本 plan 做最后一次自查:

**1. Spec 覆盖**:

| Spec 决策 | Plan 覆盖在 task |
|---|---|
| SR-D1(rename 映射) | Tasks 1, 2, 3, 4, 7, 8 |
| SR-D2(双语法分流) | Task 2 (`UI.ResolveSprite` 实现) + Task 6 (7 控件接入) |
| SR-D3(`UI.ResolveSprite` 助手) | Task 2 |
| SR-D4(`<Icon>` 不接 fallback) | 隐式覆盖:Task 5 `Icon.cs` 仍调 `UI.SpriteResolver` 直连,不走 `ResolveSprite` |
| SR-D5(helper 方法 rename) | Tasks 3, 4 |
| SR-D6(默认 label `SpriteSets`) | Tasks 3, 4 |
| SR-D7(菜单路径) | Task 8 |
| SR-D8(Syncer scan 扩展) | Task 9 |
| SR-D9(`pugui.png` 不变) | 不覆盖(留待 ProceduralBuilders.cs 原样,确实无 task 触碰它,符合预期) |
| SR-D10(错误处理) | Task 2 实现里的 LogError 分支 |
| SR-D11(Icon.cs 直连) | Task 5(只改文案,不改调用点形式) |
| SR-D12(不做向后兼容) | 整个 plan 不写 `[Obsolete]` / `[MovedFrom]` |
| SR-D13(SKILL.md 更新) | Task 13 |

**2. 占位符 / 模糊指令检查:** Plan 内未发现 "TBD" / "适当错误处理" / "类似 Task N" 等占位;每个含代码的 step 都给出了完整代码或精确 file:line + 替换前后字符串。

**3. 类型 / 方法签名一致性:** `UI.ResolveSprite(string)` 在 Task 2 引入,后续 Task 6 + Task 13(SKILL 示例)调用形态一致;`UseSpriteSetResolver(IEnumerable<SpriteSet>)` 在 Task 3 引入,Task 11 sample 调用形态一致。

**4. 漏项:** README.md(Task 12)和 PROMPTUGUI 描述文件中可能也有引用,Task 14 Step 3 残留 grep 兜底。

---

## Implementation Notes

- **Phase 1-2(Task 1-6):**Runtime API + 控件接入。预估 1-2h 实操。需要在每次 Unity refresh 间盯编译错误。
- **Phase 3(Task 7-9):**Editor 改名 + scan 扩展。Task 9 TDD,新加 6 个 test case + 重构 ~120 行扫描代码。预估 1h。
- **Phase 4(Task 10):**测试文件 rename。机械,15 min。
- **Phase 5(Task 11-13):**Samples + 文档。30 min。
- **Phase 6(Task 14):**最终 verify。10 min。

总计 ~3-4h 实操。每个 Task 一个 commit,共 14 commits,便于 review / bisect。

---
