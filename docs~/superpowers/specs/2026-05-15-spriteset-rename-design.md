# SpriteSet 重命名 + 控件 sprite= 统一走 SpriteResolver 设计

**日期**:2026-05-15
**状态**:设计阶段(待 review,未进入实施)
**作用域**:
1. 把 `IconSet` ScriptableObject、`IconResolver` 入口、`IconResolverHelpers` / `AddressableIconResolverHelper` 全部改名为 SpriteSet / SpriteResolver / SpriteResolverHelpers / AddressableSpriteResolverHelper。`<Icon>` XML tag **不改名**(它仍是 atlas-only 的 opinionated UI primitive)。
2. 7 个内置控件(Image、Btn、Toggle、Slider、Dropdown、ScrollList、InputField)的 `sprite=` 属性新增双语法分流:`"ns:name"` 走 `UI.SpriteResolver`,无冒号的字符串继续走 `Resources.Load<Sprite>`。
3. Editor `SpriteAtlasSyncer`(原 `IconAtlasSyncer`)的 XML scan 范围扩展到这 7 个控件的 `sprite=` 属性,以维持 package-time pruning。
4. 内置 `pugui.png` 走 `Resources.LoadAll` 的逻辑不变。
5. 由于库未上线,**不做向后兼容**(无 `[Obsolete]` 类型别名、无 `[MovedFrom]`、不保留旧菜单路径)。

**依赖**:[`2026-05-08-icon-assets-design.md`](2026-05-08-icon-assets-design.md)(IconSet / IconResolver 原始形状)+ [`2026-05-12-addressable-icon-resolver-design.md`](2026-05-12-addressable-icon-resolver-design.md)(Addressable IconResolver 形状)。这两份是被重命名的对象。

---

## 1. 背景

PromptUGUI 当前对 sprite 有两条不同的解析路径:

| 控件 | 路径 | 能力 |
|---|---|---|
| `<Icon name="ns:name">` | `UI.IconResolver` (`Func<string, Sprite>`),底层 IconSet → SpriteAtlas,by-name 查找 | atlas 自动打包、Addressables、package-time pruning |
| `<Image sprite="ui/dialog">` 等 6 个控件 | `Resources.Load<Sprite>` | 单 sprite PNG 直读;多 sprite PNG **不支持**(LoadAll 不暴露);atlas / Addressables 走不通 |

实际工程中 dialog 边框、面板背景、按钮 chrome 这类 9-slice sprite 经常希望走 atlas(避免 draw call 散乱、便于打包剪枝),但目前只有 `<Icon>` 能进 atlas 通道。

观察是 IconSet 本身**已经是项目级 sprite 注册表**,只是命名 "Icon" 让作者把它当成只能装小图标。最干净的方向是:

1. 改名 IconSet → SpriteSet,使其概念上覆盖任何 sprite。
2. 让所有控件的 `sprite=` 都能走这条通道(`sprite="ns:name"` 形式)。
3. 保留 `Resources.Load` 作为无 `:` 时的回退,服务一次性 sprite / 快速原型场景。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| SR-D1 | 重命名映射 | `IconSet`→`SpriteSet`, `UI.IconResolver`→`UI.SpriteResolver`, `IconResolverHelpers`→`SpriteResolverHelpers`, `AddressableIconResolverHelper`→`AddressableSpriteResolverHelper`, `IconAtlasSyncer`→`SpriteAtlasSyncer`, `IconAtlasMenu`→`SpriteAtlasMenu`, `IconAtlasAutoSync`→`SpriteAtlasAutoSync`, `IconAtlasBuildHook`→`SpriteAtlasBuildHook`, `IconSetEditor`→`SpriteSetEditor`, `UI.HotReload.IconResolverRebuilder`→`SpriteResolverRebuilder`, `UI.HotReload.NotifyIconAssetsChanged`→`NotifySpriteAssetsChanged` | `<Icon>` XML tag 不在改名表里——它是 UI 控件,有 `size="native"` / `preserveAspect` 等 icon 专属语义 |
| SR-D2 | 控件 sprite= 双语法 | 字符串含 `:` 走 `UI.ResolveSprite` → `UI.SpriteResolver`;无 `:` 走 `Resources.Load<Sprite>` | `:` 在 Resources 路径里不合法(虚拟路径,正斜杠分隔),零歧义;`ns:name` 是 SpriteSet 既有 key 格式 |
| SR-D3 | dual-syntax 入口 | 新增 `public static Sprite UI.ResolveSprite(string value)` 助手 | 7 个控件统一收敛到这一调用;subclass 走同一入口,不重复实现 detection |
| SR-D4 | `<Icon name=>` 是否支持回退 | 否,仍只接受 `ns:name` 形式 | Icon 走 package-time pruning,XML scan 必须能识别 sprite 引用形状;允许任意 Resources 路径会破坏剪枝保证 |
| SR-D5 | helper 方法重命名 | `UseSpriteAtlasIconResolver`→`UseSpriteSetResolver`,`UseAddressableSpriteAtlasIconResolver`→`UseAddressableSpriteSetResolver` | 与 `UseResourcesResolver`/`UseAddressableResolver`(SourceResolver 侧)的 `Use<Strategy>Resolver` 命名风格对齐;"SpriteAtlas" 在新名字里隐含,不必出现 |
| SR-D6 | helper 默认 label / Resources 子路径 | `"IconSets"`→`"SpriteSets"` | 与类名一致 |
| SR-D7 | 菜单路径 | `Tools → PromptUGUI → Icon → Sync Atlases (All/Selected Sets)` → `Tools → PromptUGUI → Sprite → Sync Atlases (All/Selected Sets)` | 库未上线,无用户习惯要保 |
| SR-D8 | Syncer XML scan 范围 | 扩到 7 个控件的 `sprite=` 属性(原仅扫 `<Icon name=>`) | 不扩则 `<Image sprite="ns:name">` 等的 sprite 引用会被剪枝,silent breakage |
| SR-D9 | 内置 `pugui.png` 解析路径 | 不变,继续走 `ProceduralBuilders.GetDefaultSprite` → `Resources.LoadAll<Sprite>("PromptUGUI/Defaults/pugui")` | 用户已明确要求保持;此为内部细节,不需要 SpriteSet 化 |
| SR-D10 | `UI.ResolveSprite` 错误处理 | `null`/空 → 返回 null 静默;含 `:` 且 SpriteResolver 未设 → `Debug.LogError`,返回 null;含 `:` 且 resolver 返回 null → `Debug.LogError`,返回 null;无 `:` → `Resources.Load`,失败返回 null **静默** | 显式入 atlas 通道的报错信息有用;Resources 路径维持现有 Image/Btn 的静默行为 |
| SR-D11 | `<Icon>` Control 内部 | 仍直接调 `UI.SpriteResolver(value)`(不走 ResolveSprite),沿用既有错误消息(指向 SpriteSet / Sync 菜单) | Icon 强制 `ns:name`,走 ResolveSprite 会引入无意义的 `:` 检查;错误消息也需要 atlas-specific 提示 |
| SR-D12 | 向后兼容 | 不做。无 `[Obsolete]` 别名、无 `[MovedFrom]`、不保留旧菜单路径、不保留旧 helper 方法名 | 库未上线,用户已确认 |
| SR-D13 | SKILL.md 更新 | xml / csharp / addressables 三份都改字面,csharp 里强调 `<Image sprite=>` 双语法和 `UI.ResolveSprite` subclass 入口 | CLAUDE.md 触发条件:public C# API 改名 + 新 API + 行为扩展 |

---

## 3. 完整使用示例

启动期(Resources 路径):

```csharp
SpriteResolverHelpers.UseSpriteSetResolver();   // 默认 resourcesSubpath="SpriteSets"
UI.UseResourcesResolver("UI");
await UI.LoadDocumentAsync("screens/Dialog");
UI.Open("Dialog");
```

启动期(Addressables 路径):

```csharp
UI.UseAddressableResolver();
await SpriteResolverHelpers.UseAddressableSpriteSetResolver();   // 默认 label="SpriteSets"
await UI.LoadDocumentAsync("screens/Dialog");
UI.Open("Dialog");
```

XML 内双语法对比:

```xml
<!-- 进 atlas 通道:用 SpriteSet "ui" 里的 dialog-frame -->
<Image sprite="ui:dialog-frame" type="sliced" anchor="stretch"/>

<!-- 走 Resources.Load:Assets/.../Resources/ui/dialog-frame.png -->
<Image sprite="ui/dialog-frame" type="sliced" anchor="stretch"/>

<!-- Icon 仍强制 ns:name -->
<Icon name="ui:bell" size="48x48"/>
```

自定义控件:

```csharp
public sealed class MyImage : PromptUGUI.Controls.Control
{
    UnityEngine.UI.Image _img;
    public override void OnAttached()
        => _img = GameObject.GetComponent<UnityEngine.UI.Image>()
                  ?? GameObject.AddComponent<UnityEngine.UI.Image>();

    [UIAttr]
    public string Sprite
    {
        set => _img.sprite = UI.ResolveSprite(value);   // 与内置 Image 同一入口
    }
}
```

---

## 4. 公开 API 表(rename 后)

| 状态 | 签名 | 说明 |
|---|---|---|
| 改名 | `public sealed class SpriteSet : ScriptableObject` | 原 `IconSet` |
| 改名 | `public static Func<string, Sprite> UI.SpriteResolver { get; set; }` | 原 `UI.IconResolver` |
| 新增 | `public static Sprite UI.ResolveSprite(string value)` | 双语法分流入口;subclass 推荐路径 |
| 改名 | `public static void SpriteResolverHelpers.UseSpriteSetResolver(string resourcesSubpath = "SpriteSets")` | 原 `IconResolverHelpers.UseSpriteAtlasIconResolver` |
| 改名 | `public static void SpriteResolverHelpers.UseSpriteSetResolver(IEnumerable<SpriteSet> sets)` | 同上 |
| 改名 | `public static Awaitable SpriteResolverHelpers.UseAddressableSpriteSetResolver(string label = "SpriteSets")` | 原 `IconResolverHelpers.UseAddressableSpriteAtlasIconResolver`;仍仅在 `PROMPTUGUI_HAS_ADDRESSABLES` 下可见;经 partial class 与同步入口合并 |
| 改名 | `public static Awaitable SpriteResolverHelpers.UseAddressableSpriteSetResolver(IEnumerable<string> labels, Addressables.MergeMode mergeMode)` | 同上 |
| 改名(internal) | `UI.HotReload.SpriteResolverRebuilder` | 原 `IconResolverRebuilder` |
| 改名(internal) | `UI.HotReload.NotifySpriteAssetsChanged()` | 原 `NotifyIconAssetsChanged` |
| 不变 | `<Icon>` XML tag + `Icon.Name` setter | Icon 控件保留,Name 仍强制 `ns:name` |
| 行为扩展 | `Image.Sprite` / `Btn.Sprite` / `Toggle.Sprite` / `Slider.Sprite` / `Dropdown.Sprite` / `ScrollList.Sprite` / `InputField.Sprite` setter | 实现从 `_img.sprite = Resources.Load<Sprite>(value)` 改为 `_img.sprite = UI.ResolveSprite(value)`;XML 端可见行为:接受 `ns:name` 形式 |

---

## 5. 落地细节

### 5.1 `UI.ResolveSprite` 实现

`Runtime/Application/UI.cs`,挨着 `SpriteResolver` 字段:

```csharp
public static Sprite ResolveSprite(string value)
{
    if (string.IsNullOrEmpty(value)) return null;

    if (value.IndexOf(':') >= 0)
    {
        if (SpriteResolver == null)
        {
            Debug.LogError(
                $"sprite '{value}': UI.SpriteResolver is not registered. " +
                $"Call SpriteResolverHelpers.UseSpriteSetResolver(spriteSets) " +
                $"before opening Screens that reference sprite='ns:name'.");
            return null;
        }
        var sprite = SpriteResolver(value);
        if (sprite == null)
            Debug.LogError(
                $"sprite '{value}': resolver returned null. " +
                $"Check the sprite name spelling, or run " +
                $"Tools → PromptUGUI → Sprite → Sync Atlases (All Sets) " +
                $"to include it in the SpriteSet's atlas.");
        return sprite;
    }

    return Resources.Load<Sprite>(value);
}
```

`:` 检测用 `IndexOf` 一次扫描,不分配。Resources 路径在 Unity 里不允许包含 `:`(虚拟路径,正斜杠分隔),所以零歧义。

### 5.2 7 个控件的 Sprite setter

每个控件 `Sprite` 属性 setter 改成同一形状:

```csharp
[UIAttr]
public string Sprite
{
    set
    {
        _img.sprite = UI.ResolveSprite(value);
    }
}
// (Toggle 是 _checkmark.sprite;ScrollList 是 _bg.sprite;其他都是 _bg.sprite 或 _img.sprite)
```

ProceduralBuilders 兜底逻辑(`ApplyDefaultSlicedSprite` 等)不变——这些在 `OnAttached` 里跑,setter 在 XML 应用阶段后跑,setter 覆盖默认 sprite 的时机不变。

### 5.3 SpriteAtlasSyncer XML scan 扩展

`Editor/SpriteAtlasSyncer.cs`(原 `IconAtlasSyncer.cs`)的 `ScanXmlReferences` 方法:

**当前实现**:扫描 `.ui.xml` 里的 `<Icon name="ns:name">`,把所有 `ns:name` ref 收集起来,据此决定哪些 PNG 进 SpriteAtlas。Template 内带 `{{...}}` 占位符的形式有专门处理(参见 SKILL 里的 "Template-Param-driven icon names" 段)。

**扩展后**:同样的 ref 提取逻辑加一条规则——扫所有元素的 `sprite=` 属性。识别按以下优先级(沿用 `<Icon name=>` 当前实现风格):

| # | 形态 | 处理 |
|---|---|---|
| 1 | 字面含 `:`(eg. `sprite="ui:dialog"`) | 直接作为 SpriteSet ref 收集 |
| 2 | 字面是 `ns:{{x}}` 半占位 | 取 Template Param 各 invocation arg 值,与字面 `ns` 拼出 ref;沿用 Icon 半占位规则 |
| 3 | 字面是 `{{x}}` 全占位 | 取 Template Param 各 invocation arg 值,有 `:` 的视作完整 SpriteSet ref;沿用 Icon 全占位规则 |
| 4 | 多占位 / 不可解析(eg. `{{a}}:{{b}}`、`solar:{{a}}-{{b}}`) | logs warning,不收;沿用 Icon 现有 warning |
| 5 | 字面无 `:` 且无占位(eg. `sprite="ui/dialog"`) | **不收**(Resources.Load 路径,不在 SpriteSet 的关注范围) |

- 元素 tag 不做白名单(7 个内置 + 任意自定义控件都覆盖);只看 attr 名字 `sprite=` 与值的形态
- `<Icon name=>` 的现有扫描逻辑保持不变(它的 attr value 在 XSD 里被约束必须含 `:` 或是 `{{...}}`,所以等价于上表的 #1 / #2 / #3 / #4)

CLI 路径:`SpriteAtlasSyncer.ScanXmlReferences()` 现有方法体内部抽出一个 `CollectSpriteSetReferences(XElement root, ...)`,既被 `<Icon name=>` 路径调用,也被 `<* sprite=>` 路径调用,共享同一个 Template 占位符处理子函数。

### 5.4 菜单路径

`Editor/SpriteAtlasMenu.cs`(原 `IconAtlasMenu.cs`):

```csharp
[MenuItem("Tools/PromptUGUI/Sprite/Sync Atlases (All Sets)")]
public static void SyncAll() { ... }

[MenuItem("Tools/PromptUGUI/Sprite/Sync Atlases (Selected Set)")]
public static void SyncSelected() { ... }
```

(原路径 `Tools/PromptUGUI/Icon/...` 不保留)

### 5.5 `Icon.cs` 内部更新

`Icon.Name` setter 把所有 `UI.IconResolver` 引用替换为 `UI.SpriteResolver`;错误消息里 "IconResolverHelpers.UseSpriteAtlasIconResolver" 改成 "SpriteResolverHelpers.UseSpriteSetResolver","Sync Icon Atlases" 改成 "Sync Atlases"(菜单路径已迁到 Sprite 子菜单)。

### 5.6 SKILL.md 更新清单

| SKILL | 改动 |
|---|---|
| `authoring-promptugui-xml/SKILL.md` | 1)`<Icon>` 段落:`IconSet` → `SpriteSet`,菜单路径 `Icon → Sync` → `Sprite → Sync`;2)Built-in primitives 表里 `<Image>` / `<Btn>` / `<Toggle>` 等的 `sprite=` 描述追加双语法:"`ns:name` 走 SpriteResolver,无 `:` 走 `Resources.Load`";3)Common mistakes 表新增一行:"sprite='ns:name' 显示白图 / 报错 → 检查 `SpriteResolverHelpers.UseSpriteSetResolver` 是否在 Open 之前调过";4)Discover available icons 段落:`IconSet` → `SpriteSet`,`PromptUGUI.Application.IconSet` → `PromptUGUI.Application.SpriteSet` |
| `scripting-promptugui-csharp/SKILL.md` | 1)所有 `IconSet` / `IconResolver` / `UseSpriteAtlasIconResolver` 字面替换;2)新增 "sprite= 双语法" 小节,讲清 `UI.ResolveSprite` 用法与 subclass 推荐路径;3)`UI.SpriteResolver` 字段说明 |
| `using-promptugui-addressables/SKILL.md` | 17 处字面替换:`UseAddressableSpriteAtlasIconResolver` → `UseAddressableSpriteSetResolver`,默认 label `"IconSets"` → `"SpriteSets"`,所有 `IconSet` → `SpriteSet` |

---

## 6. 测试策略

**重命名(改字面 + 引用,断言保持原意):**

| 旧名 | 新名 |
|---|---|
| `Tests/EditMode/Application/IconResolverTests.cs` | `SpriteResolverTests.cs` |
| `Tests/EditMode/Application/IconHotReloadTests.cs` | `SpriteHotReloadTests.cs` |
| `Tests/EditMode/Editor/IconAtlasSyncerTests.cs` | `SpriteAtlasSyncerTests.cs` |
| `Tests/EditMode/Addressables/AddressableIconResolverTests.cs` | `AddressableSpriteResolverTests.cs` |

**保留不改名(测试对象是 `<Icon>` 控件本身,不是 resolver):**

- `Tests/PlayMode/Controls/IconRuntimeTests.cs`
- `Tests/EditMode/Parser/IconParserTests.cs`

**新增 `Tests/EditMode/Application/ResolveSpriteTests.cs`:**

| 测试 | 断言 |
|---|---|
| `ResolveSprite_with_null_or_empty_returns_null` | 输入 null / "" → 返回 null,不调用 resolver |
| `ResolveSprite_with_colon_routes_to_SpriteResolver` | 设 stub `SpriteResolver = key => sentinelSprite`;`ResolveSprite("ui:x")` → sentinelSprite |
| `ResolveSprite_without_colon_routes_to_Resources_Load` | Resources 里有 `Tests/sprite-A`;`ResolveSprite("Tests/sprite-A")` 返回非 null |
| `ResolveSprite_without_colon_missing_returns_null_silently` | `ResolveSprite("does/not/exist")` → null,**无 LogError**(用 `LogAssert.NoUnexpectedReceived` 验证) |
| `ResolveSprite_with_colon_and_null_resolver_logs_and_returns_null` | SpriteResolver = null;`ResolveSprite("ui:x")` → null + `LogAssert.Expect(Error, ...)` |
| `ResolveSprite_with_colon_and_resolver_returns_null_logs_and_returns_null` | SpriteResolver = _ => null;`ResolveSprite("ui:x")` → null + LogAssert |

**新增 `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs` 内新 case:**

| 测试 | 断言 |
|---|---|
| `ScanXmlReferences_picks_up_Image_sprite_colon_form` | XML 内 `<Image sprite="ui:x"/>` → ref 集合含 `ui:x` |
| `ScanXmlReferences_picks_up_Btn_Toggle_etc_sprite_colon_form` | 单一 fixture 覆盖 6 个其他控件,断言每个都被识别 |
| `ScanXmlReferences_ignores_Image_sprite_without_colon` | XML 内 `<Image sprite="ui/x"/>` → ref 集合不含此项 |
| `ScanXmlReferences_template_param_driven_sprite_full_placeholder` | `<Image sprite="{{x}}"/>` 在 Template body 内 + 调用点传 `x="ui:bell"` → ref 集合含 `ui:bell`(沿用 Icon 的全占位规则) |
| `ScanXmlReferences_template_param_driven_sprite_partial_placeholder` | `<Image sprite="ui:{{x}}"/>` + 调用点 `x="bell"` → ref 集合含 `ui:bell`(沿用 Icon 的半占位规则) |
| `ScanXmlReferences_unanalyzable_sprite_pattern_logs_warning` | `<Image sprite="{{a}}:{{b}}"/>` → ref 集合不变 + warning(沿用 Icon 现有 warning 规则) |

**控件 setter 接入 `ResolveSprite`:**

不写 7 个控件每个的单测——`ResolveSpriteTests` 已经覆盖 helper 逻辑,控件 setter 改成单行 `UI.ResolveSprite(value)` 后,行为正确性由 helper 测试保证。Smoke 走 `Tests/PlayMode/Controls` 现有 Btn / Toggle / Slider / Dropdown / ScrollList / InputField runtime 测试在 sprite= 路径上跑通即可。

---

## 7. 迁移与破坏性影响

不做迁移。库未上线,用户已确认。具体:

- 旧 `IconSet.asset` 资产:不会有,Samples 直接随重命名一起改。
- 旧 C# 使用 `IconSet` 类型的代码:不会有,自家 Runtime / Editor / Tests 内部引用随 PR 一起改。
- 旧菜单路径 `Tools → PromptUGUI → Icon → ...`:直接消失,新路径 `Sprite → ...`。
- 旧 helper 方法名:不保留 `[Obsolete]` 别名。

Samples~/MainMenu 现存的 `.ui.xml` / 任何 `IconSet.asset`:随 PR 改 setName / 字段;新的 `<Image sprite="ns:name">` 示例补一个最小 case 验证 dual-syntax 端到端。

---

## 8. 非目标 / 推迟

- **`<Btn>` / `<Toggle>` 等控件的 sprite 状态变体**(hover / pressed 各自有 sprite):本设计不涉及。这些用户走 subclass + R3 `OnPointerEnter` 自己切。
- **`UI.ResolveSpriteAsync(string)`**:本期所有 sprite 解析都是同步的(SpriteResolver 自己负责预加载缓存)。如果将来要做"按需异步加载单 sprite",再加。
- **`pugui.png` 迁到内置 SpriteSet**:用户明确要求保持 Resources.LoadAll。
- **`<Icon>` 接受 Resources 路径回退**:SR-D4 已决,Icon 仍只接受 `ns:name`。
- **菜单路径双写一段时间**:不做。
- **`AssetReference<SpriteSet>` 入口**:同 IconSet 时代的非目标,等真需要再扩。

---

## 9. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| `:` 检测把用户意外的 Resources 路径误判进 SpriteResolver | 报错 + 显示白图 | Unity Resources 虚拟路径不允许 `:`(只有 `/` 分隔);此风险只在用户文件名里含 `:` 时触发,实际不会发生;SKILL 写明 |
| 用户在 Samples / 自己的项目用 `<Image sprite="ns:name">` 但忘了 `UseSpriteSetResolver` | 报错 + 显示白图 | `UI.ResolveSprite` 的 LogError 已经把所需调用名拼出来,易于自查 |
| Syncer 扫描扩展后误把非 SpriteSet `:` 引用当成 SpriteSet 引用(eg. 用户在 XML 里塞了诡异的 `sprite="C:/path"` 字面) | 该 ref 进剪枝集合后找不到对应 sprite,sync 时报"该 SpriteSet 里没有 'C/path' 这个 key" | sync 工具现有报错走 IconAtlasSyncer 原有的"unknown ref"路径,人类作者会很快定位;无静默 corruption |
| `<Image sprite="ns:name">` 在 hot reload SpriteResolverRebuilder 后**不重新应用**到已加载的 Image 控件 | 切 SpriteSet 时旧 Image 仍显示旧 sprite,直到 Screen 重开 | 已知行为,与 `<Icon name=>` 当前一致;`docs` 与 `SKILL` 在 hot-reload 段落统一备注;真要 live re-apply 走 Screen.ReSolve 钩子是后续工作 |
| `UI.ResolveSprite` 加 `Debug.LogError` 后,作者在原型期用错 atlas 名会刷屏 | 控制台噪音 | 与现有 `<Icon>` 一致;视为 "loud failure",作者修正后即消失 |

---

## 10. 实施粒度(提示)

writing-plans 阶段细化,大致 4 块:

1. **Runtime rename + `UI.ResolveSprite` + 7 控件 setter 切换**
   - `IconSet`→`SpriteSet`、`UI.IconResolver`→`UI.SpriteResolver`、helper class + 方法重命名
   - 新增 `UI.ResolveSprite` + 单测
   - 7 个控件的 `Sprite` setter 切到 `UI.ResolveSprite`
   - `Icon.cs` 内部引用更新 + 错误消息字面更新
   - Runtime 测试 rename
2. **Editor rename + Syncer XML scan 扩展**
   - Editor 类重命名(`IconAtlasSyncer`→`SpriteAtlasSyncer` 等)
   - 菜单路径迁移
   - `ScanXmlReferences` 扩展到 `<* sprite="ns:name">` + 新 case 单测
3. **SKILL.md 三份同步** + lint pass
4. **Samples 验证** + Unity MCP `run_tests` 全跑过

---
